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
/// ・ひび画像は新しいものが表示（フェードイン完了）されるたびに、直前の画像を非表示にする。
///   常に最新の1枚だけが見える状態になる。
/// ・インゲームで勝利したプレイヤーが使っていたキャラ（セレクト画面の選択結果）を出現させる。
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

    [Header("ひび割れ演出（画像を上から順に表示。直前の画像は消える）")]
    [Tooltip("ひび割れの各段階のSprite。配列の上（Element 0）から順に1枚ずつフェード表示される。新しい画像のフェードインが完了すると、直前の画像は非表示になる。前半（Slow Stage Count枚）はCrack Stage Interval、後半はCrack Stage Interval Fastの間隔で表示する。各ひびごとのSEはCrack Stage SEsで指定する。\nボタン操作とは無関係に、シーンに入ってからの時間経過で進む。")]
    [SerializeField] Sprite[] crackStageSprites;  // ひびの段階Sprite。配列の並び順（上から順）に1枚ずつ表示していく
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

    [System.Serializable]
    public class TimedTextEntry
    {
        [Tooltip("時間が経つと消すTMPテキスト。結果画面に入った時に表示され、Hide After Seconds秒後に非表示になる。")]
        public TMP_Text text;
        [Tooltip("結果画面に入ってから、このテキストが消えるまでの時間（秒）。")]
        [Min(0f)]
        public float hideAfterSeconds = 3f;
    }

    [Header("時間で消えるテキスト")]
    [Tooltip("結果画面に入ってから一定時間が経つと消えるテキストの一覧。1つずつ消えるまでの時間を設定できる。「HIT ANY BUTTON」は別設定（Hit Any Button Text Duration）。")]
    [SerializeField] TimedTextEntry[] timedTexts;

    [System.Serializable]
    public class WinnerCharacterEntry
    {
        [Tooltip("CharacterSelectController側のCharacterEntry.characterName／GameInputManagerの対応表と完全一致させること")]
        public string characterName;
        [Tooltip("結果画面に出すキャラ。\n" +
                 "・シーン内に置いたオブジェクト → 勝利時にその場で出現（Inspectorでは非アクティブでも可）\n" +
                 "・プロジェクト内のPrefab → Winner Spawn Pointの位置に生成して出現\n" +
                 "※インゲーム用プレハブ（Player/PlayerInput付き）は入力・カメラ・GameMNGに干渉するため使わず、見た目だけのモデル／Prefabを指定すること。")]
        public GameObject displayObject;
    }

    [Header("勝者キャラクター演出")]
    [Tooltip("キャラ名 → 結果画面で出すキャラ の対応表。勝者が使っていたキャラ名（セレクト画面の選択結果）と一致するものが出現する。")]
    [SerializeField] WinnerCharacterEntry[] winnerCharacters;
    [Tooltip("Prefabを指定した場合に、勝者キャラを生成する位置・向き。シーン内オブジェクトを指定した場合は使われない。")]
    [SerializeField] Transform winnerSpawnPoint;
    [Tooltip("対応表に該当キャラが無かった／デバッグ起動でキャラ名が不明な時に出すキャラ（任意）。")]
    [SerializeField] GameObject fallbackWinnerDisplay;
    [Tooltip("結果画面に入ってから、勝者キャラが出現するまでの待ち時間（秒）。")]
    [SerializeField] float winnerAppearDelay = 0.3f;
    [Tooltip("勝者キャラが 0 → 元の大きさ まで拡大しながら出現する時間（秒）。0にすると瞬時に表示する。")]
    [SerializeField] float winnerPopDuration = 0.4f;
    [Tooltip("勝者キャラが出現する時に鳴らすSE。未設定なら鳴らさない。")]
    [SerializeField] AudioClip winnerAppearSE;
    [Tooltip("ONにすると、勝者キャラが出現する時にカメラの正面（カメラの方）を向く。シーン内オブジェクト／Prefabどちらにも適用される。")]
    [SerializeField] bool faceCameraOnAppear = true;
    [Tooltip("キャラが向く対象のカメラ。未設定ならCamera.main（MainCameraタグのカメラ）を使う。")]
    [SerializeField] Camera faceTargetCamera;
    [Tooltip("モデルの「正面」がZ+方向と違う場合の補正角度（Y軸回転・度）。通常は0。\n例：モデルが後ろ向きにカメラへ向いてしまう場合は180。")]
    [SerializeField] float faceCameraYawOffset = 0f;
    [Tooltip("【テスト用】1か2を入れると、インゲームの結果に関係なくそのプレイヤーを勝者として扱う。0なら通常どおりMatchResult.LastWinner（GameMNGが書き込む対戦結果）を使う。")]
    [Range(0, 2)]
    [SerializeField] int debugForceWinner = 0;

    [Header("設定")]
    [Tooltip("画面が割れてシーン遷移するまでに必要なボタン連打回数。キーボード／ゲームパッド／マウスのどのボタンでも1回と数える。")]
    [SerializeField] int requiredHits = 3;          // 遷移に必要な連打回数
    [Tooltip("「HIT ANY BUTTON」の文字が点滅する間隔（秒）。")]
    [SerializeField] float blinkInterval = 0.4f;    // 文字の点滅間隔
    [Tooltip("「HIT ANY BUTTON」の文字が、結果画面に入ってから何秒後に消えるか。0以下なら消えずに、画面が割れる演出が始まるまで点滅し続ける。\n消えた後もボタン連打のカウントは続く。")]
    [SerializeField] float hitAnyButtonTextDuration = 5f; // 文字を表示しておく時間（秒）。0以下なら消さない
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
    Coroutine winnerRoutine;
    GameObject winnerInstance; // Prefabから生成した勝者キャラ（再入場時に破棄する）
    // シーン内オブジェクトの元の大きさ（出現演出で0に縮めるため、あらかじめ保存しておく）
    readonly System.Collections.Generic.Dictionary<GameObject, Vector3> sceneDisplayBaseScales =
        new System.Collections.Generic.Dictionary<GameObject, Vector3>();

    void Awake()
    {
        if (winnerCharacters != null)
        {
            foreach (var entry in winnerCharacters)
            {
                if (entry == null || entry.displayObject == null) continue;
                if (entry.displayObject.scene.IsValid())
                    sceneDisplayBaseScales[entry.displayObject] = entry.displayObject.transform.localScale;
            }
        }
        if (fallbackWinnerDisplay != null && fallbackWinnerDisplay.scene.IsValid())
            sceneDisplayBaseScales[fallbackWinnerDisplay] = fallbackWinnerDisplay.transform.localScale;
    }

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

        // 勝者キャラ：前回生成したものを破棄し、シーン内のキャラはいったん全て隠してから、勝者のキャラだけ出現させる
        if (winnerInstance != null)
        {
            Destroy(winnerInstance);
            winnerInstance = null;
        }
        if (winnerCharacters != null)
        {
            foreach (var entry in winnerCharacters)
            {
                if (entry == null || entry.displayObject == null) continue;
                if (entry.displayObject.scene.IsValid()) entry.displayObject.SetActive(false);
            }
        }
        if (fallbackWinnerDisplay != null && fallbackWinnerDisplay.scene.IsValid())
            fallbackWinnerDisplay.SetActive(false);

        if (winnerRoutine != null) StopCoroutine(winnerRoutine);
        winnerRoutine = StartCoroutine(ShowWinnerCharacter());

        // 時間で消えるテキスト：表示してから、それぞれの時間が経ったら消す
        if (timedTexts != null)
        {
            foreach (var entry in timedTexts)
            {
                if (entry == null || entry.text == null) continue;
                entry.text.enabled = true;
                StartCoroutine(HideTextAfter(entry.text, entry.hideAfterSeconds));
            }
        }
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
    // 新しい画像のフェードインが完了したら、直前の画像を非表示にする（常に最新の1枚だけが見える）。
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
            SpriteRenderer prev = (i > 0) ? crackRenderers[i - 1] : null;

            if (img != null)
            {
                // 新しい画像のフェードインが完了したら、直前の画像を消す
                crackFadeRoutines.Add(StartCoroutine(FadeInCrackImage(img, prev)));
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

    // imgをフェードインさせ、完了したらprevious（直前の画像）を非表示にする。
    IEnumerator FadeInCrackImage(SpriteRenderer img, SpriteRenderer previous = null)
    {
        img.gameObject.SetActive(true);

        Color c = img.color;

        if (crackFadeDuration <= 0f)
        {
            c.a = 1f;
            img.color = c;
            HidePrevious(previous);
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

        // 新しい画像が完全に表示されてから、直前の画像を消す
        HidePrevious(previous);
    }

    // 直前のひび画像を非表示にする
    void HidePrevious(SpriteRenderer previous)
    {
        if (previous == null) return;
        previous.gameObject.SetActive(false);
    }

    // 全てのひび画像を非表示にする（画面が割れる演出に切り替える時に使用）
    void HideAllCrackImages()
    {
        foreach (var routine in crackFadeRoutines)
        {
            if (routine != null) StopCoroutine(routine);
        }
        crackFadeRoutines.Clear();

        if (crackRenderers == null) return;
        foreach (var img in crackRenderers)
        {
            if (img == null) continue;
            img.gameObject.SetActive(false);
        }
    }

    IEnumerator PlayCrackAndTransition()
    {
        isTransitioning = true;

        // 最後に残っているひび画像を消してから、画面が割れる演出に切り替える
        HideAllCrackImages();

        if (crackEffect != null) crackEffect.SetActive(true);
        if (se != null && crackSE != null) se.PlayOneShot(crackSE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // 勝者が使っていたキャラの名前を、
    // セレクト画面の選択結果（CharacterSelectionResult）から取得する。
    // 勝者のプレイヤー番号（1 or 2）。GameMNGが対戦終了時に書き込む MatchResult.LastWinner を読む。
    // 引き分け／未設定の場合は0を返す。
    int GetWinnerPlayer()
    {
        if (debugForceWinner > 0) return debugForceWinner;

        if (MatchResult.LastWinner == MatchResult.Winner.Player1) return 1;
        if (MatchResult.LastWinner == MatchResult.Winner.Player2) return 2;
        return 0;
    }

    string GetWinnerCharacterName()
    {
        // GameInputManagerが対戦開始時に記録したキャラ名（MatchCharacters）を優先し、
        // 空ならセレクト画面の選択結果にフォールバックする。
        switch (GetWinnerPlayer())
        {
            case 1:
                return !string.IsNullOrEmpty(MatchCharacters.Player1Name)
                    ? MatchCharacters.Player1Name
                    : CharacterSelectController.CharacterSelectionResult.Player1CharacterName;
            case 2:
                return !string.IsNullOrEmpty(MatchCharacters.Player2Name)
                    ? MatchCharacters.Player2Name
                    : CharacterSelectController.CharacterSelectionResult.Player2CharacterName;
            default: return null;
        }
    }

    // キャラ名に対応する表示用オブジェクトを探す。無ければfallbackWinnerDisplayを返す。
    GameObject FindWinnerDisplay(string characterName)
    {
        if (!string.IsNullOrEmpty(characterName) && winnerCharacters != null)
        {
            foreach (var entry in winnerCharacters)
            {
                if (entry != null && entry.characterName == characterName && entry.displayObject != null)
                    return entry.displayObject;
            }
        }
        return fallbackWinnerDisplay;
    }

    // キャラをカメラの正面（カメラの方）へ向ける。
    // 上下に傾かないよう、水平方向（Y軸回りの回転）だけを合わせる。
    void FaceCamera(Transform tr)
    {
        Camera cam = faceTargetCamera != null ? faceTargetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[InGame1V1ResultController] カメラが見つからないため、キャラをカメラへ向けられません。" +
                             "Face Target Cameraを設定するか、カメラにMainCameraタグを付けてください。");
            return;
        }

        Vector3 toCamera = cam.transform.position - tr.position;
        toCamera.y = 0f; // 水平方向のみ
        if (toCamera.sqrMagnitude < 0.0001f) return; // カメラ真上／真下などで向きが定まらない場合は何もしない

        tr.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up) * Quaternion.Euler(0f, faceCameraYawOffset, 0f);
    }

    // 勝者が使っていたキャラを、拡大しながら出現させる。
    // 勝者が決まっていない（引き分け／未設定）場合は何も出さない。
    IEnumerator ShowWinnerCharacter()
    {
        int winner = GetWinnerPlayer();
        if (winner != 1 && winner != 2)
        {
            Debug.LogWarning($"[InGame1V1ResultController] 勝者が未設定（MatchResult.LastWinner={MatchResult.LastWinner}）のため、キャラを出しません。" +
                             "InGame1v1シーンを経由せずにこの画面を直接再生していないか確認してください。" +
                             "（単体テストはDebug Force Winnerに1か2を入れると可能）");
            yield break;
        }

        string characterName = GetWinnerCharacterName();
        GameObject source = FindWinnerDisplay(characterName);
        Debug.Log($"[InGame1V1ResultController] 勝者={winner}P / キャラ名='{characterName}' / 表示対象={(source != null ? source.name : "なし")}");

        if (source == null)
        {
            Debug.LogWarning($"[InGame1V1ResultController] 勝者（{winner}P）のキャラ'{characterName}'に" +
                             "対応する表示用オブジェクトが見つかりません。Winner Charactersの対応表（characterNameの一致）と、" +
                             "Fallback Winner Displayの設定を確認してください。");
            yield break;
        }

        if (winnerAppearDelay > 0f) yield return new WaitForSeconds(winnerAppearDelay);

        GameObject target;
        Vector3 baseScale;

        if (source.scene.IsValid())
        {
            // シーン内に置いたオブジェクト：その場で出現させる
            target = source;
            baseScale = sceneDisplayBaseScales.TryGetValue(source, out var saved) ? saved : source.transform.localScale;
        }
        else
        {
            // Prefab：スポーン位置に生成する
            Vector3 pos = winnerSpawnPoint != null ? winnerSpawnPoint.position : Vector3.zero;
            Quaternion rot = winnerSpawnPoint != null ? winnerSpawnPoint.rotation : source.transform.rotation;
            target = Instantiate(source, pos, rot);
            winnerInstance = target;
            baseScale = source.transform.localScale;
        }

        if (se == null) se = GetComponent<AudioSource>();
        if (se != null && winnerAppearSE != null) se.PlayOneShot(winnerAppearSE);

        Transform tr = target.transform;

        if (faceCameraOnAppear) FaceCamera(tr);

        if (winnerPopDuration <= 0f)
        {
            tr.localScale = baseScale;
            target.SetActive(true);
            yield break;
        }

        tr.localScale = Vector3.zero;
        target.SetActive(true);

        float t = 0f;
        while (t < winnerPopDuration)
        {
            t += Time.deltaTime;
            float ratio = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / winnerPopDuration));
            tr.localScale = baseScale * ratio;
            yield return null;
        }

        tr.localScale = baseScale;
    }

    // 指定秒数が経ったら、TMPテキストを非表示にする。
    IEnumerator HideTextAfter(TMP_Text target, float seconds)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        if (target != null) target.enabled = false;
    }

    // 「HIT ANY BUTTON」を点滅させる。
    // hitAnyButtonTextDurationが0より大きい場合は、その時間が経過した時点で点滅を止めて消す。
    // 画面が割れる演出が始まった場合も、点滅を止めて消す。
    IEnumerator BlinkText()
    {
        if (hitAnyButtonText == null) yield break;

        float elapsed = 0f;

        while (!isTransitioning)
        {
            bool hasLimit = hitAnyButtonTextDuration > 0f;
            if (hasLimit && elapsed >= hitAnyButtonTextDuration) break;

            hitAnyButtonText.enabled = !hitAnyButtonText.enabled;

            // 時間切れの瞬間に確実に消せるよう、残り時間より長くは待たない
            float wait = blinkInterval;
            if (hasLimit) wait = Mathf.Min(wait, hitAnyButtonTextDuration - elapsed);

            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }
        hitAnyButtonText.enabled = false;
    }
}
