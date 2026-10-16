using UnityEngine;

/// <summary>
/// インゲーム(CPU戦)シーン用。
/// CPU戦のセレクト画面(CharacterSelect_SFC_Controller)で選んだキャラを、
/// 設定した空のオブジェクトの位置に出現させる。
///
/// 【使い方】
/// 1. インゲームシーンに空のGameObjectを作り、このスクリプトを付ける。
/// 2. 出現位置用に空のGameObjectを2つ作り、playerSpawnPoint / cpuSpawnPoint に設定する。
/// 3. characterPrefabs に、セレクト画面の characters 配列と「同じ順番」でキャラのプレハブを並べる。
///    （選択結果はインデックスで渡されるため、順番がズレると別のキャラが出る）
/// </summary>
public class CpuBattleSpawner : MonoBehaviour
{
    [Header("キャラクターのプレハブ（セレクト画面の characters と同じ順番）")]
    [SerializeField] GameObject[] characterPrefabs;

    [Header("出現位置（空のオブジェクト）")]
    [SerializeField] Transform playerSpawnPoint;
    [SerializeField] Transform cpuSpawnPoint;

    [Header("生成設定")]
    [Tooltip("生成したキャラの親。未設定ならシーン直下に生成")]
    [SerializeField] Transform spawnParent;
    [Tooltip("空オブジェクトの回転も反映する。OFFならプレハブ自身の回転を使う")]
    [SerializeField] bool useSpawnPointRotation = true;
    [Tooltip("CPU側のキャラをX方向に反転する（向かい合わせにしたい2Dゲーム用）")]
    [SerializeField] bool flipCpuX = false;
    [Tooltip("シーン開始時に自動で生成する。OFFなら SpawnAll() を自分で呼ぶ")]
    [SerializeField] bool spawnOnStart = true;

    [Header("デバッグ用（このシーン単体で再生した時だけ使われる）")]
    [Tooltip("セレクト画面を通さずに再生した時、下の値でキャラを出す")]
    [SerializeField] bool useDebugWhenNoSelection = true;
    [SerializeField] int debugPlayerIndex = 0;
    [SerializeField] int debugCpuIndex = 1;
    [SerializeField] int debugDifficultyIndex = 1;

    /// <summary>生成されたプレイヤーキャラ（生成前はnull）</summary>
    public GameObject PlayerInstance { get; private set; }
    /// <summary>生成されたCPUキャラ（生成前はnull）</summary>
    public GameObject CpuInstance { get; private set; }
    /// <summary>CPUの強さ（セレクト画面の difficulties 配列のインデックス。0が最弱）</summary>
    public int CpuDifficultyIndex { get; private set; }

    void Start()
    {
        if (spawnOnStart) SpawnAll();
    }

    public void SpawnAll()
    {
        int playerIndex, cpuIndex;

        if (CharacterSelect_SFC_Controller.CpuSelectionResult.IsValid)
        {
            playerIndex = CharacterSelect_SFC_Controller.CpuSelectionResult.PlayerCharacterIndex;
            cpuIndex = CharacterSelect_SFC_Controller.CpuSelectionResult.CpuCharacterIndex;
            CpuDifficultyIndex = CharacterSelect_SFC_Controller.CpuSelectionResult.CpuDifficultyIndex;
        }
        else if (useDebugWhenNoSelection)
        {
            Debug.LogWarning("[CpuBattleSpawner] セレクト結果が無いため、デバッグ用のインデックスで生成します。", this);
            playerIndex = debugPlayerIndex;
            cpuIndex = debugCpuIndex;
            CpuDifficultyIndex = debugDifficultyIndex;
        }
        else
        {
            Debug.LogError("[CpuBattleSpawner] セレクト結果がありません。セレクト画面を経由してください。", this);
            return;
        }

        PlayerInstance = Spawn(playerIndex, playerSpawnPoint, false, "Player");
        CpuInstance = Spawn(cpuIndex, cpuSpawnPoint, flipCpuX, "CPU");

        // CPU側に強さを通知する。受け取り側に「void SetDifficulty(int level)」があれば呼ばれる（無くてもエラーにならない）
        if (CpuInstance != null)
        {
            CpuInstance.SendMessage("SetDifficulty", CpuDifficultyIndex, SendMessageOptions.DontRequireReceiver);
        }
    }

    GameObject Spawn(int characterIndex, Transform point, bool flipX, string label)
    {
        if (characterPrefabs == null || characterIndex < 0 || characterIndex >= characterPrefabs.Length)
        {
            Debug.LogError($"[CpuBattleSpawner] {label} のキャラ(index={characterIndex})に対応するプレハブが characterPrefabs にありません。", this);
            return null;
        }

        var prefab = characterPrefabs[characterIndex];
        if (prefab == null)
        {
            Debug.LogError($"[CpuBattleSpawner] characterPrefabs[{characterIndex}] が未設定です。({label})", this);
            return null;
        }

        if (point == null)
        {
            Debug.LogError($"[CpuBattleSpawner] {label} の出現位置(空のオブジェクト)が未設定です。", this);
            return null;
        }

        var rotation = useSpawnPointRotation ? point.rotation : prefab.transform.rotation;
        var instance = Instantiate(prefab, point.position, rotation, spawnParent);
        instance.name = $"{prefab.name} ({label})";

        if (flipX)
        {
            var s = instance.transform.localScale;
            s.x = -s.x;
            instance.transform.localScale = s;
        }

        return instance;
    }
}
