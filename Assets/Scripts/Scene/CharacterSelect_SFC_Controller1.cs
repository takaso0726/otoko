using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// キャラクターセレクト画面の制御（1P専用）。
/// 1Pがカーソルでキャラクターを選び、決定ボタンで確定したら
/// 演出→ディレイを挟んでインゲームシーンへ遷移する。
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
    }

    [Header("キャラクター一覧（グリッド順）")]
    [SerializeField] CharacterEntry[] characters;

    [Header("プレイヤー")]
    [SerializeField] Transform cursor;         // 1P用カーソル（3Dオブジェクト／UI要素どちらでも可）
    [SerializeField] GameObject readyMark;     // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく）
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

        cursor.position = characters[0].anchor.position;
        SetReadyMarkActive(false);
    }

    void Update()
    {
        if (isTransitioning || decided) return;

        int nav = ReadHorizontal();
        if (nav != 0)
        {
            currentIndex = (currentIndex + nav + characters.Length) % characters.Length;
            PlaySE(moveSE);

            if (cursorRoutine != null) StopCoroutine(cursorRoutine);
            cursorRoutine = StartCoroutine(MoveCursorSmooth(cursor, characters[currentIndex].anchor.position));
        }

        if (ReadDecide())
        {
            Decide();
        }
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

    // AudioClipがInspectorで未設定の場合にPlayOneShot(null)警告が出るのを防ぐ
    void PlaySE(AudioClip clip)
    {
        if (se != null && clip != null)
        {
            se.PlayOneShot(clip);
        }
    }

    // ---- 入力読み取り ----
    // 1P: キーボード 左右／A・D で移動、Enterで決定
    // （ゲームパッドが繋がっていればそちらの入力も受け付ける）
    int ReadHorizontal()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) return 1;
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) return -1;
        }
        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.right.wasPressedThisFrame || Gamepad.current.leftStick.right.wasPressedThisFrame) return 1;
            if (Gamepad.current.dpad.left.wasPressedThisFrame || Gamepad.current.leftStick.left.wasPressedThisFrame) return -1;
        }
        return 0;
    }

    bool ReadDecide()
    {
        return (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
    }
}
