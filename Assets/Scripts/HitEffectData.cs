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

    [Header("生成位置の微調整")]
    [Tooltip("計算後の生成位置にさらに加算するオフセット。X=左右（右が+）、Y=上下（上が+）、Z=奥行き（奥が+）")]
    public Vector3 spawnPositionOffset = new Vector3(0f, 1.0f, 0f);

    [Header("全体の大きさ")]
    [Tooltip("scaleRangeにさらに掛け合わせる全体スケール倍率。1で等倍、大きくすると全体的に大きく表示される")]
    public float overallScale = 1.0f;

    [Header("見た目")]
    public Vector2 scaleRange = new Vector2(0.9f, 1.3f);
    public float lifeTime = 0.6f;
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("軸")]
    [Tooltip("2D格闘ゲームならZ軸回転、3Dで奥行きを使うならY軸回転を使う")]
    public bool use2DRotation = true;
}
