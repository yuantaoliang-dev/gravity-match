using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    // ===== ZERO-ALLOC BUFFERS =====
    // ForceValidColors runs every frame during Play — eliminating its allocations is the
    // single biggest GC win in the project. Each buffer has a dedicated owner so there's
    // no reentry corruption between PickNextColor, ForceValidColors, FillPairColorsNonAlloc.
    private readonly HashSet<int>   _pairVisited       = new HashSet<int>();
    private readonly List<Ball>     _pairGroupScratch  = new List<Ball>(64);
    private readonly HashSet<Color> _colorSet          = new HashSet<Color>();
    private readonly List<Color>    _pickPcBuf         = new List<Color>(4);
    private readonly List<Color>    _forcePcBuf        = new List<Color>(4);
    private readonly List<(Color c, float w)> _weights = new List<(Color, float)>(4);
    private readonly List<Ball>     _pickTouchScratch  = new List<Ball>(64);

    // Camera pan during aim (to reveal balls near edges)
    private float camTargetX = 0f;
    private const float CamPanLerpSpeed = 8f;
    public void SetCameraPanTarget(float x) { camTargetX = x; }

    // Vertical play-area bounds (balls outside these Y values are hidden)
    public float PlayTopY { get; private set; }
    public float PlayBottomY { get; private set; }

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
        // Don't use immersive mode — we WANT system bars to take their space
        // so Screen.safeArea properly excludes them and our content stays visible.

        // Show splash screen overlay
        SplashScreen.ShowOnCurrentScene();

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

        // Reserve large bottom margin for Android navigation bar (gesture/3-button).
        // Use both Screen.safeArea AND a minimum fixed margin as a safety net,
        // because safeArea can return 0 inset on some devices/modes.
        float visibleHeight = CamHH * 2f;
        Rect safeArea = Screen.safeArea;
        float screenH = Screen.height;
        float topInsetRatio = Mathf.Max((screenH - safeArea.yMax) / screenH, 0.04f);    // min 4% top
        float bottomInsetRatio = Mathf.Max(safeArea.yMin / screenH, 0.08f);              // min 8% bottom (nav bar)
        float topInset = topInsetRatio * visibleHeight;
        float bottomInset = bottomInsetRatio * visibleHeight;

        float safeTop = CamHH - topInset;
        float safeBottom = -CamHH + bottomInset;
        float safeHeight = safeTop - safeBottom;

        // BH at 35% from safe top
        float bhY = safeTop - safeHeight * 0.35f;
        blackHole.position = new Vector3(0, bhY, 0);

        // Shooter at safe bottom with margin
        float shooterMargin = 0.3f * GameConstants.WorldScale;
        if (shooter)
        {
            shooter.transform.position = new Vector3(0, safeBottom + shooterMargin, 0);
        }

        // Vertical play-area bounds: exclude HUD area on top and shooter area on bottom.
        float hudHeight = 0.35f * GameConstants.WorldScale;
        float shooterHeight = GameConstants.BallRadius * 3.5f;
        PlayTopY = safeTop - hudHeight;
        PlayBottomY = shooter ? shooter.transform.position.y + shooterHeight : safeBottom + shooterHeight;

        // Create dark mask strips to clip balls visually at top and bottom.
        // Balls remain logical/active, just covered visually in these zones.
        CreateEdgeMask(true);
        CreateEdgeMask(false);

        Debug.Log($"[GravityMatch] SafeArea: topInset={topInset:F3}, bottomInset={bottomInset:F3}, bhY={bhY:F2}, shooterY={safeBottom + shooterMargin:F2}, playTop={PlayTopY:F2}, playBottom={PlayBottomY:F2}");

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

        // Smooth camera pan toward current target X
        if (cam != null)
        {
            Vector3 cp = cam.transform.position;
            cp.x = Mathf.Lerp(cp.x, camTargetX, Time.deltaTime * CamPanLerpSpeed);
            cam.transform.position = cp;
        }

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


    /// <summary>
    /// Create a full-width dark mask strip that clips balls visually in the
    /// HUD (top) or shooter (bottom) zones. Balls entering these zones appear
    /// sliced by the edge. Parented to camera so it pans horizontally with it.
    /// </summary>
    void CreateEdgeMask(bool isTop)
    {
        var go = new GameObject(isTop ? "TopEdgeMask" : "BottomEdgeMask");
        // Parent to camera so it pans horizontally along with camera
        go.transform.SetParent(cam.transform, false);

        // Runtime 1x1 white sprite, pixelsPerUnit=1 so sprite is 1x1 world unit.
        // Scale below then controls final world-space size directly.
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        var maskSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = maskSprite;
        sr.color = new Color(0.04f, 0.05f, 0.08f, 1f); // background color
        sr.sortingOrder = 8; // above balls (2) and breathing glow (3), below shooter visuals (12+)
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) sr.material = mat;

        // Width: wider than camera (cover beyond pan range)
        float width = CamHW * 3f;
        // Height: from screen edge to play bound
        float worldTop = CamHH;
        float worldBottom = -CamHH;
        float top = isTop ? worldTop : PlayBottomY;
        float bottom = isTop ? PlayTopY : worldBottom;
        float height = top - bottom;
        float centerY = (top + bottom) * 0.5f;

        // Use localPosition since parented to camera (camera is at world origin center)
        go.transform.localPosition = new Vector3(0, centerY, 1f); // +1 Z so in front of balls
        go.transform.localScale = new Vector3(width, height, 1f);
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
        go.transform.position = b.cachedPos;
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
    public void GetTouchingNonAlloc(Ball b, float maxDist, List<Ball> results)
        => matchSystem.GetTouchingNonAlloc(b, maxDist, results);
    public void FindGroupNonAlloc(Ball start, float maxDist, List<Ball> results)
        => matchSystem.FindGroupNonAlloc(start, maxDist, results);

    // ===== COLOR SELECTION =====

    /// <summary>
    /// Zero-alloc: fills <paramref name="into"/> with one color per connected group of size >= 2.
    /// Uses class-level buffers; not reentrancy-safe. Synchronous only.
    /// </summary>
    void FillPairColorsNonAlloc(List<Color> into)
    {
        into.Clear();
        _pairVisited.Clear();
        foreach (var b in balls)
        {
            if (_pairVisited.Contains(b.id)) continue;
            matchSystem.FindGroupNonAlloc(b, GameConstants.MatchTouchDist, _pairGroupScratch);
            for (int i = 0; i < _pairGroupScratch.Count; i++) _pairVisited.Add(_pairGroupScratch[i].id);
            if (_pairGroupScratch.Count >= 2 && !into.Contains(b.ballColor))
                into.Add(b.ballColor);
        }
    }

    /// <summary>Allocating convenience wrapper. Prefer FillPairColorsNonAlloc on hot paths.</summary>
    public List<Color> GetPairColors()
    {
        var list = new List<Color>(4);
        FillPairColorsNonAlloc(list);
        return list;
    }

    public Color PickNextColor()
    {
        if (balls.Count == 0) return levelManager.GetCurrentLevelDef().colors[0];
        FillPairColorsNonAlloc(_pickPcBuf);
        if (_pickPcBuf.Count == 0)
        {
            // Fallback: all unique colors currently on field
            _colorSet.Clear();
            foreach (var b in balls) _colorSet.Add(b.ballColor);
            _pickPcBuf.Clear();
            foreach (var c in _colorSet) _pickPcBuf.Add(c);
        }
        if (_pickPcBuf.Count == 0) return levelManager.GetCurrentLevelDef().colors[0];
        if (_pickPcBuf.Count == 1) return _pickPcBuf[0];

        // Weighted random — prefer colors that already have a matched neighbour on field
        _weights.Clear();
        for (int ci = 0; ci < _pickPcBuf.Count; ci++)
        {
            Color c = _pickPcBuf[ci];
            float w = 0;
            foreach (var b in balls)
            {
                if (b.ballColor != c) continue;
                // Replaces `GetTouching(b).Any(t => t.ballColor == c)`
                matchSystem.GetTouchingNonAlloc(b, GameConstants.MatchTouchDist, _pickTouchScratch);
                bool hasPair = false;
                for (int i = 0; i < _pickTouchScratch.Count; i++)
                {
                    if (_pickTouchScratch[i].ballColor == c) { hasPair = true; break; }
                }
                w += hasPair ? 4f : 1f;
            }
            _weights.Add((c, Mathf.Max(w, 1f)));
        }
        float total = 0f;
        for (int i = 0; i < _weights.Count; i++) total += _weights[i].w;
        float r = Random.Range(0f, total);
        float acc = 0;
        for (int i = 0; i < _weights.Count; i++)
        {
            acc += _weights[i].w;
            if (r <= acc) return _weights[i].c;
        }
        return _pickPcBuf[0];
    }

    public void ForceValidColors()
    {
        if (balls.Count == 0) return;
        FillPairColorsNonAlloc(_forcePcBuf);
        if (_forcePcBuf.Count == 0)
        {
            _colorSet.Clear();
            foreach (var b in balls) _colorSet.Add(b.ballColor);
            _forcePcBuf.Clear();
            foreach (var c in _colorSet) _forcePcBuf.Add(c);
        }
        if (_forcePcBuf.Count == 0) return;
        if (!_forcePcBuf.Contains(currentColor)) currentColor = _forcePcBuf[Random.Range(0, _forcePcBuf.Count)];
        if (!_forcePcBuf.Contains(nextColor)) nextColor = _forcePcBuf[Random.Range(0, _forcePcBuf.Count)];
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

    /// <summary>
    /// Instantly finish the current rotation (snap to target angle) and
    /// transition to Play state. Used when player commits to a shot during
    /// rotation — prevents the "stutter" of releasing as animation ends.
    /// </summary>
    public void SnapRotationAndFire()
    {
        if (!isRotating) return;

        float remainingDiff = rotationTarget - fieldAngle;
        Vector2 center = blackHole.position;
        float rad = remainingDiff * Mathf.Deg2Rad;
        foreach (var b in balls)
        {
            // Read from cachedPos (avoids transform.position C++ boundary crossing)
            Vector2 offset = b.cachedPos - center;
            float a = Mathf.Atan2(offset.y, offset.x) + rad;
            float d = offset.magnitude;
            Vector2 newPos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            b.SetPos(newPos);
        }
        fieldAngle = rotationTarget;
        isRotating = false;
        state = GameState.Play;
        ForceValidColors();
        ui.UpdateHUD(ballsLeft, score, balls.Count);
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

        // Rotate all balls around BH center (read + write via cachedPos / SetPos)
        Vector2 center = blackHole.position;
        float rad = step * Mathf.Deg2Rad;
        foreach (var b in balls)
        {
            Vector2 offset = b.cachedPos - center;
            float a = Mathf.Atan2(offset.y, offset.x) + rad;
            float d = offset.magnitude;
            Vector2 newPos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            b.SetPos(newPos);
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
