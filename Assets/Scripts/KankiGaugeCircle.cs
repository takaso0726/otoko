using UnityEngine;
using UnityEngine.UI;

//=====================================================
// 漢気ゲージを円形（放射状）ゲージとして表示するUIコンポーネント
//
// 【Hierarchy構成例】
//   KankiGaugeCircle (このスクリプトをアタッチする空のGameObject)
//     ├─ FrameImage … 「MAXになるまで」の背景・輪郭画像（Image Type = Simple）
//     ├─ ArcImage   … 実際にたまる弧のpng（Image Type = Filled / Fill Method = Radial 360）
//     └─ MaxImage   … MAX時専用の画像（Image Type = Simple、通常は非アクティブ）
//
// 【ArcImageの重要設定】
//   Image Type   = Filled
//   Fill Method  = Radial 360
//   Fill Origin  = Bottom（弧を下側〜から溜めたい場合。好みに応じてTop/Left/Rightに変更可）
//   Clockwise    = 好みで（見た目が逆なら反転する）
//   Fill Amount  = このスクリプトのSetRatio()で自動更新される
//
// これにより「弧を回転させて溜まっているように見せ、180度で透明スプライトを消して
// 同じ画像を生成し直す」という手動処理を、Unity標準のRadial 360 Fillが
// 内部シェーダーで自動的に肩代わりしてくれる（弧のpngは1枚で足りる）。
//=====================================================
public class KankiGaugeCircle : MonoBehaviour
{
    [Header("参照Image")]
    [Tooltip("MAXになるまでの背景・輪郭画像（Simple）")]
    [SerializeField] Image frameImage;
    [Tooltip("実際に溜まっていく弧の画像（Filled / Radial 360）")]
    [SerializeField] Image arcImage;
    [Tooltip("MAX時に表示する専用画像（Simple）")]
    [SerializeField] Image maxImage;

    [Header("変化を滑らかにする（任意）")]
    [Tooltip("ONにすると、目標値までfillAmountを毎フレーム補間して滑らかに動かす。OFFなら即座に反映。")]
    [SerializeField] bool smoothFill = true;
    [Tooltip("補間速度。大きいほど素早く目標値へ追いつく")]
    [SerializeField] float fillLerpSpeed = 6f;

    float targetRatio;   // 外部から指定された目標割合(0〜1)
    float displayRatio;  // 実際に表示に使っている割合(0〜1)。smoothFill時はここが徐々にtargetRatioへ近づく

    void Awake()
    {
        // 初期状態は0%。Frameは背景として最初から表示され、Arcはfillamount=0で自動的に空の見た目になる。
        // MaxImageだけがこの時点では非表示。
        targetRatio = 0f;
        displayRatio = 0f;
        ApplyRatio(0f);
    }

    void Update()
    {
        if (!smoothFill) return;
        if (Mathf.Approximately(displayRatio, targetRatio)) return;

        displayRatio = Mathf.MoveTowards(displayRatio, targetRatio, fillLerpSpeed * Time.deltaTime);
        ApplyRatio(displayRatio);
    }

    /// <summary>
    /// ゲージ割合(0〜1)を外部から設定する。
    /// 例: circle.SetRatio(player.GetGaugeFillRatio(0));
    /// </summary>
    public void SetRatio(float ratio01)
    {
        targetRatio = Mathf.Clamp01(ratio01);

        if (!smoothFill)
        {
            displayRatio = targetRatio;
            ApplyRatio(displayRatio);
        }
    }

    // 実際にImageへ反映する処理
    void ApplyRatio(float ratio)
    {
        bool isMax = ratio >= 1f;

        // MAX画像は100%の時だけ表示
        if (maxImage != null) maxImage.gameObject.SetActive(isMax);

        // ★Frame/Arcは「たまる前から背景として常に表示」し、MAXになった瞬間だけ非表示にしてMaxImageに切り替える。
        if (frameImage != null) frameImage.gameObject.SetActive(!isMax);
        if (arcImage != null)
        {
            arcImage.gameObject.SetActive(!isMax);
            arcImage.fillAmount = ratio; // Radial 360が弧の見た目を自動計算してくれる
        }
    }
}
