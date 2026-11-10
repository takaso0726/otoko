using UnityEngine;

/// <summary>
/// キャラクター1人分のパラメーター(ラベル名と値)を保存するデータ。
///
/// 【作り方】
/// Project ウィンドウで右クリック → Create → Pentagon Chart → Character Parameter
/// キャラクターの数だけ作って、それぞれに名前・ラベル・値を入力する。
/// </summary>
[CreateAssetMenu(fileName = "CharacterParameter", menuName = "Pentagon Chart/Character Parameter")]
public class CharacterParameterData : ScriptableObject
{
    public const int Count = 5;

    [Tooltip("キャラクターの名前(管理用。チャートには表示されない)。")]
    public string characterName = "Character";

    [Tooltip("キャラクターのタイプ名(例: パワー型、テクニック型、バランス型)。\nPentagonChart の Type Label に表示される。")]
    public string typeName = "バランス型";

    [Tooltip("このキャラクターの5項目のラベル名。\n順番は 上 → 右上 → 右下 → 左下 → 左(上から時計回り)。\nキャラクターごとに全く違う項目名にできる。")]
    public string[] labelNames = { "Power", "Stamina", "Speed", "Dexterity", "Humor" };

    [Tooltip("このキャラクターの5項目の値(0〜1)。\nlabelNames と同じ順番。0 = 中心、1 = 外枠の頂点。")]
    [Range(0f, 1f)]
    public float[] values = { 0.7f, 0.8f, 0.6f, 1f, 0.5f };

#if UNITY_EDITOR
    // 配列の長さが5から変わらないようにする
    void OnValidate()
    {
        if (labelNames == null || labelNames.Length != Count)
            System.Array.Resize(ref labelNames, Count);
        if (values == null || values.Length != Count)
            System.Array.Resize(ref values, Count);
    }
#endif
}
