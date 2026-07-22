using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// キャラクターセレクト画面の制御。
/// 1P・2Pがそれぞれ独立したカーソルでキャラクターを選び、決定ボタンで確定する。
/// 両者の選択が確定したら、演出→ディレイを挟んでインゲームシーンへ遷移する。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// </summary>
public class CharacterSelectController : MonoBehaviour
{
    [System.Serializable]
    public class CharacterEntry
    {
        public string characterName;  // 表示名
        public RectTransform anchor;  // グリッド上の位置（各キャラアイコンのUI要素）
    }

    [System.Serializable]
    public class PlayerSelector
    {
        public string playerName;      // "1P" / "2P" など表示用
        public RectTransform cursor;   // このプレイヤー用カーソル
        public GameObject readyMark;   // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく）

        [HideInInspector] public int currentIndex;
        [HideInInspector] public bool decided;
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
    Coroutine p1CursorRoutine;
    Coroutine p2CursorRoutine;

    void Start()
    {
        if (characters.Length == 0) return;

        player1.cursor.position = characters[0].anchor.position;
        player2.cursor.position = characters[0].anchor.position;
        player1.readyMark?.SetActive(false);
        player2.readyMark?.SetActive(false);
    }

    void Update()
    {
        if (isTransitioning) return;

        HandlePlayer(player1, ReadP1Horizontal(), ReadP1Decide(), ReadP1Cancel(), ref p1CursorRoutine);
        HandlePlayer(player2, ReadP2Horizontal(), ReadP2Decide(), ReadP2Cancel(), ref p2CursorRoutine);

        // 両者が決定していたら遷移演出へ
        if (player1.decided && player2.decided)
        {
            StartCoroutine(BothReadyAndTransition());
        }
    }

    void HandlePlayer(PlayerSelector p, int nav, bool decide, bool cancel, ref Coroutine routine)
    {
        if (p.decided)
        {
            // 決定後はキャンセル入力のみ受け付けて選び直しできるようにする
            if (cancel)
            {
                p.decided = false;
                p.readyMark?.SetActive(false);
            }
            return;
        }

        if (nav != 0)
        {
            p.currentIndex = (p.currentIndex + nav + characters.Length) % characters.Length;
            se.PlayOneShot(moveSE);

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(MoveCursorSmooth(p.cursor, characters[p.currentIndex].anchor.position));
        }

        if (decide)
        {
            p.decided = true;
            p.readyMark?.SetActive(true);
            se.PlayOneShot(decideSE);
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

        if (bothReadySE != null) se.PlayOneShot(bothReadySE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // ---- 入力読み取り ----

    // 1P: キーボード 左右／A・D で移動、Enterで決定、Escでキャンセル
    int ReadP1Horizontal()
    {
        if (Keyboard.current == null) return 0;
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) return 1;
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) return -1;
        return 0;
    }
    bool ReadP1Decide() => Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
    bool ReadP1Cancel() => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

    // 2P: 2台目のゲームパッド（1台しか無い場合はそれを共用）
    int ReadP2Horizontal()
    {
        var pad = GetPlayer2Gamepad();
        if (pad == null) return 0;
        if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) return 1;
        if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) return -1;
        return 0;
    }
    bool ReadP2Decide()
    {
        var pad = GetPlayer2Gamepad();
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }
    bool ReadP2Cancel()
    {
        var pad = GetPlayer2Gamepad();
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    Gamepad GetPlayer2Gamepad()
    {
        if (Gamepad.all.Count >= 2) return Gamepad.all[1];
        return Gamepad.all.Count == 1 ? Gamepad.all[0] : null;
    }
}
