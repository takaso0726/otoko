using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

//=====================================================
// 【デバッグ】F12キーで、どのシーンからでもタイトル画面へ戻る。
//
// ・シーンへの配置は不要。ゲーム起動時に自動で常駐オブジェクトが作られ、シーンをまたいで動き続ける。
// ・Editor / Development Build でのみ有効（製品ビルドでは何も生成されない）。
// ・タイトル画面のシーン名を下の TitleSceneName に合わせること。
//   シーン名で見つからなかった場合は、Build Settings の0番目のシーンへ戻る。
//   （どちらの場合も、Build Settings にタイトルシーンが登録されている必要があります）
//=====================================================
public class DebugReturnToTitle : MonoBehaviour
{
    // ★タイトル画面のシーン名に変更してください
    const string TitleSceneName = "Title";

    // 戻るキー
    const Key ReturnKey = Key.F12;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // 二重生成防止（シーンを読み直しても常駐は1つだけ）
        if (FindAnyObjectByType<DebugReturnToTitle>() != null) return;

        var go = new GameObject("[Debug] ReturnToTitle");
        go.AddComponent<DebugReturnToTitle>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb[ReturnKey].wasPressedThisFrame) return;

        ReturnToTitle();
    }

    static void ReturnToTitle()
    {
        // ヒットストップ等でtimeScaleが0や小さい値のまま戻ると、タイトルが止まって見えるので元に戻す
        Time.timeScale = 1f;

        if (Application.CanStreamedLevelBeLoaded(TitleSceneName))
        {
            Debug.Log($"[DebugReturnToTitle] {ReturnKey}キーでタイトル画面（{TitleSceneName}）へ戻ります。");
            SceneManager.LoadScene(TitleSceneName);
        }
        else if (SceneManager.sceneCountInBuildSettings > 0)
        {
            Debug.LogWarning($"[DebugReturnToTitle] シーン'{TitleSceneName}'が見つからないため、" +
                             "Build Settingsの0番目のシーンへ戻ります。TitleSceneNameを確認してください。");
            SceneManager.LoadScene(0);
        }
        else
        {
            Debug.LogError("[DebugReturnToTitle] Build Settingsにシーンが登録されていないため、タイトルへ戻れません。");
        }
    }
#endif
}
