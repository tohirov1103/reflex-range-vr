using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The world-space scoreboard ("Neon Arcade" style).
/// Two panels on one canvas: "live" during a round and "results" after it.
/// GameManager decides WHAT to show, this script only decides HOW it looks.
/// </summary>
public class ScoreboardUI : MonoBehaviour
{
    [Header("Live panel")]
    [SerializeField] GameObject livePanel;
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI secondsText;
    [SerializeField] Image timerRing;
    [SerializeField] TextMeshProUGUI accuracyText;
    [SerializeField] TextMeshProUGUI bestText;
    [SerializeField] GameObject streakBadge;
    [SerializeField] TextMeshProUGUI streakText;
    [SerializeField] TextMeshProUGUI messageText;

    [Header("Results panel")]
    [SerializeField] GameObject resultsPanel;
    [SerializeField] TextMeshProUGUI gradeText;
    [SerializeField] TextMeshProUGUI finalScoreText;
    [SerializeField] TextMeshProUGUI hitsText;
    [SerializeField] TextMeshProUGUI shotsText;
    [SerializeField] TextMeshProUGUI finalAccuracyText;
    [SerializeField] TextMeshProUGUI reactionText;
    [SerializeField] GameObject newBestBadge;

    [Header("Colours")]
    [SerializeField] Color accent = new Color(0.23f, 0.91f, 1f);     // cyan
    [SerializeField] Color warning = new Color(1f, 0.24f, 0.65f);    // magenta, last 10 seconds
    [SerializeField] float warningSeconds = 10f;

    public void ShowLive(int score, float timeLeft, float roundLength, int accuracy, int best, int streak)
    {
        livePanel.SetActive(true);
        resultsPanel.SetActive(false);

        scoreText.text = score.ToString();
        accuracyText.text = accuracy + "%";
        bestText.text = best.ToString();

        // The ring empties as time runs out and turns magenta near the end
        Color timeColor = timeLeft <= warningSeconds ? warning : accent;
        timerRing.fillAmount = timeLeft / roundLength;
        timerRing.color = timeColor;
        secondsText.text = Mathf.CeilToInt(timeLeft).ToString();
        secondsText.color = timeColor;

        // Only show the streak once there are at least 2 hits in a row
        streakBadge.SetActive(streak >= 2);
        streakText.text = "STREAK ×" + streak;
    }

    public void ShowMessage(string message)
    {
        messageText.text = message;
    }

    public void ShowResults(int score, int hits, int shots, int accuracy, float averageReaction, bool newBest, string grade)
    {
        livePanel.SetActive(false);
        resultsPanel.SetActive(true);

        gradeText.text = grade;
        finalScoreText.text = score.ToString();
        hitsText.text = hits.ToString();
        shotsText.text = shots.ToString();
        finalAccuracyText.text = accuracy + "%";
        reactionText.text = hits > 0 ? averageReaction.ToString("0.00") + "s" : "-";
        newBestBadge.SetActive(newBest);
    }
}
