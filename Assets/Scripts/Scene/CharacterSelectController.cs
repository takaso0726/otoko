using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// キャラクターセレクト画面の制御。
/// 1P・2Pがそれぞれ独立したカーソルでキャラクターを選び、決定ボタンで確定する。
/// 左右の担当エリアという制限は無く、上下左右自由に移動できる。
/// ただし各キャラの selectableBy で「1Pだけ／2Pだけ」が選べるキャラを指定できる（既定は両方選択可）。
/// 両者の選択が確定したら、演出→ディレイを挟んでインゲームシーンへ遷移する。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// </summary>
public class CharacterSelectController : MonoBehaviour
{
    /// <summary>
    /// セレクト画面で確定した「どちらのプレイヤーが・どのキャラクターを選んだか」を
    /// シーン遷移後も参照できるように保持しておく置き場。
    ///
    /// 重要: Player1 / Player2 を配列やListのインデックス(0番目・1番目)で管理すると、
    /// 次シーン側の取得順（生成順・検索順など）次第で1Pと2Pの中身が入れ替わってしまう。
    /// それを避けるため、必ず名前で区別された別々のフィールドとして持つこと。
    ///
    /// 次シーン側では、必ず下記のように「名前で明示的に」参照すること。
    ///   int p1 = CharacterSelectionResult.Player1CharacterIndex;
    ///   int p2 = CharacterSelectionResult.Player2CharacterIndex;
    /// （foreachやインデックス0/1でPlayerSelectorを列挙して割り当てる、といった
    ///   順序依存の実装は絶対に行わないこと。1Pと2Pの入れ替わりバグの原因になる。）
    /// </summary>
    public static class CharacterSelectionResult
    {
        public static int Player1CharacterIndex = -1;
        public static int Player2CharacterIndex = -1;
        public static string Player1CharacterName;
        public static string Player2CharacterName;

        public static bool IsValid =>
            Player1CharacterIndex >= 0 && Player2CharacterIndex >= 0;

        public static void Clear()
        {
            Player1CharacterIndex = -1;
            Player2CharacterIndex = -1;
            Player1CharacterName = null;
            Player2CharacterName = null;
        }
    }

    /// <summary>このキャラクターをどのプレイヤーが選択できるか</summary>
    public enum SelectablePlayer
    {
        Both,         // 1P・2Pどちらも選択可能
        Player1Only,  // 1Pだけが選択可能（2Pのカーソルは飛ばされる）
        Player2Only   // 2Pだけが選択可能（1Pのカーソルは飛ばされる）
    }

    [System.Serializable]
    public class CharacterEntry
    {
        public string characterName;  // 表示名
        public RectTransform anchor;  // グリッド上の位置（各キャラアイコンのUI要素）

        [Tooltip("このキャラを選べるプレイヤー。例: 「Player1」というキャラは Player1Only、「Player2」は Player2Only にすると、それぞれ1P・2Pだけが選べる")]
        public SelectablePlayer selectableBy = SelectablePlayer.Both;

        [Header("ドアップ表示")]
        [Tooltip("カーソルを合わせた時に表示するドアップ用プレハブ（1P・2P共通）。各プレイヤーの portraitRoot の子として生成される。Image/アニメーション/Spine等、中身は自由")]
        public GameObject portraitPrefab;

        [Header("名前表示（画像）")]
        public Sprite nameImage;      // キャラ名を表す画像（ロゴ・ロゴタイプ等）。選択中にnameImageObjectへ反映する
    }

    [System.Serializable]
    public class PlayerSelector
    {
        public string playerName;      // "1P" / "2P" など表示用
        public RectTransform cursor;   // このプレイヤー用カーソル
        public GameObject readyMark;   // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく。1Pと2Pで必ず別々のオブジェクトを割り当てること）
        [Tooltip("READYマークを選択キャラのアンカー位置からどれだけずらすか。1Pと2Pで違う値にしておくと、同じキャラを選んでも重ならず両方見える（例: 1P=(-40,0) / 2P=(40,0)）")]
        public Vector2 readyMarkOffset; // ScreenSpace-Overlayならピクセル単位
        [Header("ドアップ表示位置")]
        [Tooltip("ドアップ用プレハブを映す場所（親）。UIならCanvas内のRectTransform、3Dならシーン内の空オブジェクトなど、どのTransformでもOK。1Pと2Pで別々の場所を指定する")]
        public Transform portraitRoot;
        [Tooltip("portraitRoot の中心からのズレ。UIプレハブ(RectTransform)ならanchoredPosition、それ以外はlocalPosition。0なら中心にぴったり配置")]
        public Vector3 portraitOffset = Vector3.zero;
        [Tooltip("ドアップの拡大率。プレハブ自身のスケールに掛け算される（1=等倍、1.5=1.5倍）")]
        public float portraitScale = 1f;
        public Image nameImageObject;  // 選択中キャラの名前画像表示用（未設定なら何もしない）
        public int startIndex;         // このプレイヤーの初期カーソル位置（characters配列のインデックス）

