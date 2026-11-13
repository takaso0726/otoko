using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// フレームメーターに表示する「このフレームの状態」
public enum MeterState
{
    Idle,      // 灰：何もしていない（初期状態）
    Startup,   // 緑：発生（技を出してから当たり判定がONになるまで）
    Active,    // 赤：攻撃用の当たり判定がONの間
    Recovery,  // 青：当たり判定が終わった後の硬直
    Stun,      // 黄：相手の攻撃が当たった後のやられ硬直
}

// SF6風のフレームメーター（デバッグ用）。
// ・Canvasやイメージの設定は不要。空のGameObjectに付けるだけで、画面に描画される（OnGUI方式）。
// ・P1/P2はGameMNGのp1/p2から自動で取得する（Inspectorで手動指定も可）。
// ・F11キーで表示ON/OFF。
public class FrameMeter : MonoBehaviour
{
    [Header("対象（autoAssignがONならGameMNGのp1/p2を自動で使う）")]
    public bool autoAssign = true;
    public Player p1;
    public Player p2;

    [Header("表示設定")]
    public bool visible = true;
    public int maxFrames = 80;          // 横に並べるフレーム数
    public float cellSize = 12f;        // 1フレームのマスの大きさ
    public float cellGap = 1f;          // マス同士の隙間
    public float groupGap = 4f;         // 5フレームごとの追加の隙間（数えやすくする）
    public int fontSize = 18;
    [Range(0f, 1f)] public float yRatio = 0.80f; // 画面の上から何割の位置に出すか
    [Tooltip("両者が何もしていない状態がこのフレーム数続いたら、次の行動が始まった時に表示をリセットする")]
    public int holdIdleFrames = 30;

    [Header("画像（未設定の項目は色の四角で代用される）")]
    public Texture2D texFrame;     // 黒枠
    public Texture2D texIdle;      // グレー箱（待機）
    public Texture2D texStartup;   // 緑箱（発生）
    public Texture2D texActive;    // 赤箱（当たり判定ON）
    public Texture2D texRecovery;  // 青箱（硬直）
    public Texture2D texStun;      // 黄箱（やられ）
    public Texture2D texExtra;     // 紫箱（今は未使用。用途が決まったら使う）
    public bool showText = true;   // 「発生/全体/硬直差」の文字を出すか

    static readonly Color cIdle     = new Color(0.55f, 0.55f, 0.55f);
    static readonly Color cStartup  = new Color(0.10f, 0.85f, 0.45f);
    static readonly Color cActive   = new Color(1.00f, 0.20f, 0.25f);
    static readonly Color cRecovery = new Color(0.20f, 0.50f, 1.00f);
    static readonly Color cStun     = new Color(1.00f, 0.95f, 0.10f);
    static readonly Color cEmpty    = new Color(0.12f, 0.12f, 0.12f);

