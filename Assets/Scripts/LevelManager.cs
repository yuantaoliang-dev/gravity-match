using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles level loading, ball layout validation, win/lose checks,
/// and level progression. Extracted from GameManager.
/// </summary>
public class LevelManager : MonoBehaviour
{
    private GameManager gm;
    private Camera cam;

    private List<LevelDef> levels;
    private int[] levelStars;

    public int CurrentLevel { get; private set; }
    public int LevelCount => levels.Count;
    public int[] LevelStars => levelStars;
    public LevelDef GetCurrentLevelDef() => levels[CurrentLevel];

    public void Init(GameManager gm, Camera cam)
    {
        this.gm = gm;
        this.cam = cam;
        levels = LevelDataBuilder.BuildAll();
        levelStars = new int[levels.Count];
    }

    // ===== LEVEL LOADING =====

    public void LoadLevel(int index)
    {
        CurrentLevel = index;
        var lv = levels[index];

        // Return existing balls to pool and reset state
        gm.ResetForLevel(lv.budget);

        // Spawn balls from level data
        Vector2 center = gm.blackHole.position;
        var positions = lv.GenerateBallPositions(center);
        foreach (var (pos, color) in positions)
        {
            gm.SpawnBall(pos, color);
        }

        // Startup validation: prevent 3+ same-color groups
        for (int v = 0; v < 50; v++)
        {
            bool found = false;
            foreach (var b in gm.Balls)
            {
                var grp = gm.FindGroup(b, GameConstants.StrictTouchDist);
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
        RestorePairDistances();

        // Init color queue
        gm.InitColorQueue();

        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);
        gm.ui.ShowLevelName(index + 1, lv.name);
    }

    public void Restart() => LoadLevel(CurrentLevel);
    public void NextLevel() { if (CurrentLevel < levels.Count - 1) LoadLevel(CurrentLevel + 1); }

    // ===== BALL LAYOUT VALIDATION =====

    void PushApartDifferentColors()
    {
        // Identify pair balls
        var pairIds = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (var b in gm.Balls)
        {
            if (visited.Contains(b.id)) continue;
            var grp = gm.FindGroup(b, GameConstants.MatchTouchDist);
            foreach (var g in grp) visited.Add(g.id);
            if (grp.Count >= 2) foreach (var g in grp) pairIds.Add(g.id);
        }

        float minVis = GameConstants.MinVisDist;

        for (int iter = 0; iter < 150; iter++)
        {
            bool pushed = false;
            var balls = gm.Balls;
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
        float od = GameConstants.OverlapDistance;
        var visited = new HashSet<int>();

        foreach (var b in gm.Balls)
        {
            if (visited.Contains(b.id)) continue;
            var grp = gm.FindGroup(b, GameConstants.MatchTouchDist);
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
        float hh = gm.CamHH;
        float hw = gm.CamHW;
        pos.x = Mathf.Clamp(pos.x, -hw + r, hw - r);
        pos.y = Mathf.Clamp(pos.y, -hh + r, hh - r);

        Vector2 center = gm.blackHole.position;
        float dist = Vector2.Distance(pos, center);
        if (dist < gm.BHEventHorizon + r + 0.02f * GameConstants.WorldScale)
        {
            Vector2 dir = (pos - center).normalized;
            pos = center + dir * (gm.BHEventHorizon + r + 0.03f * GameConstants.WorldScale);
        }
        b.transform.position = pos;
    }

    // ===== WIN/LOSE =====

    /// <summary>Check for win (0 balls) or lose (0 budget).</summary>
    public void CheckEnd()
    {
        if (gm.state != GameManager.GameState.Play) return;
        if (gm.Balls.Count == 0)
        {
            gm.state = GameManager.GameState.Won;
            var lv = levels[CurrentLevel];
            int remBonus = gm.ballsLeft * GameConstants.ScoreLeftover;
            gm.score += remBonus;
            int stars = gm.score >= lv.starScore3 ? 3 : gm.score >= lv.starScore2 ? 2 : 1;
            levelStars[CurrentLevel] = Mathf.Max(levelStars[CurrentLevel], stars);
            bool hasNext = CurrentLevel < levels.Count - 1;
            if (AudioManager.Instance) AudioManager.Instance.PlayWin();
            gm.ui.ShowWin(stars, gm.score, gm.ballsLeft, hasNext);
        }
        else if (gm.ballsLeft <= 0 && !gm.shooter.HasProjectile)
        {
            gm.state = GameManager.GameState.Lost;
            if (AudioManager.Instance) AudioManager.Instance.PlayLose();
            gm.ui.ShowLose(gm.Balls.Count);
        }
    }
}
