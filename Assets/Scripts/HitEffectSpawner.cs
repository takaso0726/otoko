using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// ヒット時／根性復活の連打時などに呼び出して、
/// 調整可能な角度・距離で擬音エフェクトを空白地帯へ配置する。
///
/// 使い方例（攻撃ヒット時。攻撃者→被弾者の方向を基準にする）:
///   HitEffectSpawner.Instance.Spawn(hitEffectData, attackerTransform.position, victimTransform.position);
///
/// 使い方例（復活連打時など、攻撃者がいない場合。基準方向を直接渡す）:
///   HitEffectSpawner.Instance.SpawnAtDirection(mashHitEffectData, transform.position, transform.forward);
/// </summary>
public class HitEffectSpawner : MonoBehaviour
{
    public static HitEffectSpawner Instance { get; private set; }

    [Header("任意：常にプレイヤー向きに合わせたい場合はCameraを指定")]
    [SerializeField] private Camera targetCamera;

    // ★追加：allowOverlap=falseなHitEffectDataについて、「今表示中のインスタンス」を1つだけ覚えておく。
    //   同じdataでの新規生成リクエストが来たとき、ここを見て出してよいか判定する。
    private readonly System.Collections.Generic.Dictionary<HitEffectData, HitEffectAnimator> activeInstances
        = new System.Collections.Generic.Dictionary<HitEffectData, HitEffectAnimator>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        if (targetCamera == null) targetCamera = Camera.main;
    }

    /// <summary>
    /// attackerPos → victimPos の方向を基準として、
    /// data.baseAngle 度だけ回転させた場所にエフェクトを出す（攻撃ヒット用）。
    /// </summary>
    public void Spawn(HitEffectData data, Vector3 attackerPos, Vector3 victimPos)
    {
        if (data == null || data.effectPrefab == null) return;

        // 基準方向（攻撃者→被弾者）
        Vector3 baseDir = (victimPos - attackerPos);
        baseDir.z = 0f; // 2D格闘想定。3D奥行きを使うならここを調整
        if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector3.right;

        SpawnAtDirection(data, victimPos, baseDir);
    }

    /// <summary>
    /// 攻撃者がいない演出（復活連打、仁王立ち開始時など）向け。
    /// 基準方向を直接渡し、そこから data.baseAngle 分回転させた位置にエフェクトを出す。
    /// </summary>
    public void SpawnAtDirection(HitEffectData data, Vector3 originPos, Vector3 baseDirection)
    {
        if (data == null || data.effectPrefab == null) return;

        // ★追加：重ねて出してはいけない演出の場合、今出してよいタイミングかを判定する
        if (!CanSpawn(data)) return;

        Vector3 baseDir = baseDirection;
        baseDir.z = 0f;
        if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector3.right;
        baseDir.Normalize();

        // 角度オフセット + ランダムばらつき
        float angle = data.baseAngle + Random.Range(-data.angleRandomRange, data.angleRandomRange);

        // 回転を適用（2D=Z軸回転、3D=Y軸回転）
        Vector3 spawnDir;
        if (data.use2DRotation)
        {
            spawnDir = Quaternion.Euler(0, 0, angle) * baseDir;
        }
        else
        {
            spawnDir = Quaternion.Euler(0, angle, 0) * baseDir;
        }

        // 距離を決めて配置座標を算出
        float distance = Random.Range(data.distanceRange.x, data.distanceRange.y);
        Vector3 spawnPos = originPos + spawnDir * distance;

        // 手動での位置微調整（上下左右奥）を反映。HitEffectDataのInspectorで技ごとに調整できる
        spawnPos += data.spawnPositionOffset;

        // 生成
        GameObject fx = Instantiate(data.effectPrefab, spawnPos, Quaternion.identity);

        // カメラの方を向かせたい場合（3Dビルボード）
        if (!data.use2DRotation && targetCamera != null)
        {
            fx.transform.rotation = Quaternion.LookRotation(fx.transform.position - targetCamera.transform.position);
        }

        // スケール・寿命の演出（全体スケール倍率も反映）
        float scale = Random.Range(data.scaleRange.x, data.scaleRange.y) * data.overallScale;
        fx.transform.localScale = Vector3.one * scale;

        HitEffectAnimator animator = fx.GetComponent<HitEffectAnimator>();
        if (animator == null) animator = fx.AddComponent<HitEffectAnimator>();
        animator.Play(data, scale);

        // ★追加：重ねて出してはいけない演出なら、今生成したものを「表示中」として覚えておく
        if (!data.allowOverlap)
        {
            activeInstances[data] = animator;
        }
    }

    /// <summary>
    /// このHitEffectDataについて、今新しく生成してよいかどうかを判定する。
    /// ・allowOverlap=trueなら常にOK（従来通り、何個でも重ねられる）。
    /// ・allowOverlap=falseの場合、今表示中のインスタンスが無ければOK。
    ///   表示中のインスタンスがある場合は、その残り時間が
    ///   overlapAllowedBeforeEnd以下（＝もうすぐ消える）になっていればOK、
    ///   まだ十分残っていればNG（＝重ねずに待たせる）。
    /// </summary>
    private bool CanSpawn(HitEffectData data)
    {
        if (data.allowOverlap) return true;

        if (activeInstances.TryGetValue(data, out HitEffectAnimator existing) && existing != null)
        {
            // Unity側でDestroy済みのオブジェクトはnull判定になるため、
            // ここに来るのは「まだ表示中」の場合のみ
            if (existing.RemainingTime > data.overlapAllowedBeforeEnd)
            {
                return false; // まだ消える直前ではないので、重ねて出さない
            }
        }

        return true;
    }
}

