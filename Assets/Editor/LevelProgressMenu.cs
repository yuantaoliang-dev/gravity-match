using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor menu helpers for inspecting and resetting saved level progress.
/// Access via: Unity menu → Tools → Gravity Match → ...
/// </summary>
public static class LevelProgressMenu
{
    // Assumes up to 50 levels. Overkill is fine — DeleteKey on a missing key is a no-op.
    const int MaxLevelsToClear = 50;

    [MenuItem("Tools/Gravity Match/Clear Level Progress")]
    public static void ClearProgress()
    {
        if (!EditorUtility.DisplayDialog(
                "Clear Level Progress",
                "This will wipe every saved star count and best score for all levels. Continue?",
                "Clear", "Cancel"))
        {
            return;
        }

        LevelProgressStore.ClearAll(MaxLevelsToClear);
        Debug.Log($"[LevelProgressMenu] Cleared progress for up to {MaxLevelsToClear} levels.");
    }

    [MenuItem("Tools/Gravity Match/Print Saved Progress")]
    public static void PrintProgress()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[LevelProgressMenu] Saved level progress:");
        for (int i = 0; i < MaxLevelsToClear; i++)
        {
            int stars = LevelProgressStore.GetStars(i);
            int score = LevelProgressStore.GetBestScore(i);
            if (stars > 0 || score > 0)
            {
                sb.AppendLine($"  Level {i + 1}: stars={stars}, best={score}");
            }
        }
        Debug.Log(sb.ToString());
    }
}
