using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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

    // Level select (built dynamically)
    private GameObject levelSelectPanel;
    private Button levelsButton;

    // Center reward text (v21 "showRw")
    private TextMeshProUGUI rewardText;
    private float rewardTimer;

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

        // Ensure EventSystem exists (required for UI button clicks)
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        SetupCanvasScaler();
        SetupHUDLayout();
        SetupOverlayLayout();
        CreateRewardText();
        CreateLevelSelectUI();
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
        // Disable raycast on all HUD elements (they block buttons)
        var hudPanel = ballsText?.transform.parent;
        if (hudPanel != null)
        {
            var hudImg = hudPanel.GetComponent<Image>();
            if (hudImg) hudImg.raycastTarget = false;
        }
        // Disable raycast on all TMP texts in the canvas
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            foreach (var tmp in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                tmp.raycastTarget = false;
        }
        // Level name: top center
        if (levelNameText)
        {
            var rt = levelNameText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -8);
            rt.sizeDelta = new Vector2(200, 20);
            levelNameText.fontSize = 12;
            levelNameText.fontStyle = FontStyles.Bold;
            levelNameText.alignment = TextAlignmentOptions.Center;
        }

        // v21 HUD: label on top, value below. Left=balls, Center=score, Right=targets
        SetupHUDItem(ballsText, "balls", 0f, 1f, 40f);
        SetupHUDItem(scoreText, "score", 0.5f, 0.5f, 0f);
        SetupHUDItem(targetsText, "targets", 1f, 0f, -40f);

        // Remaining count: hide (duplicate of ballsText)
        if (remainingCount && remainingCount != ballsText)
        {
            remainingCount.gameObject.SetActive(false);
        }
    }

    void SetupHUDItem(TextMeshProUGUI valueText, string label, float anchorX, float anchorXMax, float offsetX)
    {
        if (valueText == null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

        // Value text
        var rt = valueText.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorX, 1);
        rt.anchorMax = new Vector2(anchorX, 1);
        rt.anchoredPosition = new Vector2(offsetX, -35);
        rt.sizeDelta = new Vector2(70, 18);
        valueText.fontSize = 14;
        valueText.fontStyle = FontStyles.Bold;
        valueText.alignment = TextAlignmentOptions.Center;

        // Label above value (v21: small monospace label)
        var labelGo = new GameObject($"Label_{label}");
        labelGo.transform.SetParent(canvas.transform, false);
        var labelRT = labelGo.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(anchorX, 1);
        labelRT.anchorMax = new Vector2(anchorX, 1);
        labelRT.anchoredPosition = new Vector2(offsetX, -23);
        labelRT.sizeDelta = new Vector2(70, 14);
        var labelText = labelGo.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 9;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = new Color(1, 1, 1, 0.4f); // v21: rgba(255,255,255,0.4)
        labelText.raycastTarget = false;
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

    // ===== REWARD TEXT (v21 showRw) =====
    void CreateRewardText()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("RewardText");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        // v21: positioned at top 35% of screen
        rt.anchorMin = new Vector2(0.5f, 0.65f);
        rt.anchorMax = new Vector2(0.5f, 0.65f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(300, 50);

        rewardText = go.AddComponent<TextMeshProUGUI>();
        rewardText.fontSize = 16;
        rewardText.fontStyle = FontStyles.Bold;
        rewardText.alignment = TextAlignmentOptions.Center;
        rewardText.enableWordWrapping = false;
        rewardText.raycastTarget = false;
        rewardText.color = new Color(1, 1, 1, 0);
    }

    /// <summary>Show a reward/announcement text in the center, fades out after ~1.3s.</summary>
    public void ShowReward(string text, Color color)
    {
        if (rewardText == null) return;
        rewardText.text = text;
        rewardText.color = color;
        rewardTimer = 1.3f; // v21: 80 frames ≈ 1.3s
    }

    void Update()
    {
        // Fade out reward text
        if (rewardTimer > 0 && rewardText != null)
        {
            rewardTimer -= Time.deltaTime;
            if (rewardTimer <= 0)
            {
                rewardText.color = new Color(rewardText.color.r, rewardText.color.g, rewardText.color.b, 0);
            }
            else if (rewardTimer < 0.3f)
            {
                // Fade out in last 0.3s
                float a = rewardTimer / 0.3f;
                rewardText.color = new Color(rewardText.color.r, rewardText.color.g, rewardText.color.b, a);
            }
        }
    }

    // ===== LEVEL SELECT =====
    void CreateLevelSelectUI()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // "Levels" button at top-left corner
        var btnGo = new GameObject("LevelsButton");
        btnGo.transform.SetParent(canvas.transform, false);
        var btnRT = btnGo.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0, 1);
        btnRT.anchorMax = new Vector2(0, 1);
        btnRT.anchoredPosition = new Vector2(30, -8);
        btnRT.sizeDelta = new Vector2(50, 18);

        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(1, 1, 1, 0.1f);

        levelsButton = btnGo.AddComponent<Button>();
        levelsButton.onClick.AddListener(ShowLevelSelect);
        // Ensure button is on top of all other UI
        btnGo.transform.SetAsLastSibling();

        var btnTextGo = new GameObject("Text");
        btnTextGo.transform.SetParent(btnGo.transform, false);
        var btnTextRT = btnTextGo.AddComponent<RectTransform>();
        btnTextRT.anchorMin = Vector2.zero;
        btnTextRT.anchorMax = Vector2.one;
        btnTextRT.offsetMin = Vector2.zero;
        btnTextRT.offsetMax = Vector2.zero;
        var btnText = btnTextGo.AddComponent<TextMeshProUGUI>();
        btnText.text = "Levels";
        btnText.fontSize = 12;
        btnText.fontStyle = FontStyles.Bold;
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.color = new Color(1, 1, 1, 0.6f);

        // Level select panel (hidden by default)
        levelSelectPanel = new GameObject("LevelSelect");
        levelSelectPanel.transform.SetParent(canvas.transform, false);
        var panelRT = levelSelectPanel.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        var panelImg = levelSelectPanel.AddComponent<Image>();
        panelImg.color = new Color(0.04f, 0.05f, 0.08f, 1f); // solid background matching game bg

        levelSelectPanel.SetActive(false);
    }

    public void ShowLevelSelect()
    {
        Debug.Log("[GravityMatch] ShowLevelSelect called");
        if (levelSelectPanel == null) return;

        // Clear old children
        foreach (Transform child in levelSelectPanel.transform)
            Destroy(child.gameObject);

        var gm = GameManager.Instance;

        // Title
        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(levelSelectPanel.transform, false);
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

        // Level buttons in a grid
        int count = gm.LevelCount;
        int cols = 3;
        float btnW = 70, btnH = 70, gap = 10;
        float gridW = cols * btnW + (cols - 1) * gap;
        float startX = -gridW / 2f + btnW / 2f;
        float startY = -120;

        for (int i = 0; i < count; i++)
        {
            int lvIndex = i; // capture for closure
            int row = i / cols;
            int col = i % cols;

            var go = new GameObject($"LvBtn{i}");
            go.transform.SetParent(levelSelectPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(startX + col * (btnW + gap), startY - row * (btnH + gap));
            rt.sizeDelta = new Vector2(btnW, btnH);

            var img = go.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.06f);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => {
                gm.LoadLevel(lvIndex);
                levelSelectPanel.SetActive(false);
            });

            // Level number
            var numGo = new GameObject("Num");
            numGo.transform.SetParent(go.transform, false);
            var numRT = numGo.AddComponent<RectTransform>();
            numRT.anchorMin = Vector2.zero;
            numRT.anchorMax = Vector2.one;
            numRT.offsetMin = new Vector2(0, 10);
            numRT.offsetMax = Vector2.zero;
            var numText = numGo.AddComponent<TextMeshProUGUI>();
            numText.text = (i + 1).ToString();
            numText.fontSize = 22;
            numText.fontStyle = FontStyles.Bold;
            numText.alignment = TextAlignmentOptions.Center;
            numText.color = Color.white;

            // Stars display
            int starCount = gm.LevelStars[i];
            var starGo = new GameObject("Stars");
            starGo.transform.SetParent(go.transform, false);
            var starRT = starGo.AddComponent<RectTransform>();
            starRT.anchorMin = new Vector2(0, 0);
            starRT.anchorMax = new Vector2(1, 0);
            starRT.anchoredPosition = new Vector2(0, 12);
            starRT.sizeDelta = new Vector2(0, 14);
            var starText = starGo.AddComponent<TextMeshProUGUI>();
            starText.text = starCount > 0 ? new string('*', starCount) : "-";
            starText.fontSize = 11;
            starText.alignment = TextAlignmentOptions.Center;
            starText.color = new Color(0.937f, 0.624f, 0.153f); // amber
        }

        // Close button
        var closeGo = new GameObject("CloseBtn");
        closeGo.transform.SetParent(levelSelectPanel.transform, false);
        var closeRT = closeGo.AddComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(0.5f, 0);
        closeRT.anchorMax = new Vector2(0.5f, 0);
        closeRT.anchoredPosition = new Vector2(0, 80);
        closeRT.sizeDelta = new Vector2(100, 32);

        var closeImg = closeGo.AddComponent<Image>();
        closeImg.color = new Color(1, 1, 1, 0.1f);

        var closeBtn = closeGo.AddComponent<Button>();
        closeBtn.onClick.AddListener(() => levelSelectPanel.SetActive(false));

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

        levelSelectPanel.SetActive(true);
    }
}
