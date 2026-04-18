using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// View component for a single level button in the level select grid.
/// Three stacked text rows: level number (top), stars (middle), best score (bottom).
/// Created programmatically by LevelSelectView.
/// </summary>
public class LevelButtonView : MonoBehaviour
{
    public TextMeshProUGUI numberText;
    public TextMeshProUGUI starsText;
    public TextMeshProUGUI scoreText;
    public Button button;

    /// <summary>Bind level data to this button.</summary>
    public void Bind(int levelIndex, int starCount, int bestScore, System.Action onClick)
    {
        numberText.text = (levelIndex + 1).ToString();
        starsText.text = starCount > 0 ? new string('*', starCount) : "-";
        starsText.color = new Color(0.937f, 0.624f, 0.153f); // amber
        // Best score row: hidden (empty) if never cleared, otherwise show value
        scoreText.text = bestScore > 0 ? bestScore.ToString() : "";
        scoreText.color = new Color(1f, 1f, 1f, 0.75f);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick());
    }

    /// <summary>Create a LevelButtonView programmatically (no prefab needed).</summary>
    public static LevelButtonView Create(Transform parent, float width, float height)
    {
        var go = new GameObject("LevelButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        var img = go.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.06f);

        var view = go.AddComponent<LevelButtonView>();
        view.button = go.AddComponent<Button>();

        // Number text (upper area) — large, bold
        var numGo = new GameObject("Num");
        numGo.transform.SetParent(go.transform, false);
        var numRT = numGo.AddComponent<RectTransform>();
        numRT.anchorMin = new Vector2(0, 1);
        numRT.anchorMax = new Vector2(1, 1);
        numRT.pivot = new Vector2(0.5f, 1);
        numRT.anchoredPosition = new Vector2(0, -6);
        numRT.sizeDelta = new Vector2(0, 26);
        view.numberText = numGo.AddComponent<TextMeshProUGUI>();
        view.numberText.fontSize = 20;
        view.numberText.fontStyle = FontStyles.Bold;
        view.numberText.alignment = TextAlignmentOptions.Center;
        view.numberText.color = Color.white;
        view.numberText.raycastTarget = false;

        // Stars row (middle) — amber
        var starGo = new GameObject("Stars");
        starGo.transform.SetParent(go.transform, false);
        var starRT = starGo.AddComponent<RectTransform>();
        starRT.anchorMin = new Vector2(0, 0.5f);
        starRT.anchorMax = new Vector2(1, 0.5f);
        starRT.pivot = new Vector2(0.5f, 0.5f);
        starRT.anchoredPosition = new Vector2(0, -4);
        starRT.sizeDelta = new Vector2(0, 14);
        view.starsText = starGo.AddComponent<TextMeshProUGUI>();
        view.starsText.fontSize = 12;
        view.starsText.alignment = TextAlignmentOptions.Center;
        view.starsText.raycastTarget = false;

        // Best score row (bottom) — dim white, shown only after first clear
        var scoreGo = new GameObject("Score");
        scoreGo.transform.SetParent(go.transform, false);
        var scoreRT = scoreGo.AddComponent<RectTransform>();
        scoreRT.anchorMin = new Vector2(0, 0);
        scoreRT.anchorMax = new Vector2(1, 0);
        scoreRT.pivot = new Vector2(0.5f, 0);
        scoreRT.anchoredPosition = new Vector2(0, 6);
        scoreRT.sizeDelta = new Vector2(0, 12);
        view.scoreText = scoreGo.AddComponent<TextMeshProUGUI>();
        view.scoreText.fontSize = 10;
        view.scoreText.alignment = TextAlignmentOptions.Center;
        view.scoreText.raycastTarget = false;

        return view;
    }
}
