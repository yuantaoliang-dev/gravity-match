using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

public class Shooter : MonoBehaviour
{
    [Header("References")]
    public GameManager gm;
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

    // Trajectory dot pool (v21 style: small circles with fading alpha)
    private const int MaxTrajDots = 100;
    private List<SpriteRenderer> trajDots = new List<SpriteRenderer>();
    private List<Vector2> trajPoints = new List<Vector2>(400);

    // Combo display (right side of shooter)
    private GameObject comboDisplay;
    private TMPro.TextMeshPro comboText;
    private SpriteRenderer[] comboDots = new SpriteRenderer[3];

    void Start()
    {
        var mat = GameConstants.CreateUnlitSpriteMaterial();

        // Shooter platform indicator
        var sr = GetComponent<SpriteRenderer>();
        if (sr)
        {
            sr.color = new Color(0.4f, 0.45f, 0.55f, 0.35f);
            sr.sortingOrder = -5;
            if (mat != null) sr.material = new Material(mat);
        }

        // Setup ball color displays (match HTML: current at center, next to LEFT)
        float br = GameConstants.BallRadius;
        float ballDiam = br * 2f;
        float curSize = (br + 0.02f * GameConstants.WorldScale) * 2f; // HTML: BR+2 radius
        if (currentBallDisplay)
        {
            currentBallDisplay.transform.localPosition = Vector3.zero;
            currentBallDisplay.transform.localScale = new Vector3(curSize, curSize, 1f);
            currentBallDisplay.sortingOrder = 5;
            if (mat != null) currentBallDisplay.material = new Material(mat);

            // White outline ring behind current ball (HTML: BR+5 stroke)
            var outlineGo = new GameObject("CurrentOutline");
            outlineGo.transform.SetParent(transform, false);
            outlineGo.transform.localPosition = Vector3.zero;
            float outlineSize = (br + 0.05f * GameConstants.WorldScale) * 2f;
            outlineGo.transform.localScale = new Vector3(outlineSize, outlineSize, 1f);
            var outlineSr = outlineGo.AddComponent<SpriteRenderer>();
            outlineSr.sprite = currentBallDisplay.sprite;
            outlineSr.color = new Color(1f, 1f, 1f, 0.25f);
            outlineSr.sortingOrder = 4;
            if (mat != null) outlineSr.material = new Material(mat);
        }
        if (nextBallDisplay)
        {
            // HTML: nxX = SX - BR*9, scaled to Unity view
            float nxOffset = -br * 5f;
            nextBallDisplay.transform.localPosition = new Vector3(nxOffset, 0, 0);
            nextBallDisplay.transform.localScale = new Vector3(ballDiam, ballDiam, 1f);
            nextBallDisplay.sortingOrder = 5;
            if (mat != null) nextBallDisplay.material = new Material(mat);
        }

        // Create trajectory LineRenderer programmatically
        CreateTrajectoryDots(mat);
        CreateAimLine(mat);
        CreateComboDisplay(mat);
    }

    void CreateComboDisplay(Material mat)
    {
        // v21: combo counter at SX + BR*7, SY (right of shooter)
        float br = GameConstants.BallRadius;
        comboDisplay = new GameObject("ComboDisplay");
        comboDisplay.transform.SetParent(transform, false);
        comboDisplay.transform.localPosition = new Vector3(br * 7f, 0, 0);

        // "COMBO" text
        var textGo = new GameObject("ComboText");
        textGo.transform.SetParent(comboDisplay.transform, false);
        textGo.transform.localPosition = new Vector3(0, 0.02f, 0);
        comboText = textGo.AddComponent<TMPro.TextMeshPro>();
        comboText.text = "COMBO";
        comboText.fontSize = 0.8f;
        comboText.fontStyle = TMPro.FontStyles.Bold;
        comboText.alignment = TMPro.TextAlignmentOptions.Center;
        comboText.sortingOrder = 10;
        var textRT = textGo.GetComponent<RectTransform>();
        textRT.sizeDelta = new Vector2(0.4f, 0.1f);

        // 3 progress dots
        Sprite dotSprite = currentBallDisplay ? currentBallDisplay.sprite : null;
        float dotSize = 0.04f * GameConstants.WorldScale;
        float dotSpacing = 0.06f * GameConstants.WorldScale;
        for (int i = 0; i < 3; i++)
        {
            var dotGo = new GameObject($"ComboDot{i}");
            dotGo.transform.SetParent(comboDisplay.transform, false);
            dotGo.transform.localPosition = new Vector3(
                (i - 1) * dotSpacing, -0.04f, 0);
            dotGo.transform.localScale = new Vector3(dotSize, dotSize, 1f);
            var sr = dotGo.AddComponent<SpriteRenderer>();
            sr.sprite = dotSprite;
            sr.sortingOrder = 10;
            if (mat != null) sr.material = new Material(mat);
            comboDots[i] = sr;
        }

        comboDisplay.SetActive(false);
    }

