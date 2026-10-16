using UnityEngine;

//=====================================================
// マスク方式のゲージ表示（HPバー／漢気ゲージ共通）
//
// 【仕組み】
//   マスク用のRectTransform（RectMask2D または Mask を付ける）の「幅（高さ）」を割合に応じて変え、
//   その子の中身（Fill画像）は常にフルサイズのまま固定しておく。
//   → 中身が縮むのではなく「切り取られて」減っていくので、斜めの縁取りや模様のあるバーでも崩れない。
//
// 【ヒエラルキー例】
//   HPBar              （背景。Imageなど）
//   └ Mask            ← maskRect。RectMask2D（または Mask）を付ける。このスクリプトもここに付けてOK
//      └ Fill         ← fillRect。バー本体の画像
//
// 【使い方】
//   1. maskRect に Mask を持つオブジェクト、fillRect にバー本体の画像を割り当てる。
//   2. Source を選ぶ：
//        PlayerHP     … player の HP / maxHP
//        PlayerKanki  … player の 漢気ゲージ / (1本分 × 本数)
//        Manual       … 他のスクリプトから SetRatio(0〜1) を呼ぶ
//   3. Direction で減る向きを選ぶ（2P側は左右反転にしたければ RightToLeft など）。
//
// ※Playerの値を毎フレーム読むため、GameMNG側のUI更新とは独立して動く。
//   旧ゲージ（シリンダー）は非表示にするか、GameMNGからの更新対象から外すこと。
//=====================================================
public class MaskGauge : MonoBehaviour
{
    public enum Source
    {
        Manual,       // SetRatio()で外から指定
        PlayerHP,     // Player.HP / Player.maxHP
        PlayerKanki,  // Player.GetKankiGauge() / (kankiGaugePerBar * kankiGaugeBarCount)
    }

    // 生成されたPlayerを、GameInputManagerが自動で割り当てる時の「どちら側か」
    public enum PlayerSlot
    {
        None, // 自動割り当てしない（Inspectorで直接Playerを入れた場合など）
        P1,
        P2,
    }

    public enum Direction
    {
        LeftToRight,  // 左を起点に、右へ向かって伸びる（減る時は右端から削れる）
        RightToLeft,  // 右を起点に、左へ向かって伸びる（減る時は左端から削れる）
        BottomToTop,  // 下を起点に、上へ向かって伸びる
        TopToBottom,  // 上を起点に、下へ向かって伸びる
    }

    [Header("参照")]
    [Tooltip("RectMask2D または Mask を付けたRectTransform。この幅（高さ）が変化する。")]
    [SerializeField] RectTransform maskRect;
    [Tooltip("バー本体（画像）のRectTransform。maskRectの子にする。常にフルサイズで固定される。")]
    [SerializeField] RectTransform fillRect;

    [Header("値の取得元")]
    [SerializeField] Source source = Source.PlayerHP;
    [Tooltip("SourceがPlayerHP／PlayerKankiの時に参照するPlayer。")]
    [SerializeField] Player player;
    [Tooltip("プレイヤーが実行時に生成される場合は、ここで1P用か2P用かを選ぶ。生成後にGameInputManagerが自動でPlayerを割り当てる。\nPlayerを直接割り当てた場合はNoneのままでOK。")]
    [SerializeField] PlayerSlot playerSlot = PlayerSlot.None;

    [Header("表示")]
    [SerializeField] Direction direction = Direction.LeftToRight;
    [Tooltip("1秒あたりに動く割合（1＝全量を1秒で動く）。0なら即座に反映。値が大きいほど素早く追従する。")]
    [SerializeField, Min(0f)] float followSpeed = 2f;

    float targetRatio = 1f;    // 目標の割合（0〜1）
    float displayRatio = 1f;   // 実際に表示している割合（0〜1）
    float fullSize;            // フルの時の幅（縦方向なら高さ）
    bool initialized;
    float searchTimer;         // Player自動検索の間隔用
    float warnTimer;           // 未割り当て警告用
    bool warned;

    bool IsHorizontal => direction == Direction.LeftToRight || direction == Direction.RightToLeft;
    RectTransform.Axis Axis => IsHorizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;

    void Awake()
    {
        if (maskRect == null) maskRect = GetComponent<RectTransform>();
        Initialize();
    }

    void OnEnable()
    {
        // 有効化のたびに、現在値へ即座に合わせる（シーン開始時やリスポーン後に中途半端な表示にならないように）
        if (!initialized) Initialize();
        targetRatio = ReadRatio(targetRatio);
        displayRatio = targetRatio;
        Apply();
    }

