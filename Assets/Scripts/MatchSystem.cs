using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles match detection (BFS group finding), match sequence animation
/// (highlight → suck → combo), and cone sweep FX.
/// Extracted from GameManager to follow single-responsibility principle.
/// </summary>
public class MatchSystem : MonoBehaviour
{
    private GameManager gm;
    private FXPool fxPool;

    // ===== ZERO-ALLOC BUFFERS =====
    // DO NOT expose. Each buffer has a documented single-owner call path to
    // prevent reentry corruption. They are class-scoped so allocations happen
    // exactly once per MatchSystem lifetime, eliminating per-match GC spikes.
    private readonly List<Ball>       _findGroupScratch = new List<Ball>(64);      // inner GetTouching buffer used by FindGroupNonAlloc
    private readonly HashSet<int>     _findGroupVisited = new HashSet<int>();
    private readonly Queue<Ball>      _findGroupQueue   = new Queue<Ball>(128);
    private readonly HashSet<int>     _matchIds         = new HashSet<int>();      // matched ball ids during match sequence
    private readonly List<GameObject> _highlightRings   = new List<GameObject>(64);
    private readonly List<Ball>       _comboSingles     = new List<Ball>(64);
    private readonly List<Ball>       _comboScratch     = new List<Ball>(64);      // GetTouching buffer used in combo / ForceValidColors check
    private readonly List<GameObject> _buddyRings       = new List<GameObject>(4);

    public void Init(GameManager gm, FXPool fxPool)
    {
        this.gm = gm;
        this.fxPool = fxPool;
    }

    // ===== MATCH DETECTION =====

    /// <summary>
    /// Zero-alloc variant: fills <paramref name="results"/> (clears first) with all balls
    /// within maxDist of <paramref name="b"/>. Caller supplies a reusable buffer.
    /// </summary>
    public void GetTouchingNonAlloc(Ball b, float maxDist, List<Ball> results)
    {
        results.Clear();
        if (maxDist < 0) maxDist = GameConstants.TouchDist;
        float maxDistSq = maxDist * maxDist;
        foreach (var o in gm.Balls)
        {
            if (o.id != b.id && b.SqrDistTo(o) <= maxDistSq)
                results.Add(o);
        }
    }

    /// <summary>
    /// Allocating convenience wrapper. Prefer <see cref="GetTouchingNonAlloc"/> on hot paths.
    /// </summary>
    public List<Ball> GetTouching(Ball b, float maxDist = -1)
    {
        var result = new List<Ball>();
        GetTouchingNonAlloc(b, maxDist, result);
        return result;
    }

    /// <summary>
    /// Zero-alloc BFS flood-fill: fills <paramref name="results"/> with all connected
    /// same-color balls reachable from <paramref name="start"/>. Uses class-level BFS
    /// state buffers — not safe to call re-entrantly (all current callers are sync).
    /// </summary>
    public void FindGroupNonAlloc(Ball start, float maxDist, List<Ball> results)
    {
        results.Clear();
        if (maxDist < 0) maxDist = GameConstants.MatchTouchDist;

        _findGroupVisited.Clear();
        _findGroupQueue.Clear();
        _findGroupVisited.Add(start.id);
        _findGroupQueue.Enqueue(start);
        results.Add(start);

        while (_findGroupQueue.Count > 0)
        {
            var cur = _findGroupQueue.Dequeue();
            GetTouchingNonAlloc(cur, maxDist, _findGroupScratch);
            for (int i = 0; i < _findGroupScratch.Count; i++)
            {
                var t = _findGroupScratch[i];
                if (!_findGroupVisited.Contains(t.id) && t.ballColor == start.ballColor)
                {
                    _findGroupVisited.Add(t.id);
                    _findGroupQueue.Enqueue(t);
                    results.Add(t);
                }
            }
        }
    }

