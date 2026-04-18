using UnityEngine;

/// <summary>
/// Persistent storage for per-level best score + best star count.
/// Uses Unity's PlayerPrefs, which is:
///   - Android: SharedPreferences XML under app's private data dir
///   - iOS:    NSUserDefaults
///   - Editor: Windows Registry / macOS plist
///
/// Values survive app kills, updates, and device reboots. Only cleared if
/// the user uninstalls the app or explicitly clears game data.
///
/// Keys are indexed by level number (0-based). Adding new levels is safe —
/// they default to 0 stars / 0 score. Removing levels leaves orphan keys
/// (harmless, PlayerPrefs is small).
/// </summary>
public static class LevelProgressStore
{
    const string StarsKeyPrefix = "gm.lvStars_";
    const string ScoreKeyPrefix = "gm.lvScore_";

    public static int GetStars(int levelIndex)
    {
        return PlayerPrefs.GetInt(StarsKeyPrefix + levelIndex, 0);
    }

    public static int GetBestScore(int levelIndex)
    {
        return PlayerPrefs.GetInt(ScoreKeyPrefix + levelIndex, 0);
    }

    /// <summary>
    /// Write-back best record for a level. Only updates values that beat the
    /// existing stored value. Returns true if anything changed (new record).
    /// Calls PlayerPrefs.Save() so the write is flushed to disk immediately
    /// (the OS might batch writes otherwise).
    /// </summary>
    public static bool SaveBest(int levelIndex, int stars, int score)
    {
        bool changed = false;

        int prevStars = GetStars(levelIndex);
        if (stars > prevStars)
        {
            PlayerPrefs.SetInt(StarsKeyPrefix + levelIndex, stars);
            changed = true;
        }

        int prevScore = GetBestScore(levelIndex);
        if (score > prevScore)
        {
            PlayerPrefs.SetInt(ScoreKeyPrefix + levelIndex, score);
            changed = true;
        }

        if (changed) PlayerPrefs.Save();
        return changed;
    }

    /// <summary>Debug / reset helper. Wipes all saved level progress.</summary>
    public static void ClearAll(int levelCount)
    {
        for (int i = 0; i < levelCount; i++)
        {
            PlayerPrefs.DeleteKey(StarsKeyPrefix + i);
            PlayerPrefs.DeleteKey(ScoreKeyPrefix + i);
        }
        PlayerPrefs.Save();
    }
}
