using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Modal dialog that confirms exiting the game.
/// Created programmatically and managed by UIManager.
/// </summary>
public class ExitConfirmDialog : MonoBehaviour
{
    public bool IsOpen => gameObject.activeSelf;

    public static ExitConfirmDialog Create(Transform parent)
    {
        var panelGo = new GameObject("ExitConfirmDialog");
        panelGo.transform.SetParent(parent, false);
        var panelRT = panelGo.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0, 0, 0, 0.75f);

        var view = panelGo.AddComponent<ExitConfirmDialog>();

        // Message
        var msgGo = new GameObject("Message");
        msgGo.transform.SetParent(panelGo.transform, false);
        var msgRT = msgGo.AddComponent<RectTransform>();
        msgRT.anchorMin = new Vector2(0.5f, 0.5f);
        msgRT.anchorMax = new Vector2(0.5f, 0.5f);
        msgRT.anchoredPosition = new Vector2(0, 30);
        msgRT.sizeDelta = new Vector2(280, 40);
        var msgText = msgGo.AddComponent<TextMeshProUGUI>();
        msgText.text = "Exit game?";
        msgText.fontSize = 22;
        msgText.fontStyle = FontStyles.Bold;
        msgText.alignment = TextAlignmentOptions.Center;
        msgText.color = Color.white;
        msgText.raycastTarget = false;

        // Yes button
        CreateButton(panelGo.transform, "Yes", new Vector2(-60, -30),
            () => { Application.Quit(); });

        // No button
        CreateButton(panelGo.transform, "No", new Vector2(60, -30),
            () => { view.Hide(); });

        panelGo.SetActive(false);
        return view;
    }

    static void CreateButton(Transform parent, string label, Vector2 pos, System.Action onClick)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(90, 36);

        var img = go.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.15f);

        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick());

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRT = textGo.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // ensure on top
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
