using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// PentagonChartGraphic の値をアニメーションさせ、ラベルを頂点の外側に配置してフェードインさせる。
///
/// 【使い方】
/// PentagonChartGraphic と同じ GameObject に付ける。
/// ラベル用の TextMeshPro - Text (UI) を5つ作り、Labels に上から時計回りの順で割り当てる。
/// ラベルは毎フレーム、五角形(PentagonChartGraphic)の位置・大きさ・回転を基準に配置されるので、
/// チャートを動かす・拡縮するとラベルも一緒に追従する(ラベルをチャートの子にしてもしなくてもよい)。
///
/// 同じチャートを複数生成しても(1P用・2P用など)、ラベルはチャートごとに専用のものが使われる。
/// ラベルがこのチャートの「子」でない場合は、起動時にコピーを作ってこのチャート専用にする
/// (元のラベルは非表示になる)。そのため、ラベルがプレハブの外にあっても、複数のチャートで奪い合わない。
/// </summary>
public class PentagonChart : MonoBehaviour
{
    const int N = 5;

    // ───────────── 参照 ─────────────
    [Header("参照")]
    [Tooltip("五角形を描画しているコンポーネント。\n同じ GameObject の PentagonChartGraphic をドラッグして割り当てる。")]
    [SerializeField] PentagonChartGraphic graphic;

    [Tooltip("ラベル用の TextMeshPro(5つ)。\n順番は 上 → 右上 → 右下 → 左下 → 左(上から時計回り)。\n位置は五角形を基準に自動で決まり、チャートが動くと追従する。\nチャートの子でないラベルは、起動時にこのチャート専用のコピーが作られる。")]
    [SerializeField] TMP_Text[] labels = new TMP_Text[N];

    [Tooltip("タイプ名を表示する TextMeshPro(任意)。\nチャートの下や中心など、好きな場所に置いてよい。\n空のままならタイプ名は表示されない。")]
    [SerializeField] TMP_Text typeLabel;

    [Tooltip("ONにすると、タイプ名もチャートの位置に追従する。\n起動時の「チャートに対する相対位置」をそのまま保つので、プレハブ内で置いた位置・大きさ関係が維持される。\n(チャートの子にしている場合は、もともと追従するので何もしない)")]
    [SerializeField] bool typeLabelFollows = true;

    // ───────────── ラベル ─────────────
    [Header("ラベル")]
    [Tooltip("各項目の表示名。Labels と同じ順番(上から時計回り)。\n起動時にラベルの文字へ自動で反映される。")]
    [SerializeField] string[] names = { "Power", "Stamina", "Speed", "Dexterity", "Humor" };

    [Tooltip("ラベルを外枠の頂点からどれだけ外側に置くか(ピクセル)。\n文字が頂点の点に重なる場合は大きくする。")]
    [SerializeField] float labelOffset = 45f;

    [Tooltip("ONにすると、ラベルの基準点を頂点の外向きに自動で合わせる。\n上のラベルは頂点の真上に、右のラベルは頂点の右に、左のラベルは頂点の左に、文字が頂点から離れる向きに伸びる。\nOFFだと、ラベルの中心が基準点になる(文字が長いと頂点に重なりやすい)。")]
    [SerializeField] bool alignOutward = true;

    [Tooltip("ラベルごとの位置の微調整(ピクセル)。Labels と同じ順番。\nX = 右がプラス、Y = 上がプラス。\n全体の距離は変えずに、特定のラベルだけ動かしたいときに使う。")]
    [SerializeField] Vector2[] labelAdjust = new Vector2[N];

    // ───────────── アニメーション ─────────────
    [Header("アニメーション")]
    [Tooltip("値が0から目標値まで伸びるのにかかる時間(秒)。\n最初はゆっくり、最後は減速して止まる動き。")]
    [SerializeField, Min(0.01f)] float valueDuration = 0.8f;

    [Tooltip("ラベル1つがフェードインするのにかかる時間(秒)。")]
    [SerializeField, Min(0.01f)] float labelFadeDuration = 0.4f;

    [Tooltip("ラベルが順番に現れるときの、ラベル同士の開始時間のずれ(秒)。\n0にすると全ラベルが同時に現れる。")]
    [SerializeField, Min(0f)] float labelStagger = 0.1f;

    // ───────────── 起動時の値 ─────────────
    [Header("キャラクターデータ")]
    [Tooltip("起動時に表示するキャラクターのデータ(ラベル名と値)。\nCreate → Pentagon Chart → Character Parameter で作ったアセットを割り当てる。\n設定すると、下の Names と Initial Values より優先される。\n空のままなら Names / Initial Values が使われる。")]
    [SerializeField] CharacterParameterData initialData;

    [Header("起動時の値(キャラクターデータが空のときに使用)")]
    [Tooltip("起動時に自動でアニメーションする値(0〜1が5つ)。\nLabels と同じ順番。\n別のタイミングで再生したい場合は、このスクリプトの Start() を消して Play() を自分で呼ぶ。")]
    [SerializeField, Range(0f, 1f)] float[] initialValues = { 0.7f, 0.8f, 0.6f, 1f, 0.5f };

