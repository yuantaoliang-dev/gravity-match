using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    public GameObject ballPrefab;
    public Transform ballContainer;
    public Transform blackHole;
    public Shooter shooter;
    public UIManager ui;

    [Header("Level Data")]
    private List<LevelDef> levels;

    [Header("State")]
    public GameState state = GameState.Play;
    public int currentLevel = 0;
    public int ballsLeft;
    public int score;
    public int comboCount;

    // Ball tracking
    private List<Ball> balls = new List<Ball>();
    private int nextBallId = 0;

    // Black hole growth
    private int bhAte = 0;
    public float BHRadius => GameConstants.BHRadiusBase + bhAte * GameConstants.BHGrowthRadius;
    public float BHEventHorizon => GameConstants.BHEventHorizonBase + bhAte * GameConstants.BHGrowthEH;

    // Field rotation
    private float fieldAngle = 0f;
    private float rotationTarget = 0f;
    private bool isRotating = false;

    // Color queue
    public Color currentColor { get; private set; }
    public Color nextColor { get; private set; }

    // Level star tracking
    private int[] levelStars;

    public enum GameState { Play, Highlight, Suck, Buddy, Rotating, Won, Lost }

    void Awake()
    {
        Instance = this;
        levels = LevelDataBuilder.BuildAll();
        levelStars = new int[levels.Count];
    }

    void Start()
    {
        // Set camera to fit the game field (YAML edits get overwritten by Unity Editor)
        var cam = Camera.main;
        if (cam)
        {
            cam.orthographicSize = 3.5f;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
        }

        // Position shooter at bottom of camera view
        if (shooter)
        {
            shooter.transform.position = new Vector3(0, -cam.orthographicSize + 0.7f, 0);
        }

        // Setup BlackHole visual (unlit shader so it's visible without 2D light)
        var bhSr = blackHole.GetComponent<SpriteRenderer>();
        if (bhSr)
        {
            bhSr.color = new Color(0.08f, 0.08f, 0.14f, 1f);
            bhSr.sortingOrder = -10;
            var unlitShader = Shader.Find("Sprites/Default");
            if (unlitShader != null) bhSr.material = new Material(unlitShader);
        }

        LoadLevel(0);
    }

    void Update()
    {
        // Update BlackHole visual size
        float bhDiam = BHRadius * 2f;
        blackHole.localScale = new Vector3(bhDiam, bhDiam, 1f);

        if (state == GameState.Play)
        {
            ForceValidColors();
            BHAutoAbsorb();
        }
        else if (state == GameState.Rotating)
        {
            UpdateRotation();
        }
    }

    // ===== LEVEL LOADING =====
    public void LoadLevel(int index)
    {
        currentLevel = index;
        var lv = levels[index];

        // Clear existing balls
        foreach (var b in balls) if (b != null) Destroy(b.gameObject);
        balls.Clear();
        nextBallId = 0;
        bhAte = 0;
        score = 0;
        comboCount = 0;
        fieldAngle = 0f;
        rotationTarget = 0f;
        isRotating = false;
        ballsLeft = lv.budget;
        state = GameState.Play;

        // Spawn balls from level data
        Vector2 center = blackHole.position;
        var positions = lv.GenerateBallPositions(center);
        foreach (var (pos, color) in positions)
        {
            SpawnBall(pos, color);
        }

        // === DEBUG: diagnose visibility ===
        var cam = Camera.main;
        Debug.Log($"[GravityMatch] === Level {index + 1}: {lv.name} ===");
        Debug.Log($"[GravityMatch] Camera orthoSize={cam.orthographicSize} pos={cam.transform.position} bg={cam.backgroundColor}");
        Debug.Log($"[GravityMatch] Visible range: X=[{-cam.orthographicSize * cam.aspect:F2}, {cam.orthographicSize * cam.aspect:F2}] Y=[{-cam.orthographicSize:F2}, {cam.orthographicSize:F2}]");
        Debug.Log($"[GravityMatch] BlackHole pos={blackHole.position} scale={blackHole.localScale} BHRadius={BHRadius}");
        var bhSr = blackHole.GetComponent<SpriteRenderer>();
        if (bhSr) Debug.Log($"[GravityMatch] BlackHole SR enabled={bhSr.enabled} color={bhSr.color} material={bhSr.material.shader.name} sortOrder={bhSr.sortingOrder}");
        Debug.Log($"[GravityMatch] Shooter pos={shooter.transform.position} scale={shooter.transform.localScale}");
        Debug.Log($"[GravityMatch] Ball count={balls.Count} WorldScale={GameConstants.WorldScale} BallRadius={GameConstants.BallRadius}");
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var b in balls)
        {
            Vector2 p = b.transform.position;
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            var bsr = b.GetComponent<SpriteRenderer>();
            Debug.Log($"[GravityMatch]   Ball#{b.id} pos=({p.x:F3},{p.y:F3}) scale={b.transform.localScale} color={b.ballColor} shader={bsr?.material.shader.name} enabled={bsr?.enabled}");
        }
        Debug.Log($"[GravityMatch] Ball bounds: X=[{minX:F3}, {maxX:F3}] Y=[{minY:F3}, {maxY:F3}]");
        Debug.Log($"[GravityMatch] ballPrefab scale={ballPrefab.transform.localScale} SR={ballPrefab.GetComponent<SpriteRenderer>()?.sprite?.name}");
        // === END DEBUG ===

        // Startup validation: prevent 3+ same-color groups
        for (int v = 0; v < 50; v++)
        {
            bool found = false;
            foreach (var b in balls)
            {
                var grp = FindGroup(b, GameConstants.StrictTouchDist);
                if (grp.Count >= 3)
                {
                    var otherColors = lv.colors.Where(c => c != b.ballColor).ToArray();
                    b.Init(b.id, otherColors[Random.Range(0, otherColors.Length)]);
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }

        // Push apart overlapping different-color balls
        PushApartDifferentColors();

        // Init color queue
        currentColor = PickNextColor();
        nextColor = PickNextColor();

        ui.UpdateHUD(ballsLeft, score, balls.Count);
        ui.ShowLevelName(index + 1, lv.name);
    }

    void PushApartDifferentColors()
    {
        // Identify pair balls
        var pairIds = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (var b in balls)
        {
            if (visited.Contains(b.id)) continue;
            var grp = FindGroup(b, GameConstants.MatchTouchDist);
            foreach (var g in grp) visited.Add(g.id);
            if (grp.Count >= 2) foreach (var g in grp) pairIds.Add(g.id);
        }

        float minVis = GameConstants.MinVisDist;
        Vector2 center = blackHole.position;

        for (int iter = 0; iter < 150; iter++)
        {
            bool pushed = false;
            for (int i = 0; i < balls.Count; i++)
            {
                for (int j = i + 1; j < balls.Count; j++)
                {
                    var bi = balls[i]; var bj = balls[j];
                    if (bi.ballColor == bj.ballColor) continue;
                    float d = bi.DistTo(bj);
                    if (d < minVis && d > 0.001f)
                    {
                        Vector2 dir = ((Vector2)bj.transform.position - (Vector2)bi.transform.position).normalized;
                        float push = minVis - d + 0.01f;
                        bool iP = pairIds.Contains(bi.id), jP = pairIds.Contains(bj.id);
                        if (iP && !jP)
                            bj.transform.position += (Vector3)(dir * push);
                        else if (!iP && jP)
                            bi.transform.position -= (Vector3)(dir * push);
                        else
                        {
                            bi.transform.position -= (Vector3)(dir * push * 0.5f);
                            bj.transform.position += (Vector3)(dir * push * 0.5f);
                        }
                        // Clamp to bounds and away from BH
                        ClampBallPosition(bi);
                        ClampBallPosition(bj);
                        pushed = true;
                    }
                }
            }
            if (!pushed) break;
        }
    }

    void ClampBallPosition(Ball b)
    {
        // TODO: clamp to camera bounds
        Vector2 center = blackHole.position;
        float dist = b.DistTo(center);
        if (dist < BHEventHorizon + GameConstants.BallRadius + 0.02f)
        {
            Vector2 dir = ((Vector2)b.transform.position - center).normalized;
            b.transform.position = center + dir * (BHEventHorizon + GameConstants.BallRadius + 0.03f);
        }
    }

    // ===== BALL MANAGEMENT =====
    public Ball SpawnBall(Vector2 pos, Color color)
    {
        var go = Instantiate(ballPrefab, pos, Quaternion.identity, ballContainer);
        var ball = go.GetComponent<Ball>();
        ball.Init(nextBallId++, color);
        balls.Add(ball);
        return ball;
    }

    public void RemoveBalls(List<Ball> list)
    {
        foreach (var b in list)
        {
            // TODO: play suction animation
            balls.Remove(b);
            Destroy(b.gameObject, 0.5f); // delay for animation
        }
        bhAte += list.Count;
    }

    // ===== MATCHING =====
    public List<Ball> GetTouching(Ball b, float maxDist = -1)
    {
        if (maxDist < 0) maxDist = GameConstants.TouchDist;
        return balls.Where(o => o.id != b.id && b.DistTo(o) <= maxDist).ToList();
    }

    public List<Ball> FindGroup(Ball start, float maxDist = -1)
    {
        if (maxDist < 0) maxDist = GameConstants.MatchTouchDist;
        var group = new List<Ball> { start };
        var visited = new HashSet<int> { start.id };
        var queue = new Queue<Ball>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var t in GetTouching(cur, maxDist))
            {
                if (!visited.Contains(t.id) && t.ballColor == start.ballColor)
                {
                    visited.Add(t.id);
                    queue.Enqueue(t);
                    group.Add(t);
                }
            }
        }
        return group;
    }

    // ===== COLOR SELECTION =====
    public List<Color> GetPairColors()
    {
        var visited = new HashSet<int>();
        var paired = new HashSet<Color>();
        foreach (var b in balls)
        {
            if (visited.Contains(b.id)) continue;
            var grp = FindGroup(b, GameConstants.MatchTouchDist);
            foreach (var g in grp) visited.Add(g.id);
            if (grp.Count >= 2) paired.Add(b.ballColor);
        }
        return paired.ToList();
    }

    public Color PickNextColor()
    {
        if (balls.Count == 0) return levels[currentLevel].colors[0];
        var pc = GetPairColors();
        if (pc.Count == 0) pc = balls.Select(b => b.ballColor).Distinct().ToList();
        if (pc.Count == 0) return levels[currentLevel].colors[0];
        if (pc.Count == 1) return pc[0];

        // Weighted random
        var weights = new List<(Color c, float w)>();
        foreach (var c in pc)
        {
            float w = 0;
            foreach (var b in balls)
            {
                if (b.ballColor != c) continue;
                bool hasPair = GetTouching(b, GameConstants.MatchTouchDist)
                    .Any(t => t.ballColor == c);
                w += hasPair ? 4f : 1f;
            }
            weights.Add((c, Mathf.Max(w, 1f)));
        }
        float total = weights.Sum(x => x.w);
        float r = Random.Range(0f, total);
        float acc = 0;
        foreach (var (c, w) in weights) { acc += w; if (r <= acc) return c; }
        return pc[0];
    }

    public void ForceValidColors()
    {
        if (balls.Count == 0 || state != GameState.Play) return;
        var pc = GetPairColors();
        if (pc.Count == 0) pc = balls.Select(b => b.ballColor).Distinct().ToList();
        if (pc.Count == 0) return;
        if (!pc.Contains(currentColor)) currentColor = pc[Random.Range(0, pc.Count)];
        if (!pc.Contains(nextColor)) nextColor = pc[Random.Range(0, pc.Count)];
    }

    // ===== SHOOTING =====
    public void OnBallFired()
    {
        ballsLeft--;
        currentColor = nextColor;
        nextColor = PickNextColor();
        ForceValidColors();
        ui.UpdateHUD(ballsLeft, score, balls.Count);
    }

    // ===== ROTATION =====
    public void StartRotation()
    {
        rotationTarget = fieldAngle + GameConstants.FieldRotation;
        isRotating = true;
        state = GameState.Rotating;
    }

    void UpdateRotation()
    {
        float diff = rotationTarget - fieldAngle;
        if (Mathf.Abs(diff) < 0.1f)
        {
            fieldAngle = rotationTarget;
            isRotating = false;
            ForceValidColors();
            state = GameState.Play;
            ui.UpdateHUD(ballsLeft, score, balls.Count);
            CheckEnd();
        }
        else
        {
            float step = diff * 0.1f;
            fieldAngle += step;
            // Rotate all balls around BH center
            Vector2 center = blackHole.position;
            float rad = step * Mathf.Deg2Rad;
            foreach (var b in balls)
            {
                Vector2 offset = (Vector2)b.transform.position - center;
                float a = Mathf.Atan2(offset.y, offset.x) + rad;
                float d = offset.magnitude;
                b.transform.position = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            }
            ForceValidColors();
        }
    }

    // ===== BLACK HOLE AUTO-ABSORB =====
    void BHAutoAbsorb()
    {
        if (state != GameState.Play || shooter.HasProjectile) return;

        Vector2 center = blackHole.position;
        var absIds = new HashSet<int>();
        foreach (var b in balls)
        {
            if (b.DistTo(center) < BHEventHorizon + GameConstants.BallRadius * 0.5f)
                absIds.Add(b.id);
        }
        if (absIds.Count == 0) return;

        // Expand to touching neighbors
        float pullRange = BHEventHorizon + GameConstants.BallRadius * 2.5f;
        foreach (var b in balls)
        {
            if (absIds.Contains(b.id)) continue;
            if (b.DistTo(center) > pullRange) continue;
            foreach (var nb in GetTouching(b))
            {
                if (absIds.Contains(nb.id)) { absIds.Add(b.id); break; }
            }
        }

        var absorbed = balls.Where(b => absIds.Contains(b.id)).ToList();
        RemoveBalls(absorbed);
        score += absorbed.Count * GameConstants.ScoreBHAbsorb;
        ForceValidColors();
        ui.UpdateHUD(ballsLeft, score, balls.Count);
        CheckEnd();
    }

    public void OnProjectileAbsorbedByBH()
    {
        bhAte++;
        comboCount = 0;
        StartRotation();
    }

    // ===== WIN/LOSE =====
    void CheckEnd()
    {
        if (state != GameState.Play && state != GameState.Rotating) return;
        if (balls.Count == 0)
        {
            state = GameState.Won;
            var lv = levels[currentLevel];
            int remBonus = ballsLeft * GameConstants.ScoreLeftover;
            score += remBonus;
            int stars = score >= lv.starScore3 ? 3 : score >= lv.starScore2 ? 2 : 1;
            levelStars[currentLevel] = Mathf.Max(levelStars[currentLevel], stars);
            ui.ShowWin(stars, score, ballsLeft);
        }
        else if (ballsLeft <= 0 && !shooter.HasProjectile && !isRotating)
        {
            state = GameState.Lost;
            ui.ShowLose(balls.Count);
        }
    }

    // ===== PUBLIC ACCESSORS =====
    public List<Ball> Balls => balls;
    public bool IsRotating => isRotating;
    public int[] LevelStars => levelStars;
    public void Restart() => LoadLevel(currentLevel);
    public void NextLevel() { if (currentLevel < levels.Count - 1) LoadLevel(currentLevel + 1); }
}