    // 起点側の端を固定するため、マスクの軸を「起点」に合わせ、中身をフルサイズで固定する
    void Initialize()
    {
        initialized = true;
        if (maskRect == null)
        {
            Debug.LogError("[MaskGauge] maskRectが設定されていません。", this);
            return;
        }

        // レイアウトが未確定のままだとサイズが0になるので、先に確定させる
        Canvas.ForceUpdateCanvases();

        Vector2 originPivot = GetOriginPivot();
        fullSize = IsHorizontal ? maskRect.rect.width : maskRect.rect.height;
        if (fullSize <= 0f)
        {
            // まだレイアウトが確定していない。Updateでサイズが取れるまで再試行する
            initialized = false;
            return;
        }

        // 見た目の位置を動かさずにpivotだけを起点側へ移す
        SetPivotKeepingPosition(maskRect, originPivot);

        if (fillRect != null)
        {
            // 中身は「マスクの起点側の端」に固定し、常にフルサイズを保つ（マスクが縮んでも一緒に縮まない）
            fillRect.anchorMin = originPivot;
            fillRect.anchorMax = originPivot;
            fillRect.pivot = originPivot;
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(
                IsHorizontal ? fullSize : maskRect.rect.width,
                IsHorizontal ? maskRect.rect.height : fullSize);
        }
        else
        {
            Debug.LogWarning("[MaskGauge] fillRectが未設定です。バー本体の画像を割り当てないと、マスクを狭めた時に中身も一緒に縮みます。", this);
        }
    }

    // 方向ごとの「起点側のpivot」。LeftToRightなら左端(0, 0.5)
    Vector2 GetOriginPivot()
    {
        switch (direction)
        {
            case Direction.LeftToRight: return new Vector2(0f, 0.5f);
            case Direction.RightToLeft: return new Vector2(1f, 0.5f);
            case Direction.BottomToTop: return new Vector2(0.5f, 0f);
            default: return new Vector2(0.5f, 1f); // TopToBottom
        }
    }

    // pivotを変えると見た目の位置がずれるので、その分anchoredPositionで補正する
    static void SetPivotKeepingPosition(RectTransform rt, Vector2 newPivot)
    {
        Vector2 size = rt.rect.size;
        Vector2 deltaPivot = newPivot - rt.pivot;
        rt.pivot = newPivot;
        rt.anchoredPosition += new Vector2(deltaPivot.x * size.x, deltaPivot.y * size.y);
    }

    void Update()
    {
        if (maskRect == null) return;
        if (!initialized) { Initialize(); if (!initialized) return; }

        EnsurePlayer();
        targetRatio = ReadRatio(targetRatio);

        if (followSpeed <= 0f) displayRatio = targetRatio;
        else displayRatio = Mathf.MoveTowards(displayRatio, targetRatio, followSpeed * Time.unscaledDeltaTime);

        Apply();
    }

    // Playerが未割り当ての間、playerSlot（P1/P2）に合うPlayerをシーンから探す。
    // GameInputManagerの割り当てが効かなかった場合の保険。0.5秒ごとにしか探さないので負荷は軽い。
    void EnsurePlayer()
    {
        if (source == Source.Manual || player != null) return;

        searchTimer -= Time.unscaledDeltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = 0.5f;
            if (playerSlot != PlayerSlot.None)
            {
                string wanted = playerSlot == PlayerSlot.P1 ? "P1" : "P2";
                var all = FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (var p in all)
                {
                    if (p.PlayerName == wanted) { player = p; break; }
                }
            }
        }

        // 2秒たってもPlayerが見つからない時は、原因をConsoleに出す（1回だけ）
        warnTimer += Time.unscaledDeltaTime;
        if (player == null && !warned && warnTimer > 2f)
        {
            warned = true;
            Debug.LogWarning("[MaskGauge] Playerが見つかりません。InspectorでPlayerを割り当てるか、" +
                             "Player SlotをP1/P2にしてください（現在：" + playerSlot + "）。", this);
        }
    }

    // Sourceに応じて現在の割合（0〜1）を取得する。取得できない時はfallbackを返す
    float ReadRatio(float fallback)
    {
        switch (source)
        {
            case Source.PlayerHP:
                if (player == null || player.maxHP <= 0) return fallback;
                return Mathf.Clamp01((float)player.HP / player.maxHP);

            case Source.PlayerKanki:
                if (player == null) return fallback;
                float max = player.kankiGaugePerBar * player.kankiGaugeBarCount;
                if (max <= 0f) return fallback;
                return Mathf.Clamp01(player.GetKankiGauge() / max);

            default: // Manual
                return fallback;
        }
    }

    // マスクの幅（高さ）を現在の表示割合に合わせる
    void Apply()
    {
        if (fullSize <= 0f) return;
        float size = fullSize * Mathf.Clamp01(displayRatio);
        maskRect.SetSizeWithCurrentAnchors(Axis, size);

    }

    //-----------------------------------------------------
    // 外部から呼ぶ用
    //-----------------------------------------------------
    // Source=Manualの時に、割合（0〜1）を指定する
    public void SetRatio(float ratio)
    {
        targetRatio = Mathf.Clamp01(ratio);
    }

    // 追従演出を飛ばして、今の目標値に即座に合わせる（ラウンド開始時のリセットなど）
    public void SnapToTarget()
    {
        targetRatio = ReadRatio(targetRatio);
        displayRatio = targetRatio;
        if (initialized && maskRect != null) Apply();
    }

    // 生成された1P/2Pを受け取り、このゲージのplayerSlotに合う方を割り当てる（GameInputManagerから呼ぶ）
    public void AssignFromSlot(Player p1, Player p2)
    {
        if (playerSlot == PlayerSlot.P1) SetPlayer(p1);
        else if (playerSlot == PlayerSlot.P2) SetPlayer(p2);
    }

    // 参照するPlayerをコードから差し替えたい時用（P1/P2の割り当てを実行時に行う場合）
    public void SetPlayer(Player newPlayer)
    {
        player = newPlayer;
        SnapToTarget();
    }
}
