using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles match detection (BFS group finding), match sequence animation
/// (highlight → suck → combo), and cone sweep FX.
/// Extracted from GameManager to follow single-responsibility principle.
/// </summary>
public class MatchSystem : MonoBehaviour
{
    private GameManager gm;

    public void Init(GameManager gm)
    {
        this.gm = gm;
    }

    // ===== MATCH DETECTION =====

    /// <summary>Return all balls within maxDist of the given ball.</summary>
    public List<Ball> GetTouching(Ball b, float maxDist = -1)
    {
        if (maxDist < 0) maxDist = GameConstants.TouchDist;
        return gm.Balls.Where(o => o.id != b.id && b.DistTo(o) <= maxDist).ToList();
    }

    /// <summary>BFS flood-fill to find all connected same-color balls.</summary>
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
            gm.ui.ShowReward(msg, bright);
        }

        gm.state = GameManager.GameState.Highlight;
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

        // Phase 1: Highlight — v21 style pulsing rings
        float highlightEnd = Time.time + GameConstants.HighlightDuration;
        while (Time.time < highlightEnd)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            float ringSize = (GameConstants.BallRadius + (0.015f + pulse * 0.01f) * GameConstants.WorldScale) * 2f;

            for (int i = 0; i < rings.Count && i < targets.Count; i++)
            {
                if (targets[i] == null || rings[i] == null) continue;
                bool isMatch = matchIds.Contains(targets[i].id);
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

        // Cleanup highlight FX
        foreach (var ring in rings) if (ring != null) Destroy(ring);
        if (coneFxGo != null) Destroy(coneFxGo);

        // Phase 2: Remove balls + suck pause
        gm.state = GameManager.GameState.Suck;
        gm.RemoveBalls(targets);
        gm.ForceValidColors();
        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);

        yield return new WaitForSeconds(GameConstants.SuckDuration);

        // Phase 3: Combo check → rotation
        bool hasSingle = gm.Balls.Any(b =>
            !GetTouching(b, GameConstants.MatchTouchDist).Any(nb => nb.ballColor == b.ballColor));
        if (hasSingle)
        {
            gm.comboCount++;
            if (gm.comboCount >= 3)
            {
                gm.comboCount = 0;
                ComboBonus();
                gm.ui.ShowReward("3x COMBO! +300", new Color(1f, 0.9f, 0.43f));
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
        float outerR = Camera.main.orthographicSize * 2f;
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
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) mr.material = mat;
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
        var singles = new List<Ball>();
        foreach (var b in gm.Balls)
        {
            bool hasMatch = GetTouching(b, GameConstants.MatchTouchDist)
                .Any(t => t.ballColor == b.ballColor);
            if (!hasMatch) singles.Add(b);
        }
        if (singles.Count == 0) return;

        var target = singles[Random.Range(0, singles.Count)];
        Vector2 tPos = target.transform.position;
        Vector2 bhPos = gm.blackHole.position;
        float od = GameConstants.OverlapDistance;
        float hh = Camera.main.orthographicSize;
        float hw = hh * Camera.main.aspect;
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
                if (Vector2.Distance(tryPos, b2.transform.position) < od - 0.01f * GameConstants.WorldScale)
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
            Debug.Log($"[GravityMatch] 3x COMBO! Buddy spawned for {GameConstants.ColorToHex(target.ballColor)}");
        }
    }

    IEnumerator BuddyFX(Ball target, Ball buddy)
    {
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
        foreach (var ring in rings) if (ring != null) Destroy(ring);
    }
}
