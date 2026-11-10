using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 五角形のレーダーチャートをuGUIのメッシュで描画する。
/// 外枠(半透明の塗り+線+頂点の点)、中心からの破線、値の多角形(グラデーション)を1コンポーネントで描く。
/// 値は 0〜1。上から時計回りに並ぶ。
///
/// 【使い方】
/// Canvas の下に空の GameObject を作ってこれを付ける。
/// Rect Transform の Width / Height を同じ値にし、Pivot は (0.5, 0.5) のままにする。
/// 五角形の大きさは Width / Height の小さい方の半分が半径になる。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class PentagonChartGraphic : MaskableGraphic
{
    const int N = 5;

    // ───────────── 値 ─────────────
    [Header("値")]
    [Tooltip("5項目の値(0〜1)。上から時計回りの順番。\n0 = 中心、1 = 外枠の頂点。\n実行中は PentagonChart.Play() が書き換えるので、ここは初期表示用。")]
    [SerializeField, Range(0f, 1f)]
    float[] values = { 0.7f, 0.8f, 0.6f, 1f, 0.5f };

    // ───────────── 外枠 ─────────────
    [Header("外枠")]
    [Tooltip("外枠の五角形の中を塗る色。アルファを下げると背景が透ける。\n動画では白の半透明(アルファ約0.35)。")]
    [SerializeField] Color frameFill = new Color(1f, 1f, 1f, 0.35f);

    [Tooltip("外枠の輪郭線・中心からの破線・頂点の点に使う色。")]
    [SerializeField] Color lineColor = Color.white;

    [Tooltip("外枠の輪郭線の太さ(ピクセル)。")]
    [SerializeField, Min(0f)] float lineWidth = 3f;

    [Tooltip("各頂点に描く白い点の半径(ピクセル)。0にすると点を描かない。")]
    [SerializeField, Min(0f)] float dotRadius = 5f;

    // ───────────── 値の塗り ─────────────
    [Header("値の塗り(グラデーション)")]
    [Tooltip("値の多角形の「中心側」の色。\n動画では濃いオレンジ。")]
    [SerializeField] Color valueInner = new Color(0.75f, 0.40f, 0.05f, 0.95f);

    [Tooltip("値の多角形の「外側(各頂点)」の色。\n動画では明るい金色。中心側の色から滑らかにつながる。")]
    [SerializeField] Color valueOuter = new Color(0.95f, 0.70f, 0.05f, 0.95f);

    // ───────────── 破線 ─────────────
    [Header("中心からの破線")]
    [Tooltip("破線1本分(線の部分)の長さ(ピクセル)。0以下にすると破線を描かない。")]
    [SerializeField] float dashLength = 14f;

    [Tooltip("破線と破線の間の隙間の長さ(ピクセル)。")]
    [SerializeField, Min(0.1f)] float gapLength = 8f;

    [Tooltip("破線の太さ(ピクセル)。")]
    [SerializeField, Min(0f)] float spokeWidth = 2f;

    /// <summary>五角形の外枠の半径(ピクセル)。ラベル配置にも使う。</summary>
    public float Radius => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;

    /// <summary>i番目の頂点の向き。0が真上で、時計回りに並ぶ。</summary>
    public static Vector2 Direction(int i)
    {
        float a = (90f - i * 360f / N) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    /// <summary>5つの値(0〜1)を設定して再描画する。</summary>
    public void SetValues(float[] newValues)
    {
        for (int i = 0; i < N && i < newValues.Length; i++)
            values[i] = Mathf.Clamp01(newValues[i]);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float radius = Radius;
        Vector2 c = rectTransform.rect.center;

        var outer = new Vector2[N];
        var val = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            Vector2 dir = Direction(i);
            float v = (values != null && i < values.Length) ? Mathf.Clamp01(values[i]) : 0f;
            outer[i] = c + dir * radius;
            val[i] = c + dir * radius * v;
        }

        // 1. 外枠の塗り
        Fan(vh, c, outer, frameFill, frameFill);
        // 2. 値の多角形
        Fan(vh, c, val, valueInner, valueOuter);
        // 3. 外枠の線
        for (int i = 0; i < N; i++)
            Line(vh, outer[i], outer[(i + 1) % N], lineWidth, lineColor);
        // 4. 中心からの破線
        for (int i = 0; i < N; i++)
            Dashed(vh, c, outer[i]);
        // 5. 頂点の点
        for (int i = 0; i < N; i++)
            Circle(vh, outer[i], dotRadius, lineColor);
    }

    // ───────────── 描画ヘルパー ─────────────

    static void Fan(VertexHelper vh, Vector2 center, Vector2[] ring, Color centerColor, Color ringColor)
    {
        int s = vh.currentVertCount;
        vh.AddVert(center, centerColor, Vector2.zero);
        for (int i = 0; i < ring.Length; i++)
            vh.AddVert(ring[i], ringColor, Vector2.zero);
        for (int i = 0; i < ring.Length; i++)
            vh.AddTriangle(s, s + 1 + i, s + 1 + (i + 1) % ring.Length);
    }

    static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color col)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f || width <= 0f) return;
        d.Normalize();
        Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
        int s = vh.currentVertCount;
        vh.AddVert(a - n, col, Vector2.zero);
        vh.AddVert(a + n, col, Vector2.zero);
        vh.AddVert(b + n, col, Vector2.zero);
        vh.AddVert(b - n, col, Vector2.zero);
        vh.AddTriangle(s, s + 1, s + 2);
        vh.AddTriangle(s, s + 2, s + 3);
    }

    void Dashed(VertexHelper vh, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f || dashLength <= 0f) return;
        Vector2 dir = d / len;
        float pos = 0f;
        while (pos < len)
        {
            float end = Mathf.Min(pos + dashLength, len);
            Line(vh, a + dir * pos, a + dir * end, spokeWidth, lineColor);
            pos = end + Mathf.Max(gapLength, 0.1f);
        }
    }

    static void Circle(VertexHelper vh, Vector2 center, float r, Color col)
    {
        if (r <= 0f) return;
        const int seg = 16;
        int s = vh.currentVertCount;
        vh.AddVert(center, col, Vector2.zero);
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            vh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector2.zero);
        }
        for (int i = 0; i < seg; i++)
            vh.AddTriangle(s, s + 1 + i, s + 1 + (i + 1) % seg);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (values == null || values.Length != N)
            System.Array.Resize(ref values, N);
    }
#endif
}
