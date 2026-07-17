using UnityEngine;

/// <summary>
/// ⑥漢の散り際（ゲーム終了）専用の確認ポップアップ。
/// 「ゲームを終了しますか？」ではなく「ここで背中を向けるのか？」とプライドを煽る。
/// はい／いいえボタンのOnClickからConfirmQuit/Cancelを呼び出す。
/// </summary>
public class QuitConfirmPopup : MonoBehaviour
{
    [SerializeField] GameObject popupRoot; // 「ここで背中を向けるのか？（はい／いいえ）」のUIルート
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip openSE;

    public void Open()
    {
        popupRoot.SetActive(true);
        if (se != null && openSE != null) se.PlayOneShot(openSE);
    }

    /// <summary>「はい」ボタンから呼ぶ</summary>
    public void ConfirmQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>「いいえ」ボタンから呼ぶ</summary>
    public void Cancel()
    {
        popupRoot.SetActive(false);
    }
}