    readonly List<MeterState> log1 = new List<MeterState>();
    readonly List<MeterState> log2 = new List<MeterState>();
    GameMNG gm;
    int lastFrame = -1;
    int idleCount;
    GUIStyle style;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f11Key.wasPressedThisFrame)
            visible = !visible;

        if (autoAssign)
        {
            if (gm == null) gm = FindAnyObjectByType<GameMNG>();
            if (gm != null) { p1 = gm.p1; p2 = gm.p2; }
        }
    }

    // Playerが論理フレームを進めた後（全PlayerのUpdate後）に記録する
    void LateUpdate()
    {
        int now = Player.CurrentFrame;
        if (lastFrame < 0) lastFrame = now;
        int steps = Mathf.Min(now - lastFrame, 4); // 描画が極端に遅い時の暴走防止
        lastFrame = now;

        if (p1 == null || p2 == null) return;
        for (int i = 0; i < steps; i++) Record();
    }

    void Record()
    {
        // ヒットストップ中・演出中は、SF6と同じくメーターを進めない
        if (p1.IsHitStopping || p2.IsHitStopping) return;
        if (Player.IsActionFrozen) return;

        MeterState s1 = p1.CurrentMeterState;
        MeterState s2 = p2.CurrentMeterState;

        if (s1 == MeterState.Idle && s2 == MeterState.Idle)
        {
            idleCount++;
            if (idleCount > holdIdleFrames) return; // 結果を表示したまま保持
        }
        else
        {
            if (idleCount > holdIdleFrames) { log1.Clear(); log2.Clear(); } // 新しい攻防が始まったのでリセット
            idleCount = 0;
        }

        log1.Add(s1);
        log2.Add(s2);
        if (log1.Count > maxFrames) { log1.RemoveAt(0); log2.RemoveAt(0); }
    }

    //------------------------------------------------------
    // 描画
    //------------------------------------------------------
    void OnGUI()
    {
        if (!visible || p1 == null || p2 == null) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true };
            style.normal.textColor = Color.white;
        }

        float pitch = cellSize + cellGap;
        float rowWidth = maxFrames * pitch + (maxFrames / 5) * groupGap;
        float textH = fontSize + 8f;
        float rowH = cellSize + 10f;
        float pad = 8f;

        float x = (Screen.width - rowWidth) * 0.5f;
        float y = Screen.height * yRatio;
        float totalH = textH * 3f + rowH * 2f + pad * 2f;

        // 一番後ろの黒い枠
        DrawRect(new Rect(x - pad, y - pad, rowWidth + pad * 2f, totalH), new Color(0f, 0f, 0f, 0.9f));

        // 色の凡例
        DrawLabel(new Rect(x, y, rowWidth, textH),
            "<color=#8c8c8c>■灰 待機</color>  <color=#1ad973>■緑 発生</color>  <color=#ff3340>■赤 判定ON</color>  " +
            "<color=#3380ff>■青 硬直</color>  <color=#fff21a>■黄 やられ</color>", style);
        y += textH;

        // P1
        DrawLabel(new Rect(x, y, rowWidth, textH), "P1  " + BuildInfo(log1, log2), style);
        y += textH;
        DrawRow(x, y, log1, pitch);
        y += rowH;

        // P2
        DrawRow(x, y, log2, pitch);
        y += rowH;
        DrawLabel(new Rect(x, y, rowWidth, textH), "P2  " + BuildInfo(log2, log1), style);
    }

    void DrawLabel(Rect r, string text, GUIStyle st)
    {
        if (showText) GUI.Label(r, text, st);
    }

    void DrawRow(float x, float y, List<MeterState> log, float pitch)
    {
        float rowWidth = maxFrames * pitch + (maxFrames / 5) * groupGap;
        if (texFrame != null)
            GUI.DrawTexture(new Rect(x - 4f, y - 4f, rowWidth + 8f, cellSize + 8f), texFrame, ScaleMode.StretchToFill);

        for (int i = 0; i < maxFrames; i++)
        {
            float cx = x + i * pitch + (i / 5) * groupGap;
            Rect cell = new Rect(cx, y, cellSize, cellSize);
            if (i < log.Count) DrawCell(cell, log[i]);
            else if (texFrame == null) DrawRect(cell, cEmpty); // 黒枠画像がある時は空きマスは黒枠のまま見せる
        }
    }

    void DrawCell(Rect r, MeterState st)
    {
        Texture2D tex = null;
        switch (st)
        {
            case MeterState.Startup:  tex = texStartup;  break;
            case MeterState.Active:   tex = texActive;   break;
            case MeterState.Recovery: tex = texRecovery; break;
            case MeterState.Stun:     tex = texStun;     break;
            default:                  tex = texIdle;     break;
        }
        if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
        else DrawRect(r, ColorOf(st));
    }

    static void DrawRect(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    static Color ColorOf(MeterState s)
    {
        switch (s)
        {
            case MeterState.Startup:  return cStartup;
            case MeterState.Active:   return cActive;
            case MeterState.Recovery: return cRecovery;
            case MeterState.Stun:     return cStun;
            default:                  return cIdle;
        }
    }

    //------------------------------------------------------
    // 発生 / 全体 / 硬直差 の計算
    //------------------------------------------------------
    static int FirstAction(List<MeterState> l)
    {
        for (int i = 0; i < l.Count; i++) if (l[i] != MeterState.Idle) return i;
        return -1;
    }

    static int LastAction(List<MeterState> l)
    {
        for (int i = l.Count - 1; i >= 0; i--) if (l[i] != MeterState.Idle) return i;
        return -1;
    }

    static int FirstActive(List<MeterState> l)
    {
        for (int i = 0; i < l.Count; i++) if (l[i] == MeterState.Active) return i;
        return -1;
    }

    static string BuildInfo(List<MeterState> self, List<MeterState> other)
    {
        int start = FirstAction(self);
        int act = FirstActive(self);
        int lastSelf = LastAction(self);
        int lastOther = LastAction(other);

        // 硬直差 = 相手が動けるようになるフレーム − 自分が動けるようになるフレーム（プラスなら自分が先に動ける）
        string diff = (lastSelf >= 0 && lastOther >= 0)
            ? string.Format("{0:+0;-0;±0}F", lastOther - lastSelf)
            : "--";

        if (act < 0) return "発生 -- / 全体 -- / 硬直差 " + diff; // 攻撃していない側
        return "発生 " + (act - start + 1) + "F / 全体 " + (lastSelf - start + 1) + "F / 硬直差 " + diff;
    }
}
