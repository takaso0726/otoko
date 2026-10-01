using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

//=====================================================
// PlayerInputManagerと同じGameObjectにアタッチしてください（PlayerInputManagerは残す）。
//
// 役割：
//   ・セレクト画面で選んだキャラを、PlayerInputManager.JoinPlayer で1P/2Pとして生成する
//     （JoinPlayerの直前に playerPrefab を選択キャラのものへ差し替える）
//   ・1P/2Pは名前付きで固定（配列の順番に依存しない）
//       1P = Player1CharacterName / player1SpawnPoint / キーボード + Gamepad[0]
//       2P = Player2CharacterName / player2SpawnPoint / Gamepad[1]
//   ・ゲームパッドの抜き差しがあっても、生成済みキャラは入れ替わらず、
//     操作デバイスだけを割り当て直す（新しいパッドでの自動参加はしない）
//   ・【デバッグ】セレクト画面でキャラが選択されていない場合、
//     インゲーム画面で F9 を押すと、1P/2Pのスポーン地点にデバッグ用プレハブを生成する
//     （Editor / Development Build のみ有効）
//
// PlayerInputManager側の設定：
//   ・Player Prefab は空でOK（このスクリプトが実行時に設定する）
//   ・Max Player Count は 2 以上（または -1）にすること
//=====================================================
[RequireComponent(typeof(PlayerInputManager))]
[DefaultExecutionOrder(-100)]
public class GameInputManager : MonoBehaviour
{
    [Serializable]
    public class CharacterPrefabEntry
    {
        [Tooltip("CharacterSelectController側のCharacterEntry.characterNameと完全一致させること")]
        public string characterName;
        [Tooltip("PlayerInputが付いたキャラクターのプレハブ")]
        public GameObject prefab;
    }

    [Header("キャラクター名→プレハブの対応表")]
    [SerializeField] CharacterPrefabEntry[] characterPrefabs;

    [Header("生成位置（1Pは1P用、2Pは2P用に配置）")]
    [SerializeField] Transform player1SpawnPoint;
    [SerializeField] Transform player2SpawnPoint;

    [Header("該当キャラが見つからなかった場合の保険（任意）")]
    [SerializeField] GameObject fallbackPrefab;

    [Header("デバッグ生成（セレクト未選択時にキーで生成するプレハブ）")]
    [Tooltip("1P用。空ならfallbackPrefabを使用します。")]
    [SerializeField] GameObject debugPlayer1Prefab;
    [Tooltip("2P用。空ならfallbackPrefabを使用します。")]
    [SerializeField] GameObject debugPlayer2Prefab;
    [Tooltip("デバッグ生成を行うキー")]
    [SerializeField] Key debugSpawnKey = Key.F9;

    [Header("シーン内の参照（空なら実行時に自動で探す）")]
    [Tooltip("HPバー・漢気ゲージを管理するGameMNG。空ならシーンから自動で探します。")]
    [SerializeField] GameMNG gameMNG;
    [Tooltip("追従カメラ（FightingCameraController）。空ならシーンから自動で探します。")]
    [SerializeField] FightingCameraController cameraController;

    [Header("Control Scheme（空なら自動選択）")]
    [SerializeField] string player1ControlScheme = "";
    [SerializeField] string player2ControlScheme = "";

    PlayerInputManager manager;
    bool spawned; // 二重生成防止

    public GameObject Player1Instance { get; private set; }
    public GameObject Player2Instance { get; private set; }

    /// <summary>両キャラの生成後に通知（引数: 1P, 2P）</summary>
    public event Action<GameObject, GameObject> OnSpawned;

    void Awake()
    {
        manager = GetComponent<PlayerInputManager>();

        // ボタン押下による自動参加は使わず、こちらから完全に制御する
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
        if (manager.maxPlayerCount >= 0 && manager.maxPlayerCount < 2)
        {
            Debug.LogWarning("[GameInputManager] PlayerInputManagerのMax Player Countが2未満です。" +
                             "2Pが生成できないため、2以上（または-1）にしてください。");
        }

        if (!CharacterSelectController.CharacterSelectionResult.IsValid)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 未選択時は自動生成せず、デバッグキー入力を待つ（Updateで処理）
            Debug.LogWarning($"[GameInputManager] キャラが選択されていません。" +
                             $"{debugSpawnKey}キーでデバッグ生成できます。");
            return;
#else
            Debug.LogWarning("[GameInputManager] CharacterSelectionResultが未設定です" +
                             "（fallbackPrefabを使用）。");
#endif
        }

