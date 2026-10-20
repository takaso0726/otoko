using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// CPU戦のキャラクターセレクト画面の制御（操作はすべて1P）。
/// 1v1用の CharacterSelectController.cs と同じ作り（ドアップprefab・名前画像・READYマーク・キャンセル・selectableBy）で、
/// 次の3ステップを順番に進める。
///   ① 自分のキャラを選ぶ  → 決定
///   ② CPUのキャラを選ぶ   → 決定
///   ③ CPUの強さを選ぶ     → 決定 → 演出→ディレイ→インゲームシーンへ遷移
/// キャンセル（Esc / ボタンEast）で1つ前のステップへ戻れる。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// </summary>
public class CharacterSelect_SFC_Controller : MonoBehaviour
{
    /// <summary>
    /// セレクト画面で確定した内容をシーン遷移後も参照できるように保持しておく置き場。
    /// 次シーン側では、必ず下記のように「名前で明示的に」参照すること。
    ///   int me  = CharacterSelect_SFC_Controller.CpuSelectionResult.PlayerCharacterIndex;
    ///   int cpu = CharacterSelect_SFC_Controller.CpuSelectionResult.CpuCharacterIndex;
    ///   int lv  = CharacterSelect_SFC_Controller.CpuSelectionResult.CpuDifficultyIndex; // difficulties配列のインデックス（0が最弱）
    /// </summary>
    public static class CpuSelectionResult
    {
        public static int PlayerCharacterIndex = -1;
        public static int CpuCharacterIndex = -1;
        public static int CpuDifficultyIndex = -1;
        public static string PlayerCharacterName;
        public static string CpuCharacterName;
        public static string CpuDifficultyName;

        public static bool IsValid =>
            PlayerCharacterIndex >= 0 && CpuCharacterIndex >= 0 && CpuDifficultyIndex >= 0;

        public static void Clear()
        {
            PlayerCharacterIndex = -1;
            CpuCharacterIndex = -1;
            CpuDifficultyIndex = -1;
            PlayerCharacterName = null;
            CpuCharacterName = null;
            CpuDifficultyName = null;
        }
    }

    /// <summary>選択の進行ステップ</summary>
    public enum SelectStep
    {
        Player,      // ① 自分のキャラを選ぶ
        Cpu,         // ② CPUのキャラを選ぶ
        Difficulty   // ③ CPUの強さを選ぶ
    }

    /// <summary>このキャラクターをどちらの側が選択できるか（自分=1P側 / CPU側）</summary>
    public enum SelectablePlayer
    {
        Both,         // 自分・CPUどちらも選択可能
        Player1Only,  // 自分だけが選択可能（CPU選択時は飛ばされる）
        CpuOnly       // CPUだけが選択可能（自分の選択時は飛ばされる）
    }

    [System.Serializable]
    public class CharacterEntry
    {
        public string characterName;  // 表示名
        public Transform anchor;  // グリッド上の位置（空のGameObjectでもUI要素でもOK。ワールド座標(position)で比較される）

        [Tooltip("このキャラを選べる側。Player1Only=自分(1P)だけ、CpuOnly=CPUだけ、Both=どちらも")]
        public SelectablePlayer selectableBy = SelectablePlayer.Both;

        [Header("ドアップ表示")]
        [Tooltip("カーソルを合わせた時に表示するドアップ用プレハブ（自分・CPU共通）。各セレクターの portraitRoot の子として生成される")]
        public GameObject portraitPrefab;

        [Header("ネームアップ表示")]
        [Tooltip("カーソルを合わせた時に表示するネームアップ用プレハブ（自分・CPU共通）。各セレクターの nameRoot の子として生成される。Image/アニメーション等、中身は自由")]
        public GameObject namePrefab;
        [Tooltip("namePrefab が未設定のキャラだけ使われる、従来の名前画像（セレクターの nameImageObject に反映される）")]
        public Sprite nameImage;
    }

