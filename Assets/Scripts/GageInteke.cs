using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleAttractor : MonoBehaviour
{
    [SerializeField] private RectTransform targetUI;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float speed = 10f;       // 引き寄せる速度
    [SerializeField] private float reachDistance = 0.2f; // 吸収判定の距離

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        if (targetCamera == null) targetCamera = Camera.main;

    }

    void LateUpdate()
    {
        if (targetUI == null || ps == null) return;

        // UIのWorld位置を取得（CanvasのRender Modeに応じたスクリーン座標換算）
        Vector3 targetWorldPos = GetTargetWorldPosition();

        int maxParticles = ps.main.maxParticles;
        if (particles == null || particles.Length < maxParticles)
        {
            particles = new ParticleSystem.Particle[maxParticles];
        }

        int count = ps.GetParticles(particles);

        for (int i = 0; i < count; i++)
        {
            // ターゲットに向かって徐々に移動（LerpまたはMoveTowards）
            particles[i].position = Vector3.MoveTowards(
                particles[i].position,
                targetWorldPos,
                speed * Time.deltaTime
            );

            // ターゲットに到達したら粒子の寿命をゼロにして消滅させる（「吸収」の表現）
            if (Vector3.Distance(particles[i].position, targetWorldPos) < reachDistance)
            {
                particles[i].remainingLifetime = 0;
                // ここでUIゲージを増やすイベントなどを呼ぶと効果的！
            }
        }

        ps.SetParticles(particles, count);
    }

    private Vector3 GetTargetWorldPosition()
    {
        // UIのScreen/World位置をParticleSystemの座標系に合わせる
        Vector3 uiScreenPos = RectTransformUtility.WorldToScreenPoint(targetCamera, targetUI.position);

        // 3D空間上のZ軸深度（ParticleSystemのZ位置に合わせる）
        uiScreenPos.z = Mathf.Abs(targetCamera.transform.position.z - transform.position.z);

        return targetCamera.ScreenToWorldPoint(uiScreenPos);
    }
}