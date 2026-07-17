using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

//=====================================================
// シーン上のPlayerInputManagerと同じGameObjectにアタッチしてください。
// 役割：
//   ・ゲーム開始時点で既に繋がっているコントローラーを全員自動参加させる
//   ・ゲーム中に新しくコントローラーが繋がったら、その場で自動参加させる
// PlayerInputManager側の設定：
//   ・Player Prefab に PlayerInput(Behavior=Send Messages) + testスクリプト
//     を付けたキャラクタープレハブを設定
//   ・Joining Enabled By Default はOFFのままでOK（このスクリプトが手動でJoinさせるため）
//=====================================================
[RequireComponent(typeof(PlayerInputManager))]
public class GameInputManager : MonoBehaviour
{
    PlayerInputManager manager;

    void Awake()
    {
        manager = GetComponent<PlayerInputManager>();

        // ボタン押下やJoinアクションによる参加は使わず、
        // コントローラー接続イベントだけで完全にこちらから制御する
        manager.DisableJoining();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void Start()
    {
        // ゲーム開始時点で既に繋がっているコントローラーを全部参加させる
        foreach (var gamepad in Gamepad.all)
        {
            JoinWithDevice(gamepad);
        }
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // ゲーム中に新しくコントローラーが繋がったら即参加させる
        if (change == InputDeviceChange.Added && device is Gamepad)
        {
            JoinWithDevice(device);
        }
    }

    void JoinWithDevice(InputDevice device)
    {
        // すでにこのデバイスで参加済みのプレイヤーがいないか確認（二重参加防止）
        foreach (var p in PlayerInput.all)
        {
            if (p.devices.Contains(device))
            {
                return;
            }
        }

        // 最大人数（通常は2人）に達していたら参加させない
        if (manager.maxPlayerCount >= 0 && PlayerInput.all.Count >= manager.maxPlayerCount)
        {
            Debug.Log("最大参加人数に達しているため、これ以上は参加できません");
            return;
        }

        manager.JoinPlayer(pairWithDevice: device);
        Debug.Log($"{device.displayName} が参加しました（現在の参加人数：{PlayerInput.all.Count}）");
    }
}
