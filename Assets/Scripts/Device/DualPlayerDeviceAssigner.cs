using System.Linq;
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

    // 現在P1・P2それぞれに割り当てられているデバイスを覚えておく。
    // 「Gamepad.all[0]/[1]」のようにその場の並び順から毎回引き直すと、
    // 抜き差しでリストの並びが変わった時に持ち主が入れ替わってしまうため、
    // 一度割り当てたデバイスは、そのデバイス自体が完全に取り外されるまで
    // 同じプレイヤーに固定し続ける。
    Gamepad p1Device;
    Gamepad p2Device;

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
        if (!(device is Gamepad pad)) return;

        switch (change)
        {
            case InputDeviceChange.Removed:
                // 完全に取り外された場合のみ、そのプレイヤーのスロットを空ける。
                // （Disconnected/Reconnectedでは持ち主を変えない）
                if (pad == p1Device) p1Device = null;
                if (pad == p2Device) p2Device = null;
                AssignDevices();
                break;

            case InputDeviceChange.Added:
                AssignDevices();
                break;

            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Reconnected:
                // 同じデバイスの抜き差しでは、そのデバイスがどちらのプレイヤーに
                // 属していたかは変えず、ペアリング状態だけ更新する。
                RefreshPairing();
                break;
        }
    }

    void AssignDevices()
    {
        var gamepads = Gamepad.all;

        // 既に割り当て済みのデバイスが本当に居なくなっていないか確認
        if (p1Device != null && !gamepads.Contains(p1Device)) p1Device = null;
        if (p2Device != null && !gamepads.Contains(p2Device)) p2Device = null;

        // まだどちらにも割り当てられていないゲームパッドを、空いているスロットへ
        // 検出順に詰めていく（＝どちらかが既に確保しているデバイスの奪い合いは起きない）
        foreach (var pad in gamepads)
        {
            if (pad == p1Device || pad == p2Device) continue;

            if (p1Device == null) { p1Device = pad; continue; }
            if (p2Device == null) { p2Device = pad; continue; }

            // 3台目以降は今回は未対応（1P/2P固定の2人プレイのため）
            break;
        }

        RefreshPairing();
    }

    // p1Device/p2Deviceの「持ち主」は変えずに、現在の状態をPlayerInputへ反映するだけの処理
    void RefreshPairing()
    {
        ConfigureSlot(p1Input, p1Device);
        ConfigureSlot(p2Input, p2Device);
    }

    // 直前にConfigureSlotへ渡したデバイスを覚えておき、変化が無ければ何もしない
    // （AssignDevices/RefreshPairingは片方のプレイヤーに無関係な抜き差しでも
    //   毎回両方呼ばれるため、無変化な側まで毎回ペアリングし直すと
    //   一瞬入力が途切れるなどの余計な副作用が出てしまう）
    readonly System.Collections.Generic.Dictionary<PlayerInput, InputDevice> lastConfiguredDevice =
        new System.Collections.Generic.Dictionary<PlayerInput, InputDevice>();

    // 指定したPlayerInputに対して、割り当てるデバイスがあればペアリングして有効化し、
    // 無ければ「PlayerInputコンポーネント自体を無効化」して一切入力を受け取らないようにする。
    // UnpairDevices()だけだと、無効化される直前に受け取った古い入力値（moveInput等）が
    // Player側に残ったまま動き続けてしまうため、コンポーネントごと無効化して確実に止める。
    void ConfigureSlot(PlayerInput input, InputDevice device)
    {
        if (lastConfiguredDevice.TryGetValue(input, out var previous) && previous == device)
        {
            return; // 前回と同じ割り当てなら何もしない
        }
        lastConfiguredDevice[input] = device;

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