    /// <summary>
    /// Allocating convenience wrapper. Prefer <see cref="FindGroupNonAlloc"/> on hot paths
    /// or when the result is consumed synchronously; use this when the result must survive
    /// across yields (e.g. passed to StartMatchSequence as matchGrp).
    /// </summary>
    public List<Ball> FindGroup(Ball start, float maxDist = -1)
    {
        var group = new List<Ball>(32);
        FindGroupNonAlloc(start, maxDist, group);
        return group;
    }

    // ===== MATCH SEQUENCE =====

    /// <summary>
    /// Start the full match resolution sequence:
    /// 1. Highlight (0.37s) — matched balls pulse
    /// 2. Remove + Suck (0.53s) — balls disappear with ghost animation
    /// 3. Combo check → rotation
    /// </summary>
    public void StartMatchSequence(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
    {
        int pts = matchCount >= 5 ? GameConstants.Score5Match :
                  matchCount == 4 ? GameConstants.Score4Match : GameConstants.Score3Match;
        gm.score += targets.Count * pts;
        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);

        Dbg.Log($"[GravityMatch] StartMatchSequence: {matchCount}-match, targets={targets.Count}");

        // Audio + haptic
        if (AudioManager.Instance)
        {
            if (matchCount >= 4) AudioManager.Instance.PlayMatch45();
            else AudioManager.Instance.PlayMatch3();
        }

        // Show reward text for 4/5-match, color = matched ball color brightened
        if (matchCount >= 4 && matchGrp.Count > 0)
        {
            Color bc = matchGrp[0].ballColor;
            Color bright = new Color(
                Mathf.Min(1f, bc.r * 1.4f + 0.2f),
                Mathf.Min(1f, bc.g * 1.4f + 0.2f),
                Mathf.Min(1f, bc.b * 1.4f + 0.2f));
            string msg = matchCount >= 5 ? "5+ MATCH!" : "4 MATCH!";
            gm.ui.ShowReward(msg, bright);
        }

        gm.state = GameManager.GameState.Highlight;
        StartCoroutine(MatchSequenceCoroutine(matchCount, targets, matchGrp, coneBaseAngle, coneAngle));
    }

    IEnumerator MatchSequenceCoroutine(int matchCount, List<Ball> targets, List<Ball> matchGrp, float coneBaseAngle, float coneAngle)
    {
        // Zero-alloc: reuse class-scoped HashSet instead of `new HashSet(matchGrp.Select(...))`
        _matchIds.Clear();
        for (int i = 0; i < matchGrp.Count; i++) _matchIds.Add(matchGrp[i].id);
        bool hasCone = matchCount >= 4 && coneAngle > 0;

        // Create highlight rings for each target ball (pooled). Reuse buffer.
        _highlightRings.Clear();
        var rings = _highlightRings;
        foreach (var b in targets)
        {
            if (b == null) continue;
            var ring = fxPool.Get(FXPool.FXType.HighlightRing, b.GetComponent<SpriteRenderer>().sprite);
            ring.transform.SetParent(b.transform, false);
            ring.transform.localPosition = Vector3.zero;
            rings.Add(ring);
        }

        // Create cone FX mesh if 4/5-match
        GameObject coneFxGo = null;
        if (hasCone)
        {
            coneFxGo = CreateConeFX(coneBaseAngle, coneAngle);
        }

        // Phase 1: Highlight — v21 style pulsing rings
        float highlightEnd = Time.time + GameConstants.HighlightDuration;
        while (Time.time < highlightEnd)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            float ringSize = (GameConstants.BallRadius + (0.015f + pulse * 0.01f) * GameConstants.WorldScale) * 2f;

            for (int i = 0; i < rings.Count && i < targets.Count; i++)
            {
                if (targets[i] == null || rings[i] == null) continue;
                bool isMatch = _matchIds.Contains(targets[i].id);
                rings[i].transform.localScale = new Vector3(
                    ringSize / targets[i].transform.localScale.x,
                    ringSize / targets[i].transform.localScale.y, 1f);
                var sr = rings[i].GetComponent<SpriteRenderer>();
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

            // Fade cone FX vertex alpha
            if (coneFxGo != null)
            {
                float cp = (highlightEnd - Time.time) / GameConstants.HighlightDuration;
                var mf = coneFxGo.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    var cols = mf.mesh.colors32;
                    cols[0] = new Color32(255, 255, 255, (byte)(cp * 90));
                    for (int ci = 1; ci < cols.Length; ci++)
                        cols[ci] = new Color32(255, 255, 255, (byte)(cols[ci].a > 0 ? cp * cols[ci].a : 0));
                    mf.mesh.colors32 = cols;
                }
            }

            yield return null;
        }

