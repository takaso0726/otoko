using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CharacterSelectController.CharacterSelectionResult に保存されたP1/P2の選択結果を、
/// 次シーンで実際のキャラクター（プレハブ）として反映するためのスクリプト。
///
/// 重要な設計方針:
/// ・P1の生成物は必ず Player1CharacterName / player1SpawnPoint に紐付ける。
/// ・P2の生成物は必ず Player2CharacterName / player2SpawnPoint に紐付ける。
/// ・DualPlayerDeviceAssignerが管理する「物理コントローラーがどちらのPlayerInputに
///   割り当てられているか」とは完全に独立している。
///   つまりコントローラーの抜き差しで p1Input/p2Input の中身が入れ替わったとしても、
///   「1Pが選んだキャラクター」は常にplayer1SpawnPoint側に生成される。
///   （操作するプレイヤーが入れ替わる話と、選んだキャラが入れ替わる話を混同しないこと）
/// </summary>
public class PlayerCharacterSpawner : MonoBehaviour
{
    [System.Serializable]
    public class CharacterPrefabEntry
    {
        [Tooltip("CharacterSelectController側のCharacterEntry.characterNameと一致させること")]
        public string characterName;
        public GameObject prefab;
    }

    [Header("キャラクター名→プレハブの対応表")]
    [SerializeField] CharacterPrefabEntry[] characterPrefabs;

    [Header("生成位置（1Pは1P用、2Pは2P用と、Inspectorで明示的に配置しておくこと）")]
    [SerializeField] Transform player1SpawnPoint;
    [SerializeField] Transform player2SpawnPoint;

    [Header("該当キャラが見つからなかった場合の保険（任意）")]
    [SerializeField] GameObject fallbackPrefab;

    // 生成したキャラクターを外部（PlayerInputの紐付け処理など）から参照できるように保持
    public GameObject Player1Instance { get; private set; }
    public GameObject Player2Instance { get; private set; }

    void Start()
    {
        if (!CharacterSelectController.CharacterSelectionResult.IsValid)
        {
            Debug.LogWarning("[PlayerCharacterSpawner] CharacterSelectionResultが未設定です。" +
                              "セレクト画面を経由せずにこのシーンを直接再生した場合などに発生します。");
        }

        Player1Instance = SpawnFor(
            CharacterSelectController.CharacterSelectionResult.Player1CharacterName, player1SpawnPoint, "1P");
        Player2Instance = SpawnFor(
            CharacterSelectController.CharacterSelectionResult.Player2CharacterName, player2SpawnPoint, "2P");
    }

    GameObject SpawnFor(string characterName, Transform spawnPoint, string label)
    {
        if (spawnPoint == null)
        {
            Debug.LogWarning($"[PlayerCharacterSpawner] {label} 用のspawnPointが設定されていません。");
            return null;
        }

        var prefab = FindPrefab(characterName);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerCharacterSpawner] {label} の選択キャラ'{characterName}'に対応するプレハブが" +
                              $"見つかりません。fallbackPrefabを使用します。");
            prefab = fallbackPrefab;
        }
        if (prefab == null) return null;

        return Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
    }

    GameObject FindPrefab(string characterName)
    {
        if (string.IsNullOrEmpty(characterName) || characterPrefabs == null) return null;

        foreach (var entry in characterPrefabs)
        {
            if (entry != null && entry.characterName == characterName)
            {
                return entry.prefab;
            }
        }
        return null;
    }
}