        SpawnAll(false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void Update()
    {
        if (spawned) return;
        if (CharacterSelectController.CharacterSelectionResult.IsValid) return;

        var kb = Keyboard.current;
        if (kb != null && kb[debugSpawnKey].wasPressedThisFrame)
            SpawnAll(true);
    }
#endif

    // 1P/2Pの生成から、相互参照・シーン連携・通知までをまとめて行う
    void SpawnAll(bool debugSpawn)
    {
        if (spawned) return;
        spawned = true;

        if (debugSpawn)
        {
            Debug.Log("[GameInputManager] デバッグ生成を実行します。");

            Player1Instance = SpawnPrefab(
                debugPlayer1Prefab != null ? debugPlayer1Prefab : fallbackPrefab,
                player1SpawnPoint, "1P", 0, player1ControlScheme, GetP1Devices());

            Player2Instance = SpawnPrefab(
                debugPlayer2Prefab != null ? debugPlayer2Prefab : fallbackPrefab,
                player2SpawnPoint, "2P", 1, player2ControlScheme, GetP2Devices());
        }
        else
        {
            Player1Instance = SpawnFor(
                CharacterSelectController.CharacterSelectionResult.Player1CharacterName,
                player1SpawnPoint, "1P", 0, player1ControlScheme, GetP1Devices());

            Player2Instance = SpawnFor(
                CharacterSelectController.CharacterSelectionResult.Player2CharacterName,
                player2SpawnPoint, "2P", 1, player2ControlScheme, GetP2Devices());
        }

        LinkOpponents(Player1Instance, Player2Instance);
        SetupSceneLinks(Player1Instance, Player2Instance);

        OnSpawned?.Invoke(Player1Instance, Player2Instance);
    }

    // 生成した1P/2PのenemyPlayerを、お互いに設定する。
    // プレハブはシーン上のオブジェクトを参照できないため、プレハブのenemyPlayerは空になっている。
    // ここで相互に設定しないと、相手の攻撃を「相手の攻撃か？」と判定できず、当たり判定が働かない。
    // （Player側にも保険の自動補完はあるが、ここで確実に設定しておく）
    void LinkOpponents(GameObject p1, GameObject p2)
    {
        if (p1 == null || p2 == null) return;

        var player1 = p1.GetComponent<Player>();
        var player2 = p2.GetComponent<Player>();
        if (player1 == null || player2 == null)
        {
            Debug.LogWarning("[GameInputManager] 生成したオブジェクトにPlayerコンポーネントが見つからず、" +
                             "enemyPlayerを設定できませんでした。");
            return;
        }

        player1.enemyPlayer = player2;
        player2.enemyPlayer = player1;
    }

    // 生成した1P/2Pを、シーン上のカメラ・GameMNG（HPバー/漢気ゲージ）につなぐ。
    // プレハブはシーン上のオブジェクトを参照できないため、生成のたびに実行時に設定する必要がある。
    //   ・PlayerName ... GameMNGがHPバー更新や勝敗判定を"P1"/"P2"で振り分けているため、
    //                    プレハブ側の値に関係なく、生成時に1Pは"P1"、2Pは"P2"へ固定する。
    //   ・カメラ ........ 追従対象へ登録し、Player側のfightingCamera（ガード演出・復活演出用）も設定する。
    //   ・GameMNG ....... p1/p2を差し替え、HPバー・漢気ゲージを生成したプレイヤーの値に合わせ直す。
    void SetupSceneLinks(GameObject p1, GameObject p2)
    {
        var player1 = p1 != null ? p1.GetComponent<Player>() : null;
        var player2 = p2 != null ? p2.GetComponent<Player>() : null;

        if (player1 != null) player1.PlayerName = "P1";
        if (player2 != null) player2.PlayerName = "P2";

        var cam = cameraController != null ? cameraController : FindAnyObjectByType<FightingCameraController>();
        if (cam != null)
        {
            if (player1 != null)
            {
                player1.fightingCamera = cam;
                cam.RegisterTarget(player1.transform);
            }
            if (player2 != null)
            {
                player2.fightingCamera = cam;
                cam.RegisterTarget(player2.transform);
            }
        }
        else
        {
            Debug.LogWarning("[GameInputManager] シーン内にFightingCameraControllerが見つからないため、" +
                             "カメラの追従対象に登録できませんでした。");
        }

        var mng = gameMNG != null ? gameMNG : FindAnyObjectByType<GameMNG>();
        if (mng != null)
        {
            mng.SetPlayers(player1, player2, cam);
        }
        else
        {
            Debug.LogWarning("[GameInputManager] シーン内にGameMNGが見つからないため、" +
                             "HPバー・漢気ゲージを生成したプレイヤーにつなげませんでした。");
        }
    }

    // ---- 生成 ----

    // キャラ名からプレハブを解決して生成する（通常のセレクト経由）
    GameObject SpawnFor(string characterName, Transform spawnPoint, string label,
                        int playerIndex, string controlScheme, InputDevice[] devices)
    {
        var prefab = FindPrefab(characterName);
        if (prefab == null)
        {
            Debug.LogWarning($"[GameInputManager] {label} の選択キャラ'{characterName}'に対応するプレハブが" +
                             "見つかりません。fallbackPrefabを使用します。");
            prefab = fallbackPrefab;
        }

        return SpawnPrefab(prefab, spawnPoint, label, playerIndex, controlScheme, devices);
    }

    // 指定されたプレハブを実際に生成する（通常・デバッグ共通）
    GameObject SpawnPrefab(GameObject prefab, Transform spawnPoint, string label,
                           int playerIndex, string controlScheme, InputDevice[] devices)
    {
        if (spawnPoint == null)
        {
            Debug.LogWarning($"[GameInputManager] {label} 用のspawnPointが設定されていません。");
            return null;
        }

        if (prefab == null)
        {
            Debug.LogWarning($"[GameInputManager] {label} 用のプレハブが設定されていません。");
            return null;
        }

        if (prefab.GetComponent<PlayerInput>() == null)
        {
            Debug.LogWarning($"[GameInputManager] {prefab.name} にPlayerInputが付いていないため、" +
                             "JoinPlayerを使わず通常生成します。");
            var plain = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
            plain.name = $"{label}_{prefab.name}";
            return plain;
        }

        // JoinPlayerの直前にPlayer Prefabを選択キャラへ差し替える
        manager.playerPrefab = prefab;

        // JoinPlayerは1デバイスしか渡せないので、まず先頭のデバイスで参加させる
        var firstDevice = devices.Length > 0 ? devices[0] : null;
        var scheme = string.IsNullOrEmpty(controlScheme) ? null : controlScheme;
        var pi = manager.JoinPlayer(playerIndex, -1, scheme, firstDevice);

        if (pi == null)
        {
            Debug.LogWarning($"[GameInputManager] {label} のJoinPlayerに失敗しました。");
            return null;
        }

        // 残りのデバイス（1Pのキーボードなど）も含めて割り当てを確定する
        Repair(pi.gameObject, devices);

        pi.gameObject.name = $"{label}_{prefab.name}";
        pi.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        Physics.SyncTransforms(); // CharacterController/Rigidbodyの位置ズレ対策
        return pi.gameObject;
    }

    GameObject FindPrefab(string characterName)
    {
        if (string.IsNullOrEmpty(characterName) || characterPrefabs == null) return null;

        foreach (var entry in characterPrefabs)
        {
            if (entry != null && entry.characterName == characterName)
                return entry.prefab;
        }
        return null;
    }

    // ---- ゲームパッドの抜き差し対応 ----

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!(device is Gamepad)) return;

        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Removed:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Disconnected:
                RefreshInputPairing();
                break;
        }
    }

    /// <summary>1P/2Pのデバイス割り当てをやり直す（キャラは再生成しない）</summary>
    public void RefreshInputPairing()
    {
        Repair(Player1Instance, GetP1Devices());
        Repair(Player2Instance, GetP2Devices());
    }

    static void Repair(GameObject go, InputDevice[] devices)
    {
        if (go == null) return;
        var pi = go.GetComponent<PlayerInput>();
        if (pi == null || !pi.user.valid) return;

        pi.user.UnpairDevices();
        foreach (var d in devices)
            InputUser.PerformPairingWithDevice(d, pi.user);
    }

    // ---- デバイス取得（CharacterSelectControllerの入力割り当てと同じ規則） ----

    // 1P: Gamepad[0] + キーボード（Gamepadを先頭にしてJoinPlayerへ渡す）
    static InputDevice[] GetP1Devices()
    {
        var list = new List<InputDevice>();
        if (Gamepad.all.Count > 0) list.Add(Gamepad.all[0]);
        if (Keyboard.current != null) list.Add(Keyboard.current);
        return list.ToArray();
    }

    // 2P: Gamepad[1]のみ
    static InputDevice[] GetP2Devices()
    {
        return Gamepad.all.Count > 1
            ? new InputDevice[] { Gamepad.all[1] }
            : new InputDevice[0];
    }
}
