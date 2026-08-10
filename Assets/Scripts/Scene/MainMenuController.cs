using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// メインメニューの制御。
/// ①漢たちの生き様 ②猛者連戦 ③拳の交差点 ④己の鍛錬と衣替え ⑤漢の御法度 ⑥漢の散り際
/// の6項目を、選択中アイコンの「外側の光（アウトライン）」で選択表示する。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [System.Serializable]
    public class MenuItem
    {
        public string label;          // 例：「① 漢たちの生き様（ストーリーモード）」
        public RectTransform anchor;  // アイコンの位置（未使用でも将来のために保持）
        public string sceneToLoad;    // 遷移先シーン名。終了項目は "QUIT" にする

        [Tooltip("選択中に光らせる、アイコンの外側に重ねて配置したアウトライン用オブジェクト（Imageなど）")]
        public GameObject glowOutline;
    }

    [Header("メニュー項目（上から並べる）")]
    [SerializeField] MenuItem[] items;

    [Header("選択アイコンの光る演出")]
    [SerializeField] float glowPulseSpeed = 3f;   // 明滅の速さ
    [SerializeField] float glowMinAlpha = 0.4f;   // 明滅の下限（0〜1）
    [SerializeField] float glowMaxAlpha = 1f;     // 明滅の上限（0〜1）

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
    Coroutine glowPulseRoutine;

    void OnEnable()
    {
        // シーンに戻ってきた／再アクティブ化されたときに必ず入力ロックを解除する。
        // これが無いと、一度決定した後に元のオブジェクトが再利用された場合、
        // 二度と入力を受け付けなくなる不具合の原因になっていた。
        inputLocked = false;
    }

    void Start()
    {
        // items が未設定の場合に例外で Update が止まらないよう防御。
        if (items == null || items.Length == 0)
        {
            Debug.LogWarning("[MainMenuController] items が設定されていません。", this);
            return;
        }

        currentIndex = 0;
        SetSelectedGlow(currentIndex);
    }

    void Update()
    {
        if (inputLocked) return;
        if (items == null || items.Length == 0) return;

        int nav = ReadHorizontalNav();
        if (nav != 0)
        {
            MoveSelection(nav); // 右入力(+1)でインデックスが進む想定
        }

        if (ReadDecide())
        {
            Decide();
        }
    }

    int ReadHorizontalNav()
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

    void MoveSelection(int dir)
    {
        currentIndex = (currentIndex + dir + items.Length) % items.Length;

        if (se != null && moveSE != null) se.PlayOneShot(moveSE);
        idleShowcase?.NotifyInput();

        SetSelectedGlow(currentIndex);
    }

    /// <summary>
    /// 選択中の項目だけアウトラインを表示し、明滅させる。他は非表示にする。
    /// </summary>
    void SetSelectedGlow(int selectedIndex)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var glow = items[i].glowOutline;
            if (glow == null) continue;

            if (i == selectedIndex)
            {
                glow.SetActive(true);
            }
            else
            {
                glow.SetActive(false);
            }
        }

        if (glowPulseRoutine != null) StopCoroutine(glowPulseRoutine);

        var selectedGlow = items[selectedIndex].glowOutline;
        if (selectedGlow != null)
        {
            glowPulseRoutine = StartCoroutine(GlowPulse(selectedGlow));
        }
    }

    /// <summary>
    /// CanvasGroupのalphaを使って、選択中アイコンの光をゆっくり明滅させる。
    /// glowOutlineにCanvasGroupが無ければ自動で追加する。
    /// </summary>
    IEnumerator GlowPulse(GameObject glowObject)
    {
        var canvasGroup = glowObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = glowObject.AddComponent<CanvasGroup>();
        }

        while (true)
        {
            // 0〜1を往復するサイン波でalphaをmin〜maxの範囲に収める。
            float t = (Mathf.Sin(Time.time * glowPulseSpeed) + 1f) * 0.5f;
            canvasGroup.alpha = Mathf.Lerp(glowMinAlpha, glowMaxAlpha, t);
            yield return null;
        }
    }

    void Decide()
    {
        idleShowcase?.NotifyInput();
        if (se != null && decideSE != null) se.PlayOneShot(decideSE);

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
            // PlayOneShot直後にLoadSceneすると、SEが鳴り切る前にAudioSourceごと
            // 破棄されて音が切れてしまうため、SEの再生時間だけ待ってから遷移する。
            StartCoroutine(LoadSceneAfterDecideSE(item.sceneToLoad));
        }
    }

    IEnumerator LoadSceneAfterDecideSE(string sceneName)
    {
        float wait = (se != null && decideSE != null) ? decideSE.length : 0f;
        if (wait > 0f)
        {
            yield return new WaitForSeconds(wait);
        }
        SceneManager.LoadScene(sceneName);
    }
}
