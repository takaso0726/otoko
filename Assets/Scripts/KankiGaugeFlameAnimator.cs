using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

/// <summary>
/// 漢気ゲージ（KankiGaugeCircle）1本ごとに付ける、炎アニメーション制御スクリプト。
///
/// ・ゲージが「マックス（満タン）」になった瞬間に炎を点火し、マックスでなくなったらフェードアウトして消す。
/// ・炎の絵は、スライス済みアトラス画像（Sprite Mode = Multiple）のスプライトを順番に切り替えて再生する。
/// ・「点火アニメ（1回だけ再生）」→「燃焼ループ（満タン中ずっと再生）」の2段構えにできる。
///
/// 使い方：
///  1) KankiGaugeCircle が付いているオブジェクトに、このスクリプトを追加する（P1/P2/敵の全ゲージ分）。
///  2) Inspectorでアトラスのスプライトを frames に設定する
///     （atlasTexture にアトラスを入れて、右クリックメニュー「アトラスからフレームを自動取得」でもOK）。
///  3) GameMNG側から SetRatio(0〜1) が呼ばれる（GameMNG.cs側の変更を参照）。
/// </summary>
[DisallowMultipleComponent]
public class KankiGaugeFlameAnimator : MonoBehaviour
{
    // 炎の状態
    private enum FlameState
    {
        Off,        // 消えている
        Igniting,   // 点火アニメ再生中（1回だけ）
        Burning,    // 燃焼ループ中
        Fading      // マックスでなくなったので、フェードアウト中
    }

    // ------------------------------------------------------------
    // 表示先
    // ------------------------------------------------------------
    [Header("表示先")]
    [Tooltip("炎を表示するImage。未設定の場合は、実行時に子オブジェクト「FlameImage」を自動生成します。")]
    public Image flameImage;

    [Tooltip("ONにすると、フレーム切り替えのたびにImageをスプライト本来のサイズに合わせます。OFFならImageの大きさのままです。")]
    public bool setNativeSize = false;

    // ------------------------------------------------------------
    // アトラス（フレーム）
    // ------------------------------------------------------------
    [Header("アトラスのフレーム（再生順に並べる）")]
    [Tooltip("Sprite Mode = Multiple でスライス済みのアトラス画像。右クリックメニューからフレームを自動取得できます（Editor専用）。")]
    public Texture2D atlasTexture;

    [Tooltip("炎のスプライト（再生順）。")]
    public Sprite[] frames;

    [Tooltip("燃焼ループを開始するフレーム番号（0始まり）。\n" +
             "0 ＝ 全フレームをループ再生。\n" +
             "例：5 ＝ 0〜4番を点火アニメとして1回だけ再生し、5番以降を満タン中ずっとループ。")]
    public int loopStartIndex = 0;

    [Tooltip("1秒あたりのコマ数")]
    public float framesPerSecond = 12f;

    // ------------------------------------------------------------
    // 表示制御
    // ------------------------------------------------------------
    [Header("表示制御")]
    [Tooltip("この値以上でマックス（満タン）とみなす。浮動小数点の誤差対策のため1ではなく0.999を既定にしています。")]
    [Range(0.9f, 1f)]
    public float fullThreshold = 0.999f;

    [Tooltip("点火時のフェードイン時間（秒）。0なら即表示。")]
    public float fadeInDuration = 0.1f;

    [Tooltip("マックスでなくなった時のフェードアウト時間（秒）。0なら即消える。")]
    public float fadeOutDuration = 0.25f;

    [Tooltip("ONにすると、Time.timeScaleの影響を受けずに再生します（ヒットストップ中も炎が動き続けます）。")]
    public bool useUnscaledTime = true;

    // ------------------------------------------------------------
    // 内部状態
    // ------------------------------------------------------------
    private FlameState state = FlameState.Off;
    private bool initialized = false;   // 最初のSetRatioを受け取ったか
    private bool isFull = false;        // 現在マックス状態か
    private float animTime = 0f;        // アニメ経過時間
    private float alpha = 0f;           // 現在の透明度(0〜1)
    private int lastFrameIndex = -1;    // 直前に表示したフレーム（無駄なSprite更新を避ける）

