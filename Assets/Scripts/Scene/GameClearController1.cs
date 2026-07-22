using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // Observable<T>.Call拡張メソッド用
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// タイトル画面（待機画面）の制御。
/// 「HIT ANY BUTTON」を点滅させつつ、3回連打されたら
/// 画面が割れる演出→メインメニューへ遷移する。
/// 旧Title.csのバグ（SEが毎フレーム再生される／連打を数えていない）を修正。
/// </summary>
public class GameClearController : MonoBehaviour
{
    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip hitSE;      // 1回押すごとに鳴る「ドンッ」
    [SerializeField] AudioClip crackSE;    // 3回目に鳴る「バリィィィン！」

    [Header("UI")]
    [SerializeField] TMP_Text hitAnyButtonText; // 「ボタンを連打して始めろ！（HIT ANY BUTTON）」
    [SerializeField] GameObject crackEffect;    // 画面が割れるパーティクル／アニメーション（Inspectorで非アクティブにしておく）

    [Header("設定")]
    [SerializeField] int requiredHits = 3;         // 遷移に必要な連打回数
    [SerializeField] float blinkInterval = 0.4f;   // 文字の点滅間隔
    [SerializeField] float transitionDelay = 0.7f; // 割れる演出後、シーン遷移までのウェイト
    //[SerializeField] string nextSceneName = "MainMenu"; // 直接InGameではなくメインメニューへ

    int hitCount;
    bool isTransitioning;
    System.IDisposable anyButtonListener;

    void Start()
    {
        if (se == null) se = GetComponent<AudioSource>();
        StartCoroutine(BlinkText());
    }

    void OnEnable()
    {
        // 「何らかのボタンが押された」を検知（キーボード／ゲームパッド／マウス共通）
        anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);
    }

    void OnDisable()
    {
        anyButtonListener?.Dispose();
    }

    void OnAnyButtonPressed(InputControl control)
    {
        if (isTransitioning) return;

        hitCount++;
        se.PlayOneShot(hitSE);

        if (hitCount >= requiredHits)
        {
            StartCoroutine(PlayCrackAndTransition());
        }
    }

    IEnumerator PlayCrackAndTransition()
    {
        isTransitioning = true;

        if (crackEffect != null) crackEffect.SetActive(true);
        se.PlayOneShot(crackSE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene("Title");
    }

    IEnumerator BlinkText()
    {
        if (hitAnyButtonText == null) yield break;

        while (!isTransitioning)
        {
            hitAnyButtonText.enabled = !hitAnyButtonText.enabled;
            yield return new WaitForSeconds(blinkInterval);
        }
        hitAnyButtonText.enabled = false;
    }
}