    void UpdateComboDisplay()
    {
        int count = gm.comboCount;
        if (count <= 0)
        {
            if (comboDisplay) comboDisplay.SetActive(false);
            return;
        }

        comboDisplay.SetActive(true);
        // v21: comboCount >= 2 → amber, else half-transparent white
        Color activeCol = count >= 2
            ? new Color(0.937f, 0.624f, 0.153f, 1f) // #EF9F27
            : new Color(1f, 1f, 1f, 0.5f);
        Color inactiveCol = new Color(1f, 1f, 1f, 0.1f);

        comboText.color = activeCol;
        for (int i = 0; i < 3; i++)
        {
            comboDots[i].color = i < count ? activeCol : inactiveCol;
        }
    }

    static Material CreateLineMaterial()
    {
        // Use project-standard unlit sprite shader for LineRenderer
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) mat.renderQueue = 3000; // Transparent queue
        return mat;
    }

    void CreateTrajectoryDots(Material baseMat)
    {
        // v21 style: pool of small dot sprites, positioned along trajectory
        Sprite dotSprite = currentBallDisplay ? currentBallDisplay.sprite : null;
        float dotSize = 0.025f * GameConstants.WorldScale; // v21: radius 1.2px
        var parent = new GameObject("TrajectoryDots");
        parent.transform.SetParent(transform, false);

        for (int i = 0; i < MaxTrajDots; i++)
        {
            var go = new GameObject($"Dot{i}");
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = new Vector3(dotSize, dotSize, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dotSprite;
            sr.sortingOrder = 10;
            if (baseMat != null) sr.material = new Material(baseMat);
            go.SetActive(false);
            trajDots.Add(sr);
        }
    }

    void CreateAimLine(Material baseMat)
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        aimLine = go.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.startWidth = 0.02f * GameConstants.WorldScale;
        aimLine.endWidth = 0.02f * GameConstants.WorldScale;
        aimLine.startColor = new Color(1f, 1f, 1f, 0.6f);
        aimLine.endColor = new Color(1f, 1f, 1f, 0.3f);
        aimLine.sortingOrder = 10;
        aimLine.positionCount = 0;
        aimLine.material = CreateLineMaterial();
    }

    void Update()
    {
        // Always update ball color display and combo counter
        UpdateDisplay();
        UpdateComboDisplay();

        if (gm.state != GameManager.GameState.Play) return;
        if (projectile != null)
        {
            UpdateProjectile();
        }
        else
        {
            HandleInput();
        }
    }

    void HandleInput()
    {
        if (gm.IsRotating) return;

        // Ignore touches on UI elements (supports both mouse and touch fingerId)
        if (IsPointerOverUI()) return;

        Vector2 inputScreenPos = GetInputScreenPosition();
        Vector2 inputWorld = Camera.main.ScreenToWorldPoint(inputScreenPos);
        Vector2 shooterPos = transform.position;

        // Press to start aiming
        if (GetInputDown())
        {
            aiming = true;
        }

        if (aiming)
        {
            Vector2 dir = (inputWorld - shooterPos).normalized;

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

        if (GetInputUp() && aiming)
        {
            aiming = false;
            HideTrajectory();
            HideAimLine();
            if (aimDir.y > 0.05f) Fire(aimDir);
        }
    }

    // ===== INPUT HELPERS (mouse + touch compatible) =====

    /// <summary>Check if pointer/touch just started this frame.</summary>
    bool GetInputDown()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).phase == TouchPhase.Began;
        return Input.GetMouseButtonDown(0);
    }

    /// <summary>Check if pointer/touch just ended this frame.</summary>
    bool GetInputUp()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).phase == TouchPhase.Ended;
        return Input.GetMouseButtonUp(0);
    }

    /// <summary>Get current pointer/touch screen position.</summary>
    Vector2 GetInputScreenPosition()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;
        return Input.mousePosition;
    }

    /// <summary>Check if pointer is over a UI element (works for both mouse and touch).</summary>
    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        // For touch: pass fingerId; for mouse: no argument
        if (Input.touchCount > 0)
            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        return EventSystem.current.IsPointerOverGameObject();
    }

    void Fire(Vector2 dir)
    {
        if (gm.ballsLeft <= 0) return;

        gm.ForceValidColors();

        // Create projectile from pool
        projectile = gm.SpawnBall(transform.position, gm.currentColor).gameObject;
        // Remove from field ball list (projectile is in-flight, not a field ball)
        var ball = projectile.GetComponent<Ball>();
        gm.Balls.Remove(ball);
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
            float minDistSq = float.MaxValue;
            foreach (var b in gm.Balls)
            {
                float dSq = ((Vector2)b.transform.position - pos).sqrMagnitude;
                if (dSq < minDistSq) minDistSq = dSq;
            }

            // Sub-stepping near balls (compare with squared thresholds)
            float thresh3 = GameConstants.BallRadius * 3f;
            float thresh6 = GameConstants.BallRadius * 6f;
            int sub = minDistSq < thresh3 * thresh3 ? 6 :
                      minDistSq < thresh6 * thresh6 ? 3 : 1;
            Vector2 subVel = projVelocity / sub * dt / 60f;

            for (int step = 0; step < sub; step++)
            {
                pos += subVel;

                // Wall bounces (cached camera bounds)
                float hh = gm.CamHH;
                float hw = gm.CamHW;
                float r = GameConstants.BallRadius;
                if (pos.x - r <= -hw) { pos.x = -hw + r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (pos.x + r >= hw) { pos.x = hw - r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (pos.y + r >= hh) { pos.y = hh - r; projVelocity.y *= -1; subVel.y *= -1; projBounces++; }

                if (projBounces > GameConstants.MaxBounces || pos.y < transform.position.y - 0.2f)
                {
                    gm.ReturnBallToPool(projectile);
                    projectile = null;
                    gm.comboCount = 0;
                    gm.StartRotation();
                    return;
                }

                // Black hole absorb
                Vector2 bhPos = gm.blackHole.position;
                float bhEH = gm.BHEventHorizon;
                if (((Vector2)pos - bhPos).sqrMagnitude < bhEH * bhEH)
                {
                    gm.ReturnBallToPool(projectile);
                    projectile = null;
                    gm.OnProjectileAbsorbedByBH();
                    return;
                }

                // Hit detection (squared distance comparison)
                Ball hitBall = null;
                float hitDistSq = float.MaxValue;
                float hdSq = GameConstants.HitDetectDist * GameConstants.HitDetectDist;
                foreach (var b in gm.Balls)
                {
                    float dSq = ((Vector2)b.transform.position - pos).sqrMagnitude;
                    if (dSq < hdSq && dSq < hitDistSq)
                    {
                        hitDistSq = dSq;
                        hitBall = b;
                    }
                }

                if (hitBall != null)
                {
                    Color projColor = projectile.GetComponent<Ball>().ballColor;
                    Vector2 vel = projVelocity.normalized;
                    gm.ReturnBallToPool(projectile);
                    projectile = null;

                    Vector2 hitCenter = hitBall.transform.position;
                    float od = GameConstants.OverlapDistance;
                    float hd = GameConstants.HitDetectDist;

                    // Look-ahead: continue flying only 1/3 ball diameter past
                    // the first hit point to check for a second ball.
                    float maxTravel = GameConstants.BallRadius * 2f / 3f;
                    Ball secondHit = null;
                    float secondHitT = float.MaxValue;

                    foreach (var b in gm.Balls)
                    {
                        if (b.id == hitBall.id) continue;
                        Vector2 bc = b.transform.position;
                        Vector2 db = pos - bc;
                        // Ray-circle intersection: |pos + t*vel - bc| = HitDetectDist
                        float bCoeff = 2f * Vector2.Dot(db, vel);
                        float cCoeff = db.sqrMagnitude - hd * hd;
                        float disc = bCoeff * bCoeff - 4f * cCoeff;
                        if (disc < 0) continue;

                        float sqrtDisc = Mathf.Sqrt(disc);
                        float t = (-bCoeff - sqrtDisc) / 2f; // entry point
                        if (t < 0) t = 0f; // already inside range
                        if (t > maxTravel) continue;

                        if (t < secondHitT)
                        {
                            secondHitT = t;
                            secondHit = b;
                        }
                    }

                    // Placement distance depends on color match:
                    // Same color → OverlapDistance (visual overlap)
                    // Different color → just touching, no visual overlap
                    float diffColorDist = GameConstants.BallRadius * 2f;
                    bool sameColorHit = (hitBall.ballColor == projColor);
                    float placeDist = sameColorHit ? od : diffColorDist;
                    Vector2 approachDir = (pos - hitCenter).normalized;
                    Vector2 newPos = hitCenter + approachDir * placeDist;

                    // Slide-through: only when BOTH hitBall and secondHit are
                    // same color as projectile, and they're in different groups.
                    // If hitBall is different color, always stop at first contact.
                    if (sameColorHit && secondHit != null && secondHit.ballColor == projColor)
                    {
                        var hitGroup = gm.FindGroup(hitBall, GameConstants.MatchTouchDist);
                        bool alreadyConnected = hitGroup.Any(b => b.id == secondHit.id);
                        if (!alreadyConnected)
                        {
                            Vector2 contactPos = pos + vel * secondHitT;
                            Vector2 dirFromSecond = (contactPos - (Vector2)secondHit.transform.position).normalized;
                            newPos = (Vector2)secondHit.transform.position + dirFromSecond * od;
                            Debug.Log($"[GravityMatch] Slide-through: bridging separate groups");
                        }
                    }

                    // Push away from other balls
                    // Same color: use OverlapDistance (allow overlap)
                    // Different color: use MinVisDist (no visual overlap)
                    float pushNudge = 0.002f * GameConstants.WorldScale;
                    for (int it = 0; it < 30; it++)
                    {
                        bool pushed = false;
                        foreach (var b2 in gm.Balls)
                        {
                            if (b2.id == hitBall.id) continue;
                            if (secondHit != null && b2.id == secondHit.id) continue;
                            float d2 = Vector2.Distance(newPos, b2.transform.position);
                            bool sameColor = b2.ballColor == projColor;
                            float pushDist = sameColor ? (od - 0.005f * GameConstants.WorldScale) : diffColorDist;
                            if (d2 < pushDist)
                            {
                                Vector2 pDir = (newPos - (Vector2)b2.transform.position).normalized;
                                newPos += pDir * (pushDist - d2 + pushNudge);
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
        // Standard v21 match detection: BFS with MatchTouchDist
        var grp = gm.FindGroup(newBall, GameConstants.MatchTouchDist);

        Debug.Log($"[GravityMatch] ProcessMatch: group size={grp.Count}, color={GameConstants.ColorToHex(newBall.ballColor)}");
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
                Debug.Log($"[GravityMatch] Cone sweep: {grp.Count}-match, colorOnly={colorOnly}, coneExtra={coneExtra.Count}, totalTargets={allTargets.Count}");

                // Pass cone info for visual FX
                gm.StartMatchSequence(grp.Count, allTargets, grp, baseAngle, coneAngle);
                return;
            }

            // 3-match (no cone)
            gm.StartMatchSequence(grp.Count, allTargets, grp, 0, 0);
        }
        else
        {
            gm.comboCount = 0;
            gm.StartRotation();
        }
    }

    // ===== TRAJECTORY PREVIEW (v21 style: dotted path) =====
    void ShowTrajectory(Vector2 start, Vector2 vel)
    {
        // Simulate trajectory matching v21: half-speed steps, stop at ball/BH
        trajPoints.Clear();
        Vector2 pos = start;
        Vector2 dir = vel.normalized;
        // v21 step: BSPD*0.5 pixels = 2.25px → world units
        float stepDist = 0.0225f * GameConstants.WorldScale;
        Vector2 v = dir * stepDist;
        int bounces = 0;
        float hh = gm.CamHH;
        float hw = gm.CamHW;
        float r = GameConstants.BallRadius;
        float hd = GameConstants.HitDetectDist;
        Vector2 bhPos = gm.blackHole.position;
        float shooterY = transform.position.y;
        float trajBhEH = gm.BHEventHorizon;
        float trajBhEHSq = trajBhEH * trajBhEH;
        float hdSqTraj = hd * hd;

        for (int i = 0; i < 400; i++) // v21 max 400 steps
        {
            pos += v;

            // Wall bounces
            if (pos.x - r <= -hw) { pos.x = -hw + r; v.x *= -1; bounces++; }
            if (pos.x + r >= hw) { pos.x = hw - r; v.x *= -1; bounces++; }
            if (pos.y + r >= hh) { pos.y = hh - r; v.y *= -1; bounces++; }

            // Stop conditions
            if (bounces > GameConstants.MaxBounces) break;
            if (pos.y < shooterY - 0.1f) break;
            if ((pos - bhPos).sqrMagnitude < trajBhEHSq) break;

            // Stop at ball hit (v21: dist < BR*1.7, squared comparison)
            bool hitBall = false;
            foreach (var b in gm.Balls)
            {
                if (((Vector2)b.transform.position - pos).sqrMagnitude < hdSqTraj) { hitBall = true; break; }
            }
            if (hitBall) break;

            trajPoints.Add(pos);
        }

        // Render as dots: every other point, fading alpha (v21 line 716-719)
        int dotIdx = 0;
        int show = Mathf.Min(trajPoints.Count, GameConstants.TrajectoryMaxDots);
        for (int i = 0; i < show && dotIdx < MaxTrajDots; i += 2)
        {
            var dot = trajDots[dotIdx];
            dot.gameObject.SetActive(true);
            dot.transform.position = (Vector3)trajPoints[i];
            // v21: alpha = max(0.05, (1 - i/show) * 0.35)
            float alpha = Mathf.Max(0.05f, (1f - (float)i / show) * 0.35f);
            dot.color = new Color(1f, 1f, 1f, alpha);
            dotIdx++;
        }
        // Hide unused dots
        for (int i = dotIdx; i < MaxTrajDots; i++)
            trajDots[i].gameObject.SetActive(false);
    }

    void HideTrajectory()
    {
        for (int i = 0; i < trajDots.Count; i++)
            trajDots[i].gameObject.SetActive(false);
    }

    void ShowAimLine(Vector2 start, Vector2 dir)
    {
        if (aimLine == null) return;
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, (Vector3)start);
        aimLine.SetPosition(1, (Vector3)(start + dir * 0.3f * GameConstants.WorldScale));
    }

    void HideAimLine()
    {
        if (aimLine) aimLine.positionCount = 0;
    }

    void UpdateDisplay()
    {
        // Hide current ball when no balls left
        if (currentBallDisplay)
        {
            bool showCurrent = gm.ballsLeft > 0 || projectile != null;
            currentBallDisplay.gameObject.SetActive(showCurrent);
            if (showCurrent) currentBallDisplay.color = gm.currentColor;
        }

        // Hide next ball when 1 or fewer balls left
        if (nextBallDisplay)
        {
            bool showNext = gm.ballsLeft > 1;
            nextBallDisplay.gameObject.SetActive(showNext);
            if (showNext)
            {
                Color nc = gm.nextColor;
                nc.a = 0.5f;
                nextBallDisplay.color = nc;
            }
        }
    }
}