    // ============================================================
    // 外部から呼ぶAPI
    // ============================================================

    /// <summary>
    /// ゲージの充填率(0〜1)を渡す。KankiGaugeCircle.SetRatio と同じ形で呼べる。
    /// マックスになった瞬間／マックスでなくなった瞬間だけ状態が切り替わる。
    /// </summary>
    public void SetRatio(float ratio)
    {
        bool full = ratio >= fullThreshold;

        // 最初の1回目：すでに満タンなら点火演出は飛ばして、いきなり燃焼状態にする
        // （キャラ再生成やシーン開始直後に、不自然に点火アニメが走らないようにするため）
        if (!initialized)
        {
            initialized = true;
            isFull = full;
            if (full) StartBurningImmediately();
            else SetOffImmediately();
            return;
        }

        if (full == isFull) return; // 状態が変わっていなければ何もしない
        isFull = full;

        if (full) StartIgnite();
        else StartFadeOut();
    }

    /// <summary>炎を即座に消し、状態をリセットする（シーン切替・リスポーン時用）。</summary>
    public void ResetFlame()
    {
        initialized = false;
        isFull = false;
        SetOffImmediately();
    }

    // ============================================================
    // Unityイベント
    // ============================================================

    void Awake()
    {
        EnsureImage();
        SetOffImmediately();
    }

    void OnEnable()
    {
        // 非アクティブ化→再アクティブ化された時に、満タン状態なら炎を復帰させる
        if (initialized && isFull && state == FlameState.Off) StartBurningImmediately();
    }

    void Update()
    {
        if (state == FlameState.Off) return;
        if (frames == null || frames.Length == 0) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        animTime += dt;

        // --- アルファ（フェードイン／フェードアウト） ---
        if (state == FlameState.Fading)
        {
            alpha = fadeOutDuration <= 0f ? 0f : alpha - dt / fadeOutDuration;
            if (alpha <= 0f)
            {
                SetOffImmediately();
                return;
            }
        }
        else
        {
            alpha = fadeInDuration <= 0f ? 1f : Mathf.Min(1f, alpha + dt / fadeInDuration);
        }

        // --- フレーム切り替え ---
        int index = CalcFrameIndex();
        if (index != lastFrameIndex) ShowFrame(index);

        ApplyAlpha();
    }

    // ============================================================
    // 状態遷移
    // ============================================================

    private void StartIgnite()
    {
        if (frames == null || frames.Length == 0) return;

        animTime = 0f;
        lastFrameIndex = -1;
        // 点火アニメ（ループ開始前のフレーム）がなければ、そのまま燃焼ループへ
        state = (ClampedLoopStart() > 0) ? FlameState.Igniting : FlameState.Burning;
        alpha = fadeInDuration <= 0f ? 1f : 0f;
        SetImageVisible(true);
        ShowFrame(CalcFrameIndex());
        ApplyAlpha();
    }

    private void StartBurningImmediately()
    {
        if (frames == null || frames.Length == 0) return;

        // 点火アニメは飛ばして、燃焼ループの先頭から再生
        animTime = 0f;
        lastFrameIndex = -1;
        state = FlameState.Burning;
        alpha = 1f;
        SetImageVisible(true);
        ShowFrame(CalcFrameIndex());
        ApplyAlpha();
    }

    private void StartFadeOut()
    {
        if (state == FlameState.Off) return;

        // 点火アニメ途中でも、そのまま燃焼ループのコマでフェードアウトさせる
        if (state == FlameState.Igniting)
        {
            animTime = 0f;
            lastFrameIndex = -1;
        }
        state = FlameState.Fading;
        if (fadeOutDuration <= 0f) SetOffImmediately();
    }

    private void SetOffImmediately()
    {
        state = FlameState.Off;
        alpha = 0f;
        animTime = 0f;
        lastFrameIndex = -1;
        SetImageVisible(false);
    }

