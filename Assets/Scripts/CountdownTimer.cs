using UnityEngine;
using TMPro; // TextMeshProを使わない場合は削除し、UnityEngine.UIのTextに変更してください

/// <summary>
/// 1秒ずつ減っていくカウントダウンタイマー
/// InspectorでstartTimeとtimerTextを設定して使用します
/// </summary>
public class CountdownTimer : MonoBehaviour
{
    [Header("タイマー設定")]
    public float startTime = 60f;      // 開始秒数
    public bool startOnAwake = true;   // 自動でスタートするか

    [Header("表示設定")]
    public TextMeshProUGUI timerText;  // 表示用テキスト(なければ空でOK)
    public bool showMinutes = true;    // 分:秒 形式で表示するか

    private float remainingTime;
    private bool isRunning;

    public bool IsRunning => isRunning;
    public float RemainingTime => remainingTime;

    void Awake()
    {
        remainingTime = startTime;
        isRunning = startOnAwake;
        UpdateDisplay();
    }

    void Update()
    {
        if (!isRunning) return;

        if (remainingTime > 0f)
        {
            remainingTime -= Time.deltaTime;

            if (remainingTime <= 0f)
            {
                remainingTime = 0f;
                isRunning = false;
                OnTimerEnd();
            }

            UpdateDisplay();
        }
    }

    // --- 外部から呼び出す操作用メソッド ---

    public void StartTimer()
    {
        isRunning = true;
    }

    public void PauseTimer()
    {
        isRunning = false;
    }

    public void ResetTimer(float newTime = -1f)
    {
        remainingTime = newTime >= 0f ? newTime : startTime;
        UpdateDisplay();
    }

    public void AddTime(float seconds)
    {
        remainingTime = Mathf.Max(0f, remainingTime + seconds);
        UpdateDisplay();
    }

    // --- 表示処理 ---

    void UpdateDisplay()
    {
        if (timerText == null) return;

        if (showMinutes)
        {
            int minutes = Mathf.FloorToInt(remainingTime / 60f);
            int seconds = Mathf.FloorToInt(remainingTime % 60f);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
        else
        {
            timerText.text = Mathf.CeilToInt(remainingTime).ToString();
        }
    }

    // --- 終了時の処理 ---

    void OnTimerEnd()
    {
        Debug.Log("タイマー終了!");
        // ここに終了時の処理を追加(例: シーン遷移、ゲームオーバー処理など)
    }
}