        // Cleanup highlight FX (return to pool)
        foreach (var ring in rings)
        {
            if (ring != null)
            {
                ring.transform.SetParent(null, false);
                fxPool.Return(ring);
            }
        }
        if (coneFxGo != null) Destroy(coneFxGo);

        // Phase 2: Remove balls + suck pause
        gm.state = GameManager.GameState.Suck;
        gm.RemoveBalls(targets);
        gm.ForceValidColors();
        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);

        yield return new WaitForSeconds(GameConstants.SuckDuration);

        // Phase 3: Combo check → rotation
        // Zero-alloc replacement for `gm.Balls.Any(b => !GetTouching(b, ...).Any(nb => ...))`:
        //   outer scan + GetTouchingNonAlloc + manual inner scan breaks out on first neighbour.
        bool hasSingle = false;
        foreach (var b in gm.Balls)
        {
            GetTouchingNonAlloc(b, GameConstants.MatchTouchDist, _comboScratch);
            bool hasSameColorNeighbor = false;
            for (int i = 0; i < _comboScratch.Count; i++)
            {
                if (_comboScratch[i].ballColor == b.ballColor) { hasSameColorNeighbor = true; break; }
            }
            if (!hasSameColorNeighbor) { hasSingle = true; break; }
        }
        if (hasSingle)
        {
            gm.comboCount++;
            if (gm.comboCount >= 3)
            {
                gm.comboCount = 0;
                ComboBonus();
                if (AudioManager.Instance) AudioManager.Instance.PlayCombo();
                gm.ui.ShowReward("3x COMBO!", new Color(1f, 0.9f, 0.43f));
                gm.state = GameManager.GameState.Buddy;
                yield return new WaitForSeconds(GameConstants.BuddyFXDuration);
            }
        }
        else
        {
            gm.comboCount = 0;
        }

        gm.ForceValidColors();
        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);
        gm.StartRotation();
    }

    // ===== CONE FX =====

    /// <summary>
    /// v21 flashlight cone: filled wedge mesh with radial gradient.
    /// Center bright, 30% radius medium, outer edge transparent.
    /// </summary>
    GameObject CreateConeFX(float baseAngle, float halfAngle)
    {
        Vector2 bhPos = gm.blackHole.position;
        float outerR = gm.CamHH * 2f;
        float midR = outerR * 0.3f;
        int arcSegs = 16;

        var go = new GameObject("ConeFX");
        go.transform.position = new Vector3(bhPos.x, bhPos.y, 0);

        int midStart = 1;
        int outerStart = midStart + arcSegs + 1;
        int vertCount = 1 + (arcSegs + 1) * 2;
        var verts = new Vector3[vertCount];
        var colors = new Color32[vertCount];

        verts[0] = Vector3.zero;
        colors[0] = new Color32(255, 255, 255, 90);

        for (int i = 0; i <= arcSegs; i++)
        {
            float a = baseAngle - halfAngle + (2f * halfAngle * i / arcSegs);
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);
            verts[midStart + i] = new Vector3(cos * midR, sin * midR, 0);
            colors[midStart + i] = new Color32(255, 255, 255, 38);
            verts[outerStart + i] = new Vector3(cos * outerR, sin * outerR, 0);
            colors[outerStart + i] = new Color32(255, 255, 255, 0);
        }

        var tris = new List<int>();
        for (int i = 0; i < arcSegs; i++)
        {
            tris.Add(0);
            tris.Add(midStart + i);
            tris.Add(midStart + i + 1);
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
        mr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
        mr.sortingOrder = 1;

        return go;
    }

    // ===== COMBO BUDDY =====

    /// <summary>
    /// 3x combo bonus: find a lonely ball and attach a same-color buddy.
    /// Tries 12 angles around the singleton, picks furthest from BH.
    /// </summary>
    void ComboBonus()
    {
        // Zero-alloc: reuse class-scoped list + non-alloc neighbour scan.
        _comboSingles.Clear();
        foreach (var b in gm.Balls)
        {
            GetTouchingNonAlloc(b, GameConstants.MatchTouchDist, _comboScratch);
            bool hasMatch = false;
            for (int i = 0; i < _comboScratch.Count; i++)
            {
                if (_comboScratch[i].ballColor == b.ballColor) { hasMatch = true; break; }
            }
            if (!hasMatch) _comboSingles.Add(b);
        }
        if (_comboSingles.Count == 0) return;

        var target = _comboSingles[Random.Range(0, _comboSingles.Count)];
        Vector2 tPos = target.cachedPos;
        Vector2 bhPos = gm.blackHole.position;
        float od = GameConstants.OverlapDistance;
        float hh = gm.CamHH;
        float hw = gm.CamHW;
        float r = GameConstants.BallRadius;

        Vector2 bestPos = tPos + Vector2.right * od;
        float bestScore = -1f;

        for (int ai = 0; ai < 12; ai++)
        {
            float ang = ai * Mathf.PI * 2f / 12f;
            Vector2 tryPos = tPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * od;
            if (tryPos.x < -hw + r || tryPos.x > hw - r) continue;
            if (tryPos.y < -hh + r || tryPos.y > hh - r) continue;
            if (Vector2.Distance(tryPos, bhPos) < gm.BHEventHorizon + r) continue;

            bool ok = true;
            foreach (var b2 in gm.Balls)
            {
                if (b2.id == target.id) continue;
                if (Vector2.Distance(tryPos, b2.cachedPos) < od - 0.01f * GameConstants.WorldScale)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            float sc = Vector2.Distance(tryPos, bhPos);
            if (sc > bestScore)
            {
                bestScore = sc;
                bestPos = tryPos;
            }
        }

        if (bestScore > 0)
        {
            var buddy = gm.SpawnBall(bestPos, target.ballColor);
            StartCoroutine(BuddyFX(target, buddy));
            gm.score += GameConstants.ScoreComboBonus;
            gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);
            Dbg.Log($"[GravityMatch] 3x COMBO! Buddy spawned for {GameConstants.ColorToHex(target.ballColor)}");
        }
    }

    void AddBuddyRing(Ball b, List<GameObject> into)
    {
        if (b == null) return;
        var ring = fxPool.Get(FXPool.FXType.BuddyRing, b.GetComponent<SpriteRenderer>().sprite);
        ring.transform.SetParent(b.transform, false);
        ring.transform.localPosition = Vector3.zero;
        into.Add(ring);
    }

    IEnumerator BuddyFX(Ball target, Ball buddy)
    {
        // Zero-alloc: dedicated class buffer (BuddyFX runs parallel to MatchSequenceCoroutine's
        // final WaitForSeconds, so we can't share _highlightRings even if empty — keep isolated).
        _buddyRings.Clear();
        var rings = _buddyRings;
        AddBuddyRing(target, rings);
        AddBuddyRing(buddy, rings);

        float duration = GameConstants.BuddyFXDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float ringScale = (1.1f + t * 0.4f);
            float alpha = (1f - t) * 0.5f;
            foreach (var ring in rings)
            {
                if (ring == null) continue;
                ring.transform.localScale = new Vector3(ringScale, ringScale, 1f);
                ring.GetComponent<SpriteRenderer>().color =
                    new Color(1f, 0.9f, 0.43f, alpha);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        foreach (var ring in rings)
        {
            if (ring != null)
            {
                ring.transform.SetParent(null, false);
                fxPool.Return(ring);
            }
        }
    }
}