    // ============================================================
    // フレーム計算・表示
    // ============================================================

    private int ClampedLoopStart()
    {
        if (frames == null || frames.Length == 0) return 0;
        return Mathf.Clamp(loopStartIndex, 0, frames.Length - 1);
    }

    private int CalcFrameIndex()
    {
        int loopStart = ClampedLoopStart();
        int step = Mathf.FloorToInt(animTime * Mathf.Max(0.01f, framesPerSecond));

        if (state == FlameState.Igniting)
        {
            if (step < loopStart) return step;

            // 点火アニメが終わったので、燃焼ループへ移行（ループ側の時間は0から数え直す）
            state = FlameState.Burning;
            animTime = 0f;
            step = 0;
        }

        // 燃焼ループ（ループ範囲のフレームを繰り返す）
        int loopCount = frames.Length - loopStart;
        return loopStart + (step % loopCount);
    }

    private void ShowFrame(int index)
    {
        if (flameImage == null || frames == null) return;
        if (index < 0 || index >= frames.Length) return;

        lastFrameIndex = index;
        flameImage.sprite = frames[index];
        if (setNativeSize) flameImage.SetNativeSize();
    }

    private void ApplyAlpha()
    {
        if (flameImage == null) return;
        Color c = flameImage.color;
        c.a = Mathf.Clamp01(alpha);
        flameImage.color = c;
    }

    private void SetImageVisible(bool visible)
    {
        if (flameImage != null) flameImage.enabled = visible;
    }

    // ============================================================
    // Image の自動生成
    // ============================================================

    private void EnsureImage()
    {
        if (flameImage != null)
        {
            flameImage.raycastTarget = false;
            return;
        }

        // 子オブジェクト「FlameImage」を探し、無ければ作る（親いっぱいに広げる）
        Transform child = transform.Find("FlameImage");
        if (child == null)
        {
            var go = new GameObject("FlameImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            child = go.transform;
            child.SetParent(transform, false);

            var rt = (RectTransform)child;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        flameImage = child.GetComponent<Image>();
        if (flameImage == null) flameImage = child.gameObject.AddComponent<Image>();

        flameImage.raycastTarget = false;
        flameImage.preserveAspect = true;
    }

    // ============================================================
    // Editor専用：アトラスからフレームを自動取得
    // ============================================================
#if UNITY_EDITOR
    [ContextMenu("アトラスからフレームを自動取得")]
    private void LoadFramesFromAtlas()
    {
        if (atlasTexture == null)
        {
            Debug.LogWarning("[KankiGaugeFlameAnimator] atlasTextureが未設定です。", this);
            return;
        }

        string path = AssetDatabase.GetAssetPath(atlasTexture);
        var sprites = new List<Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (obj is Sprite s) sprites.Add(s);
        }

        if (sprites.Count == 0)
        {
            Debug.LogWarning("[KankiGaugeFlameAnimator] スプライトが見つかりません。Texture ImporterのSprite Modeを「Multiple」にしてスライスしてください。", this);
            return;
        }

        // 名前末尾の数字で並べ替え（Flame_2 が Flame_10 より前に来るようにする）
        sprites.Sort((a, b) =>
        {
            int na = TrailingNumber(a.name);
            int nb = TrailingNumber(b.name);
            return na != nb ? na.CompareTo(nb) : string.CompareOrdinal(a.name, b.name);
        });

        Undo.RecordObject(this, "アトラスからフレームを自動取得");
        frames = sprites.ToArray();
        EditorUtility.SetDirty(this);
        Debug.Log($"[KankiGaugeFlameAnimator] {frames.Length}枚のフレームを取得しました。", this);
    }

    private static int TrailingNumber(string name)
    {
        int end = name.Length;
        int start = end;
        while (start > 0 && char.IsDigit(name[start - 1])) start--;
        if (start == end) return int.MaxValue; // 数字が無い名前は最後尾へ
        return int.TryParse(name.Substring(start), out int n) ? n : int.MaxValue;
    }
#endif
}
