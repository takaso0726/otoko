using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // Observable<T>.Call拡張メソッド用
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// InGame1V1（1P vs 2P対戦）の勝敗結果画面の制御。
///
/// ・画面左＝1P側、画面右＝2P側に、それぞれ「勝ち」「負け」のSpriteを
///   MatchResult.LastWinner の値に応じてImageへ差し替える。
/// ・「HIT ANY BUTTON」を点滅させつつ、既定回数連打されたら
///   画面が割れる演出→次のシーン（デフォルトはTitle）へ遷移する。
///   このロジックは GameClearController1.cs / GameOverController2.cs /
///   TitleController.cs と同じ構成。
/// </summary>
public class InGame1V1ResultController : MonoBehaviour
{
    [Header("1P側（画面左）の勝敗表示")]
    [SerializeField] Image p1ResultImage;   // 1P側の結果を表示するUI Image（シーンに配置しておく）
    [SerializeField] Sprite p1WinSprite;    // 1Pが勝った時に表示するSprite
    [SerializeField] Sprite p1LoseSprite;   // 1Pが負けた時に表示するSprite

    [Header("2P側（画面右）の勝敗表示")]
    [SerializeField] Image p2ResultImage;   // 2P側の結果を表示するUI Image（シーンに配置しておく）
    [SerializeField] Sprite p2WinSprite;    // 2Pが勝った時に表示するSprite
    [SerializeField] Sprite p2LoseSprite;   // 2Pが負けた時に表示するSprite

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip hitSE;      // 1回押すごとに鳴る「ドンッ」
    [SerializeField] AudioClip crackSE;    // 規定回数連打された時に鳴る「バリィィィン！」

    [Header("UI")]
    [SerializeField] TMP_Text hitAnyButtonText; // 「ボタンを連打して続行しろ！（HIT ANY BUTTON）」
    [SerializeField] GameObject crackEffect;    // 画面が割れるパーティクル／アニメーション（Inspectorで非アクティブにしておく）

    [Header("設定")]
    [SerializeField] int requiredHits = 3;          // 遷移に必要な連打回数
    [SerializeField] float blinkInterval = 0.4f;    // 文字の点滅間隔
    [SerializeField] float transitionDelay = 0.7f;  // 割れる演出後、シーン遷移までのウェイト
    [SerializeField] string nextSceneName = "Title"; // 遷移先シーン名

    int hitCount;
    bool isTransitioning;
    System.IDisposable anyButtonListener;
    Coroutine blinkRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        // これが無いと、一度この画面を通過した後にオブジェクトが再利用された場合、
        // 二度と入力を受け付けなくなる（過去に他画面で起きたのと同じ不具合パターン）。
        hitCount = 0;
        isTransitioning = false;

        if (crackEffect != null) crackEffect.SetActive(false);
        if (hitAnyButtonText != null) hitAnyButtonText.enabled = true;

        // 「何らかのボタンが押された」を検知（キーボード／ゲームパッド／マウス共通）
        anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);

        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(BlinkText());

        ShowResult();
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

    // MatchResult.LastWinner を見て、1P側・2P側それぞれのImageに「勝ち」「負け」Spriteを反映する
    void ShowResult()
    {
        bool p1Win = MatchResult.LastWinner == MatchResult.Winner.Player1;
        bool p2Win = MatchResult.LastWinner == MatchResult.Winner.Player2;

        // 引き分け(Draw)やNone(未設定)の場合は現状どちらも「負け」表示側になる。
        // 引き分け専用の表示が必要になったら、p1DrawSprite / p2DrawSpriteを追加して
        // ここの分岐にDrawの判定を足してください。
        SetResultSprite(p1ResultImage, p1Win ? p1WinSprite : p1LoseSprite);
        SetResultSprite(p2ResultImage, p2Win ? p2WinSprite : p2LoseSprite);
    }

    // ImageにSpriteを設定する。Sprite未設定の場合はImageを非表示にして
    // 「空の白い四角」が出ないようにする
    void SetResultSprite(Image image, Sprite sprite)
    {
        if (image == null) return;

        image.sprite = sprite;
        image.enabled = sprite != null;
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
