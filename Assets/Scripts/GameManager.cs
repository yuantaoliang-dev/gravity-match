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

    // Cached camera (Camera.main requires MainCamera tag)
    private Camera cam;
    private SpriteRenderer bhRingSr;

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
    // Highlight, Suck, Buddy: used during post-match sequence (blocks input)

    void Awake()
    {
        Instance = this;
        levels = LevelDataBuilder.BuildAll();
        levelStars = new int[levels.Count];
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

        // Setup BlackHole visual
        var bhSr = blackHole.GetComponent<SpriteRenderer>();
        if (bhSr)
        {
            bhSr.color = new Color(0.06f, 0.06f, 0.10f, 1f);
            bhSr.sortingOrder = -10;
            var mat = GameConstants.CreateUnlitSpriteMaterial();
            if (mat != null) bhSr.material = mat;

            // Purple ring: child circle scaled slightly larger behind the dark center
            var ringGo = new GameObject("BHRing");
            ringGo.transform.SetParent(blackHole, false);
            ringGo.transform.localScale = new Vector3(1.35f, 1.35f, 1f);
            bhRingSr = ringGo.AddComponent<SpriteRenderer>();
            bhRingSr.sprite = bhSr.sprite;  // same Circle sprite
            bhRingSr.color = new Color(0.55f, 0.15f, 0.85f, 0.5f);
            bhRingSr.sortingOrder = -11; // behind center
            if (mat != null) bhRingSr.material = new Material(mat);
        }

        LoadLevel(0);
    }

    void Update()
    {
        // Update BlackHole visual size + ring pulse
        float bhDiam = BHRadius * 2f;
        blackHole.localScale = new Vector3(bhDiam, bhDiam, 1f);
        if (bhRingSr)
        {
            float pulse = 0.4f + 0.2f * Mathf.Sin(Time.time * 2.5f);
            bhRingSr.color = new Color(0.55f, 0.15f, 0.85f, pulse);
        }

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
        bhAte += list.Count;
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

    // ===== MATCH RESOLUTION =====
    /// <summary>
    /// Full match sequence matching HTML v21 phases:
    /// 1. Highlight (0.37s) — matched balls pulse white, player sees what matched
    /// 2. Remove + Suck (0.53s) — balls disappear, pause before rotation
    /// 3. Combo check → rotation
    /// </summary>
    public void StartMatchSequence(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
    {
        int pts = matchCount >= 5 ? GameConstants.Score5Match :
                  matchCount == 4 ? GameConstants.Score4Match : GameConstants.Score3Match;
        score += targets.Count * pts;
        ui.UpdateHUD(ballsLeft, score, balls.Count);

        Debug.Log($"[GravityMatch] StartMatchSequence: {matchCount}-match, targets={targets.Count}");

        // Show reward text for 4/5-match, color = matched ball color brightened
        if (matchCount >= 4 && matchGrp.Count > 0)
        {
            Color bc = matchGrp[0].ballColor;
            Color bright = new Color(
                Mathf.Min(1f, bc.r * 1.4f + 0.2f),
                Mathf.Min(1f, bc.g * 1.4f + 0.2f),
                Mathf.Min(1f, bc.b * 1.4f + 0.2f));
            string msg = matchCount >= 5 ? "5+ MATCH!" : "4 MATCH!";
            ui.ShowReward(msg, bright);
        }

        state = GameState.Highlight;
        StartCoroutine(MatchSequenceCoroutine(matchCount, targets, matchGrp, coneBaseAngle, coneAngle));
    }

    IEnumerator MatchSequenceCoroutine(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
    {
        var matchIds = new HashSet<int>(matchGrp.Select(b => b.id));
        bool hasCone = matchCount >= 4 && coneAngle > 0;

        // Create highlight rings for each target ball
        var rings = new List<GameObject>();
        foreach (var b in targets)
        {
            if (b == null) continue;
            var ring = new GameObject("HighlightRing");
            ring.transform.SetParent(b.transform, false);
            ring.transform.localPosition = Vector3.zero;
            var sr = ring.AddComponent<SpriteRenderer>();
            sr.sprite = b.GetComponent<SpriteRenderer>().sprite;
            sr.sortingOrder = 15;
            var mat = GameConstants.CreateUnlitSpriteMaterial();
            if (mat != null) sr.material = mat;
            rings.Add(ring);
        }

        // Create cone FX mesh if 4/5-match
        GameObject coneFxGo = null;
        if (hasCone)
        {
            coneFxGo = CreateConeFX(coneBaseAngle, coneAngle);
        }

        // Phase 1: Highlight — v21 style pulsing rings (22 frames ≈ 0.37s)
        float highlightEnd = Time.time + GameConstants.HighlightDuration;
        while (Time.time < highlightEnd)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            // Subtle ring just outside the ball edge
            float ringSize = (GameConstants.BallRadius + (0.015f + pulse * 0.01f) * GameConstants.WorldScale) * 2f;

            for (int i = 0; i < rings.Count && i < targets.Count; i++)
            {
                if (targets[i] == null || rings[i] == null) continue;
                bool isMatch = matchIds.Contains(targets[i].id);
                rings[i].transform.localScale = new Vector3(
                    ringSize / targets[i].transform.localScale.x,
                    ringSize / targets[i].transform.localScale.y, 1f);
                var sr = rings[i].GetComponent<SpriteRenderer>();
                // Use the ball's own color for the ring (brighter version)
                Color ballCol = targets[i].ballColor;
                float brightFactor = isMatch ? 1.3f : 1.1f;
                Color ringCol = new Color(
                    Mathf.Min(1f, ballCol.r * brightFactor),
                    Mathf.Min(1f, ballCol.g * brightFactor),
                    Mathf.Min(1f, ballCol.b * brightFactor),
                    (isMatch ? 0.2f : 0.15f) + pulse * 0.25f
                );
                sr.color = ringCol;
            }

            // Fade cone FX (update vertex alpha for mesh)
            if (coneFxGo != null)
            {
                float cp = (highlightEnd - Time.time) / GameConstants.HighlightDuration;
                var mf = coneFxGo.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    var cols = mf.mesh.colors32;
                    // Center vertex (index 0): bright
                    cols[0] = new Color32(255, 255, 255, (byte)(cp * 90));
                    // Mid ring (index 1..arcSegs+1): medium
                    // Outer ring (rest): zero
                    for (int ci = 1; ci < cols.Length; ci++)
                        cols[ci] = new Color32(255, 255, 255, (byte)(cols[ci].a > 0 ? cp * cols[ci].a : 0));
                    mf.mesh.colors32 = cols;
                }
            }

            yield return null;
        }

        // Cleanup highlight FX
        foreach (var ring in rings) if (ring != null) Destroy(ring);
        if (coneFxGo != null) Destroy(coneFxGo);

        // Phase 2: Remove balls + suck pause
        state = GameState.Suck;
        RemoveBalls(targets);
        ForceValidColors();
        ui.UpdateHUD(ballsLeft, score, balls.Count);

        yield return new WaitForSeconds(GameConstants.SuckDuration);

        // Phase 3: Combo check → rotation
        bool hasSingle = balls.Any(b =>
            !GetTouching(b, GameConstants.MatchTouchDist).Any(nb => nb.ballColor == b.ballColor));
        if (hasSingle)
        {
            comboCount++;
            if (comboCount >= 3)
            {
                comboCount = 0;
                ComboBonus();
                // v21: showRw('3× COMBO! +300', '#FFE66D')
                ui.ShowReward("3x COMBO! +300", new Color(1f, 0.9f, 0.43f));
                // Wait for buddy FX to show
                state = GameState.Buddy;
                yield return new WaitForSeconds(GameConstants.BuddyFXDuration);
            }
        }
        else
        {
            comboCount = 0;
        }

        ForceValidColors();
        ui.UpdateHUD(ballsLeft, score, balls.Count);
        StartRotation();
    }

    /// <summary>
    /// v21 flashlight cone: filled wedge mesh with radial gradient.
    /// Center bright, 30% radius medium, outer edge transparent.
    /// </summary>
    GameObject CreateConeFX(float baseAngle, float halfAngle)
    {
        Vector2 bhPos = blackHole.position;
        float outerR = cam.orthographicSize * 2f;
        float midR = outerR * 0.3f; // v21: gradient stop at 30%
        int arcSegs = 16;

        var go = new GameObject("ConeFX");
        go.transform.position = new Vector3(bhPos.x, bhPos.y, 0);

        // Mesh: center(1) + mid ring(arcSegs+1) + outer ring(arcSegs+1)
        int midStart = 1;
        int outerStart = midStart + arcSegs + 1;
        int vertCount = 1 + (arcSegs + 1) * 2;
        var verts = new Vector3[vertCount];
        var colors = new Color32[vertCount];

        // Center vertex: bright white
        verts[0] = Vector3.zero;
        colors[0] = new Color32(255, 255, 255, 90); // v21: p*0.35 ≈ 90/255

        for (int i = 0; i <= arcSegs; i++)
        {
            float a = baseAngle - halfAngle + (2f * halfAngle * i / arcSegs);
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);

            // Mid ring: medium brightness
            verts[midStart + i] = new Vector3(cos * midR, sin * midR, 0);
            colors[midStart + i] = new Color32(255, 255, 255, 38); // v21: p*0.15 ≈ 38/255

            // Outer ring: transparent
            verts[outerStart + i] = new Vector3(cos * outerR, sin * outerR, 0);
            colors[outerStart + i] = new Color32(255, 255, 255, 0);
        }

        // Triangles: center→mid fan, then mid→outer strip
        var tris = new List<int>();
        for (int i = 0; i < arcSegs; i++)
        {
            // Center to mid ring fan
            tris.Add(0);
            tris.Add(midStart + i);
            tris.Add(midStart + i + 1);

            // Mid to outer ring strip (two triangles per segment)
            tris.Add(midStart + i);
            tris.Add(outerStart + i);
            tris.Add(outerStart + i + 1);

            tris.Add(midStart + i);
            tris.Add(outerStart + i + 1);
            tris.Add(midStart + i + 1);
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris.ToArray();
        mesh.colors32 = colors;

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) mr.material = mat;
        mr.sortingOrder = 1; // behind balls

        return go;
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

    // ===== COMBO BUDDY =====
    /// <summary>
    /// 3x combo bonus: find a lonely ball and attach a same-color buddy next to it.
    /// Matches v21 comboBonus(): try 12 angles around the singleton, pick the
    /// position furthest from BH that doesn't overlap other balls.
    /// </summary>
    void ComboBonus()
    {
        // Find singletons (no same-color touching neighbor)
        var singles = new List<Ball>();
        foreach (var b in balls)
        {
            bool hasMatch = GetTouching(b, GameConstants.MatchTouchDist)
                .Any(t => t.ballColor == b.ballColor);
            if (!hasMatch) singles.Add(b);
        }
        if (singles.Count == 0) return;

        var target = singles[Random.Range(0, singles.Count)];
        Vector2 tPos = target.transform.position;
        Vector2 bhPos = blackHole.position;
        float od = GameConstants.OverlapDistance;
        float hh = cam.orthographicSize;
        float hw = hh * cam.aspect;
        float r = GameConstants.BallRadius;

        // Try 12 angles, pick position furthest from BH
        Vector2 bestPos = tPos + Vector2.right * od;
        float bestScore = -1f;

        for (int ai = 0; ai < 12; ai++)
        {
            float ang = ai * Mathf.PI * 2f / 12f;
            Vector2 tryPos = tPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * od;

            // Must be in camera bounds
            if (tryPos.x < -hw + r || tryPos.x > hw - r) continue;
            if (tryPos.y < -hh + r || tryPos.y > hh - r) continue;

            // Must not overlap BH
            if (Vector2.Distance(tryPos, bhPos) < BHEventHorizon + r) continue;

            // Must not overlap other balls
            bool ok = true;
            foreach (var b2 in balls)
            {
                if (b2.id == target.id) continue;
                if (Vector2.Distance(tryPos, b2.transform.position) < od - 0.01f * GameConstants.WorldScale)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            // Prefer positions further from BH (safer)
            float sc = Vector2.Distance(tryPos, bhPos);
            if (sc > bestScore)
            {
                bestScore = sc;
                bestPos = tryPos;
            }
        }

        if (bestScore > 0)
        {
            var buddy = SpawnBall(bestPos, target.ballColor);

            // Visual FX: glow rings on both target and buddy
            StartCoroutine(BuddyFX(target, buddy));

            score += GameConstants.ScoreComboBonus;
            ui.UpdateHUD(ballsLeft, score, balls.Count);
            Debug.Log($"[GravityMatch] 3x COMBO! Buddy spawned for {GameConstants.ColorToHex(target.ballColor)}");
        }
    }

    IEnumerator BuddyFX(Ball target, Ball buddy)
    {
        // Create glow rings on both balls
        var rings = new List<GameObject>();
        foreach (var b in new[] { target, buddy })
        {
            if (b == null) continue;
            var ring = new GameObject("BuddyRing");
            ring.transform.SetParent(b.transform, false);
            ring.transform.localPosition = Vector3.zero;
            var sr = ring.AddComponent<SpriteRenderer>();
            sr.sprite = b.GetComponent<SpriteRenderer>().sprite;
            sr.sortingOrder = 15;
            var mat = GameConstants.CreateUnlitSpriteMaterial();
            if (mat != null) sr.material = mat;
            rings.Add(ring);
        }

        // Animate: expanding gold ring
        float duration = GameConstants.BuddyFXDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float ringScale = (1.1f + t * 0.4f); // expand outward
            float alpha = (1f - t) * 0.5f; // fade out
            foreach (var ring in rings)
            {
                if (ring == null) continue;
                ring.transform.localScale = new Vector3(ringScale, ringScale, 1f);
                ring.GetComponent<SpriteRenderer>().color =
                    new Color(1f, 0.9f, 0.43f, alpha); // gold (#FFE66D)
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (var ring in rings) if (ring != null) Destroy(ring);
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
    // v21: only checks during 'play' state, not during rotation or pending match
    void CheckEnd()
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