        [HideInInspector] public int currentIndex;   // characters配列内でのカーソル位置（グローバルインデックス）
        [HideInInspector] public bool decided;

        // キャラのインデックス → 生成済みドアッププレハブ。毎回Instantiate/Destroyせず、表示切り替えで再利用する
        [System.NonSerialized] public Dictionary<int, GameObject> portraitInstances;
        [System.NonSerialized] public GameObject currentPortrait;
    }

    [Header("キャラクター一覧（グリッド順）")]
    [SerializeField] CharacterEntry[] characters;

    [Header("プレイヤー")]
    [SerializeField] PlayerSelector player1;
    [SerializeField] PlayerSelector player2;
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;      // カーソル移動時
    [SerializeField] AudioClip decideSE;    // 1人が決定した時
    [SerializeField] AudioClip bothReadySE; // 両者決定＆遷移演出時

    [Header("遷移設定")]
    [SerializeField] string nextSceneName = "InGame1v1";
    [SerializeField] float transitionDelay = 0.7f;

    bool isTransitioning;
    bool initialized;
    Coroutine p1CursorRoutine;
    Coroutine p2CursorRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        // isTransitioning や decided が戻らないと、一度両者が決定した後に
        // このオブジェクトが再利用された場合、二度と入力を受け付けなくなる。
        isTransitioning = false;

        if (player1 != null)
        {
            player1.decided = false;
            SetReadyMarkActive(player1, false);
            SetCursorVisible(player1, true);
        }
        if (player2 != null)
        {
            player2.decided = false;
            SetReadyMarkActive(player2, false);
            SetCursorVisible(player2, true);
        }

