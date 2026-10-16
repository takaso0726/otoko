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

    //=====================================================
    // ★追加：キャラの向き別・生成位置の微調整（Inspectorで調整）
    //   全エフェクト共通の、向きごとの追加オフセット（HitEffectDataのspawnPositionOffsetと、データごとの向き別オフセットに加算される）。
    //   横＝Z軸、高さ＝Y軸、X＝奥行き。向きはワールドのZ軸の符号で判定する。
    //   ・Z+を向いている時 ／ Z-を向いている時 のどちらが画面の左右どちらかは、カメラの配置で決まる。
    //     実際にエフェクトを出して確認し、見た目に合わせて値を入れること。
    //   ・向きが判定できない時（Z成分がほぼ0）は、どちらも適用しない。
    //   ・対象：Spawn（被弾者の向き＝攻撃者の方を向いている前提）／SpawnAtDirection（渡した基準方向）／SpawnBehindFacing（facingForward）
    //=====================================================
    [Header("向き別の生成位置微調整（HitEffectDataのオフセットに加算）")]
    [Tooltip("キャラがZ+方向を向いている時の追加オフセット（X=奥行き／Y=高さ／Z=横）。")]
    [SerializeField] private Vector3 offsetWhenFacingPositiveZ = Vector3.zero;
    [Tooltip("キャラがZ-方向を向いている時の追加オフセット（X=奥行き／Y=高さ／Z=横）。")]
    [SerializeField] private Vector3 offsetWhenFacingNegativeZ = Vector3.zero;

    // facingSign: +1=Z+向き／-1=Z-向き／0=不明（追加オフセットなし）
    private Vector3 GetFacingOffset(int facingSign)
    {
        if (facingSign > 0) return offsetWhenFacingPositiveZ;
        if (facingSign < 0) return offsetWhenFacingNegativeZ;
        return Vector3.zero;
    }

    private static int GetFacingSign(float z)
    {
        return Mathf.Abs(z) < 0.01f ? 0 : (z > 0f ? 1 : -1);
    }

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

        // 被弾者は攻撃者の方を向いている前提：被弾者の向き＝被弾者→攻撃者のZ方向
        int victimFacing = GetFacingSign(attackerPos.z - victimPos.z);
        SpawnAtDirection(data, victimPos, baseDir, victimFacing);
    }

    /// <summary>
    /// 攻撃者がいない演出（復活連打、仁王立ち開始時など）向け。
    /// 基準方向を直接渡し、そこから data.baseAngle 分回転させた位置にエフェクトを出す。
    /// </summary>
    public void SpawnAtDirection(HitEffectData data, Vector3 originPos, Vector3 baseDirection)
    {
        // 向きは基準方向のZ成分の符号（下のz=0化より前に判定する）
        SpawnAtDirection(data, originPos, baseDirection, GetFacingSign(baseDirection.z));
    }

    // facingSign: 向き別オフセット用（+1=Z+／-1=Z-／0=不明）
    private void SpawnAtDirection(HitEffectData data, Vector3 originPos, Vector3 baseDirection, int facingSign)
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

        SpawnInstance(data, spawnPos, facingSign);
    }

    /// <summary>
    /// ★追加：キャラの向きの「反対側」の斜め上へ出す。
    ///   横＝Z軸、高さ＝Y軸。Z+を向いている → Z-側の斜め上／Z-を向いている → Z+側の斜め上。
    /// カメラの向きには依存しない（ワールドのZ軸だけで決まる）。
    /// 角度はdata.baseAngle：0°で真横（Z軸方向）、45°で斜め上（既定）、90°で真上。
    /// 距離（distanceRange）・ばらつき（angleRandomRange）・位置微調整（spawnPositionOffset）は従来どおりHitEffectDataで調整する。
    /// </summary>
    /// <param name="originPos">基準位置（この位置から斜め上へ離して出す）</param>
    /// <param name="facingForward">出す対象のキャラの向き（通常はtransform.forward）</param>
    public void SpawnBehindFacing(HitEffectData data, Vector3 originPos, Vector3 facingForward)
    {
        if (data == null || data.effectPrefab == null) return;
        if (!CanSpawn(data)) return;

        // Z軸方向の向き。Z成分がほぼ0で判定できない時はZ+向き扱い
        float facingSign = Mathf.Abs(facingForward.z) < 0.01f ? 1f : Mathf.Sign(facingForward.z);

        // 向きと反対側のZ方向
        Vector3 backSide = Vector3.forward * -facingSign;

        float angle = data.baseAngle + Random.Range(-data.angleRandomRange, data.angleRandomRange);
        float rad = angle * Mathf.Deg2Rad;
        Vector3 spawnDir = backSide * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);

        float distance = Random.Range(data.distanceRange.x, data.distanceRange.y);
        SpawnInstance(data, originPos + spawnDir * distance, GetFacingSign(facingForward.z));
    }

    // 生成位置が決まった後の共通処理（位置微調整・生成・スケール・アニメーション開始）
    private void SpawnInstance(HitEffectData data, Vector3 spawnPos, int facingSign = 0)
    {
        // 手動での位置微調整（上下左右奥）を反映。HitEffectDataのInspectorで技ごとに調整できる
        spawnPos += data.spawnPositionOffset;

        // ★追加：キャラの向き別の追加オフセット（Inspectorで左右それぞれ調整）
        spawnPos += GetFacingOffset(facingSign);
        // 技（HitEffectData）ごとの向き別オフセットも加算
        if (facingSign > 0) spawnPos += data.offsetWhenFacingPositiveZ;
        else if (facingSign < 0) spawnPos += data.offsetWhenFacingNegativeZ;

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