    readonly float[] current = new float[N];
    Coroutine valueRoutine;

    // Show() がすでに呼ばれていたかどうか。
    // 外部(キャラセレクトなど)が先に Show() を呼んでいた場合、Start() で初期表示に上書きしないために使う。
    bool shown;

    // ラベルの文字色（SetLabelColor で指定された時だけ使う）
    Color labelColor = Color.white;
    bool hasLabelColor;
    bool awoken; // Awake（ラベルの専用化）が済んでいるか。済む前に色を塗ると、共有の元ラベルを塗ってしまうため

    // タイプ名ラベルの「チャート中心からの相対位置」(チャート内側の座標)。起動時に記録して追従に使う
    Vector2 typeLabelLocalOffset;
    bool typeOffsetCaptured;

    void Awake()
    {
        CaptureTypeLabelOffset(); // 専用化(コピー)する前の位置関係を記録しておく
        EnsureOwnLabels();
        awoken = true;
    }

    /// <summary>
    /// 5つのラベル(Power など)の文字色を設定する。透明度(フェードイン)はそのまま保たれる。
    /// 例: chart.SetLabelColor(Color.blue);
    /// </summary>
    public void SetLabelColor(Color color)
    {
        labelColor = color;
        hasLabelColor = true;
        if (awoken) ApplyLabelColor(); // まだなら、Show() 内の PlaceLabels() で反映される
    }

    void ApplyLabelColor()
    {
        if (!hasLabelColor || labels == null) return;

        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null) continue;
            Color c = labelColor;
            c.a = labels[i].color.a; // フェードイン中の透明度は変えない
            labels[i].color = c;
        }
    }

    // ラベルがこのチャートの子でない場合は、このチャート専用のコピーを作って使う。
    // 子であれば、チャートをコピー(Instantiate)した時にラベルも一緒にコピーされるので、そのまま使う。
    void EnsureOwnLabels()
    {
        if (labels != null)
        {
            for (int i = 0; i < labels.Length; i++)
                labels[i] = MakeOwn(labels[i]);
        }
        typeLabel = MakeOwn(typeLabel);
    }

    TMP_Text MakeOwn(TMP_Text src)
    {
        if (src == null || src.transform.IsChildOf(transform)) return src;

        // 見た目・大きさはそのままに、このチャートの子としてコピーする
        TMP_Text copy = Instantiate(src, transform, true);
        copy.gameObject.SetActive(true);   // 元が非表示でも、コピーは表示状態にする
        src.gameObject.SetActive(false);   // 元のラベルは隠す(コピーだけが表示される)
        return copy;
    }

    // ラベルは毎フレーム、チャートの現在の位置を基準に置き直す。
    // (チャートが移動・拡縮・回転しても、ラベルがその場に取り残されない)
    void LateUpdate()
    {
        UpdateLabelPositions();
    }

    void Start()
    {
        if (shown) return;

        if (initialData != null)
        {
            Show(initialData);
        }
        else
        {
            PlaceLabels();
            HideLabels();
            Play(initialValues);
        }
    }

    /// <summary>
    /// キャラクターのデータに切り替える。ラベル名が入れ替わり、値が新しい形へ変化する。
    /// 例: chart.Show(characterDataList[index]);
    /// </summary>
    public void Show(CharacterParameterData data)
    {
        if (data == null) return;
        Show(data.labelNames, data.values, data.typeName);
    }

    /// <summary>
    /// ラベル名と値を直接指定して切り替える(ScriptableObject を使わない場合)。
    /// タイプ名は省略可(省略すると表示中のタイプ名はそのまま)。
    /// 例: chart.Show(new[]{"力","体力","速さ","器用さ","ユーモア"}, new[]{0.7f,0.8f,0.6f,1f,0.5f}, "パワー型");
    /// </summary>
    public void Show(string[] newNames, float[] newValues, string newTypeName = null)
    {
        shown = true;
        if (!gameObject.activeSelf) gameObject.SetActive(true); // 非表示のままだとアニメーション(コルーチン)が動かないため

        CopyNames(newNames);
        PlaceLabels();
        HideLabels();
        Play(newValues);

        if (typeLabel != null && newTypeName != null)
        {
            typeLabel.text = newTypeName;
            SetLabelAlpha(typeLabel, 0f);
            StartCoroutine(FadeLabel(typeLabel)); // ラベルと同じようにフェードイン
        }
    }

    void CopyNames(string[] src)
    {
        if (names == null || names.Length != N) names = new string[N];
        for (int i = 0; i < N; i++)
            names[i] = (src != null && i < src.Length) ? src[i] : "";
    }

    /// <summary>ラベルの文字・基準点を設定し、位置を合わせる。</summary>
    void PlaceLabels()
    {
        if (graphic == null) return;

        for (int i = 0; i < N; i++)
        {
            if (labels == null || i >= labels.Length || labels[i] == null) continue;

            Vector2 dir = PentagonChartGraphic.Direction(i);
            RectTransform rt = labels[i].rectTransform;

            if (names != null && i < names.Length) labels[i].text = names[i];

            // 基準点(Pivot)を頂点の外向きに合わせる: 頂点が右なら文字は右へ、上なら上へ伸びる
            rt.pivot = alignOutward ? new Vector2(0.5f - dir.x * 0.5f, 0.5f - dir.y * 0.5f)
                                    : new Vector2(0.5f, 0.5f);
        }

        ApplyLabelColor();
        UpdateLabelPositions();
    }

    /// <summary>
    /// ラベルの位置を、チャート(五角形)の現在の位置を基準に合わせる。
    /// 「チャートの中心からの相対位置」をワールド座標へ変換して置くので、
    /// ラベルがチャートの子でも別の場所にあっても、チャートの移動・拡縮・回転に追従する。
    /// </summary>
    void UpdateLabelPositions()
    {
        if (graphic == null || labels == null) return;

        float r = graphic.Radius + labelOffset;
        for (int i = 0; i < N && i < labels.Length; i++)
        {
            if (labels[i] == null) continue;

            Vector2 dir = PentagonChartGraphic.Direction(i);
            Vector2 adjust = (labelAdjust != null && i < labelAdjust.Length) ? labelAdjust[i] : Vector2.zero;
            labels[i].rectTransform.position = ChartPointToWorld(dir * r + adjust);
        }

        if (typeLabelFollows && typeLabel != null && typeOffsetCaptured)
        {
            typeLabel.rectTransform.position = ChartPointToWorld(typeLabelLocalOffset);
        }
    }

    // チャートの中心を原点とした座標(チャート内側の単位)を、ワールド座標へ変換する
    Vector3 ChartPointToWorld(Vector2 localFromCenter)
    {
        RectTransform g = graphic.rectTransform;
        return g.TransformPoint((Vector3)(g.rect.center + localFromCenter));
    }

    // 起動時のタイプ名ラベルの位置を「チャートの中心からの相対位置」として記録する
    void CaptureTypeLabelOffset()
    {
        if (typeLabel == null || graphic == null || typeOffsetCaptured) return;
        if (typeLabel.transform.IsChildOf(graphic.transform)) return; // チャートの子なら、もともと追従する

        RectTransform g = graphic.rectTransform;
        Vector3 local = g.InverseTransformPoint(typeLabel.rectTransform.position);
        typeLabelLocalOffset = (Vector2)local - g.rect.center;
        typeOffsetCaptured = true;
    }

    /// <summary>ラベルをいったん透明にする(フェードイン前の状態)。</summary>
    void HideLabels()
    {
        for (int i = 0; i < N; i++)
            if (labels[i] != null) SetLabelAlpha(labels[i], 0f);
    }

