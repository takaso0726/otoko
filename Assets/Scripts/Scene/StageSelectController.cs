using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// ステージセレクト画面の制御（1P専用）。
/// 1Pがカーソルでステージを選び、決定ボタンで確定したら
/// 演出→ディレイを挟んで「そのステージごとにInspectorで設定したシーン」へ遷移する。
/// カーソルを合わせたステージのプレビュー画像をpreviewImageに表示する。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// ・全体の構成は CharacterSelect_SFC_Controller1.cs（1P専用キャラセレ）を踏襲。
/// </summary>
public class StageSelectController : MonoBehaviour
{
    [System.Serializable]
    public class StageEntry
    {
        public string stageName;      // 表示名
        public RectTransform anchor;  // グリッド上の位置（各ステージサムネイルのUI要素）
        public Sprite thumbnail;      // 一覧上のサムネイル画像
        public Sprite preview;        // カーソルを合わせた時に表示する大きめのプレビュー画像
        public string sceneName;      // このステージが決定された時に遷移するシーン名（Inspectorで設定）
    }

    [Header("ステージ一覧（グリッド順）")]
    [SerializeField] StageEntry[] stages;

    [Header("プレイヤー")]
    [SerializeField] RectTransform cursor;     // 1P用カーソル
    [SerializeField] GameObject readyMark;     // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく）
    [SerializeField] Image previewImage;       // 選択中ステージのプレビュー表示用UI Image
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;      // カーソル移動時
    [SerializeField] AudioClip decideSE;    // 決定した時（遷移演出のSEも兼ねる）

    [Header("遷移設定")]
    [Tooltip("各StageEntryのsceneNameが未設定（空文字）だった場合に使うフォールバック用シーン名。")]
    [SerializeField] string fallbackSceneName = "InGame1v1";
    [SerializeField] float transitionDelay = 0.7f;

