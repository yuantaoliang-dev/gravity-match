using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

/// <summary>
/// Splash screen: shows studio name and game title, then loads the game scene.
/// Attach to a Camera in a dedicated Splash scene, or call Show() from GameManager.
/// </summary>
public class SplashScreen : MonoBehaviour
{
    /// <summary>Show splash overlay on the current scene (no separate scene needed).</summary>
    public static void ShowOnCurrentScene()
    {
        var go = new GameObject("SplashScreen");
        go.AddComponent<SplashScreen>();
    }

    void Start()
    {
        StartCoroutine(SplashSequence());
    }

    IEnumerator SplashSequence()
    {
        // Create full-screen canvas overlay
        var canvasGo = new GameObject("SplashCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(360, 640);
        scaler.matchWidthOrHeight = 0.5f;

        // Dark background
        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRT = bgGo.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGo.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0.043f, 0.051f, 0.078f, 1f); // #0B0D14
        bgImg.raycastTarget = false;

        // Studio name (upper area)
        var studioGo = CreateText(canvasGo.transform, "Lyta Studio",
            new Vector2(0.5f, 0.6f), 18, new Color(1, 1, 1, 0.6f));

        // Game title
        var titleGo = CreateText(canvasGo.transform, "Gravity Match",
            new Vector2(0.5f, 0.5f), 28, Color.white, FontStyles.Bold);

        // Subtitle
        var subGo = CreateText(canvasGo.transform, "Event Horizon",
            new Vector2(0.5f, 0.44f), 14, new Color(0.55f, 0.15f, 0.85f, 0.8f));

        // Fade in
        yield return FadeTexts(new[] { studioGo, titleGo, subGo }, 0f, 1f, 0.5f);

        // Hold
        yield return new WaitForSeconds(1.5f);

        // Fade out
        float fadeDuration = 0.5f;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            float a = 1f - elapsed / fadeDuration;
            SetTextAlpha(studioGo, a * 0.6f);
            SetTextAlpha(titleGo, a);
            SetTextAlpha(subGo, a * 0.8f);
            bgImg.color = new Color(0.043f, 0.051f, 0.078f, a);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Cleanup
        Destroy(canvasGo);
        Destroy(gameObject);
    }

    GameObject CreateText(Transform parent, string text, Vector2 anchorPos,
                          float fontSize, Color color, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorPos;
        rt.anchorMax = anchorPos;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(300, 40);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(color.r, color.g, color.b, 0); // start invisible
        tmp.raycastTarget = false;
        return go;
    }

    IEnumerator FadeTexts(GameObject[] texts, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float a = Mathf.Lerp(from, to, t);
            foreach (var go in texts)
            {
                var tmp = go.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    Color c = tmp.color;
                    c.a = a * (c.a > 0 ? c.a / Mathf.Max(a, 0.01f) : 1f);
                    tmp.color = new Color(c.r, c.g, c.b, a);
                }
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    void SetTextAlpha(GameObject go, float alpha)
    {
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, alpha);
    }
}