#if UNITY_EDITOR
    // インスペクターで値を変えると、再生しなくてもラベルの位置がすぐ反映される
    void OnValidate()
    {
        if (labelAdjust == null || labelAdjust.Length != N)
            System.Array.Resize(ref labelAdjust, N);
        if (initialData != null)
        {
            // キャラクターデータのラベル名・タイプ名をエディタ上でもすぐ確認できる
            CopyNames(initialData.labelNames);
            if (typeLabel != null) typeLabel.text = initialData.typeName;
        }
        PlaceLabels();
    }
#endif

    /// <summary>現在の値から target(0〜1が5つ)に向かってアニメーションし、ラベルを順番にフェードインさせる。</summary>
    public void Play(float[] target)
    {
        // カーソルを素早く動かした時に、前のアニメーションが残って表示が乱れないよう、いったん全部止める
        StopAllCoroutines();
        valueRoutine = StartCoroutine(AnimateValues(target));
        StartCoroutine(FadeInLabels());
    }

    IEnumerator AnimateValues(float[] target)
    {
        var from = (float[])current.Clone();
        for (float t = 0f; t < valueDuration; t += Time.deltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / valueDuration, 3f); // ease-out
            for (int i = 0; i < N; i++)
                current[i] = Mathf.Lerp(from[i], target[i], k);
            graphic.SetValues(current);
            yield return null;
        }
        for (int i = 0; i < N; i++) current[i] = target[i];
        graphic.SetValues(current);
    }

    IEnumerator FadeInLabels()
    {
        for (int i = 0; i < N; i++)
        {
            if (labels[i] != null) StartCoroutine(FadeLabel(labels[i]));
            yield return new WaitForSeconds(labelStagger);
        }
    }

    IEnumerator FadeLabel(TMP_Text label)
    {
        for (float t = 0f; t < labelFadeDuration; t += Time.deltaTime)
        {
            SetLabelAlpha(label, t / labelFadeDuration);
            yield return null;
        }
        SetLabelAlpha(label, 1f);
    }

    static void SetLabelAlpha(TMP_Text label, float a)
    {
        Color c = label.color;
        c.a = a;
        label.color = c;
    }
}
