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

    void Start()
    {
        retryButton.onClick.AddListener(() => GameManager.Instance.Restart());
        nextButton.onClick.AddListener(() => GameManager.Instance.NextLevel());
        overlay.SetActive(false);
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

    public void ShowWin(int stars, int score, int ballsLeft)
    {
        overlay.SetActive(true);
        starsText.text = new string('\u2605', stars) + new string('\u2606', 3 - stars);
        resultTitle.text = "All clear!";
        resultDetail.text = $"{score} pts (bonus: {ballsLeft}×200)";
        nextButton.gameObject.SetActive(true);
    }

    public void ShowLose(int ballsRemaining)
    {
        overlay.SetActive(true);
        starsText.text = "";
        resultTitle.text = "Out of balls";
        resultDetail.text = $"{ballsRemaining} balls left";
        nextButton.gameObject.SetActive(false);
    }
}
