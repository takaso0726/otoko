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
    [Tooltip("効果音を鳴らすAudioSource。未設定の場合は、このオブジェクトに付いているAudioSourceを自動で使う。")]
    [SerializeField] AudioSource se;
    [Tooltip("ボタンを1回押すごとに鳴る効果音（「ドンッ」）。未設定なら鳴らさない。")]
    [SerializeField] AudioClip hitSE;      // 1回押すごとに鳴る「ドンッ」
    [Tooltip("規定回数（Required Hits）連打されて画面が割れる瞬間に鳴る効果音（「バリィィィン！」）。未設定なら鳴らさない。")]
    [SerializeField] AudioClip crackSE;    // 規定回数連打された時に鳴る「バリィィィン！」

    [Header("UI")]
    [Tooltip("「HIT ANY BUTTON」のテキスト。Blink Intervalの間隔で点滅し、画面が割れる演出が始まると非表示になる。")]
    [SerializeField] TMP_Text hitAnyButtonText; // 「ボタンを連打して続行しろ！（HIT ANY BUTTON）」
    [Tooltip("規定回数連打された瞬間に有効化される「画面が割れる」演出（パーティクル／アニメーション）のオブジェクト。Inspectorでは非アクティブにしておく。")]
    [SerializeField] GameObject crackEffect;    // 最後のヒットで発動する「画面が割れる」パーティクル／アニメーション（Inspectorで非アクティブにしておく）

    [Header("ひび割れ演出（画像を上から順に重ねて表示）")]
    [Tooltip("ひび割れの各段階のSprite。配列の上（Element 0）から順に1枚ずつ重ねてフェード表示される。前半（Slow Stage Count枚）はCrack Stage Interval、後半はCrack Stage Interval Fastの間隔で表示する。各ひびごとのSEはCrack Stage SEsで指定する。\nボタン操作とは無関係に、シーンに入ってからの時間経過で進む。")]
    [SerializeField] Sprite[] crackStageSprites;  // ひびの段階Sprite。配列の並び順（上から順）に1枚ずつ重ねて表示していく
    [Tooltip("ひび割れの各段階（Crack Stage Sprites）が表示される瞬間に鳴らすSE。\n" +
             "Element番号がCrack Stage Spritesと対応する（Element 0 = 1枚目のひびの表示時に鳴る）。\n" +
             "要素がNone、またはSprites より短い場合、その段階では鳴らさない。")]
    [SerializeField] AudioClip[] crackStageSEs;   // 各ひびSpriteが表示される瞬間に鳴らすSE（Spriteと同じ並び順）
    [Tooltip("ひび割れ用のSpriteRendererを生成する親Transform。未設定ならこのオブジェクト自身の下に生成する。")]
    [SerializeField] Transform crackSpriteParent; // 生成するSpriteRendererの親（未設定ならこのオブジェクト自身の位置に生成）
    [Tooltip("ひび割れ用SpriteRendererのSorting Layer名。プロジェクトに存在するLayer名を指定する。")]
    [SerializeField] string crackSortingLayerName = "Default"; // 生成するSpriteRendererのSorting Layer名
    [Tooltip("1枚目のSorting Order。2枚目以降はインデックスの分だけ加算されるので、配列の後ろの要素ほど手前に重なる。")]
    [SerializeField] int crackBaseSortingOrder = 0; // 1枚目のSorting Order（配列のインデックス分だけ加算して重ね順を作る）
    [Tooltip("前半（Slow Stage Count枚）のひびが、1枚ずつ表示される間隔（秒）。大きいほどゆっくり。")]
    [SerializeField] float crackStageInterval = 0.5f; // 前半（slowStageCount枚）のひびが進む間隔（秒）
    [Tooltip("配列の上から何枚目までを「ゆっくり」（Crack Stage Interval）で表示するか。\n例：8枚のうち前半4枚をゆっくり、後半4枚を速くしたいなら4。\n0なら全部を速い間隔（Crack Stage Interval Fast）で表示する。")]
    [Min(0)]
    [SerializeField] int slowStageCount = 4; // 前半として「ゆっくり」表示する枚数
    [Tooltip("後半（Slow Stage Count枚より後ろ）のひびが、1枚ずつ表示される間隔（秒）。小さいほど速い。")]
    [SerializeField] float crackStageIntervalFast = 0.15f; // 後半のひびが進む間隔（秒）
    [Tooltip("各ひび画像がフェードインして完全に表示されるまでの時間（秒）。0にすると瞬時に表示する。")]
    [SerializeField] float crackFadeDuration = 0.15f; // 各ひび画像がフェードインする時間（秒）。0にすると瞬時に表示
    [Tooltip("ひび画像のフェードインの速度カーブ。横軸=経過割合(0〜1)、縦軸=不透明度(0〜1)。")]
    [SerializeField] AnimationCurve crackFadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f); // フェードインの速度カーブ（お好みで調整可）

    [Header("設定")]
    [Tooltip("画面が割れてシーン遷移するまでに必要なボタン連打回数。キーボード／ゲームパッド／マウスのどのボタンでも1回と数える。")]
    [SerializeField] int requiredHits = 3;          // 遷移に必要な連打回数
    [Tooltip("「HIT ANY BUTTON」の文字が点滅する間隔（秒）。")]
    [SerializeField] float blinkInterval = 0.4f;    // 文字の点滅間隔
    [Tooltip("画面が割れる演出が始まってから、次のシーンへ遷移するまでの待ち時間（秒）。")]
    [SerializeField] float transitionDelay = 0.7f;  // 割れる演出後、シーン遷移までのウェイト
    [Tooltip("遷移先のシーン名。Build Settingsのシーン一覧に登録されている必要がある。")]
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

    // crackRenderersを配列の並び順（上から順）に、自動的に1枚ずつフェード表示していく。
    // 前半（slowStageCount枚）はcrackStageInterval（ゆっくり）、後半はcrackStageIntervalFast（速い）の間隔で表示する。
    // ※「次の1枚が出るまでの間隔」は、次に出る1枚が前半か後半かで決まる
    //   （例：slowStageCount=4なら、1〜4枚目はゆっくり、5枚目以降は速く出る）。
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

            // このひびに対応するSEを鳴らす
            PlayCrackStageSE(i);

            // 最後の1枚を出した後は待つ必要がない
            if (i + 1 >= crackRenderers.Length) yield break;

            // 次に出す1枚（インデックス i+1）が前半ならゆっくり、後半なら速い間隔で待つ
            float interval = (i + 1 < slowStageCount) ? crackStageInterval : crackStageIntervalFast;
            yield return new WaitForSeconds(interval);
        }
    }

    // i枚目のひびSpriteに対応するSE（crackStageSEs[i]）を鳴らす。未設定・範囲外・Noneなら何もしない。
    void PlayCrackStageSE(int index)
    {
        if (crackStageSEs == null || index < 0 || index >= crackStageSEs.Length) return;
        var clip = crackStageSEs[index];
        if (clip == null) return;

        // OnEnable（Startより先に呼ばれる）の中で1枚目が表示される場合に備えて、ここでもAudioSourceを補完する
        if (se == null) se = GetComponent<AudioSource>();
        if (se != null) se.PlayOneShot(clip);
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
