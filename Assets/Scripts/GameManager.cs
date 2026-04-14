using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Central game orchestrator. Delegates to subsystems:
///   MatchSystem — match detection, elimination sequence, combo buddy
///   BlackHoleController — BH growth, visuals, auto-absorb
///   LevelManager — level loading, layout validation, win/lose
/// Retains: ball collection, color queue, field rotation, shooting callback.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scene References")]
    public GameObject ballPrefab;
    public Transform ballContainer;
    public Transform blackHole;
    public Shooter shooter;
    public UIManager ui;

    [Header("Game State")]
    public GameState state = GameState.Play;
    public int ballsLeft;
    public int score;
    public int comboCount;

    /// <summary>Game state machine. Highlight/Suck/Buddy block input during match sequence.</summary>
    public enum GameState { Play, Highlight, Suck, Buddy, Rotating, Won, Lost }

    // ===== SUBSYSTEMS =====
    private MatchSystem matchSystem;
    private BlackHoleController blackHoleController;
    private LevelManager levelManager;
    private FXPool fxPool;

    // ===== INTERNAL STATE =====
    private Camera cam;
    private List<Ball> balls = new List<Ball>();
    private int nextBallId = 0;

    // Ball object pool
    private Stack<GameObject> ballPool = new Stack<GameObject>();
    const int BallPoolPrewarm = 20;
    private float fieldAngle = 0f;
    private float rotationTarget = 0f;
    private bool isRotating = false;

    // ===== FORWARDED PROPERTIES =====
    public float BHRadius => blackHoleController.Radius;
    public float BHEventHorizon => blackHoleController.EventHorizon;
    public Color currentColor { get; private set; }
    public Color nextColor { get; private set; }

    // Cached camera bounds (set once in Start)
    public float CamHH { get; private set; } // half-height
    public float CamHW { get; private set; } // half-width

    void Awake()
    {
        Instance = this;

        // Create subsystems
        gameObject.AddComponent<AudioManager>();
        fxPool = gameObject.AddComponent<FXPool>();
        fxPool.Init();
        matchSystem = gameObject.AddComponent<MatchSystem>();
        matchSystem.Init(this, fxPool);
        blackHoleController = gameObject.AddComponent<BlackHoleController>();
        levelManager = gameObject.AddComponent<LevelManager>();
    }

    void Start()
    {
        // Find camera (Camera.main needs MainCamera tag which may not be set)
        cam = Camera.main;
        if (!cam) cam = FindFirstObjectByType<Camera>();
        if (!cam) { Debug.LogError("[GravityMatch] No camera found!"); return; }

        // Tag it so Camera.main works elsewhere (e.g. Shooter)
        cam.gameObject.tag = "MainCamera";

        // Adaptive camera: fix game width, adjust height for aspect ratio.
        // Design basis: 9:16 portrait (aspect=0.5625), game width=2.7 world units
        // On wider screens (16:9 landscape in editor), same width, less height.
        // On taller screens (20:9 phone), same width, more height.
        float designWidth = 2.7f; // game field width in world units
        float aspect = cam.aspect; // width / height
        CamHW = designWidth / 2f; // half-width fixed
        CamHH = CamHW / aspect;   // half-height adapts to aspect
        cam.orthographicSize = CamHH;
        cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
        cam.transform.position = new Vector3(0, 0, -10f);

        // Position BH and shooter relative to visible area.
        // BH at 30% from top (upper 70% = play field, lower 30% = shoot zone)
        float visibleHeight = CamHH * 2f;
        float bhY = CamHH - visibleHeight * 0.35f; // 35% from top
        blackHole.position = new Vector3(0, bhY, 0);

        // Shooter at bottom with small margin
        float shooterMargin = 0.3f * GameConstants.WorldScale;
        if (shooter)
        {
            shooter.transform.position = new Vector3(0, -CamHH + shooterMargin, 0);
        }

        Debug.Log($"[GravityMatch] Screen adapt: aspect={aspect:F3}, orthoSize={CamHH:F2}, width={designWidth}, bhY={bhY:F2}");

        // Pre-warm ball pool
        for (int i = 0; i < BallPoolPrewarm; i++)
        {
            var go = Instantiate(ballPrefab, Vector3.zero, Quaternion.identity, ballContainer);
            go.SetActive(false);
            ballPool.Push(go);
        }

        // Initialize subsystems
        blackHoleController.Init(this, blackHole);
        levelManager.Init(this, cam);

        levelManager.LoadLevel(0);
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

    /// <summary>Reset game state for a new level. Called by LevelManager.</summary>
    public void ResetForLevel(int budget)
    {
        // Return all active balls to pool
        foreach (var b in balls)
        {
            if (b != null) ReturnBallToPool(b.gameObject);
        }
        balls.Clear();
        nextBallId = 0;
        blackHoleController.ResetForLevel();
        score = 0;
        comboCount = 0;
        fieldAngle = 0f;
        rotationTarget = 0f;
        isRotating = false;
        ballsLeft = budget;
        state = GameState.Play;
    }

    /// <summary>Initialize color queue after level load. Called by LevelManager.</summary>
    public void InitColorQueue()
    {
        currentColor = PickNextColor();
        nextColor = PickNextColor();
    }

    // ===== BALL MANAGEMENT (pooled) =====
    public Ball SpawnBall(Vector2 pos, Color color)
    {
        GameObject go;
        if (ballPool.Count > 0)
        {
            go = ballPool.Pop();
            go.transform.SetParent(ballContainer);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);
        }
        else
        {
            go = Instantiate(ballPrefab, pos, Quaternion.identity, ballContainer);
        }
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
            ReturnBallToPool(b.gameObject);
        }
        blackHoleController.OnBallsEaten(list.Count);
    }

    /// <summary>Deactivate ball and return to pool for reuse.</summary>
    public void ReturnBallToPool(GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(ballContainer);
        ballPool.Push(go);
    }

    /// <summary>
    /// v21 suckBall: ghost sprite slides toward BH, shrinking + fading.
    /// Duration = SuckDuration * 1.5 so animation extends slightly past the wait.
    /// </summary>
    void SpawnSuckGhost(Ball b)
    {
        var go = fxPool.Get(FXPool.FXType.SuckGhost, b.GetComponent<SpriteRenderer>().sprite);
        go.transform.position = b.transform.position;
        var sr = go.GetComponent<SpriteRenderer>();
        sr.color = b.ballColor;
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
        fxPool.Return(go);
    }

    // ===== SUBSYSTEM FORWARDS (Shooter.cs facade) =====
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
        return new List<Color>(paired);
    }

    public Color PickNextColor()
    {
        if (balls.Count == 0) return levelManager.GetCurrentLevelDef().colors[0];
        var pc = GetPairColors();
        if (pc.Count == 0)
        {
            var colorSet = new HashSet<Color>();
            foreach (var b in balls) colorSet.Add(b.ballColor);
            pc = new List<Color>(colorSet);
        }
        if (pc.Count == 0) return levelManager.GetCurrentLevelDef().colors[0];
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
        float total = 0f;
        foreach (var x in weights) total += x.w;
        float r = Random.Range(0f, total);
        float acc = 0;
        foreach (var (c, w) in weights) { acc += w; if (r <= acc) return c; }
        return pc[0];
    }

    public void ForceValidColors()
    {
        if (balls.Count == 0) return;
        var pc = GetPairColors();
        if (pc.Count == 0)
        {
            var colorSet = new HashSet<Color>();
            foreach (var b in balls) colorSet.Add(b.ballColor);
            pc = new List<Color>(colorSet);
        }
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

    public void StartMatchSequence(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
        => matchSystem.StartMatchSequence(matchCount, targets, matchGrp, coneBaseAngle, coneAngle);
    public void OnProjectileAbsorbedByBH() => blackHoleController.OnProjectileAbsorbed();
    public void CheckEnd() => levelManager.CheckEnd();
    public void LoadLevel(int index) => levelManager.LoadLevel(index);
    public void Restart() => levelManager.Restart();
    public void NextLevel() => levelManager.NextLevel();

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

    // ===== PUBLIC ACCESSORS =====
    public List<Ball> Balls => balls;
    public bool IsRotating => isRotating;
    public int[] LevelStars => levelManager.LevelStars;
    public int LevelCount => levelManager.LevelCount;
    public int currentLevel => levelManager.CurrentLevel;
}