    /// <summary>キャラ選択用のカーソル・表示まわりの設定（自分用／CPU用で1つずつ使う）</summary>
    [System.Serializable]
    public class CharacterSelector
    {
        public string label;           // "自分" / "CPU" など表示用
        [Header("カーソル")]
        [Tooltip("カーソルのプレハブ（UIでもSpriteRendererでもOK）。設定すると開始時に生成され、下の cursor に自動で入る")]
        public GameObject cursorPrefab;
        [Tooltip("カーソルを生成する親。未設定なら最初のキャラのアンカーと同じ親になる")]
        public Transform cursorParent;
        [Tooltip("シーン上のカーソルを直接使う場合はここに設定（cursorPrefab を設定した場合は不要）")]
        public Transform cursor;
        [Tooltip("カーソルを、キャラのアンカー位置からどれだけずらすか（ワールド座標）。位置ずれの調整用")]
        public Vector3 cursorOffset = Vector3.zero;
        [Header("READYマーク")]
        public GameObject readyMark;   // 決定後に表示する「READY」表示。シーン上のオブジェクトでもプレハブでもOK（プレハブなら表示時に自動生成される）
        [Tooltip("readyMark がプレハブの場合に、生成する親。未設定なら最初のキャラのアンカーと同じ親になる")]
        public Transform readyMarkParent;
        [Tooltip("READYマークを選択キャラのアンカー位置からどれだけずらすか。同じキャラを選んでも重ならないよう、自分とCPUで違う値にしておく")]
        public Vector2 readyMarkOffset;
        [Header("ドアップ表示位置")]
        [Tooltip("ドアップ用プレハブを映す場所（親）。自分用とCPU用で別々の場所を指定する")]
        public Transform portraitRoot;
        [Tooltip("portraitRoot の中心からのズレ。UIプレハブならanchoredPosition、それ以外はlocalPosition")]
        public Vector3 portraitOffset = Vector3.zero;
        [Tooltip("ドアップの拡大率。プレハブ自身のスケールに掛け算される")]
        public float portraitScale = 1f;
        [Header("ネームアップ表示位置")]
        [Tooltip("ネームアップ用プレハブを映す場所（親）。自分用とCPU用で別々の場所を指定する")]
        public Transform nameRoot;
        [Tooltip("ネームアップを表示する位置にする空のオブジェクト（その座標が使われる）。未設定なら従来どおり nameRoot の中心＋nameOffset の位置に出る。nameRoot が未設定なら、このオブジェクトの親の子として生成される")]
        public Transform namePoint;
        [Tooltip("namePoint 未設定時：nameRoot の中心からのズレ（UIプレハブならanchoredPosition、それ以外はlocalPosition）。namePoint 設定時：namePoint の座標に足すズレ（ワールド座標）")]
        public Vector3 nameOffset = Vector3.zero;
        [Tooltip("ネームアップの拡大率。プレハブ自身のスケールに掛け算される")]
        public float nameScale = 1f;
        [Tooltip("（従来方式）namePrefab 未設定のキャラ用の名前画像表示先。未設定なら何もしない")]
        public Image nameImageObject;
        public int startIndex;         // 初期カーソル位置（characters配列のインデックス）

        [HideInInspector] public int currentIndex;

        [System.NonSerialized] public Dictionary<int, GameObject> portraitInstances;
        [System.NonSerialized] public GameObject currentPortrait;
        [System.NonSerialized] public Dictionary<int, GameObject> nameInstances;
        [System.NonSerialized] public GameObject currentName;
    }

    /// <summary>CPUの強さの選択肢1つぶん</summary>
    [System.Serializable]
    public class DifficultyEntry
    {
        public string difficultyName;  // 表示名（例: EASY / NORMAL / HARD）
        public Transform anchor;   // 強さ選択の位置（空のGameObjectでもUI要素でもOK）

        [Header("カーソル位置")]
        [Tooltip("強さ選択カーソルの移動先にする空のオブジェクト（その座標が使われる）。未設定なら上の anchor の位置を使う")]
        public Transform cursorPoint;

        [Header("アイコン")]
        [Tooltip("この強さのアイコンのプレハブ。アンカーの子として生成され、強さ選択の間だけ表示される。未設定なら何も出さない")]
        public GameObject iconPrefab;
        [Tooltip("アンカーの中心からのズレ。UIプレハブならanchoredPosition、それ以外はlocalPosition")]
        public Vector3 iconOffset = Vector3.zero;
        [Tooltip("アイコンの拡大率。プレハブ自身のスケールに掛け算される")]
        public float iconScale = 1f;
    }

    [Header("キャラクター一覧（グリッド順）")]
    [SerializeField] CharacterEntry[] characters;

