using UnityEngine;

/// <summary>
/// All game constants from Gravity Match v21.
/// Distances are in Unity units (1 unit ≈ 1 pixel from HTML version).
/// </summary>
public static class GameConstants
{
    // World scale: HTML pixel / 100. 1px = 0.01 units.
    public const float WorldScale = 1f;

    // Ball
    public const float BallRadius = 0.11f * WorldScale;
    public const float OverlapDistance = BallRadius * 1.75f;  // pair placement distance
    public const float MatchTouchDist = BallRadius * 2.05f;   // match detection
    public const float StrictTouchDist = BallRadius * 1.6f;   // startup validation
    public const float TouchDist = BallRadius * 2.3f;          // general touching
    public const float HitDetectDist = BallRadius * 1.7f;     // projectile hit detection
    public const float MinVisDist = BallRadius * 3f;           // min diff-color visual separation

    // Black Hole
    public const float BHRadiusBase = 0.20f * WorldScale;
    public const float BHEventHorizonBase = 0.34f * WorldScale;
    // v21: bhR += 0.4px, bhEhr += 0.6px per ball → /100 for world units
    public const float BHGrowthRadius = 0.004f * WorldScale;     // per ball absorbed
    public const float BHGrowthEH = 0.006f * WorldScale;          // per ball absorbed

    // Projectile
    public const float BallSpeed = 4.5f * WorldScale;   // units per second
    public const int MaxBounces = 5;
    public const int TrajectoryMaxDots = 160;

    // Field
    public const float FieldRotation = 30f;  // degrees per shot
    public const float ConeAngle4 = 30f;     // degrees, ±30
    public const float ConeAngle5 = 30f;     // degrees, ±30

    // Scoring — tuned for a challenging star curve where 3★ requires ~95%
    // of theoretical max (perfect match chains + cone sweeps + combos).
    // All values roughly halved from v21 so raw numbers stay in the 1000-3000
    // range typical for casual bubble-shooter feel.
    public const int Score3Match = 50;   // per ball
    public const int Score4Match = 70;   // per ball
    public const int Score5Match = 100;  // per ball
    public const int ScoreComboBonus = 50;
    public const int ScoreBHAbsorb = 20; // per ball
    public const int ScoreLeftover = 50; // per remaining ball

    // Timing (seconds)
    public const float HighlightDuration = 0.37f;   // 22 frames @ 60fps
    public const float SuckDuration = 0.53f;         // 32 frames
    public const float BuddyFXDuration = 0.83f;      // 50 frames
    public const float RotationEaseThreshold = 0.1f;

    // Colors
    public static readonly Color Red = new Color(0.886f, 0.294f, 0.290f);    // #E24B4A
    public static readonly Color Blue = new Color(0.216f, 0.541f, 0.867f);   // #378ADD
    public static readonly Color Green = new Color(0.114f, 0.620f, 0.459f);  // #1D9E75
    public static readonly Color Amber = new Color(0.937f, 0.624f, 0.153f);  // #EF9F27
    public static readonly Color[] Palette = { Red, Blue, Green, Amber };

    public static string ColorToHex(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    /// <summary>Create an unlit sprite material (URP first, legacy fallback).</summary>
    public static Material CreateUnlitSpriteMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) : null;
    }
}
