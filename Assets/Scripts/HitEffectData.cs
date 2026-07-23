using UnityEngine;

/// <summary>
/// 技ごとの擬音演出設定（ドカン、ドドン等）
/// 技のデータアセットとして技数分作成し、AttackDataなどから参照する
/// </summary>
[CreateAssetMenu(fileName = "HitEffectData", menuName = "Fighting/Hit Effect Data")]
public class HitEffectData : ScriptableObject
{
    [Header("表示するテキスト/プレハブ")]
    public GameObject effectPrefab; // TextMeshProやSpriteを持つプレハブ

    [Header("角度設定（基準方向からの回転）")]
    [Tooltip("攻撃者→被弾者方向を0°として、そこから何度回転させるか")]
    [Range(-180f, 180f)]
    public float baseAngle = 45f;

    [Tooltip("baseAngleを中心にランダムでばらつかせる範囲（±度）")]
    [Range(0f, 90f)]
    public float angleRandomRange = 15f;

    [Header("距離設定")]
    [Tooltip("被弾地点からどれくらい離すか")]
    public Vector2 distanceRange = new Vector2(1.0f, 1.8f);

    [Header("見た目")]
    public Vector2 scaleRange = new Vector2(0.9f, 1.3f);
    public float lifeTime = 0.6f;
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("軸")]
    [Tooltip("2D格闘ゲームならZ軸回転、3Dで奥行きを使うならY軸回転を使う")]
    public bool use2DRotation = true;
}