    int currentIndex;
    bool decided;
    bool isTransitioning;
    Coroutine cursorRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化された時に必ずリセットする。
        // これが無いと、一度決定した後にこのオブジェクトが再利用された場合、
        // 二度と入力を受け付けなくなる不具合の原因になっていた。
        decided = false;
        isTransitioning = false;
        SetReadyMarkActive(false);
    }

    void Start()
    {
        if (stages == null || stages.Length == 0)
        {
            Debug.LogWarning("[StageSelectController] stages が設定されていません。", this);
            return;
        }

        currentIndex = 0;

        if (cursor != null && stages[0].anchor != null)
        {
            cursor.position = stages[0].anchor.position;
        }
        else
        {
            Debug.LogWarning("[StageSelectController] cursor または stages[0].anchor が未設定です。", this);
        }

        SetReadyMarkActive(false);
        UpdatePreview();
    }

    void Update()
    {
        if (isTransitioning || decided) return;
        if (stages == null || stages.Length == 0) return;

        Vector2Int dir = ReadDirection();
        if (dir != Vector2Int.zero)
        {
            int nextIndex = FindNearestInDirection(currentIndex, new Vector2(dir.x, dir.y));
            if (nextIndex >= 0)
            {
                currentIndex = nextIndex;
                PlaySE(moveSE);

                if (cursor != null && stages[currentIndex].anchor != null)
                {
                    if (cursorRoutine != null) StopCoroutine(cursorRoutine);
                    cursorRoutine = StartCoroutine(MoveCursorSmooth(cursor, stages[currentIndex].anchor.position));
                }

                UpdatePreview();
            }
        }

        if (ReadDecide())
        {
            Decide();
        }
    }

    // 現在位置(fromIndex)から見て、dir方向にある「一番近いステージ」のインデックスを返す。
    // 見つからなければ-1。
    // dirが伸びる方向（主軸）の距離を優先しつつ、主軸から外れる（横ズレ・縦ズレ）ほどペナルティを与えることで、
    // 単純な一直線グリッドでなくても自然に「右にある一番近いステージ」「上にある一番近いステージ」を選べるようにしている。
    int FindNearestInDirection(int fromIndex, Vector2 dir)
    {
        var fromAnchor = stages[fromIndex].anchor;
        if (fromAnchor == null) return -1;

        Vector2 currentPos = fromAnchor.position;
        Vector2 dirNormalized = dir.normalized;
        Vector2 perpAxis = new Vector2(-dirNormalized.y, dirNormalized.x); // dirに直交する軸

        int bestIndex = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < stages.Length; i++)
        {
            if (i == fromIndex) continue;

            var anchor = stages[i].anchor;
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
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    void Decide()
    {
        decided = true;
        SetReadyMarkActive(true);
        PlaySE(decideSE);

        StartCoroutine(TransitionToInGame());
    }

    IEnumerator MoveCursorSmooth(Transform target, Vector3 pos)
    {
        while (Vector3.Distance(target.position, pos) > 0.5f)
        {
            target.position = Vector3.MoveTowards(target.position, pos, cursorMoveSpeed * Time.deltaTime * 1000f);
            yield return null;
        }
        target.position = pos;
    }

    IEnumerator TransitionToInGame()
    {
        isTransitioning = true;

        var decidedStage = stages[currentIndex];

        // 確定したステージ情報を他シーンから参照できるよう保存しておく。
        // インゲーム側のロード処理で StageSelectController.SelectedStageName を読む想定。
        SelectedStageIndex = currentIndex;
        SelectedStageName = decidedStage.stageName;

        // ステージごとにInspectorで設定したsceneNameへ遷移する。
        // 未設定（空文字）の場合はfallbackSceneNameを使う。
        string targetScene = string.IsNullOrEmpty(decidedStage.sceneName)
            ? fallbackSceneName
            : decidedStage.sceneName;

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning($"[StageSelectController] ステージ「{decidedStage.stageName}」の遷移先シーンが設定されていません。", this);
            isTransitioning = false;
            decided = false;
            SetReadyMarkActive(false);
            yield break;
        }

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(targetScene);
    }

    // 確定したステージをインゲームシーン側から参照するための静的プロパティ。
    // （PlayerPrefsやGameManagerなど、プロジェクトの既存の受け渡し方法があればそちらに置き換えてOK）
    public static int SelectedStageIndex { get; private set; } = -1;
    public static string SelectedStageName { get; private set; }

    // GameObjectの「疑似null（参照は残っているが実体が破棄されている状態）」でも
    // 安全に判定できるよう、?.ではなく明示的なUnityの==比較でチェックする
    void SetReadyMarkActive(bool active)
    {
        if (readyMark != null)
        {
            readyMark.SetActive(active);
        }
    }

    // カーソルが乗っているステージのプレビュー画像をpreviewImageに反映する
    void UpdatePreview()
    {
        if (previewImage == null) return;
        if (stages == null || stages.Length == 0) return;

        var sprite = stages[currentIndex].preview;
        previewImage.sprite = sprite;

        // preview未設定のステージの場合はImageを非表示にして「空の白い四角」が出ないようにする
        previewImage.enabled = sprite != null;
    }

    // AudioClipがInspectorで未設定の場合にPlayOneShot(null)警告が出るのを防ぐ
    void PlaySE(AudioClip clip)
    {
        if (se != null && clip != null)
        {
            se.PlayOneShot(clip);
        }
    }

    // ---- 入力読み取り ----
    // 1P: キーボード 矢印キー／WASD で移動、Enterで決定
    // （ゲームパッドが繋がっていればそちらの入力も受け付ける）
    // 戻り値のVector2Intは x: 右+1/左-1、y: 上+1/下-1。
    // 同一フレームで斜め入力があった場合は左右を優先する。
    Vector2Int ReadDirection()
    {
        int x = 0;
        int y = 0;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) x = 1;
            else if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) x = -1;

            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) y = 1;
            else if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) y = -1;
        }

        if (x == 0 && y == 0 && Gamepad.current != null)
        {
            var pad = Gamepad.current;

            if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) x = 1;
            else if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) x = -1;

            if (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame) y = 1;
            else if (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame) y = -1;
        }

        if (x != 0) y = 0; // 斜め入力は左右を優先

        return new Vector2Int(x, y);
    }

    bool ReadDecide()
    {
        return (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
    }
}
