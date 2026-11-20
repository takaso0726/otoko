using UnityEngine;
using UnityEngine.SceneManagement;

//=====================================================
// 現在のシーン名が指定した名前（初期値："Select"）の間だけ、
// Animatorのbool パラメーターをtrueにするスクリプト。
//   ・Animatorと同じGameObjectに付けるだけで使えます（別のGameObjectのAnimatorはInspectorで指定）。
//   ・Animator側に、parameterNameと同じ名前のBoolパラメーターを作成してください。
//   ・シーン名はフォルダを含まないファイル名で比較します（例：Scenes/1v1/Select.unity → "Select"）。
//=====================================================
[DisallowMultipleComponent]
public class SelectSceneAnimatorParam : MonoBehaviour
{
    [Header("対象")]
    [Tooltip("値を送るAnimator。空の場合は、このGameObjectのAnimatorを自動で取得します。")]
    [SerializeField] Animator animator;

    [Header("設定")]
    [Tooltip("このシーン名の間だけパラメーターをtrueにします。")]
    [SerializeField] string sceneName = "Select";
    [Tooltip("Animatorに作成したBoolパラメーターの名前。")]
    [SerializeField] string parameterName = "Select";

    bool hasParam; // Animatorに該当のBoolパラメーターが存在するか（存在する時だけ値を送る）

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        CacheParam();
    }

    void OnEnable()
    {
        // シーンが切り替わった時にも更新する（このオブジェクトがシーンをまたいで残る場合に備える）
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Apply(SceneManager.GetActiveScene().name); // 有効になった時点のシーンを反映
    }

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    void OnActiveSceneChanged(Scene previous, Scene next)
    {
        Apply(next.name);
    }

    // Animatorに該当のBoolパラメーターがあるか調べる。無ければ警告を1回だけ出し、値は送らない。
    void CacheParam()
    {
        hasParam = false;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[{name}] AnimatorまたはAnimator Controllerが見つかりません。", this);
            return;
        }

        foreach (var param in animator.parameters)
        {
            if (param.name == parameterName && param.type == AnimatorControllerParameterType.Bool)
            {
                hasParam = true;
                break;
            }
        }

        if (!hasParam)
        {
            Debug.LogWarning($"[{name}] Animatorに'{parameterName}'(Bool)パラメーターがありません。", this);
        }
    }

    // シーン名がsceneNameと一致する間だけtrueにする。
    // シーン名の取得は文字列を生成するため、毎フレームは呼ばず、有効化時とシーン切替時だけ呼ぶ。
    void Apply(string currentSceneName)
    {
        if (!hasParam || animator == null) return;
        animator.SetBool(parameterName, currentSceneName == sceneName);
    }
}
