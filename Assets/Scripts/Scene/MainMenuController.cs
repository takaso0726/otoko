using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// メインメニューの制御。
/// ①漢たちの生き様 ②猛者連戦 ③拳の交差点 ④己の鍛錬と衣替え ⑤漢の御法度 ⑥漢の散り際
/// の6項目を「炎のカーソル」で選択する。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [System.Serializable]
    public class MenuItem
    {
        public string label;          // 例：「① 漢たちの生き様（ストーリーモード）」
        public RectTransform anchor;  // カーソルを合わせる位置（各項目のUI要素）
        public string sceneToLoad;    // 遷移先シーン名。終了項目は "QUIT" にする
    }

    [Header("メニュー項目（上から並べる）")]
    [SerializeField] MenuItem[] items;

    [Header("炎のカーソル")]
    [SerializeField] RectTransform fireCursor;
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;   // 「ドンッ」項目切り替え時
    [SerializeField] AudioClip decideSE; // 「バシィッ」決定時

    [Header("放置演出（1分放置でデモ/格言）")]
    [SerializeField] IdleShowcaseManager idleShowcase;

    [Header("終了確認ポップアップ（PC版）")]
    [SerializeField] QuitConfirmPopup quitPopup;

    int currentIndex;
    bool inputLocked;
    Coroutine cursorMoveRoutine;

    void Start()
    {
        if (items.Length > 0) fireCursor.position = items[0].anchor.position;
    }

    void Update()
    {
        if (inputLocked) return;

        int nav = ReadVerticalNav();
        if (nav != 0)
        {
            MoveSelection(-nav); // 上入力(+1)でインデックスは減る想定
        }

        if (ReadDecide())
        {
            Decide();
        }
    }

    int ReadVerticalNav()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) return 1;
            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) return -1;
        }
        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.up.wasPressedThisFrame || Gamepad.current.leftStick.up.wasPressedThisFrame) return 1;
            if (Gamepad.current.dpad.down.wasPressedThisFrame || Gamepad.current.leftStick.down.wasPressedThisFrame) return -1;
        }
        return 0;
    }

    bool ReadDecide()
    {
        return (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
    }

    void MoveSelection(int dir)
    {
        currentIndex = (currentIndex + dir + items.Length) % items.Length;

        se.PlayOneShot(moveSE);
        idleShowcase?.NotifyInput();

        if (cursorMoveRoutine != null) StopCoroutine(cursorMoveRoutine);
        cursorMoveRoutine = StartCoroutine(MoveCursorSmooth(items[currentIndex].anchor.position));
    }

    IEnumerator MoveCursorSmooth(Vector3 target)
    {
        while (Vector3.Distance(fireCursor.position, target) > 0.5f)
        {
            fireCursor.position = Vector3.MoveTowards(fireCursor.position, target, cursorMoveSpeed * Time.deltaTime * 1000f);
            yield return null;
        }
        fireCursor.position = target;
    }

    void Decide()
    {
        idleShowcase?.NotifyInput();
        se.PlayOneShot(decideSE);

        var item = items[currentIndex];

        // ⑥ 漢の散り際（ゲーム終了）は専用の煽りポップアップを出す
        if (item.sceneToLoad == "QUIT")
        {
            quitPopup?.Open();
            return;
        }

        if (!string.IsNullOrEmpty(item.sceneToLoad))
        {
            inputLocked = true;
            SceneManager.LoadScene(item.sceneToLoad);
        }
    }
}
