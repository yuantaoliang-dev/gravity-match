using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Hardcoded level definitions from Gravity Match v21.
/// Call LevelDataBuilder.BuildAll() to get all 5 levels.
/// No ScriptableObjects needed - everything is in code.
/// </summary>
public static class LevelDataBuilder
{
    // Colors
    static readonly Color R = new Color(0.886f, 0.294f, 0.290f); // #E24B4A Red
    static readonly Color B = new Color(0.216f, 0.541f, 0.867f); // #378ADD Blue
    static readonly Color G = new Color(0.114f, 0.620f, 0.459f); // #1D9E75 Green
    static readonly Color A = new Color(0.937f, 0.624f, 0.153f); // #EF9F27 Amber

    // Helper: OD = BR * 1.75, tOff(d) = OD / d
    const float BR = 0.11f;
    const float OD = BR * 1.75f;
    static float tOff(float d) => OD / d;

    public static List<LevelDef> BuildAll()
    {
        return new List<LevelDef>
        {
            BuildLevel1(),
            BuildLevel2(),
            BuildLevel3(),
            BuildLevel4(),
            BuildLevel5(),
        };
    }

    // ===== Level 1: First Steps =====
    static LevelDef BuildLevel1()
    {
        var lv = new LevelDef
        {
            name = "First Steps",
            tip = "2 colors, 1 four-match bonus. Learn to shoot!",
            budget = 14,
            colors = new[] { R, B },
            starScore3 = 2500,
            starScore2 = 1500,
        };
        var bl = lv.blobs;
        // 4 pairs
        bl.Add(Blob(4.7f, 75, Pair(75, R)));
        bl.Add(Blob(3.3f, 80, Pair(80, B)));
        bl.Add(Blob(1.8f, 75, Pair(75, R)));
        bl.Add(Blob(0.5f, 80, Pair(80, B)));
        // 1 four-match (pair + single with gap)
        bl.Add(Blob(5.5f, 58, FourMatch(58, R)));
        // 1 singleton
        bl.Add(Blob(2.8f, 55, Single(B)));
        return lv;
    }

    // ===== Level 2: Orbit =====
    static LevelDef BuildLevel2()
    {
        var lv = new LevelDef
        {
            name = "Orbit",
            tip = "3 colors, 4 lonely balls. Combo is your lifeline!",
            budget = 16,
            colors = new[] { R, B, G },
            starScore3 = 2400,
            starScore2 = 1800,
        };
        var bl = lv.blobs;
        // 5 pairs
        bl.Add(Blob(5.0f, 78, Pair(78, R)));
        bl.Add(Blob(3.5f, 82, Pair(82, B)));
        bl.Add(Blob(2.0f, 78, Pair(78, G)));
        bl.Add(Blob(0.5f, 82, Pair(82, R)));
        bl.Add(Blob(4.3f, 55, Pair(55, B)));
        // 4 singletons
        bl.Add(Blob(5.5f, 105, Single(G)));
        bl.Add(Blob(3.0f, 108, Single(R)));
        bl.Add(Blob(1.3f, 55, Single(B)));
        bl.Add(Blob(0.0f, 105, Single(G)));
        return lv;
    }

    // ===== Level 3: Debris Field =====
    static LevelDef BuildLevel3()
    {
        var lv = new LevelDef
        {
            name = "Debris Field",
            tip = "6 singles + 1 four-match. Budget razor-thin!",
            budget = 18,
            colors = new[] { R, B, G },
            starScore3 = 3000,
            starScore2 = 2200,
        };
        var bl = lv.blobs;
        // 1 four-match
        bl.Add(Blob(5.0f, 82, FourMatch(82, R)));
        // 4 pairs
        bl.Add(Blob(3.2f, 85, Pair(85, B)));
        bl.Add(Blob(1.5f, 82, Pair(82, G)));
        bl.Add(Blob(0.2f, 85, Pair(85, R)));
        bl.Add(Blob(2.3f, 50, Pair(50, G)));
        // 6 singletons
        bl.Add(Blob(5.5f, 50, Single(B)));
        bl.Add(Blob(0.8f, 108, Single(G)));
        bl.Add(Blob(3.8f, 55, Single(R)));
        bl.Add(Blob(1.0f, 50, Single(B)));
        bl.Add(Blob(4.5f, 110, Single(G)));
        bl.Add(Blob(2.8f, 108, Single(R)));
        return lv;
    }

    // ===== Level 4: Asteroid Belt =====
    static LevelDef BuildLevel4()
    {
        var lv = new LevelDef
        {
            name = "Asteroid Belt",
            tip = "4 colors, NO cones, 8 singles. Pure skill + combos!",
            budget = 22,
            colors = new[] { R, B, G, A },
            starScore3 = 3500,
            starScore2 = 2500,
        };
        var bl = lv.blobs;
        // 5 pairs
        bl.Add(Blob(5.0f, 80, Pair(80, R)));
        bl.Add(Blob(3.2f, 85, Pair(85, B)));
        bl.Add(Blob(1.5f, 80, Pair(80, G)));
        bl.Add(Blob(0.2f, 85, Pair(85, A)));
        bl.Add(Blob(4.0f, 55, Pair(55, R)));
        // 8 singletons
        bl.Add(Blob(5.5f, 50, Single(B)));
        bl.Add(Blob(0.8f, 108, Single(G)));
        bl.Add(Blob(3.8f, 55, Single(A)));
        bl.Add(Blob(1.0f, 50, Single(R)));
        bl.Add(Blob(4.5f, 110, Single(G)));
        bl.Add(Blob(2.8f, 108, Single(A)));
        bl.Add(Blob(2.0f, 50, Single(B)));
        bl.Add(Blob(5.8f, 105, Single(G)));
        return lv;
    }

