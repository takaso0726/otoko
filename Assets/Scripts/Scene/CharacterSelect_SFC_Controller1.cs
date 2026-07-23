using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// キャラクターセレクト画面の制御（1P専用）。
/// 1Pがカーソルでキャラクターを選び、決定ボタンで確定したら
/// 演出→ディレイを挟んでインゲームシーンへ遷移する。
/// カーソルを合わせたキャラクターのドアップ画像をportraitImageに表示する。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// </summary>
public class CharacterSelect_SFC_Controller : MonoBehaviour
{
    [System.Serializable]
    public class CharacterEntry
    {
        public string characterName;  // 表示名
        public Transform anchor;      // グリッド上の位置（各キャラの3Dオブジェクト／UI要素どちらでも可）
        public Sprite portrait;       // カーソルを合わせた時に表示するドアップ画像
    }

    [Header("キャラクター一覧（グリッド順）")]
    [SerializeField] CharacterEntry[] characters;

    [Header("プレイヤー")]
    [SerializeField] Transform cursor;         // 1P用カーソル（3Dオブジェクト／UI要素どちらでも可）
    [SerializeField] GameObject readyMark;     // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく）
    [SerializeField] Image portraitImage;      // 選択中キャラのドアップ表示用UI Image
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;      // カーソル移動時
    [SerializeField] AudioClip decideSE;    // 決定した時（遷移演出のSEも兼ねる）

    [Header("遷移設定")]
    [SerializeField] string nextSceneName = "InGame1v1";
    [SerializeField] float transitionDelay = 0.7f;

    int currentIndex;
    bool decided;
    bool isTransitioning;
    Coroutine cursorRoutine;

    void Start()
    {
        if (characters.Length == 0) return;

        currentIndex = 0;
        cursor.position = characters[0].anchor.position;
        SetReadyMarkActive(false);
        UpdatePortrait();
    }

    void Update()
    {
        if (isTransitioning || decided) return;

        Vector2Int dir = ReadDirection();
        if (dir != Vector2Int.zero)
        {
            int nextIndex = FindNearestInDirection(currentIndex, new Vector2(dir.x, dir.y));
            if (nextIndex >= 0)
            {
                currentIndex = nextIndex;
                PlaySE(moveSE);

                if (cursorRoutine != null) StopCoroutine(cursorRoutine);
                cursorRoutine = StartCoroutine(MoveCursorSmooth(cursor, characters[currentIndex].anchor.position));

                UpdatePortrait();
            }
        }

        if (ReadDecide())
        {
            Decide();
        }
    }

    // 現在位置(fromIndex)から見て、dir方向にある「一番近いキャラクター」のインデックスを返す。
    // 見つからなければ-1。
    // dirが伸びる方向（主軸）の距離を優先しつつ、主軸から外れる（横ズレ・縦ズレ）ほどペナルティを与えることで、
    // 単純な一直線グリッドでなくても自然に「右にある一番近いキャラ」「上にある一番近いキャラ」を選べるようにしている。
    int FindNearestInDirection(int fromIndex, Vector2 dir)
    {
        var fromAnchor = characters[fromIndex].anchor;
        if (fromAnchor == null) return -1;

        Vector2 currentPos = fromAnchor.position;
        Vector2 dirNormalized = dir.normalized;
        Vector2 perpAxis = new Vector2(-dirNormalized.y, dirNormalized.x); // dirに直交する軸

        int bestIndex = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < characters.Length; i++)
        {
            if (i == fromIndex) continue;

            var anchor = characters[i].anchor;
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

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // GameObjectの「疑似null（参照は残っているが実体が破棄されている状態）」でも
    // 安全に判定できるよう、?.ではなく明示的なUnityの==比較でチェックする
    void SetReadyMarkActive(bool active)
    {
        if (readyMark != null)
        {
            readyMark.SetActive(active);
        }
    }

    // カーソルが乗っているキャラクターのドアップ画像をportraitImageに反映する
    void UpdatePortrait()
    {
        if (portraitImage == null) return;

        var sprite = characters[currentIndex].portrait;
        portraitImage.sprite = sprite;

        // portrait未設定のキャラの場合はImageを非表示にして「空の白い四角」が出ないようにする
        portraitImage.enabled = sprite != null;
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
