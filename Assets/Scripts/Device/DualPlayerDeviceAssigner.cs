using UnityEngine;
using UnityEngine.InputSystem;

//=====================================================
// シーンに最初から配置されているP1・P2のPlayerInputに対して、
// 接続されているゲームパッドを1台ずつ明示的に割り当てる。
//
// 【重要】P1Input / P2Input それぞれのInspectorで
//   「Auto-Switch Control Scheme」を必ずOFFにしておくこと。
//   ONのままだと、ここで割り当てたはずのコントローラーの入力を
//   もう片方のPlayerInputも勝手に拾ってしまい、今回の
//   「1台しか繋いでいないのに両方動く」バグが再発する。
//
// 割り当てルール：
//   ・ゲームパッドが1台も無い → P1もP2も未ペアリング（どちらも動かない）
//   ・ゲームパッドが1台だけ   → P1にだけ割り当てる（P2は未ペアリングのまま＝動かない）
//   ・ゲームパッドが2台以上   → P1・P2にそれぞれ1台ずつ割り当てる
//=====================================================
public class DualPlayerDeviceAssigner : MonoBehaviour
{
    [Header("シーンに配置済みのPlayerInput")]
    [SerializeField] PlayerInput p1Input;
    [SerializeField] PlayerInput p2Input;

    [Header("Input Actionsで定義したゲームパッド用Control Scheme名")]
    [SerializeField] string gamepadSchemeName = "Gamepad";

    void Start()
    {
        // PlayerInput側の内部初期化(InputUserの生成)がAwakeで行われるため、
        // それより後に実行されるStartでこちらの割り当て処理を行う。
        AssignDevices();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // ゲームパッドの抜き差しがあったら割り当てを更新する
        if (device is Gamepad &&
            (change == InputDeviceChange.Added
             || change == InputDeviceChange.Removed
             || change == InputDeviceChange.Disconnected
             || change == InputDeviceChange.Reconnected))
        {
            AssignDevices();
        }
    }

    void AssignDevices()
    {
        var gamepads = Gamepad.all;

        ConfigureSlot(p1Input, gamepads.Count >= 1 ? gamepads[0] : null);
        ConfigureSlot(p2Input, gamepads.Count >= 2 ? gamepads[1] : null);
    }

    // 指定したPlayerInputに対して、割り当てるデバイスがあればペアリングして有効化し、
    // 無ければ「PlayerInputコンポーネント自体を無効化」して一切入力を受け取らないようにする。
    // UnpairDevices()だけだと、無効化される直前に受け取った古い入力値（moveInput等）が
    // Player側に残ったまま動き続けてしまうため、コンポーネントごと無効化して確実に止める。
    void ConfigureSlot(PlayerInput input, InputDevice device)
    {
        if (input.user.valid) input.user.UnpairDevices();

        if (device == null)
        {
            input.enabled = false;
            Debug.Log($"{input.name} は割り当てるコントローラーが無いため無効化しました");
        }
        else
        {
            input.enabled = true;
            input.SwitchCurrentControlScheme(gamepadSchemeName, device);
            Debug.Log($"{input.name} に {device.displayName} を割り当てました");
        }
    }
}