    // ===== Level 5: Event Horizon =====
    static LevelDef BuildLevel5()
    {
        var lv = new LevelDef
        {
            name = "Event Horizon",
            tip = "26 balls, 10 singles, 4 colors. Bridge the 5-match or perish!",
            budget = 28,
            colors = new[] { R, B, G, A },
            starScore3 = 5000,
            starScore2 = 3500,
        };
        var bl = lv.blobs;
        // 1 five-match
        bl.Add(Blob(5.0f, 100, FiveMatch(100, R)));
        // 6 pairs
        bl.Add(Blob(3.0f, 82, Pair(82, B)));
        bl.Add(Blob(1.2f, 85, Pair(85, G)));
        bl.Add(Blob(0.0f, 82, Pair(82, A)));
        bl.Add(Blob(4.0f, 60, Pair(60, B)));
        bl.Add(Blob(2.0f, 55, Pair(55, G)));
        bl.Add(Blob(1.5f, 110, Pair(110, A)));
        // 10 singletons
        bl.Add(Blob(5.5f, 55, Single(B)));
        bl.Add(Blob(3.8f, 50, Single(G)));
        bl.Add(Blob(0.8f, 50, Single(A)));
        bl.Add(Blob(2.5f, 115, Single(R)));
        bl.Add(Blob(4.5f, 115, Single(G)));
        bl.Add(Blob(1.8f, 50, Single(B)));
        bl.Add(Blob(0.5f, 108, Single(A)));
        bl.Add(Blob(3.5f, 112, Single(R)));
        bl.Add(Blob(5.2f, 50, Single(G)));
        bl.Add(Blob(4.8f, 55, Single(A)));
        return lv;
    }

    // ===== HELPERS =====

    static BlobDef Blob(float angle, float dist, List<BallDef> balls)
    {
        return new BlobDef { angle = angle, distance = dist, balls = balls };
    }

    // Pair: two same-color balls at ±tOff(d)/2
    static List<BallDef> Pair(float d, Color c)
    {
        float t = tOff(d);
        return new List<BallDef>
        {
            new BallDef { angleOffset = -t / 2f, distOffset = 0, color = c },
            new BallDef { angleOffset = t / 2f, distOffset = 0, color = c },
        };
    }

    // Single: one ball
    static List<BallDef> Single(Color c)
    {
        return new List<BallDef>
        {
            new BallDef { angleOffset = 0, distOffset = 0, color = c },
        };
    }

    // Four-match: pair + single with tOff*2.06 gap
    static List<BallDef> FourMatch(float d, Color c)
    {
        float t = tOff(d);
        return new List<BallDef>
        {
            new BallDef { angleOffset = -t / 2f, distOffset = 0, color = c },
            new BallDef { angleOffset = t / 2f, distOffset = 0, color = c },
            new BallDef { angleOffset = t * 2.06f, distOffset = 0, color = c },
        };
    }

    // Five-match: two pairs with tOff*1.78/0.78 spacing
    static List<BallDef> FiveMatch(float d, Color c)
    {
        float t = tOff(d);
        return new List<BallDef>
        {
            new BallDef { angleOffset = -t * 1.78f, distOffset = 0, color = c },
            new BallDef { angleOffset = -t * 0.78f, distOffset = 0, color = c },
            new BallDef { angleOffset = t * 0.78f, distOffset = 0, color = c },
            new BallDef { angleOffset = t * 1.78f, distOffset = 0, color = c },
        };
    }
}

// ===== DATA CLASSES (replace ScriptableObject) =====

[System.Serializable]
public class LevelDef
{
    public string name;
    public string tip;
    public int budget;
    public Color[] colors;
    public int starScore3;
    public int starScore2;
    public List<BlobDef> blobs = new List<BlobDef>();

    /// <summary>
    /// Generate world positions for all balls.
    /// Distances in level data are in "pixel units" from HTML, divide by 100 for Unity world units.
    /// </summary>
    public List<(Vector2 pos, Color color)> GenerateBallPositions(Vector2 center)
    {
        var result = new List<(Vector2, Color)>();
        foreach (var blob in blobs)
        {
            foreach (var bd in blob.balls)
            {
                float a = blob.angle + bd.angleOffset;
                float d = (blob.distance + bd.distOffset) / 100f; // pixel → world units
                Vector2 pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                result.Add((pos, bd.color));
            }
        }
        return result;
    }
}

[System.Serializable]
public class BlobDef
{
    public float angle;      // radians
    public float distance;   // pixel units (divide by 100 for world)
    public List<BallDef> balls;
}

[System.Serializable]
public class BallDef
{
    public float angleOffset;
    public float distOffset;
    public Color color;
}
