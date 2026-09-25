using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // Observable<T>.Call拡張メソッド用
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// InGame1V1（1P vs 2P対戦）の結果画面の制御。
///
/// ・「HIT ANY BUTTON」を点滅させつつ、押されるたびに画面のひびを段階的に表示し、
///   既定回数連打されたら画面が割れる演出→次のシーン（デフォルトはTitle）へ遷移する。
///   このロジックは GameClearController1.cs / GameOverController2.cs /
///   TitleController.cs と同じ構成。
/// </summary>
public class InGame1V1ResultController : MonoBehaviour
{
    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip hitSE;      // 1回押すごとに鳴る「ドンッ」
    [SerializeField] AudioClip crackSE;    // 規定回数連打された時に鳴る「バリィィィン！」

    [Header("UI")]
    [SerializeField] TMP_Text hitAnyButtonText; // 「ボタンを連打して続行しろ！（HIT ANY BUTTON）」
    [SerializeField] GameObject crackEffect;    // 最後のヒットで発動する「画面が割れる」パーティクル／アニメーション（Inspectorで非アクティブにしておく）

    [Header("ひび割れ演出（画像を上から順に重ねて表示）")]
    [SerializeField] Sprite[] crackStageSprites;  // ひびの段階Sprite。配列の並び順（上から順）に1枚ずつ重ねて表示していく
    [SerializeField] Transform crackSpriteParent; // 生成するSpriteRendererの親（未設定ならこのオブジェクト自身の位置に生成）
    [SerializeField] string crackSortingLayerName = "Default"; // 生成するSpriteRendererのSorting Layer名
    [SerializeField] int crackBaseSortingOrder = 0; // 1枚目のSorting Order（配列のインデックス分だけ加算して重ね順を作る）
    [SerializeField] float crackStageInterval = 0.5f; // ひびが自動で1段階進む間隔（秒）
    [SerializeField] float crackFadeDuration = 0.15f; // 各ひび画像がフェードインする時間（秒）。0にすると瞬時に表示
    [SerializeField] AnimationCurve crackFadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f); // フェードインの速度カーブ（お好みで調整可）

    [Header("設定")]
    [SerializeField] int requiredHits = 3;          // 遷移に必要な連打回数
    [SerializeField] float blinkInterval = 0.4f;    // 文字の点滅間隔
    [SerializeField] float transitionDelay = 0.7f;  // 割れる演出後、シーン遷移までのウェイト
    [SerializeField] string nextSceneName = "Title"; // 遷移先シーン名

    int hitCount;
    int revealedStageCount; // crackStageSpritesのうち、すでに表示済みの枚数
    bool isTransitioning;
    System.IDisposable anyButtonListener;
    Coroutine blinkRoutine;
    Coroutine crackAutoRevealRoutine;
    readonly System.Collections.Generic.List<Coroutine> crackFadeRoutines = new System.Collections.Generic.List<Coroutine>();
    SpriteRenderer[] crackRenderers; // crackStageSpritesから自動生成する表示用SpriteRenderer（内部管理）

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        // これが無いと、一度この画面を通過した後にオブジェクトが再利用された場合、
        // 二度と入力を受け付けなくなる（過去に他画面で起きたのと同じ不具合パターン）。
        hitCount = 0;
        revealedStageCount = 0;
        isTransitioning = false;

        if (crackEffect != null) crackEffect.SetActive(false);
        if (hitAnyButtonText != null) hitAnyButtonText.enabled = true;

        // ひび割れの段階表示もリセット（再入場時に前回のひびが残らないように）
        foreach (var routine in crackFadeRoutines)
        {
            if (routine != null) StopCoroutine(routine);
        }
        crackFadeRoutines.Clear();

        EnsureCrackRenderers();

        if (crackRenderers != null)
        {
            foreach (var img in crackRenderers)
            {
                if (img == null) continue;
                img.gameObject.SetActive(false);
                Color c = img.color;
                c.a = 0f;
                img.color = c;
            }
        }

        // 「何らかのボタンが押された」を検知（キーボード／ゲームパッド／マウス共通）
        anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);

        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(BlinkText());

        // ひびはボタン操作と関係なく、時間経過で自動的に1段階ずつ進める
        if (crackAutoRevealRoutine != null) StopCoroutine(crackAutoRevealRoutine);
        crackAutoRevealRoutine = StartCoroutine(AutoRevealCrackStages());
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

    // crackStageSprites（Spriteアセット）から、表示用のSpriteRendererを1回だけ自動生成する。
    // 生成済みなら何もしない（OnEnableのたびに重複生成しないように）
    void EnsureCrackRenderers()
    {
        if (crackStageSprites == null || crackStageSprites.Length == 0) return;
        if (crackRenderers != null && crackRenderers.Length == crackStageSprites.Length) return;

        Transform parent = crackSpriteParent != null ? crackSpriteParent : transform;
        crackRenderers = new SpriteRenderer[crackStageSprites.Length];

        for (int i = 0; i < crackStageSprites.Length; i++)
        {
            var go = new GameObject($"CrackStage_{i}");
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = crackStageSprites[i];
            sr.sortingLayerName = crackSortingLayerName;
            sr.sortingOrder = crackBaseSortingOrder + i;

            crackRenderers[i] = sr;
        }
    }

    // crackRenderersを配列の並び順（上から順）に、crackStageInterval間隔で自動的に1枚ずつフェード表示していく。
    // ボタン操作とは無関係に、シーンに入ってから時間経過だけで進む。
    IEnumerator AutoRevealCrackStages()
    {
        if (crackRenderers == null || crackRenderers.Length == 0) yield break;

        for (int i = 0; i < crackRenderers.Length; i++)
        {
            // 既に画面が割れる演出（PlayCrackAndTransition）が始まっていたら、自動表示は打ち切る
            if (isTransitioning) yield break;

            SpriteRenderer img = crackRenderers[i];
            if (img != null)
            {
                crackFadeRoutines.Add(StartCoroutine(FadeInCrackImage(img)));
            }
            revealedStageCount = i + 1;

            yield return new WaitForSeconds(crackStageInterval);
        }
    }

    IEnumerator FadeInCrackImage(SpriteRenderer img)
    {
        img.gameObject.SetActive(true);

        Color c = img.color;

        if (crackFadeDuration <= 0f)
        {
            c.a = 1f;
            img.color = c;
            yield break;
        }

        c.a = 0f;
        img.color = c;

        float t = 0f;
        while (t < crackFadeDuration)
        {
            t += Time.deltaTime;
            float ratio = crackFadeCurve.Evaluate(Mathf.Clamp01(t / crackFadeDuration));
            c.a = ratio;
            img.color = c;
            yield return null;
        }

        c.a = 1f;
        img.color = c;
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