    [Header("キャラ選択（自分／CPU）")]
    [SerializeField] CharacterSelector player;   // ① 自分
    [SerializeField] CharacterSelector cpu;      // ② CPU
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("キャラ選択フレーム")]
    [Tooltip("キャラクター選択のフレームのプレハブ（またはシーン上のオブジェクト）。カーソル・READYマークより後ろに表示される。未設定なら何もしない")]
    [SerializeField] GameObject framePrefab;
    [Tooltip("フレームを生成する親。未設定なら最初のキャラのアンカーと同じ親になる")]
    [SerializeField] Transform frameParent;
    [Tooltip("親の中心からのズレ。UIプレハブならanchoredPosition、それ以外はlocalPosition")]
    [SerializeField] Vector3 frameOffset = Vector3.zero;
    [Tooltip("フレームの拡大率。プレハブ自身のスケールに掛け算される")]
    [SerializeField] float frameScale = 1f;

    [Header("CPUの強さ選択")]
    [Tooltip("弱い順に並べる（配列のインデックスがそのまま強さのレベルになる）")]
    [SerializeField] DifficultyEntry[] difficulties;
    [Tooltip("強さ選択UI全体の親。③のステップの時だけ表示される（未設定でもOK）")]
    [SerializeField] GameObject difficultyRoot;
    [Tooltip("強さ選択用カーソルのプレハブ（UIでもSpriteRendererでもOK）。設定すると開始時に生成され、下の difficultyCursor に自動で入る")]
    [SerializeField] GameObject difficultyCursorPrefab;
    [Tooltip("強さ選択用カーソルを生成する親。未設定なら最初の強さボタンのアンカーと同じ親になる")]
    [SerializeField] Transform difficultyCursorParent;
    [Tooltip("シーン上のカーソルを直接使う場合はここに設定（プレハブを設定した場合は不要）")]
    [SerializeField] Transform difficultyCursor;
    [Tooltip("強さ選択カーソルを、アンカー位置からどれだけずらすか（ワールド座標）。位置ずれの調整用")]
    [SerializeField] Vector3 difficultyCursorOffset = Vector3.zero;
    [SerializeField] int difficultyStartIndex = 1;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;       // カーソル移動時
    [SerializeField] AudioClip decideSE;     // 自分／CPUのキャラ決定時
    [SerializeField] AudioClip finalDecideSE; // 強さ決定＆遷移演出時

    [Header("遷移設定")]
    [SerializeField] string nextSceneName = "InGameCPU";
    [SerializeField] float transitionDelay = 0.7f;

    SelectStep step;
    GameObject frameInstance;
    List<GameObject> difficultyIcons;
    int difficultyIndex;
    bool isTransitioning;
    bool initialized;
    Coroutine playerCursorRoutine;
    Coroutine cpuCursorRoutine;
    Coroutine difficultyCursorRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        isTransitioning = false;
        step = SelectStep.Player;

        // Start()は初回のみ呼ばれるので、2回目以降の有効化時はここで初期状態へ戻す
        if (initialized)
        {
            ResetSelectionPositions();
        }
    }

    void Start()
    {
        if (characters == null || characters.Length == 0)
        {
            Debug.LogWarning("[CharacterSelect_SFC_Controller] characters が設定されていません。", this);
            return;
        }

        if (player.readyMark != null && player.readyMark == cpu.readyMark && player.readyMark.scene.IsValid())
        {
            Debug.LogWarning("[CharacterSelect_SFC_Controller] player と cpu に同じ readyMark が設定されています。別々のオブジェクトを割り当ててください。", this);
        }

        // 前回分の選択結果が残ったまま次シーンへ持ち越されないようにクリアしておく
        CpuSelectionResult.Clear();

        // カーソルがプレハブ指定ならここで生成する
        EnsureCursors();

        // READYマークがプレハブなら、ここで生成して非表示にしておく（表示は決定したタイミングで）
        EnsureReadyMark(player);
        EnsureReadyMark(cpu);

        // キャラ選択フレームを生成し、カーソル・READYマークより後ろへ回す
        EnsureFrame();
        ApplyLayerOrder();

        // 強さのアイコンを生成する（表示は強さ選択の間だけ）
        EnsureDifficultyIcons();

        initialized = true;
        ResetSelectionPositions();
    }

    // プレハブが設定されていて、まだカーソルが無ければ生成して cursor に入れる
    void EnsureCursors()
    {
        var charParent = characters[0].anchor != null ? characters[0].anchor.parent : transform;
        player.cursor = EnsureCursor(player.cursor, player.cursorPrefab, player.cursorParent != null ? player.cursorParent : charParent);
        cpu.cursor = EnsureCursor(cpu.cursor, cpu.cursorPrefab, cpu.cursorParent != null ? cpu.cursorParent : charParent);

        if (difficulties != null && difficulties.Length > 0)
        {
            var diffParent = difficulties[0].anchor != null ? difficulties[0].anchor.parent : transform;
            difficultyCursor = EnsureCursor(difficultyCursor, difficultyCursorPrefab, difficultyCursorParent != null ? difficultyCursorParent : diffParent);
        }
    }

    // 強さのアイコンのプレハブを、各強さのアンカーの子として生成する
    void EnsureDifficultyIcons()
    {
        if (difficulties == null || difficulties.Length == 0) return;
        if (difficultyIcons != null) return; // 生成済み

        difficultyIcons = new List<GameObject>();

        // difficulties と同じ並び・同じ数にする（アイコンが無い要素は null を入れて、インデックスを揃える）
        foreach (var d in difficulties)
        {
            if (d == null || d.iconPrefab == null || d.anchor == null)
            {
                difficultyIcons.Add(null);
                continue;
            }

            var icon = Instantiate(d.iconPrefab, d.anchor, false);
            PlaceInstance(icon, d.iconOffset, d.iconScale);
            icon.SetActive(false);
            difficultyIcons.Add(icon);
        }
    }

    // 強さ選択カーソルの移動先の基準（ワールド座標）。
    // 各強さの「cursorPoint」（空のオブジェクト）の位置を返す。未設定なら anchor の位置を使う。
    Vector3? DifficultyPosition(int index)
    {
        if (difficulties == null || index < 0 || index >= difficulties.Length) return null;

        var d = difficulties[index];
        if (d == null) return null;

        if (d.cursorPoint != null) return d.cursorPoint.position;
        if (d.anchor != null) return d.anchor.position;
        return null;
    }

    // カーソルをワールド座標へ移動する（offsetはカーソル自身の位置ずれ調整）
    void SnapCursorTo(Transform cursor, Vector3? position, Vector3 offset)
    {
        if (cursor != null && position.HasValue) cursor.position = position.Value + offset;
    }

    // フレームのプレハブをシーン上に生成する（シーン上のオブジェクトが設定されていればそのまま使う）
    void EnsureFrame()
    {
        if (framePrefab == null || frameInstance != null) return;

        if (framePrefab.scene.IsValid())
        {
            frameInstance = framePrefab;
        }
        else
        {
            var parent = frameParent != null
                ? frameParent
                : (characters[0].anchor != null ? characters[0].anchor.parent : transform);

            frameInstance = Instantiate(framePrefab, parent, false);
            PlaceInstance(frameInstance, frameOffset, frameScale);
        }

        frameInstance.SetActive(true);
    }

    // 重なり順を、奥から次の順に揃える：
    //   フレーム → 自分のカーソル → 自分のREADY → CPUのカーソル → CPUのREADY
    //  ・UI(Image等)       : 同じ親の中で、この順に最前面へ並べ直す（後のものほど手前）
    //  ・SpriteRenderer等 : 前のレイヤーの最大 sortingOrder より後のレイヤーの最小値が大きくなるようにする
    void ApplyLayerOrder()
    {
        var layers = new List<GameObject>();
        if (frameInstance != null) layers.Add(frameInstance);
        if (player.cursor != null) layers.Add(player.cursor.gameObject);
        if (player.readyMark != null) layers.Add(player.readyMark);
        if (cpu.cursor != null) layers.Add(cpu.cursor.gameObject);
        if (cpu.readyMark != null) layers.Add(cpu.readyMark);
        if (difficultyIcons != null)
        {
            foreach (var icon in difficultyIcons)
            {
                if (icon != null) layers.Add(icon);
            }
        }
        if (difficultyCursor != null) layers.Add(difficultyCursor.gameObject);

        // UI：順番に最前面へ送ると、最終的に「後ろに書いたものほど手前」になる
        foreach (var go in layers)
        {
            go.transform.SetAsLastSibling();
        }

        // SpriteRenderer等
        int prevMax = int.MinValue;
        foreach (var go in layers)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            int min = int.MaxValue, max = int.MinValue;
            foreach (var r in renderers)
            {
                min = Mathf.Min(min, r.sortingOrder);
                max = Mathf.Max(max, r.sortingOrder);
            }

            if (prevMax != int.MinValue)
            {
                int delta = prevMax + 1 - min;
                if (delta > 0)
                {
                    foreach (var r in renderers) r.sortingOrder += delta;
                    max += delta;
                }
            }
            prevMax = max;
        }
    }

    // readyMark がプレハブ（シーン外のアセット）なら、シーン上に生成して差し替え、非表示にする
    void EnsureReadyMark(CharacterSelector s)
    {
        if (s == null || s.readyMark == null) return;
        if (s.readyMark.scene.IsValid()) return; // すでにシーン上のオブジェクト

        var parent = s.readyMarkParent != null
            ? s.readyMarkParent
            : (characters[0].anchor != null ? characters[0].anchor.parent : transform);

        s.readyMark = Instantiate(s.readyMark, parent, false);
        s.readyMark.SetActive(false);
    }

    Transform EnsureCursor(Transform existing, GameObject prefab, Transform parent)
    {
        if (existing != null) return existing; // 既にある（シーン上のカーソル／生成済み）
        if (prefab == null) return null;

        var instance = Instantiate(prefab, parent, false);
        instance.transform.SetAsLastSibling(); // 他のUIの裏に隠れないよう最前面へ

        // UI(RectTransform)でもSpriteRendererなどの通常のTransformでもOK
        return instance.transform;
    }

    void ResetSelectionPositions()
    {
        step = SelectStep.Player;

        player.currentIndex = ResolveStartIndex(player, 1);
        cpu.currentIndex = ResolveStartIndex(cpu, 2);
        difficultyIndex = (difficulties != null && difficulties.Length > 0)
            ? Mathf.Clamp(difficultyStartIndex, 0, difficulties.Length - 1)
            : 0;

        SnapCursor(player.cursor, characters[player.currentIndex].anchor, player.cursorOffset);
        SnapCursor(cpu.cursor, characters[cpu.currentIndex].anchor, cpu.cursorOffset);
        if (difficulties != null && difficulties.Length > 0)
        {
            SnapCursorTo(difficultyCursor, DifficultyPosition(difficultyIndex), difficultyCursorOffset);
        }

        UpdateSelectionDisplay(player);
        ApplyStepVisuals();
    }

    void SnapCursor(Transform cursor, Transform anchor, Vector3 offset)
    {
        if (cursor != null && anchor != null) cursor.position = anchor.position + offset;
    }

    // 指定した側（1=自分 / 2=CPU）がこのキャラクターを選択できるか
    bool CanSelect(int characterIndex, int side)
    {
        if (characterIndex < 0 || characterIndex >= characters.Length) return false;

        switch (characters[characterIndex].selectableBy)
        {
            case SelectablePlayer.Player1Only: return side == 1;
            case SelectablePlayer.CpuOnly: return side == 2;
            default: return true;
        }
    }

    // startIndexのキャラを選べない場合は、選べる最初のキャラへ切り替える
    int ResolveStartIndex(CharacterSelector s, int side)
    {
        int start = Mathf.Clamp(s.startIndex, 0, characters.Length - 1);
        if (CanSelect(start, side)) return start;

        for (int i = 0; i < characters.Length; i++)
        {
            if (CanSelect(i, side)) return i;
        }

        Debug.LogWarning($"[CharacterSelect_SFC_Controller] {s.label} が選択できるキャラクターがありません。characters の selectableBy を確認してください。", this);
        return start;
    }

    void Update()
    {
        if (isTransitioning) return;
        if (characters == null || characters.Length == 0) return;

        Vector2Int dir = ReadDirection();
        bool decide = ReadDecide();
        bool cancel = ReadCancel();

        switch (step)
        {
            case SelectStep.Player:
                if (HandleCharacterStep(player, 1, dir, decide, ref playerCursorRoutine))
                {
                    GoToStep(SelectStep.Cpu);
                }
                break;

            case SelectStep.Cpu:
                if (cancel)
                {
                    GoToStep(SelectStep.Player);
                    break;
                }
                if (HandleCharacterStep(cpu, 2, dir, decide, ref cpuCursorRoutine))
                {
                    if (difficulties == null || difficulties.Length == 0)
                    {
                        // 強さの選択肢が未設定なら、強さ選択を飛ばして遷移する
                        Debug.LogWarning("[CharacterSelect_SFC_Controller] difficulties が未設定のため、強さ選択を飛ばして遷移します。", this);
                        ApplyStepVisualsForCpuDecided();
                        StartCoroutine(FinalDecideAndTransition());
                    }
                    else
                    {
                        GoToStep(SelectStep.Difficulty);
                    }
                }
                break;

            case SelectStep.Difficulty:
                if (cancel)
                {
                    GoToStep(SelectStep.Cpu);
                    break;
                }
                HandleDifficultyStep(dir, decide);
                break;
        }
    }

    // キャラ選択ステップ共通の処理。決定されたらtrueを返す。
    bool HandleCharacterStep(CharacterSelector s, int side, Vector2Int inputDir, bool decide, ref Coroutine routine)
    {
        // 同一フレームで斜め入力が来た場合は左右を優先する
        Vector2Int navDir = inputDir.x != 0 ? new Vector2Int(inputDir.x, 0) : new Vector2Int(0, inputDir.y);

        if (navDir != Vector2Int.zero)
        {
            int nextIndex = FindNearest(
                characters[s.currentIndex].anchor, new Vector2(navDir.x, navDir.y), s.currentIndex, characters.Length,
                i => characters[i].anchor, i => CanSelect(i, side));

            if (nextIndex >= 0)
            {
                s.currentIndex = nextIndex;
                PlaySE(moveSE);

                if (s.cursor != null && characters[nextIndex].anchor != null)
                {
                    if (routine != null) StopCoroutine(routine);
                    routine = StartCoroutine(MoveCursorSmooth(s.cursor, characters[nextIndex].anchor.position + s.cursorOffset));
                }

                UpdateSelectionDisplay(s);
            }
        }

        if (decide)
        {
            PlaySE(decideSE);
            return true;
        }

        return false;
    }

    // 強さ選択ステップの処理
    void HandleDifficultyStep(Vector2Int inputDir, bool decide)
    {
        // 強さ選択は上下だけで操作する
        if (inputDir.y != 0)
        {
            var from = DifficultyPosition(difficultyIndex);
            int nextIndex = from.HasValue
                ? FindNearestByPosition(from.Value, new Vector2(0, inputDir.y), difficultyIndex, difficulties.Length,
                                        DifficultyPosition, null)
                : -1;

            if (nextIndex >= 0)
            {
                difficultyIndex = nextIndex;
                PlaySE(moveSE);

                var target = DifficultyPosition(nextIndex);
                if (difficultyCursor != null && target.HasValue)
                {
                    if (difficultyCursorRoutine != null) StopCoroutine(difficultyCursorRoutine);
                    difficultyCursorRoutine = StartCoroutine(MoveCursorSmooth(difficultyCursor, target.Value + difficultyCursorOffset));
                }
            }
        }

        // 決定したらシーン遷移
        if (decide)
        {
            StartCoroutine(FinalDecideAndTransition());
        }
    }

    void GoToStep(SelectStep next)
    {
        step = next;
        ApplyStepVisuals();
    }

    // 現在のステップに合わせて、カーソル／READYマーク／ドアップ／強さ選択UIの表示を切り替える
    void ApplyStepVisuals()
    {
        // 自分: ①の間はカーソル、②③の間はREADYマーク
        SetCursorVisible(player, step == SelectStep.Player);
        SetReady(player, step != SelectStep.Player);

        // CPU: ①の間は何も表示しない／②はカーソル／③はREADYマーク
        SetCursorVisible(cpu, step == SelectStep.Cpu);
        SetReady(cpu, step == SelectStep.Difficulty);
        if (step == SelectStep.Player)
        {
            HideSelectionDisplay(cpu);
        }
        else
        {
            UpdateSelectionDisplay(cpu);
        }

        // 強さ選択: ③の間だけ表示
        if (difficultyRoot != null) difficultyRoot.SetActive(step == SelectStep.Difficulty);
        bool showDifficulty = step == SelectStep.Difficulty;
        bool hasDifficulty = difficulties != null && difficulties.Length > 0;
        if (difficultyCursor != null)
        {
            if (showDifficulty && hasDifficulty)
            {
                // 表示する瞬間に、今選んでいる強さの位置へ合わせる
                SnapCursor(difficultyCursor, difficulties[difficultyIndex].anchor, difficultyCursorOffset);
            }
            difficultyCursor.gameObject.SetActive(showDifficulty);
        }
        else if (showDifficulty && hasDifficulty)
        {
            Debug.LogWarning("[CharacterSelect_SFC_Controller] 強さ選択のカーソルが未設定です。difficultyCursorPrefab か difficultyCursor を設定してください。", this);
        }

        // 強さのアイコン: ③の間だけ表示
        if (difficultyIcons != null)
        {
            foreach (var icon in difficultyIcons)
            {
                if (icon != null) icon.SetActive(step == SelectStep.Difficulty);
            }
        }

        // 強さ選択に入った時は、アイコンを表示した後でカーソルを現在の項目のアイコン位置へ合わせる
        if (step == SelectStep.Difficulty && difficulties != null && difficulties.Length > 0)
        {
            Canvas.ForceUpdateCanvases(); // レイアウトが確定してから位置を取る
            SnapCursorTo(difficultyCursor, DifficultyPosition(difficultyIndex), difficultyCursorOffset);
        }

        ApplyLayerOrder();
    }

    // 強さ選択が無い場合に、CPUのREADYマークだけ見せるための補助
    void ApplyStepVisualsForCpuDecided()
    {
        SetCursorVisible(cpu, false);
        SetReady(cpu, true);
    }

    // READYマークの表示切り替え（表示する時は選択キャラの位置へ移動してから出す）
    void SetReady(CharacterSelector s, bool active)
    {
        if (s == null) return;

        if (s.readyMark == null)
        {
            if (active)
            {
                Debug.LogWarning($"[CharacterSelect_SFC_Controller] {s.label} の readyMark が未設定です。シーン上のオブジェクトを割り当ててください。", this);
            }
            return;
        }

        // 念のため：まだプレハブのままなら生成する（プレハブのアセット自体はSetActiveしない）
        EnsureReadyMark(s);

        if (active) PlaceReadyMarkOnSelectedCharacter(s);
        s.readyMark.SetActive(active);

        // 自身をアクティブにしても、親が非アクティブだと画面には出ない
        if (active && !s.readyMark.activeInHierarchy)
        {
            Debug.LogWarning($"[CharacterSelect_SFC_Controller] {s.label} の readyMark の親が非アクティブのため表示されません。", s.readyMark);
        }
    }

    // READYマークを、選択しているキャラクターの位置へ移動させる
    void PlaceReadyMarkOnSelectedCharacter(CharacterSelector s)
    {
        if (s.readyMark == null) return;
        if (s.currentIndex < 0 || s.currentIndex >= characters.Length) return;

        var anchor = characters[s.currentIndex].anchor;
        if (anchor == null) return;

        s.readyMark.transform.position = anchor.position + (Vector3)s.readyMarkOffset;
    }

    void SetCursorVisible(CharacterSelector s, bool visible)
    {
        if (s == null) return;

        if (s.cursor == null)
        {
            if (visible)
            {
                Debug.LogWarning($"[CharacterSelect_SFC_Controller] {s.label} のカーソルが null です。cursorPrefab か cursor を設定してください。", this);
            }
            return;
        }

        if (visible)
        {
            // 表示する瞬間に現在のキャラの位置へ合わせる（Start時はレイアウト未確定でズレることがあるため）
            if (s.currentIndex >= 0 && s.currentIndex < characters.Length)
            {
                SnapCursor(s.cursor, characters[s.currentIndex].anchor, s.cursorOffset);
            }
        }

        s.cursor.gameObject.SetActive(visible);
    }

    // 現在位置(fromAnchor)から見て、dir方向にある「一番近い選択肢」のインデックスを返す。見つからなければ-1。
    // dirが伸びる方向（主軸）の距離を優先しつつ、主軸から外れるほどペナルティを与えることで、
    // 単純なグリッドでなくても自然に「右にある一番近いもの」「上にある一番近いもの」を選べるようにしている。
    // キャラ選択・強さ選択の両方で共通して使う。
    int FindNearest(Transform fromAnchor, Vector2 dir, int fromIndex, int count,
                    System.Func<int, Transform> anchorOf, System.Func<int, bool> allowed)
    {
        if (fromAnchor == null) return -1;

        return FindNearestByPosition(fromAnchor.position, dir, fromIndex, count,
            i =>
            {
                var a = anchorOf(i);
                return a != null ? (Vector3?)a.position : null;
            },
            allowed);
    }

    // 位置（ワールド座標）ベースの本体。positionOf が null を返す選択肢は無視する。
    int FindNearestByPosition(Vector3 fromPosition, Vector2 dir, int fromIndex, int count,
                              System.Func<int, Vector3?> positionOf, System.Func<int, bool> allowed)
    {
        Vector2 currentPos = fromPosition;
        Vector2 dirNormalized = dir.normalized;
        Vector2 perpAxis = new Vector2(-dirNormalized.y, dirNormalized.x); // dirに直交する軸

        int bestIndex = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (i == fromIndex) continue;
            if (allowed != null && !allowed(i)) continue;

            var pos = positionOf(i);
            if (!pos.HasValue) continue;

            Vector2 offset = (Vector2)pos.Value - currentPos;

            float primary = Vector2.Dot(offset, dirNormalized);
            if (primary <= 0.01f) continue; // 指定方向とは逆・同位置にあるものは除外

            float perpendicular = Mathf.Abs(Vector2.Dot(offset, perpAxis));

            float score = primary + perpendicular * 2f;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    // ---- ドアップ・名前表示 ----

    // カーソルが乗っているキャラクターのドアップ画像・名前表示を反映する
    void UpdateSelectionDisplay(CharacterSelector s)
    {
        if (s == null) return;
        if (characters == null || characters.Length == 0) return;

        var entry = characters[s.currentIndex];

        ShowPortrait(s, s.currentIndex);
        ShowNameUp(s, s.currentIndex);

        // 従来方式：namePrefab が無いキャラだけ、名前画像(Sprite)をnameImageObjectへ反映する
        if (s.nameImageObject != null)
        {
            var nameSprite = entry.namePrefab == null ? entry.nameImage : null;
            s.nameImageObject.sprite = nameSprite;

            // 表示するものが無い場合はImageを非表示にして「空の白い四角」が出ないようにする
            s.nameImageObject.enabled = nameSprite != null;
        }
    }

    // ドアップ・ネームアップ表示を隠す（CPU選択に入る前など）
    void HideSelectionDisplay(CharacterSelector s)
    {
        if (s == null) return;

        if (s.currentPortrait != null)
        {
            s.currentPortrait.SetActive(false);
            s.currentPortrait = null;
        }
        if (s.currentName != null)
        {
            s.currentName.SetActive(false);
            s.currentName = null;
        }
        if (s.nameImageObject != null)
        {
            s.nameImageObject.enabled = false;
        }
    }

    // 選択中キャラのドアップ用プレハブを表示する。
    void ShowPortrait(CharacterSelector s, int characterIndex)
    {
        if (s.portraitInstances == null) s.portraitInstances = new Dictionary<int, GameObject>();

        ShowPrefab(s.portraitInstances, ref s.currentPortrait, s.portraitRoot,
                   characters[characterIndex].portraitPrefab, characterIndex, s.portraitOffset, s.portraitScale, null);
    }

    // 選択中キャラのネームアップ用プレハブを表示する。
    void ShowNameUp(CharacterSelector s, int characterIndex)
    {
        if (s.nameInstances == null) s.nameInstances = new Dictionary<int, GameObject>();

        // nameRoot が未設定でも namePoint があれば、その親を生成先に使う
        var root = s.nameRoot != null ? s.nameRoot : (s.namePoint != null ? s.namePoint.parent : null);

        ShowPrefab(s.nameInstances, ref s.currentName, root,
                   characters[characterIndex].namePrefab, characterIndex, s.nameOffset, s.nameScale, s.namePoint);
    }

    // ドアップ／ネームアップ共通の表示処理。
    // 初回はroot の子として生成し、2回目以降は生成済みインスタンスのアクティブ切り替えだけを行う。
    // point が指定されている場合は、表示のたびにその座標（＋offset）へ移動させる。
    void ShowPrefab(Dictionary<int, GameObject> cache, ref GameObject current, Transform root,
                    GameObject prefab, int characterIndex, Vector3 offset, float scale, Transform point)
    {
        if (root == null) return;

        // 前に表示していたものは非表示にする
        if (current != null)
        {
            current.SetActive(false);
            current = null;
        }

        if (prefab == null) return; // 未設定のキャラは何も表示しない

        // 破棄済み（親ごとDestroyされた等）のキャッシュが残っていたら作り直す
        if (!cache.TryGetValue(characterIndex, out var instance) || instance == null)
        {
            instance = Instantiate(prefab, root, false);
            PlaceInstance(instance, offset, scale);
            cache[characterIndex] = instance;
        }

        // 位置用の空オブジェクトが指定されていれば、表示のたびにその座標へ合わせる
        // （Start時はレイアウト未確定でズレることがあるため、生成時だけでなく毎回合わせる）
        if (point != null)
        {
            instance.transform.position = point.position + offset;
        }

        instance.SetActive(true);
        current = instance;
    }

    // 生成したプレハブを root の中心（＋offset）に配置し、拡大率を反映する。
    // プレハブ自身の位置は上書きされるが、回転とスケールはプレハブの設定を活かす。
    void PlaceInstance(GameObject instance, Vector3 offset, float scale)
    {
        var t = instance.transform;

        if (t is RectTransform rt)
        {
            rt.anchoredPosition3D = offset;
        }
        else
        {
            t.localPosition = offset;
        }

        t.localScale = t.localScale * (scale > 0f ? scale : 1f);
    }

    // AudioClipがInspectorで未設定の場合にPlayOneShot(null)警告が出るのを防ぐ
    void PlaySE(AudioClip clip)
    {
        if (se != null && clip != null)
        {
            se.PlayOneShot(clip);
        }
    }

    IEnumerator MoveCursorSmooth(Transform cursor, Vector3 target)
    {
        while (Vector3.Distance(cursor.position, target) > 0.5f)
        {
            cursor.position = Vector3.MoveTowards(cursor.position, target, cursorMoveSpeed * Time.deltaTime * 1000f);
            yield return null;
        }
        cursor.position = target;
    }

    IEnumerator FinalDecideAndTransition()
    {
        isTransitioning = true;

        SaveSelectionResult();

        PlaySE(finalDecideSE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // 自分・CPUのキャラと強さを、名前で明示的に区別して CpuSelectionResult へ書き込む
    void SaveSelectionResult()
    {
        CpuSelectionResult.PlayerCharacterIndex = player.currentIndex;
        CpuSelectionResult.PlayerCharacterName = characters[player.currentIndex].characterName;

        CpuSelectionResult.CpuCharacterIndex = cpu.currentIndex;
        CpuSelectionResult.CpuCharacterName = characters[cpu.currentIndex].characterName;

        bool hasDifficulty = difficulties != null && difficulties.Length > 0;
        CpuSelectionResult.CpuDifficultyIndex = hasDifficulty ? difficultyIndex : 0;
        CpuSelectionResult.CpuDifficultyName = hasDifficulty ? difficulties[difficultyIndex].difficultyName : null;
    }

    // ---- 入力読み取り（1Pのみ） ----
    // キーボード 矢印キー／WASD で移動、Enterで決定、Escでキャンセル。
    // ゲームパッド0番も同時に受け付ける（D-Pad／左スティックで移動、South=決定、East=キャンセル）。
    // 戻り値のVector2Intは x: 右+1/左-1、y: 上+1/下-1。

    Vector2Int ReadDirection()
    {
        var kb = ReadDirectionFromKeyboard();
        if (kb != Vector2Int.zero) return kb;

        return ReadDirectionFromGamepad(GetGamepad(0));
    }

    bool ReadDecide()
    {
        if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    bool ReadCancel()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

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
