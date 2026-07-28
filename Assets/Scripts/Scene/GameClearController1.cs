using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // Observable<T>.Call拡張メソッド用
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// ゲームクリア画面の制御。
/// 「HIT ANY BUTTON」を点滅させつつ、3回連打されたら
/// 画面が割れる演出→タイトル画面へ遷移する。
/// TitleController.cs の構成を踏襲（SEが毎フレーム再生される／連打を数えていないバグの修正版）。
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
    [SerializeField] string nextSceneName = "Title"; // 遷移先シーン名

    int hitCount;
    bool isTransitioning;
    System.IDisposable anyButtonListener;
    Coroutine blinkRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        // isTransitioning や hitCount が戻らないと、一度この画面を通過した後に
        // オブジェクトが再利用された場合、二度と入力を受け付けなくなる。
        hitCount = 0;
        isTransitioning = false;

        if (crackEffect != null) crackEffect.SetActive(false);
        if (hitAnyButtonText != null) hitAnyButtonText.enabled = true;

        // 「何らかのボタンが押された」を検知（キーボード／ゲームパッド／マウス共通）
        anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);

        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(BlinkText());
    }

    void Start()
    {
        if (se == null) se = GetComponent<AudioSource>();
    }

    void OnDisable()
    {
        anyButtonListener?.Dispose();
        anyButtonListener = null;
    }

    void OnAnyButtonPressed(InputControl control)
    {
        if (isTransitioning) return;

        hitCount++;
        if (se != null && hitSE != null) se.PlayOneShot(hitSE);

        if (hitCount >= requiredHits)
        {
            StartCoroutine(PlayCrackAndTransition());
        }
    }

    IEnumerator PlayCrackAndTransition()
    {
        isTransitioning = true;

        if (crackEffect != null) crackEffect.SetActive(true);
        if (se != null && crackSE != null) se.PlayOneShot(crackSE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
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
