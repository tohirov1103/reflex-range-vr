using UnityEngine;

/// <summary>
/// Runs the game: one 60-second round, the score, streak, accuracy, reaction time and best score.
/// It only keeps the numbers - ScoreboardUI shows them.
/// </summary>
public class GameManager : MonoBehaviour
{
    // Singleton: other scripts can call GameManager.Instance.AddScore(...)
    public static GameManager Instance { get; private set; }

    [Header("Round")]
    [SerializeField] float roundLength = 60f;

    [Header("Scene references")]
    [SerializeField] TargetSpawner spawner;
    [SerializeField] GameObject startTarget;
    [SerializeField] ScoreboardUI scoreboard;
    [SerializeField] HitPopup hitPopupPrefab;
    [SerializeField] Transform player;          // the headset camera, so pop-ups face the player
    [SerializeField] AudioSource audioSource;

    [Header("Pop-up colours")]
    [SerializeField] Color outerColor = Color.white;
    [SerializeField] Color middleColor = new Color(0.23f, 0.91f, 1f);    // cyan
    [SerializeField] Color bullseyeColor = new Color(1f, 0.89f, 0.36f);  // yellow

    const string BestScoreKey = "ReflexRange.BestScore";

    int score;
    int shotsFired;
    int targetsHit;
    int streak;
    float totalReactionTime;
    int bestScore;
    float timeLeft;

    public bool IsPlaying { get; private set; }

    // 0 at the start of a round, 1 at the end. The spawner uses it to spawn targets faster.
    public float RoundProgress => 1f - timeLeft / roundLength;

    int Accuracy => shotsFired > 0 ? Mathf.RoundToInt(100f * targetsHit / shotsFired) : 0;

    void Awake()
    {
        Instance = this;
        bestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
    }

    void Start()
    {
        spawner.enabled = false;
        timeLeft = roundLength;
        RefreshScoreboard();
        scoreboard.ShowMessage("Grab the pistol and shoot the green target");
    }

    void Update()
    {
        if (!IsPlaying)
            return;

        // Time.deltaTime = seconds since the last frame, so the timer is correct at any frame rate
        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            EndRound();
            return;
        }

        RefreshScoreboard();
    }

    public void StartRound()
    {
        score = 0;
        shotsFired = 0;
        targetsHit = 0;
        streak = 0;
        totalReactionTime = 0f;
        timeLeft = roundLength;
        IsPlaying = true;

        startTarget.SetActive(false);
        spawner.enabled = true;
        audioSource.PlayOneShot(SoundMaker.Hit);
        RefreshScoreboard();
        scoreboard.ShowMessage("GO!");
    }

    void EndRound()
    {
        timeLeft = 0f;
        IsPlaying = false;

        spawner.enabled = false;
        spawner.ClearTargets();
        startTarget.SetActive(true);

        bool newBest = score > bestScore;
        if (newBest)
        {
            bestScore = score;
            PlayerPrefs.SetInt(BestScoreKey, bestScore);
            PlayerPrefs.Save();
        }

        float averageReaction = targetsHit > 0 ? totalReactionTime / targetsHit : 0f;
        scoreboard.ShowResults(score, targetsHit, shotsFired, Accuracy, averageReaction, newBest, CalculateGrade());
    }

    // S/A/B/C from accuracy and number of hits (so one lucky shot is not an S)
    string CalculateGrade()
    {
        if (Accuracy >= 90 && targetsHit >= 30) return "S";
        if (Accuracy >= 75 && targetsHit >= 20) return "A";
        if (Accuracy >= 55 && targetsHit >= 10) return "B";
        return "C";
    }

    // Called by the pistol every time the trigger fires a shot
    public void RegisterShot()
    {
        if (IsPlaying)
            shotsFired++;
    }

    // Called by the pistol when a shot hits nothing - this breaks the streak
    public void RegisterMiss()
    {
        if (IsPlaying)
            streak = 0;
    }

    // Called by a target when it is hit. multiplier: 1 = outer ring, 2 = middle ring, 3 = bullseye
    public void AddScore(int basePoints, int multiplier, Vector3 hitPoint, float reactionTime)
    {
        if (!IsPlaying)
            return;

        int points = basePoints * multiplier;
        score += points;
        targetsHit++;
        streak++;
        totalReactionTime += reactionTime;

        string label = multiplier == 3 ? "BULLSEYE" : multiplier == 2 ? "MIDDLE" : "";
        Color color = multiplier == 3 ? bullseyeColor : multiplier == 2 ? middleColor : outerColor;

        // Floating "+30" at the target, turned to face the player
        HitPopup popup = Instantiate(hitPopupPrefab, hitPoint, Quaternion.LookRotation(hitPoint - player.position));
        popup.Show("+" + points + (label != "" ? "\n<size=60%>" + label : ""), color);

        scoreboard.ShowMessage($"<size=120%>+{points}</size>  {label}");
        audioSource.PlayOneShot(SoundMaker.Hit);
    }

    void RefreshScoreboard()
    {
        scoreboard.ShowLive(score, timeLeft, roundLength, Accuracy, bestScore, streak);
    }
}
