using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class oI_Controller : MonoBehaviour
{
    [Header("遷移先のシーン名 (Build Profiles / Build Settings に登録が必要)")]
    [SerializeField] private string nextSceneName = "NextScene";

    private bool isLoading;

    void Update()
    {
        if (isLoading) return;

        if (IsAButtonPressed())
        {
            isLoading = true;
            SceneManager.LoadScene(nextSceneName);
        }
    }

    private bool IsAButtonPressed()
    {
#if ENABLE_INPUT_SYSTEM
        // 新 Input System: Xbox の A / PlayStation の × (下側のボタン)
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        // 旧 Input Manager: joystick button 0 が A ボタン
        return Input.GetKeyDown(KeyCode.JoystickButton0);
#else
        return false;
#endif
    }
}
