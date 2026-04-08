using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class Shooter : MonoBehaviour
{
    [Header("References")]
    public GameManager gm;
    public LineRenderer trajectoryLine;
    public LineRenderer aimLine;
    public SpriteRenderer currentBallDisplay;
    public SpriteRenderer nextBallDisplay;

    [Header("State")]
    public bool HasProjectile => projectile != null;

    private GameObject projectile;
    private Vector2 projVelocity;
    private int projBounces;
    private bool aiming;
    private Vector2 aimDir;

    void Start()
    {
        var unlitShader = Shader.Find("Sprites/Default");

        // Shooter platform indicator (faint circle)
        var sr = GetComponent<SpriteRenderer>();
        if (sr)
        {
            sr.color = new Color(0.4f, 0.45f, 0.55f, 0.25f);
            sr.sortingOrder = -5;
            if (unlitShader != null) sr.material = new Material(unlitShader);
        }

        // Setup ball color displays
        float ballDiam = GameConstants.BallRadius * 2f;
        if (currentBallDisplay)
        {
            currentBallDisplay.transform.localPosition = new Vector3(0, 0.15f, 0);
            currentBallDisplay.transform.localScale = new Vector3(ballDiam, ballDiam, 1f);
            currentBallDisplay.sortingOrder = 5;
            if (unlitShader != null) currentBallDisplay.material = new Material(unlitShader);
        }
        if (nextBallDisplay)
        {
            nextBallDisplay.transform.localPosition = new Vector3(0.5f, 0.15f, 0);
            nextBallDisplay.transform.localScale = new Vector3(ballDiam * 0.65f, ballDiam * 0.65f, 1f);
            nextBallDisplay.sortingOrder = 5;
            if (unlitShader != null) nextBallDisplay.material = new Material(unlitShader);
        }
    }

    void Update()
    {
        if (gm.state != GameManager.GameState.Play) return;
        if (projectile != null)
        {
            UpdateProjectile();
        }
        else
        {
            HandleInput();
            UpdateDisplay();
        }
    }

    void HandleInput()
    {
        if (gm.IsRotating) return;

        // Mouse/touch: press to aim, release to fire
        if (Input.GetMouseButtonDown(0))
        {
            aiming = true;
        }

        if (aiming)
        {
            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 shooterPos = transform.position;
            Vector2 dir = (mouseWorld - shooterPos).normalized;

            // Only allow upward shots
            if (dir.y > 0.05f)
            {
                aimDir = dir;
                ShowTrajectory(shooterPos, dir * GameConstants.BallSpeed);
                ShowAimLine(shooterPos, dir);
            }
        }
        else
        {
            HideTrajectory();
            HideAimLine();
        }

        if (Input.GetMouseButtonUp(0) && aiming)
        {
            aiming = false;
            HideTrajectory();
            HideAimLine();
            if (aimDir.y > 0.05f) Fire(aimDir);
        }
    }

    void Fire(Vector2 dir)
    {
        if (gm.ballsLeft <= 0) return;

        gm.ForceValidColors();

        // Create projectile
        projectile = Instantiate(gm.ballPrefab, transform.position, Quaternion.identity);
        var ball = projectile.GetComponent<Ball>();
        ball.Init(-1, gm.currentColor);
        projVelocity = dir * GameConstants.BallSpeed;
        projBounces = 0;

        gm.OnBallFired();
    }

    void UpdateProjectile()
    {
        if (projectile == null) return;

        // 3 full-speed steps per frame
        float dt = Time.deltaTime * 60f; // normalize to 60fps
        for (int fStep = 0; fStep < 3; fStep++)
        {
            if (projectile == null) return;

            Vector2 pos = projectile.transform.position;
            float minDist = float.MaxValue;
            foreach (var b in gm.Balls)
            {
                float d = Vector2.Distance(pos, b.transform.position);
                if (d < minDist) minDist = d;
            }

            // Sub-stepping near balls
            int sub = minDist < GameConstants.BallRadius * 3 ? 6 :
                      minDist < GameConstants.BallRadius * 6 ? 3 : 1;
            Vector2 subVel = projVelocity / sub * dt / 60f;

            for (int step = 0; step < sub; step++)
            {
                pos += subVel;

                // Wall bounces (camera bounds)
                float hh = Camera.main.orthographicSize;
                float hw = hh * Camera.main.aspect;
                float r = GameConstants.BallRadius;
                if (pos.x - r <= -hw) { pos.x = -hw + r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (pos.x + r >= hw) { pos.x = hw - r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (pos.y + r >= hh) { pos.y = hh - r; projVelocity.y *= -1; subVel.y *= -1; projBounces++; }

                if (projBounces > GameConstants.MaxBounces || pos.y < transform.position.y - 0.2f)
                {
                    Destroy(projectile);
                    projectile = null;
                    gm.comboCount = 0;
                    gm.StartRotation();
                    return;
                }

                // Black hole absorb
                Vector2 bhPos = gm.blackHole.position;
                if (Vector2.Distance(pos, bhPos) < gm.BHEventHorizon)
                {
                    Destroy(projectile);
                    projectile = null;
                    gm.OnProjectileAbsorbedByBH();
                    return;
                }

                // Hit detection
                Ball hitBall = null;
                float hitDist = float.MaxValue;
                foreach (var b in gm.Balls)
                {
                    float d = Vector2.Distance(pos, b.transform.position);
                    if (d < GameConstants.HitDetectDist && d < hitDist)
                    {
                        hitDist = d;
                        hitBall = b;
                    }
                }

                if (hitBall != null)
                {
                    Color projColor = projectile.GetComponent<Ball>().ballColor;
                    Destroy(projectile);
                    projectile = null;

                    // Place new ball at overlap distance from hit ball
                    Vector2 dir = (pos - (Vector2)hitBall.transform.position).normalized;
                    Vector2 newPos = (Vector2)hitBall.transform.position + dir * GameConstants.OverlapDistance;

                    // Push away from other balls
                    for (int it = 0; it < 30; it++)
                    {
                        bool pushed = false;
                        foreach (var b2 in gm.Balls)
                        {
                            if (b2.id == hitBall.id) continue;
                            float d2 = Vector2.Distance(newPos, b2.transform.position);
                            if (d2 < GameConstants.OverlapDistance - 0.005f)
                            {
                                Vector2 pDir = (newPos - (Vector2)b2.transform.position).normalized;
                                newPos += pDir * (GameConstants.OverlapDistance - d2 + 0.002f);
                                pushed = true;
                            }
                        }
                        if (!pushed) break;
                    }

                    var newBall = gm.SpawnBall(newPos, projColor);
                    ProcessMatch(newBall);
                    return;
                }

                projectile.transform.position = pos;
            }
        }
    }

    void ProcessMatch(Ball newBall)
    {
        var grp = gm.FindGroup(newBall, GameConstants.MatchTouchDist);
        if (grp.Count >= 3)
        {
            var allTargets = new List<Ball>(grp);

            // Cone detection for 4+/5+ match
            if (grp.Count >= 4)
            {
                Vector2 center = Vector2.zero;
                foreach (var g in grp) center += (Vector2)g.transform.position;
                center /= grp.Count;

                Vector2 bhPos = gm.blackHole.position;
                float baseAngle = Mathf.Atan2(center.y - bhPos.y, center.x - bhPos.x);
                float coneAngle = (grp.Count >= 5 ? GameConstants.ConeAngle5 : GameConstants.ConeAngle4) * Mathf.Deg2Rad;
                bool colorOnly = grp.Count < 5;
                var matchIds = new HashSet<int>(grp.Select(b => b.id));

                // Find cone targets
                var hitIds = new HashSet<int>();
                foreach (var b in gm.Balls)
                {
                    if (matchIds.Contains(b.id)) continue;
                    if (colorOnly && b.ballColor != newBall.ballColor) continue;
                    float a = Mathf.Atan2(b.transform.position.y - bhPos.y, b.transform.position.x - bhPos.x);
                    float da = Mathf.DeltaAngle(baseAngle * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float bd = b.DistTo(bhPos);
                    if (bd < GameConstants.BallRadius) continue;
                    float halfW = Mathf.Asin(Mathf.Min(1f, GameConstants.BallRadius / bd));
                    if (Mathf.Abs(da) - halfW <= coneAngle) hitIds.Add(b.id);
                }

                // Spread to neighbors
                bool spread = true;
                while (spread)
                {
                    spread = false;
                    foreach (var b in gm.Balls)
                    {
                        if (hitIds.Contains(b.id) || matchIds.Contains(b.id)) continue;
                        foreach (var nb in gm.GetTouching(b))
                        {
                            if (hitIds.Contains(nb.id)) { hitIds.Add(b.id); spread = true; break; }
                        }
                    }
                }

                var coneExtra = gm.Balls.Where(b => hitIds.Contains(b.id)).ToList();
                allTargets.AddRange(coneExtra);

                // TODO: show cone VFX
            }

            // Calculate score
            int pts = grp.Count >= 5 ? GameConstants.Score5Match :
                      grp.Count == 4 ? GameConstants.Score4Match : GameConstants.Score3Match;
            gm.score += allTargets.Count * pts;

            // TODO: show highlight, then remove after delay
            gm.RemoveBalls(allTargets);

            // Combo check
            bool hasSingle = gm.Balls.Any(b =>
                !gm.GetTouching(b, GameConstants.MatchTouchDist).Any(t => t.ballColor == b.ballColor));
            if (hasSingle)
            {
                gm.comboCount++;
                if (gm.comboCount >= 3)
                {
                    // TODO: combo buddy spawn
                    gm.comboCount = 0;
                }
            }
            else gm.comboCount = 0;

            gm.StartRotation();
        }
        else
        {
            gm.comboCount = 0;
            gm.StartRotation();
        }
    }

    // ===== TRAJECTORY PREVIEW =====
    void ShowTrajectory(Vector2 start, Vector2 vel)
    {
        // TODO: implement trajectory visualization
    }
    void HideTrajectory() { }
    void ShowAimLine(Vector2 start, Vector2 dir) { }
    void HideAimLine() { }

    void UpdateDisplay()
    {
        if (currentBallDisplay) currentBallDisplay.color = gm.currentColor;
        if (nextBallDisplay) nextBallDisplay.color = gm.nextColor;
    }
}
