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

    // Subsystems
    private MatchSystem matchSystem;
    private BlackHoleController blackHoleController;

    // Cached camera (Camera.main requires MainCamera tag)
    private Camera cam;

    // Ball tracking
    private List<Ball> balls = new List<Ball>();
    private int nextBallId = 0;

    // Black hole (forwarded to BlackHoleController)
    public float BHRadius => blackHoleController.Radius;
    public float BHEventHorizon => blackHoleController.EventHorizon;

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
    // Highlight, Suck, Buddy: used during post-match sequence (blocks input)

    void Awake()
    {
        Instance = this;
        levels = LevelDataBuilder.BuildAll();
        levelStars = new int[levels.Count];

        // Create subsystems
        matchSystem = gameObject.AddComponent<MatchSystem>();
        matchSystem.Init(this);
        blackHoleController = gameObject.AddComponent<BlackHoleController>();
    }

    void Start()
    {
        // Find camera (Camera.main needs MainCamera tag which may not be set)
        cam = Camera.main;
        if (!cam) cam = FindFirstObjectByType<Camera>();
        if (!cam) { Debug.LogError("[GravityMatch] No camera found!"); return; }

        // Tag it so Camera.main works elsewhere (e.g. Shooter)
        cam.gameObject.tag = "MainCamera";

        // Set camera to fit the game field
        // HTML: 480px height, WorldScale=1 → 480/100*1=4.8 full height → orthoSize=2.4
        cam.orthographicSize = 2.4f;
        cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
        // Camera must be behind the sprites (z=-10) for near clip plane to work
        cam.transform.position = new Vector3(0, 0, -10f);

        // HTML: BY=H/2-20=220, center=240, BH 20px above → 20/100*1=0.2 units
        blackHole.position = new Vector3(0, 0.2f, 0);

        // HTML: SY=H-36=444, BY=220, offset=224px → 224/100*1=2.24 below BH
        if (shooter)
        {
            shooter.transform.position = new Vector3(0, 0.2f - 2.24f, 0);
        }

        // Initialize BlackHole controller (handles visuals + growth)
        blackHoleController.Init(this, blackHole);

        LoadLevel(0);
    }

    void Update()
    {
        blackHoleController.UpdateVisuals();

        if (state == GameState.Play)
        {
            ForceValidColors();
            blackHoleController.AutoAbsorb();
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
        blackHoleController.ResetForLevel();
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

        // Push apart different-color balls so they don't visually overlap
        PushApartDifferentColors();

        // Re-normalize same-color pairs to consistent OverlapDistance
        // (PushApart may have shifted pair members)
        RestorePairDistances();

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
                        float push = minVis - d + 0.01f * GameConstants.WorldScale;
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

    void RestorePairDistances()
    {
        // Only fix groups of exactly 2 same-color balls (true pairs).
        // Adjust to OverlapDistance along their connecting line, midpoint fixed.
        float od = GameConstants.OverlapDistance;
        var visited = new HashSet<int>();

        foreach (var b in balls)
        {
            if (visited.Contains(b.id)) continue;
            var grp = FindGroup(b, GameConstants.MatchTouchDist);
            foreach (var g in grp) visited.Add(g.id);
            if (grp.Count != 2) continue;

            Vector2 p0 = grp[0].transform.position;
            Vector2 p1 = grp[1].transform.position;
            Vector2 mid = (p0 + p1) * 0.5f;
            Vector2 dir = (p1 - p0).normalized;
            grp[0].transform.position = (Vector3)(mid - dir * od * 0.5f);
            grp[1].transform.position = (Vector3)(mid + dir * od * 0.5f);
        }
    }

    void ClampBallPosition(Ball b)
    {
        Vector2 pos = b.transform.position;
        float r = GameConstants.BallRadius;

        // Clamp to camera bounds (v21: WALL_L+BR to WALL_R-BR)
        float hh = cam.orthographicSize;
        float hw = hh * cam.aspect;
        pos.x = Mathf.Clamp(pos.x, -hw + r, hw - r);
        pos.y = Mathf.Clamp(pos.y, -hh + r, hh - r);

        // Keep away from BH
        Vector2 center = blackHole.position;
        float dist = Vector2.Distance(pos, center);
        if (dist < BHEventHorizon + r + 0.02f * GameConstants.WorldScale)
        {
            Vector2 dir = (pos - center).normalized;
            pos = center + dir * (BHEventHorizon + r + 0.03f * GameConstants.WorldScale);
        }

        b.transform.position = pos;
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
            balls.Remove(b);
            SpawnSuckGhost(b); // v21 suck FX: ghost slides toward BH
            b.gameObject.SetActive(false);
            Destroy(b.gameObject);
        }
        blackHoleController.OnBallsEaten(list.Count);
    }

    /// <summary>
    /// v21 suckBall: ghost sprite slides toward BH, shrinking + fading.
    /// Duration = SuckDuration * 1.5 so animation extends slightly past the wait.
    /// </summary>
    void SpawnSuckGhost(Ball b)
    {
        var go = new GameObject("SuckGhost");
        go.transform.position = b.transform.position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = b.GetComponent<SpriteRenderer>().sprite;
        sr.color = b.ballColor;
        sr.sortingOrder = 5;
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) sr.material = mat;
        float diam = GameConstants.BallRadius * 2f;
        go.transform.localScale = new Vector3(diam, diam, 1f);
        StartCoroutine(AnimateSuckGhost(go, sr, b.ballColor));
    }

    IEnumerator AnimateSuckGhost(GameObject go, SpriteRenderer sr, Color color)
    {
        Vector2 bhPos = blackHole.position;
        float duration = GameConstants.SuckDuration * 1.5f;
        float br = GameConstants.BallRadius;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (go == null) yield break;
            float p = 1f - elapsed / duration; // 1 → 0

            // Slide toward BH
            Vector2 pos = go.transform.position;
            pos += (bhPos - pos) * 0.04f;
            go.transform.position = (Vector3)pos;

            // Shrink: v21 radius = BR * (p*0.7 + 0.3)
            float scale = (p * 0.7f + 0.3f) * br * 2f;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            // Fade out
            sr.color = new Color(color.r, color.g, color.b, p);

            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(go);
    }

    // ===== MATCHING =====
    // Forwarded to MatchSystem (Shooter.cs uses these via gm.FindGroup/GetTouching)
    public List<Ball> GetTouching(Ball b, float maxDist = -1) => matchSystem.GetTouching(b, maxDist);
    public List<Ball> FindGroup(Ball start, float maxDist = -1) => matchSystem.FindGroup(start, maxDist);

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
        if (balls.Count == 0) return;
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

    // Match resolution forwarded to MatchSystem
    public void StartMatchSequence(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
        => matchSystem.StartMatchSequence(matchCount, targets, matchGrp, coneBaseAngle, coneAngle);

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
        // Exponential ease: 10% of remaining per tick at 60fps, frame-rate independent
        float t = 1f - Mathf.Pow(0.9f, Time.deltaTime * 60f);
        float step = diff * t;

        if (Mathf.Abs(diff) < GameConstants.RotationEaseThreshold)
        {
            // Snap to target
            step = diff;
            fieldAngle = rotationTarget;
            isRotating = false;
        }
        else
        {
            fieldAngle += step;
        }

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

        if (!isRotating)
        {
            state = GameState.Play;
            ui.UpdateHUD(ballsLeft, score, balls.Count);
            CheckEnd();
        }
    }

    // Black hole absorption forwarded to BlackHoleController
    public void OnProjectileAbsorbedByBH() => blackHoleController.OnProjectileAbsorbed();

    // ===== WIN/LOSE =====
    // v21: only checks during 'play' state, not during rotation or pending match
    public void CheckEnd()
    {
        if (state != GameState.Play) return;
        if (balls.Count == 0)
        {
            state = GameState.Won;
            var lv = levels[currentLevel];
            int remBonus = ballsLeft * GameConstants.ScoreLeftover;
            score += remBonus;
            int stars = score >= lv.starScore3 ? 3 : score >= lv.starScore2 ? 2 : 1;
            levelStars[currentLevel] = Mathf.Max(levelStars[currentLevel], stars);
            bool hasNext = currentLevel < levels.Count - 1;
            ui.ShowWin(stars, score, ballsLeft, hasNext);
        }
        else if (ballsLeft <= 0 && !shooter.HasProjectile)
        {
            state = GameState.Lost;
            ui.ShowLose(balls.Count);
        }
    }

    // ===== PUBLIC ACCESSORS =====
    public List<Ball> Balls => balls;
    public bool IsRotating => isRotating;
    public int[] LevelStars => levelStars;
    public int LevelCount => levels.Count;
    public void Restart() => LoadLevel(currentLevel);
    public void NextLevel() { if (currentLevel < levels.Count - 1) LoadLevel(currentLevel + 1); }
}
