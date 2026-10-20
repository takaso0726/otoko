using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

//=====================================================
// 【デバッグ】F12キーで、どのシーンからでもタイトル画面へ戻る。
//
// ■ Unityエディタ上／ビルド後（共通）
//   ・シーンへの配置は不要。起動時（最初のシーンの読み込み後）に自動で常駐オブジェクトが作られ、
//     どのシーンからでも F12 で戻れる。ビルド後（Development Buildでないものも含む）でも同じ。
//   ・一度生成されたものはシーンをまたいで残る（DontDestroyOnLoad）。
//
// ■ ビルド後に無効にしたい時（リリース用のビルドなど）
//   ・下の enableInBuild の初期値を false にする。
//   ・★注意：シーンに既にこのコンポーネントを置いている場合は、Inspectorに保存された
//     [Enable In Build] の値が使われる（初期値より優先される）。ONにするか、シーン側のものを削除すること。
//
// ■ 設定
//   ・Title Scene Name … 戻り先のシーン名（Build Settingsに登録されていること）。
//                        見つからない場合は、Build Settingsの0番目のシーンへ戻る。
//   ・Return Key ……… 戻るキー（初期値 F12）
//=====================================================
public class DebugReturnToTitle : MonoBehaviour
{
    [Header("タイトルへ戻る設定")]
    [Tooltip("★タイトル画面のシーン名（Build Settingsに登録されている名前）に合わせてください。")]
    [SerializeField] string titleSceneName = "Title";

    [Tooltip("タイトルへ戻るキー")]
    [SerializeField] Key returnKey = Key.F12;

    [Header("ビルド後の挙動")]
    [Tooltip("ONにすると、ビルド後の実行でもキーでタイトルへ戻れます。OFFならビルド後は無効です（エディタ上では常に有効）。" +
             "※自動生成されるものは、ここのコード上の初期値(true)が使われます。")]
    [SerializeField] bool enableInBuild = true;

    static DebugReturnToTitle instance;
    bool isLoading; // シーン読み込み中の連打防止

    // エディタでもビルド後でも、どのシーンから起動しても使えるように、無ければ自動で生成する
    // （以前はエディタ専用だったため、ビルド後はシーンに手動で配置しないと動かなかった）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;

        // 無効設定のコンポーネントが既にシーン上にあるのに、自動生成で上書きしないようにする
        if (FindAnyObjectByType<DebugReturnToTitle>(FindObjectsInactive.Include) != null) return;

        new GameObject("[Debug] ReturnToTitle").AddComponent<DebugReturnToTitle>();
    }

    void Awake()
    {
        // タイトルへ戻るたびにシーン側のものが重複するので、最初の1つだけ残す
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // DontDestroyOnLoadはルートのオブジェクトにしか効かない
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        isLoading = false;
    }

    void Update()
    {
        if (isLoading) return;

        // ビルド後は、チェックボックスがONの時だけ有効（エディタ上では常に有効）
        if (!Application.isEditor && !enableInBuild) return;

        var kb = Keyboard.current;
        if (kb == null || !kb[returnKey].wasPressedThisFrame) return;

        ReturnToTitle();
    }

    void ReturnToTitle()
    {
        isLoading = true;

        // ヒットストップ等でtimeScaleが0や小さい値のまま戻ると、タイトルが止まって見えるので元に戻す
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (!string.IsNullOrEmpty(titleSceneName) && Application.CanStreamedLevelBeLoaded(titleSceneName))
        {
            Debug.Log($"[DebugReturnToTitle] {returnKey}キーでタイトル画面（{titleSceneName}）へ戻ります。");
            SceneManager.LoadScene(titleSceneName);
        }
        else if (SceneManager.sceneCountInBuildSettings > 0)
        {
            Debug.LogWarning($"[DebugReturnToTitle] シーン'{titleSceneName}'が見つからないため、" +
                             "Build Settingsの0番目のシーンへ戻ります。Title Scene Nameを確認してください。");
            SceneManager.LoadScene(0);
        }
        else
        {
            isLoading = false;
            Debug.LogError("[DebugReturnToTitle] Build Settingsにシーンが登録されていないため、タイトルへ戻れません。");
        }
    }
}
