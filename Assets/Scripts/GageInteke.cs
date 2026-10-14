using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleAttractorToTarget : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private Transform targetTransform; // 吸い込み先のターゲット（3D位置、またはUIに追従するオブジェクト）
    [SerializeField] private float moveSpeed = 10f;     // 吸い込まれる速度
    [SerializeField] private float arriveDistance = 0.5f; // 到着とみなす距離

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;

    private void Start()
    {
        ps = GetComponent<ParticleSystem>();

        // パーティクルの最大数分の配列を確保（GC Allocを防ぐため使い回す）
        var main = ps.main;
        particles = new ParticleSystem.Particle[main.maxParticles];
    }

    private void LateUpdate()
    {
        if (targetTransform == null) return;

        // 1. 現在生きているパーティクルの情報を取得
        int numParticlesAlive = ps.GetParticles(particles);

        Vector3 targetPos = targetTransform.position;

        // 2. 1粒ずつ位置を書き換える
        for (int i = 0; i < numParticlesAlive; i++)
        {
            // パーティクルの現在地からターゲットへ向けて補間移動
            // ※ Simulation SpaceがWorldであることが前提
            particles[i].position = Vector3.Lerp(
                particles[i].position,
                targetPos,
                moveSpeed * Time.deltaTime
            );

            // 3. ターゲットに十分近づいたら消滅させる
            float distance = Vector3.Distance(particles[i].position, targetPos);
            if (distance < arriveDistance)
            {
                particles[i].remainingLifetime = 0f; // 寿命を0にして消す
                OnParticleAbsorbed(); // ゲージ増加などの処理
            }
        }

        // 4. 変更したパーティクル情報をシステムに戻す
        ps.SetParticles(particles, numParticlesAlive);
    }

    private void OnParticleAbsorbed()
    {
        // ゲージを増やす処理やSEの再生など
        // ※ 1フレームに複数個同時に消えることがあるため、呼び出し頻度に注意
        Debug.Log("パーティクルがゲージに吸収された！");
    }
}