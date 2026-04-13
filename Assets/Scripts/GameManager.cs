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

    [Header("State")]
    public GameState state = GameState.Play;
    public int ballsLeft;
    public int score;
    public int comboCount;

    // Subsystems
    private MatchSystem matchSystem;
    private BlackHoleController blackHoleController;
    private LevelManager levelManager;

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


    public enum GameState { Play, Highlight, Suck, Buddy, Rotating, Won, Lost }
    // Highlight, Suck, Buddy: used during post-match sequence (blocks input)

    void Awake()
    {
        Instance = this;

        // Create subsystems
        matchSystem = gameObject.AddComponent<MatchSystem>();
        matchSystem.Init(this);
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

    // Level management forwarded to LevelManager
    public void LoadLevel(int index) => levelManager.LoadLevel(index);

    /// <summary>Reset game state for a new level. Called by LevelManager.</summary>
    public void ResetForLevel(int budget)
    {
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
        if (balls.Count == 0) return levelManager.GetCurrentLevelDef().colors[0];
        var pc = GetPairColors();
        if (pc.Count == 0) pc = balls.Select(b => b.ballColor).Distinct().ToList();
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

    // Win/lose check forwarded to LevelManager
    public void CheckEnd() => levelManager.CheckEnd();

    // ===== PUBLIC ACCESSORS =====
    public List<Ball> Balls => balls;
    public bool IsRotating => isRotating;
    public int[] LevelStars => levelManager.LevelStars;
    public int LevelCount => levelManager.LevelCount;
    public int currentLevel => levelManager.CurrentLevel;
    public void Restart() => levelManager.Restart();
    public void NextLevel() => levelManager.NextLevel();
}