        // Start()は初回のみ呼ばれる仕様なので、2回目以降の有効化時は
        // ここでカーソル位置とドアップ表示も選択初期状態へ戻しておく。
        if (initialized)
        {
            ResetSelectionPositions();
        }
    }

    void Start()
    {
        if (characters == null || characters.Length == 0)
        {
            Debug.LogWarning("[CharacterSelectController] characters が設定されていません。", this);
            return;
        }

        // 1Pと2Pに同じREADYマークを割り当てていると、片方しか表示されないので警告する
        if (player1.readyMark != null && player1.readyMark == player2.readyMark)
        {
            Debug.LogWarning("[CharacterSelectController] player1 と player2 に同じ readyMark が設定されています。1P用・2P用で別々のオブジェクトを割り当ててください。", this);
        }

        // 新しいセレクトセッションの開始時点で、前回分の選択結果が残ったまま
        // 次シーンへ持ち越されてしまわないようにクリアしておく。
        CharacterSelectionResult.Clear();

        initialized = true;

        ResetSelectionPositions();
    }

    void ResetSelectionPositions()
    {
        player1.currentIndex = ResolveStartIndex(player1, 1);
        player2.currentIndex = ResolveStartIndex(player2, 2);

        if (player1.cursor != null)
        {
            var anchor = characters[player1.currentIndex].anchor;
            if (anchor != null) player1.cursor.position = anchor.position;
        }
        if (player2.cursor != null)
        {
            var anchor = characters[player2.currentIndex].anchor;
            if (anchor != null) player2.cursor.position = anchor.position;
        }

        SetReadyMarkActive(player1, false);
        SetReadyMarkActive(player2, false);
        SetCursorVisible(player1, true);
        SetCursorVisible(player2, true);
        UpdateSelectionDisplay(player1, 1);
        UpdateSelectionDisplay(player2, 2);
    }

    // 指定したプレイヤーがこのキャラクターを選択できるか
    bool CanSelect(int characterIndex, int playerNumber)
    {
        if (characterIndex < 0 || characterIndex >= characters.Length) return false;

        switch (characters[characterIndex].selectableBy)
        {
            case SelectablePlayer.Player1Only: return playerNumber == 1;
            case SelectablePlayer.Player2Only: return playerNumber == 2;
            default: return true;
        }
    }

    // startIndexのキャラをそのプレイヤーが選べない場合は、選べる最初のキャラへ切り替える
    int ResolveStartIndex(PlayerSelector p, int playerNumber)
    {
        int start = Mathf.Clamp(p.startIndex, 0, characters.Length - 1);
        if (CanSelect(start, playerNumber)) return start;

        for (int i = 0; i < characters.Length; i++)
        {
            if (CanSelect(i, playerNumber)) return i;
        }

        Debug.LogWarning($"[CharacterSelectController] {playerNumber}P が選択できるキャラクターがありません。characters の selectableBy を確認してください。", this);
        return start;
    }

    void Update()
    {
        if (isTransitioning) return;
        if (characters == null || characters.Length == 0) return;

        HandlePlayer(player1, ReadP1Direction(), ReadP1Decide(), ReadP1Cancel(), ref p1CursorRoutine, 1);
        HandlePlayer(player2, ReadP2Direction(), ReadP2Decide(), ReadP2Cancel(), ref p2CursorRoutine, 2);

        // 両者が決定していたら遷移演出へ
        if (player1.decided && player2.decided)
        {
            StartCoroutine(BothReadyAndTransition());
        }
    }

    void HandlePlayer(PlayerSelector p, Vector2Int inputDir, bool decide, bool cancel, ref Coroutine routine, int playerNumber)
    {
        if (p.decided)
        {
            // 決定後はキャンセル入力のみ受け付けて選び直しできるようにする
            if (cancel)
            {
                p.decided = false;
                SetReadyMarkActive(p, false);
                SetCursorVisible(p, true);
            }
            return;
        }

        // 同一フレームで斜め入力が来た場合は左右を優先する（上下と同時に判定すると挙動が分かりづらくなるため）
        Vector2Int navDir = inputDir.x != 0 ? new Vector2Int(inputDir.x, 0) : new Vector2Int(0, inputDir.y);

        if (navDir != Vector2Int.zero)
        {
            int nextIndex = FindNearestInDirection(p.currentIndex, new Vector2(navDir.x, navDir.y), playerNumber);

            if (nextIndex >= 0)
            {
                p.currentIndex = nextIndex;
                PlaySE(moveSE);

                if (p.cursor != null && characters[nextIndex].anchor != null)
                {
                    if (routine != null) StopCoroutine(routine);
                    routine = StartCoroutine(MoveCursorSmooth(p.cursor, characters[nextIndex].anchor.position));
                }

                UpdateSelectionDisplay(p, playerNumber);
            }
        }

        if (decide)
        {
            p.decided = true;
            PlaceReadyMarkOnSelectedCharacter(p);
            SetReadyMarkActive(p, true);
            SetCursorVisible(p, false);
            PlaySE(decideSE);
        }
    }

    // 現在位置(fromGlobalIndex)から見て、dir方向にある「そのプレイヤーが選択可能なキャラの中で一番近いもの」の
    // インデックスを返す（selectableByで選べないキャラは飛ばす）。見つからなければ-1。
    // dirが伸びる方向（主軸）の距離を優先しつつ、主軸から外れる（横ズレ・縦ズレ）ほどペナルティを与えることで、
    // 単純なグリッドでなくても自然に「右にある一番近いキャラ」「上にある一番近いキャラ」を選べるようにしている。
    int FindNearestInDirection(int fromGlobalIndex, Vector2 dir, int playerNumber)
    {
        var fromAnchor = characters[fromGlobalIndex].anchor;
        if (fromAnchor == null) return -1;

        Vector2 currentPos = fromAnchor.position;
        Vector2 dirNormalized = dir.normalized;
        Vector2 perpAxis = new Vector2(-dirNormalized.y, dirNormalized.x); // dirに直交する軸

        int bestIndex = -1;
        float bestScore = float.MaxValue;

        for (int idx = 0; idx < characters.Length; idx++)
        {
            if (idx == fromGlobalIndex) continue;
            if (!CanSelect(idx, playerNumber)) continue; // このプレイヤーが選べないキャラは対象外

            var anchor = characters[idx].anchor;
            if (anchor == null) continue;

            Vector2 offset = (Vector2)anchor.position - currentPos;

            float primary = Vector2.Dot(offset, dirNormalized);
            if (primary <= 0.01f) continue; // 指定方向とは逆・同位置にあるものは除外

            float perpendicular = Mathf.Abs(Vector2.Dot(offset, perpAxis));

            // 主軸方向の距離を基本スコアにしつつ、軸ズレには重めのペナルティを掛けて
            // 「まっすぐ近い」候補を優先する
            float score = primary + perpendicular * 2f;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    // GameObjectの「疑似null（参照は残っているが実体が破棄されている状態）」でも
    // 安全に判定できるよう、?.ではなく明示的なUnityの==比較でチェックする
    void SetReadyMarkActive(PlayerSelector p, bool active)
    {
        if (p != null && p.readyMark != null)
        {
            p.readyMark.SetActive(active);
        }
    }

    // READYマークを、そのプレイヤーが選択しているキャラクターの位置へ移動させる。
    // カーソルと同様にanchor.positionへ合わせるので、同一Canvas内であれば親が違っても位置が揃う。
    void PlaceReadyMarkOnSelectedCharacter(PlayerSelector p)
    {
        if (p == null || p.readyMark == null) return;
        if (characters == null || p.currentIndex < 0 || p.currentIndex >= characters.Length) return;

        var anchor = characters[p.currentIndex].anchor;
        if (anchor == null) return;

        p.readyMark.transform.position = anchor.position + (Vector3)p.readyMarkOffset;

        // 他のUI（キャラアイコンなど）の裏に隠れないよう、同じ親の中で最前面へ持ってくる
        p.readyMark.transform.SetAsLastSibling();
    }

    // 決定時はカーソルを隠してREADYマークだけを見せ、選び直し時はカーソルを再表示する
    void SetCursorVisible(PlayerSelector p, bool visible)
    {
        if (p != null && p.cursor != null)
        {
            p.cursor.gameObject.SetActive(visible);
        }
    }

    // カーソルが乗っているキャラクターのドアップ画像・名前表示を、そのプレイヤー専用のUIに反映する
    // （1P用UIは画面左、2P用UIは画面右のRectTransformに配置しておく想定）
    // ドアップ表示は1P・2P共通の portraitPrefab を使用し、プレイヤーごとに別インスタンスを生成する。
    void UpdateSelectionDisplay(PlayerSelector p, int playerNumber)
    {
        if (p == null) return;
        if (characters == null || characters.Length == 0) return;

        var entry = characters[p.currentIndex];

        ShowPortrait(p, p.currentIndex);

        if (p.nameImageObject != null)
        {
            var nameSprite = entry.nameImage;
            p.nameImageObject.sprite = nameSprite;

            // nameImage未設定のキャラの場合はImageを非表示にして「空の白い四角」が出ないようにする
            p.nameImageObject.enabled = nameSprite != null;
        }
    }

    // 選択中キャラのドアップ用プレハブを表示する。
    // 初回はportraitRootの子として生成し、2回目以降は生成済みインスタンスのアクティブ切り替えだけを行う。
    void ShowPortrait(PlayerSelector p, int characterIndex)
    {
        if (p.portraitRoot == null) return;

        // 前に表示していたものは非表示にする（Unity的にnull＝破棄済みの場合は何もしない）
        if (p.currentPortrait != null)
        {
            p.currentPortrait.SetActive(false);
            p.currentPortrait = null;
        }

        var prefab = characters[characterIndex].portraitPrefab;
        if (prefab == null) return; // 未設定のキャラは何も表示しない

        if (p.portraitInstances == null)
        {
            p.portraitInstances = new Dictionary<int, GameObject>();
        }

        // 破棄済み（親ごとDestroyされた等）のキャッシュが残っていたら作り直す
        if (!p.portraitInstances.TryGetValue(characterIndex, out var instance) || instance == null)
        {
            instance = Instantiate(prefab, p.portraitRoot, false);
            PlacePortrait(instance, p);
            p.portraitInstances[characterIndex] = instance;
        }

        instance.SetActive(true);
        p.currentPortrait = instance;
    }

    // 生成したドアッププレハブを portraitRoot の中心（＋offset）に配置し、拡大率を反映する。
    // プレハブ自身の位置は上書きされるが、回転とスケールはプレハブの設定を活かす。
    void PlacePortrait(GameObject instance, PlayerSelector p)
    {
        var t = instance.transform;

        if (t is RectTransform rt)
        {
            rt.anchoredPosition3D = p.portraitOffset;
        }
        else
        {
            t.localPosition = p.portraitOffset;
        }

        float scale = p.portraitScale > 0f ? p.portraitScale : 1f;
        t.localScale = t.localScale * scale;
    }

    // AudioClipがInspectorで未設定の場合にPlayOneShot(null)警告が出るのを防ぐ
    void PlaySE(AudioClip clip)
    {
        if (se != null && clip != null)
        {
            se.PlayOneShot(clip);
        }
    }

    IEnumerator MoveCursorSmooth(RectTransform cursor, Vector3 target)
    {
        while (Vector3.Distance(cursor.position, target) > 0.5f)
        {
            cursor.position = Vector3.MoveTowards(cursor.position, target, cursorMoveSpeed * Time.deltaTime * 1000f);
            yield return null;
        }
        cursor.position = target;
    }

    IEnumerator BothReadyAndTransition()
    {
        isTransitioning = true;

        // 1P・2Pそれぞれの選択結果を、プレイヤー名で明示的に紐付けて保存する。
        // ここで player1 の結果は必ず Player1CharacterIndex/Name に、
        // player2 の結果は必ず Player2CharacterIndex/Name に入れること。
        // （配列やコレクションにまとめてから0番目/1番目で振り分ける、といった
        //   実装に変更すると、また入れ替わりバグが再発するので注意）
        SaveSelectionResult();

        PlaySE(bothReadySE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // 各プレイヤーが最終的にカーソルを合わせていたキャラクターを、
    // プレイヤーごとに明示的に区別してCharacterSelectionResultへ書き込む。
    void SaveSelectionResult()
    {
        CharacterSelectionResult.Player1CharacterIndex = player1.currentIndex;
        CharacterSelectionResult.Player1CharacterName = characters[player1.currentIndex].characterName;

        CharacterSelectionResult.Player2CharacterIndex = player2.currentIndex;
        CharacterSelectionResult.Player2CharacterName = characters[player2.currentIndex].characterName;
    }

    // ---- 入力読み取り ----
    //
    // PCに接続された2台のコントローラーを同時に使えるように、
    // 1P = Gamepad.all[0]、2P = Gamepad.all[1] とインデックスを固定して割り当てる。
    // （以前の「2台無い場合は1台を共用」という仕様だと、2台繋いでいても
    //   1Pがキーボード固定のため2台目を活かせなかった）
    //
    // 1Pのみキーボードも合わせて受け付ける（コントローラー無しでも1人でテスト可能にするため）。
    // キーボード／ゲームパッドどちらの入力も同一フレームでは併用OK。
    //
    // 各Read〜Direction()はVector2Int(x, y)を返す。x: 右+1/左-1、y: 上+1/下-1。

    // 1P: ゲームパッド0番 + キーボード（矢印キー／WASD、決定：Enter or ボタンSouth、キャンセル：Esc or ボタンEast）
    Vector2Int ReadP1Direction()
    {
        var kb = ReadDirectionFromKeyboard();
        if (kb != Vector2Int.zero) return kb;

        return ReadDirectionFromGamepad(GetGamepad(0));
    }
    bool ReadP1Decide()
    {
        if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }
    bool ReadP1Cancel()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    // 2P: ゲームパッド1番のみ（1Pのゲームパッドとは独立して同時入力を受け付ける）
    Vector2Int ReadP2Direction()
    {
        return ReadDirectionFromGamepad(GetGamepad(1));
    }
    bool ReadP2Decide()
    {
        var pad = GetGamepad(1);
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }
    bool ReadP2Cancel()
    {
        var pad = GetGamepad(1);
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    // キーボードの矢印キー／WASDから方向を読み取る
    Vector2Int ReadDirectionFromKeyboard()
    {
        if (Keyboard.current == null) return Vector2Int.zero;

        int x = 0;
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) x = 1;
        else if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) x = -1;

        int y = 0;
        if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) y = 1;
        else if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) y = -1;

        return new Vector2Int(x, y);
    }

    // ゲームパッドのD-Pad／左スティックから方向を読み取る
    Vector2Int ReadDirectionFromGamepad(Gamepad pad)
    {
        if (pad == null) return Vector2Int.zero;

        int x = 0;
        if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) x = 1;
        else if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) x = -1;

        int y = 0;
        if (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame) y = 1;
        else if (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame) y = -1;

        return new Vector2Int(x, y);
    }

    // index番目に接続されているゲームパッドを返す（未接続ならnull）
    Gamepad GetGamepad(int index)
    {
        return index < Gamepad.all.Count ? Gamepad.all[index] : null;
    }
}
