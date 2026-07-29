using UnityEngine;

/// <summary>
/// 生成された擬音テキストの「パッ」と出て消えるアニメーションを担当。
/// AnimationCurveでイージングを自由に調整できる。
/// </summary>
public class HitEffectAnimator : MonoBehaviour
{
    private HitEffectData data;
    private float baseScale;
    private float timer;

    // ★追加：あと何秒でこの演出が消えるか。Spawner側が「重ねて出してよいタイミングか」を
    //   判定するのに使う。dataが未設定（Play前）の場合は0を返す。
    public float RemainingTime => data != null ? Mathf.Max(0f, data.lifeTime - timer) : 0f;

    public void Play(HitEffectData sourceData, float scale)
    {
        data = sourceData;
        baseScale = scale;
        timer = 0f;
    }

    private void Update()
    {
        if (data == null) return;

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / data.lifeTime);

        float curveValue = data.scaleCurve.Evaluate(t);
        transform.localScale = Vector3.one * baseScale * curveValue;

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}
