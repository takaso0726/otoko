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
//
// 【MaxImageの回転】
//   MAX中はMaxImageのRectTransformをZ軸回転させ続ける。
//   回転の中心はPivotなので、Pivotは(0.5, 0.5)にしておくこと。
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

    [Header("MAX画像の回転")]
    [Tooltip("MAX時にMaxImageを回転させるか")]
    [SerializeField] bool rotateMaxImage = true;
    [Tooltip("回転速度（度/秒）。マイナスで時計回り、プラスで反時計回り。720 = 1秒に2周")]
    [SerializeField] float maxRotateSpeed = 720f;
    [Tooltip("ONにすると、ヒットストップ等でTime.timeScaleが0でも回し続ける")]
    [SerializeField] bool useUnscaledTime = false;

    [Header("MAX画像の表示優先度")]
    [Tooltip("ONにすると、MaxImageを同じ親の中で一番手前（最後の兄弟）に移動して優先表示する")]
    [SerializeField] bool maxImageToFront = true;
    [Tooltip("ONにすると、MaxImageにCanvas(overrideSorting)を付けて描画順を高くする")]
    [SerializeField] bool overrideMaxSorting = true;
    [Tooltip("MaxImageのSorting Order。大きいほど手前に描画される")]
    [SerializeField] int maxSortingOrder = 100;

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
        // 滑らかに補間（smoothFill時のみ）
        if (smoothFill && !Mathf.Approximately(displayRatio, targetRatio))
        {
            displayRatio = Mathf.MoveTowards(displayRatio, targetRatio, fillLerpSpeed * Time.deltaTime);
            ApplyRatio(displayRatio);
        }

        // MAX中はMaxImageを回転させ続ける
        if (rotateMaxImage && maxImage != null && maxImage.gameObject.activeSelf)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            maxImage.rectTransform.Rotate(0f, 0f, maxRotateSpeed * dt);
        }
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
        if (maxImage != null)
        {
            bool wasActive = maxImage.gameObject.activeSelf;
            maxImage.gameObject.SetActive(isMax);

            // MAXになった瞬間の処理
            if (isMax && !wasActive)
            {
                // 0度から回し始める
                maxImage.rectTransform.localRotation = Quaternion.identity;

                // 同じ親の中で一番手前に移動（優先表示）
                if (maxImageToFront) maxImage.transform.SetAsLastSibling();

                // Canvasのsorting orderを上げて描画順を高くする
                if (overrideMaxSorting)
                {
                    Canvas c = maxImage.GetComponent<Canvas>();
                    if (c == null) c = maxImage.gameObject.AddComponent<Canvas>();
                    c.overrideSorting = true;
                    c.sortingOrder = maxSortingOrder;
                }
            }
        }

        // ★MAXになったらFrameだけ非表示にする。Arcは常に表示したまま（MAX時はfillAmount=1の満タン状態）。
        if (frameImage != null) frameImage.gameObject.SetActive(!isMax);
        if (arcImage != null)
        {
            arcImage.gameObject.SetActive(true);
            arcImage.fillAmount = ratio; // Radial 360が弧の見た目を自動計算してくれる
        }
    }
}
