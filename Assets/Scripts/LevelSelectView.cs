using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Manages the level select panel UI. Handles grid layout,
/// button creation, and show/hide logic.
/// </summary>
public class LevelSelectView : MonoBehaviour
{
    // Grid layout constants
    const int Cols = 3;
    const float BtnW = 70f;
    const float BtnH = 70f;
    const float Gap = 10f;

    private Transform gridParent;
    private Button closeButton;
    private List<LevelButtonView> buttonPool = new List<LevelButtonView>();

    /// <summary>Create the level select panel programmatically under the given canvas.</summary>
    public static LevelSelectView Create(Transform canvasTransform)
    {
        // Panel (full screen, solid bg)
        var panelGo = new GameObject("LevelSelect");
        panelGo.transform.SetParent(canvasTransform, false);
        var panelRT = panelGo.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0.04f, 0.05f, 0.08f, 1f);

        var view = panelGo.AddComponent<LevelSelectView>();

        // Title
        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(panelGo.transform, false);
        var titleRT = titleGo.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 1);
        titleRT.anchorMax = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = new Vector2(0, -80);
        titleRT.sizeDelta = new Vector2(200, 30);
        var titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "Select Level";
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.raycastTarget = false;

        // Grid parent
        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(panelGo.transform, false);
        var gridRT = gridGo.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one;
        gridRT.offsetMin = Vector2.zero;
        gridRT.offsetMax = Vector2.zero;
        view.gridParent = gridGo.transform;

        // Close button
        var closeGo = new GameObject("CloseBtn");
        closeGo.transform.SetParent(panelGo.transform, false);
        var closeRT = closeGo.AddComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(0.5f, 0);
        closeRT.anchorMax = new Vector2(0.5f, 0);
        closeRT.anchoredPosition = new Vector2(0, 80);
        closeRT.sizeDelta = new Vector2(100, 32);
        var closeImg = closeGo.AddComponent<Image>();
        closeImg.color = new Color(1, 1, 1, 0.1f);
        view.closeButton = closeGo.AddComponent<Button>();
        view.closeButton.onClick.AddListener(() => panelGo.SetActive(false));

        var closeTextGo = new GameObject("Text");
        closeTextGo.transform.SetParent(closeGo.transform, false);
        var closeTextRT = closeTextGo.AddComponent<RectTransform>();
        closeTextRT.anchorMin = Vector2.zero;
        closeTextRT.anchorMax = Vector2.one;
        closeTextRT.offsetMin = Vector2.zero;
        closeTextRT.offsetMax = Vector2.zero;
        var closeText = closeTextGo.AddComponent<TextMeshProUGUI>();
        closeText.text = "Close";
        closeText.fontSize = 14;
        closeText.fontStyle = FontStyles.Bold;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.color = Color.white;

        panelGo.SetActive(false);
        return view;
    }

    /// <summary>Show the level select panel with current data.</summary>
    public void Show(int levelCount, int[] stars, int[] bestScores, System.Action<int> onSelect)
    {
        // Ensure enough buttons in pool
        while (buttonPool.Count < levelCount)
        {
            var btn = LevelButtonView.Create(gridParent, BtnW, BtnH);
            buttonPool.Add(btn);
        }

        // Position and bind buttons
        float gridW = Cols * BtnW + (Cols - 1) * Gap;
        float startX = -gridW / 2f + BtnW / 2f;
        float startY = -120f;

        for (int i = 0; i < buttonPool.Count; i++)
        {
            if (i < levelCount)
            {
                int row = i / Cols;
                int col = i % Cols;
                var rt = buttonPool[i].GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 1);
                rt.anchorMax = new Vector2(0.5f, 1);
                rt.anchoredPosition = new Vector2(startX + col * (BtnW + Gap), startY - row * (BtnH + Gap));

                int lvIndex = i;
                int score = (bestScores != null && i < bestScores.Length) ? bestScores[i] : 0;
                buttonPool[i].Bind(i, stars[i], score, () => {
                    onSelect(lvIndex);
                    gameObject.SetActive(false);
                });
                buttonPool[i].gameObject.SetActive(true);
            }
            else
            {
                buttonPool[i].gameObject.SetActive(false);
            }
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
