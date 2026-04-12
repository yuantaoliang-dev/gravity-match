using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("HUD")]
    public TextMeshProUGUI ballsText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI targetsText;
    public TextMeshProUGUI levelNameText;

    [Header("Overlay")]
    public GameObject overlay;
    public TextMeshProUGUI starsText;
    public TextMeshProUGUI resultTitle;
    public TextMeshProUGUI resultDetail;
    public Button retryButton;
    public Button nextButton;

    [Header("Remaining Warning")]
    public TextMeshProUGUI remainingCount;

    void Awake()
    {
        // Clear default "New Text" immediately so HUD starts clean
        if (ballsText) ballsText.text = "0";
        if (scoreText) scoreText.text = "0";
        if (targetsText) targetsText.text = "0";
        if (remainingCount) remainingCount.text = "0";
        if (levelNameText) levelNameText.text = "";
        if (starsText) starsText.text = "";
        if (resultTitle) resultTitle.text = "";
        if (resultDetail) resultDetail.text = "";
    }

    void Start()
    {
        if (retryButton) retryButton.onClick.AddListener(() => GameManager.Instance.Restart());
        if (nextButton) nextButton.onClick.AddListener(() => GameManager.Instance.NextLevel());
        if (overlay) overlay.SetActive(false);

        SetupCanvasScaler();
        SetupHUDLayout();
        SetupOverlayLayout();
    }

    /// <summary>Configure CanvasScaler to scale with screen size.</summary>
    void SetupCanvasScaler()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null)
        {
            // Scale with screen size, reference 360x640 (portrait mobile)
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(360, 640);
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    /// <summary>Position HUD elements at top of screen matching v21 layout.</summary>
    void SetupHUDLayout()
    {
        // Level name: top center
        if (levelNameText)
        {
            var rt = levelNameText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -12);
            rt.sizeDelta = new Vector2(300, 24);
            levelNameText.fontSize = 14;
            levelNameText.fontStyle = FontStyles.Bold;
            levelNameText.alignment = TextAlignmentOptions.Center;
        }

        // Balls count: top-left
        if (ballsText)
        {
            var rt = ballsText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(40, -35);
            rt.sizeDelta = new Vector2(70, 24);
            ballsText.fontSize = 16;
            ballsText.fontStyle = FontStyles.Bold;
            ballsText.alignment = TextAlignmentOptions.Center;
        }

        // Score: top-center
        if (scoreText)
        {
            var rt = scoreText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -35);
            rt.sizeDelta = new Vector2(100, 24);
            scoreText.fontSize = 16;
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.alignment = TextAlignmentOptions.Center;
        }

        // Targets: top-right
        if (targetsText)
        {
            var rt = targetsText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-40, -35);
            rt.sizeDelta = new Vector2(70, 24);
            targetsText.fontSize = 16;
            targetsText.fontStyle = FontStyles.Bold;
            targetsText.alignment = TextAlignmentOptions.Center;
        }

        // Remaining count: hide (duplicate of ballsText)
        if (remainingCount && remainingCount != ballsText)
        {
            remainingCount.gameObject.SetActive(false);
        }
    }

    void SetupOverlayLayout()
    {
        // Overlay: stretch to fill canvas with semi-transparent background
        var overlayRT = overlay.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        var overlayImg = overlay.GetComponent<Image>();
        if (overlayImg) overlayImg.color = new Color(0, 0, 0, 0.65f);

        // Stars: large golden text
        if (starsText)
        {
            var rt = starsText.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, 100);
            rt.sizeDelta = new Vector2(400, 60);
            starsText.fontSize = 48;
            starsText.characterSpacing = 20;
            starsText.alignment = TextAlignmentOptions.Center;
            starsText.color = new Color(0.937f, 0.624f, 0.153f); // amber
        }

        // Title
        if (resultTitle)
        {
            var rt = resultTitle.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, 40);
            rt.sizeDelta = new Vector2(400, 50);
            resultTitle.fontSize = 32;
            resultTitle.fontStyle = FontStyles.Bold;
            resultTitle.alignment = TextAlignmentOptions.Center;
            resultTitle.color = Color.white;
        }

        // Detail (multi-line)
        if (resultDetail)
        {
            var rt = resultDetail.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, -20);
            rt.sizeDelta = new Vector2(400, 60);
            resultDetail.fontSize = 18;
            resultDetail.alignment = TextAlignmentOptions.Center;
            resultDetail.color = new Color(1, 1, 1, 0.6f);
        }

        // Buttons side by side
        if (retryButton)
        {
            var rt = retryButton.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(-60, -80);
            rt.sizeDelta = new Vector2(100, 40);
        }
        if (nextButton)
        {
            var rt = nextButton.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(60, -80);
            rt.sizeDelta = new Vector2(100, 40);
        }
    }

    public void UpdateHUD(int ballsLeft, int score, int targets)
    {
        if (ballsText) ballsText.text = ballsLeft.ToString();
        if (scoreText) scoreText.text = score.ToString();
        if (targetsText) targetsText.text = targets.ToString();

        // Warning colors
        if (remainingCount)
        {
            remainingCount.text = ballsLeft.ToString();
            if (ballsLeft <= 5)
                remainingCount.color = new Color(0.973f, 0.318f, 0.286f); // red
            else if (ballsLeft <= 10)
                remainingCount.color = new Color(0.937f, 0.624f, 0.153f); // orange
            else if (ballsLeft <= 15)
                remainingCount.color = new Color(1f, 0.902f, 0.427f);     // yellow
            else
                remainingCount.color = new Color(1f, 1f, 1f, 0.6f);
        }
    }

    public void ShowLevelName(int num, string name)
    {
        if (levelNameText) levelNameText.text = $"Lv {num}: {name}";
        overlay.SetActive(false);
    }

    public void ShowWin(int stars, int score, int ballsLeft, bool hasNextLevel)
    {
        overlay.SetActive(true);
        // Use * for filled star, - for empty (TMP default font lacks Unicode stars)
        string starStr = new string('*', stars) + new string('-', 3 - stars);
        starsText.text = starStr;
        resultTitle.text = "All clear!";
        resultDetail.text = $"Score: {score}\nBonus: {ballsLeft} x 200";
        nextButton.gameObject.SetActive(hasNextLevel);
    }

    public void ShowLose(int ballsRemaining)
    {
        overlay.SetActive(true);
        starsText.text = "";
        resultTitle.text = "Out of balls";
        resultDetail.text = $"{ballsRemaining} balls remaining";
        nextButton.gameObject.SetActive(false);
    }
}
