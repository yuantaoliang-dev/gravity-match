using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// View component for a single level button in the level select grid.
/// Created programmatically by LevelSelectView.
/// </summary>
public class LevelButtonView : MonoBehaviour
{
    public TextMeshProUGUI numberText;
    public TextMeshProUGUI starsText;
    public Button button;

    /// <summary>Bind level data to this button.</summary>
    public void Bind(int levelIndex, int starCount, System.Action onClick)
    {
        numberText.text = (levelIndex + 1).ToString();
        starsText.text = starCount > 0 ? new string('*', starCount) : "-";
        starsText.color = new Color(0.937f, 0.624f, 0.153f); // amber
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

        // Number text (upper area)
        var numGo = new GameObject("Num");
        numGo.transform.SetParent(go.transform, false);
        var numRT = numGo.AddComponent<RectTransform>();
        numRT.anchorMin = Vector2.zero;
        numRT.anchorMax = Vector2.one;
        numRT.offsetMin = new Vector2(0, 10);
        numRT.offsetMax = Vector2.zero;
        view.numberText = numGo.AddComponent<TextMeshProUGUI>();
        view.numberText.fontSize = 22;
        view.numberText.fontStyle = FontStyles.Bold;
        view.numberText.alignment = TextAlignmentOptions.Center;
        view.numberText.color = Color.white;
        view.numberText.raycastTarget = false;

        // Stars text (bottom area)
        var starGo = new GameObject("Stars");
        starGo.transform.SetParent(go.transform, false);
        var starRT = starGo.AddComponent<RectTransform>();
        starRT.anchorMin = new Vector2(0, 0);
        starRT.anchorMax = new Vector2(1, 0);
        starRT.anchoredPosition = new Vector2(0, 12);
        starRT.sizeDelta = new Vector2(0, 14);
        view.starsText = starGo.AddComponent<TextMeshProUGUI>();
        view.starsText.fontSize = 11;
        view.starsText.alignment = TextAlignmentOptions.Center;
        view.starsText.raycastTarget = false;

        return view;
    }
}
