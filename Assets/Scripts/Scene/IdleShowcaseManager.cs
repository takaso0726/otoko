using UnityEngine;
using TMPro;

/// <summary>
/// タイトル／メインメニューで一定時間操作が無いと、
/// 「魂のデモバトル（CPU同士の対戦映像）」か
/// 「漢気の格言（黒画面＋筆文字＋渋いナレーション）」をランダムに流す。
/// メニュー側のUpdateから NotifyInput() を呼んでもらう想定。
/// </summary>
public class IdleShowcaseManager : MonoBehaviour
{
    [Header("放置検知")]
    [SerializeField] float idleThreshold = 60f; // 何も操作しない状態が何秒続いたら発動するか

    [Header("① 魂のデモバトル")]
    [SerializeField] GameObject demoBattleVideoObject; // VideoPlayerをアタッチしたオブジェクト

    [Header("② 漢気の格言")]
    [SerializeField] GameObject quoteScreenObject; // 黒画面＋テキストのルートオブジェクト
    [SerializeField] TMP_Text quoteText;
    [SerializeField] AudioSource narrationSource;
    [SerializeField] AudioClip[] narrationClips; // quotesと同じ並び順で対応させる
    [SerializeField] string[] quotes =
    {
        "引かぬ、媚びぬ、省みぬ",
        "根性こそ最大の武器",
    };

    float idleTimer;
    bool showcaseActive;

    void Update()
    {
        if (showcaseActive) return;

        idleTimer += Time.deltaTime;
        if (idleTimer >= idleThreshold)
        {
            StartShowcase();
        }
    }

    /// <summary>何らかの入力があったときに外部（メニュー側）から呼ぶ</summary>
    public void NotifyInput()
    {
        idleTimer = 0f;
        if (showcaseActive) StopShowcase();
    }

    void StartShowcase()
    {
        showcaseActive = true;

        bool playDemoBattle = Random.value < 0.5f;
        if (playDemoBattle && demoBattleVideoObject != null)
        {
            demoBattleVideoObject.SetActive(true);
        }
        else if (quotes.Length > 0)
        {
            int index = Random.Range(0, quotes.Length);
            quoteText.text = quotes[index];
            quoteScreenObject.SetActive(true);

            if (narrationClips != null && index < narrationClips.Length && narrationClips[index] != null)
            {
                narrationSource.PlayOneShot(narrationClips[index]);
            }
        }
    }

    void StopShowcase()
    {
        showcaseActive = false;
        if (demoBattleVideoObject != null) demoBattleVideoObject.SetActive(false);
        if (quoteScreenObject != null) quoteScreenObject.SetActive(false);
        if (narrationSource != null) narrationSource.Stop();
    }
}
