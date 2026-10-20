using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMNG : MonoBehaviour
{
    // ★追加：プレイヤーのHPが減った（＝攻撃が当たった）瞬間に、外部（観客スクリプト等）へ
    //   通知するイベント。引数は「被弾したプレイヤーのPlayerName」「減少後のHP」。
    //   ガード成功時もPlayer_ReduceHP経由でここを通るため、呼び出し側でガード反応と
    //   同一フレームかどうかを見て使い分けることを想定している。
    public event Action<string, int> OnPlayerHpReduced;

    //=======================================================================
    //【Character】プレイヤー・エネミー
    //=======================================================================
    public Player p1;   // プレイヤー1
    public Player p2;   // プレイヤー2
    public Enemy e1;   // 敵1

    //=======================================================================
    //【UI】HPバー・男気ゲージのバー（現在は２本しか対応していない。）
    //=======================================================================
    [Header("HPゲージ表示（プレイヤー2本文or敵、プレイヤー1本ずつ文）")]
    public Slider P_HPbar;          // 1人目のHPゲージ
    public Slider E_HPbar;          // 2人目/敵のHPゲージ

    // ★追加：HPバーの自動アサイン（SetPlayers呼び出し時＝キャラ生成直後に実行）
    //   P_HPbar / E_HPbar がInspectorで未設定なら、シーン内のSliderから自動で探して設定する。
    //   既にアサイン済みのものは変更しない。
    [Header("HPバーの自動アサイン（キャラ生成時）")]
    [Tooltip("ONにすると、P_HPbar/E_HPbarが未設定の場合にシーンから自動で探してアサインします。")]
    public bool autoAssignHPBars = true;
    [Tooltip("HPバーのSliderを探す時の名前キーワード（大文字小文字は無視）。この文字を名前に含むSliderが候補になります。")]
    public string hpBarNameKeyword = "hp";
    [Tooltip("ONにすると、HPバーのmin=0 / max=生成したプレイヤーのmaxHP に自動で合わせます。")]
    public bool autoSetHPBarMax = true;

    // ★追加：漢気ゲージ(KankiGaugeCircle)の自動アサイン（SetPlayers呼び出し時＝キャラ生成直後に実行）
    //   P1/P2用の4本のうち未設定のものを、シーン内のKankiGaugeCircleから自動で探して設定する。
    //   既にアサイン済みのものは変更しない。敵用（E_KankiGaugeCircle）は対象外。
    [Header("漢気ゲージ（円形）の自動アサイン（キャラ生成時）")]
    [Tooltip("ONにすると、P1/P2のKankiGaugeCircleが未設定の場合にシーンから自動で探してアサインします。")]
    public bool autoAssignKankiGauges = true;

    // ★追加：敵（Enemy）の自動アサイン（Start時／SetEnemy呼び出し時に実行）
    //   e1（Enemy）・E_HPbar・E_KankiGaugeCircle1/2 のうち未設定のものを、シーンから自動で探して設定する。
    //   既にアサイン済みのものは変更しない。対人戦などシーンにEnemyがいない場合は何もしない。
    [Header("敵（Enemy）の自動アサイン")]
    [Tooltip("ONにすると、e1（Enemy）と、敵用のHPバー・漢気ゲージが未設定の場合にシーンから自動で探してアサインします。")]
    public bool autoAssignEnemy = true;

    // ★変更：バー式Sliderから円形ゲージ(KankiGaugeCircle)へ変更。
    //   各フィールドにはHierarchy上のKankiGaugeCircle_XXXオブジェクト（FrameImage/ArcImage/MaxImageを
    //   持つGameObject）をアサインすること。値の受け渡しは.value=ではなく.SetRatio()経由になる。
    // ★修正：P1/P2も漢気ゲージは2本分あるため、Enemyと同様に1本目・2本目の両方を用意する。
    [Header("漢気ゲージ表示(円形・P1/P2/敵、いずれも2本分)")]
    public KankiGaugeCircle P1_KankiGaugeCircle1; // player1の漢気ゲージ1本目
    public KankiGaugeCircle P1_KankiGaugeCircle2; // player1の漢気ゲージ2本目
    public KankiGaugeCircle P2_KankiGaugeCircle1; // player2の漢気ゲージ1本目
    public KankiGaugeCircle P2_KankiGaugeCircle2; // player2の漢気ゲージ2本目
    public KankiGaugeCircle E_KankiGaugeCircle1;  // 敵の漢気ゲージ1本目
    public KankiGaugeCircle E_KankiGaugeCircle2;  // 敵の漢気ゲージ2本目

    // ★追加：旧バー式（Slider）ゲージとの互換用。
    //   円形ゲージへ移行済みだが、デバッグ比較や旧UIを残したいシーン用に、
    //   Inspectorのスイッチ(enableLegacyKankiGaugeBar)をONにするとこちらも同時に更新される。
    //   Sliderをアサインしていない・スイッチOFFの場合は何もしない（エラーにはならない）。
    [Header("旧バー式ゲージ（互換用・下のスイッチでON/OFF切替）")]
    [Tooltip("ONにすると、円形ゲージに加えて下のSlider（旧バー式ゲージ）も同時に更新します。未使用ならOFFのままでOKです。")]
    public bool enableLegacyKankiGaugeBar = false;
    [Tooltip("Sliderのmin=0, max=1で設定してください(GetGaugeFillRatioが0〜1を返すため)")]
    public Slider P1_KankiGaugeBar1; // player1の漢気ゲージ1本目（旧）
    public Slider P1_KankiGaugeBar2; // player1の漢気ゲージ2本目（旧）
    public Slider P2_KankiGaugeBar1; // player2の漢気ゲージ1本目（旧）
    public Slider P2_KankiGaugeBar2; // player2の漢気ゲージ2本目（旧）
    public Slider E_KankiGaugeBar1;  // 敵の漢気ゲージ1本目（旧）
    public Slider E_KankiGaugeBar2;  // 敵の漢気ゲージ2本目（旧）

    //=======================================================================
    // カメラ
    //=======================================================================
    [Header("勝敗カメラ演出")]
    public Transform PlayerTransform;                   // プレイヤーのTransform（勝利時にカメラがズームする対象）
    public Transform EnemyTransform;                    // 敵のTransform（プレイヤー敗北＝敵勝利時にカメラがズームする対象）
    public FightingCameraController cameraController;   // シーン中のカメラコントローラー

    //=======================================================================
    // BGM
    //=======================================================================
    [Header("ゲーム中に流れるBGM")]
    AudioSource BGM_Lv1;            // 音を鳴らすためのスピーカー
    public AudioClip BGM;           // ゲーム中に流すBGM

    //=======================================================================
    // シーン遷移先
    //=======================================================================
    [Header("シーン遷移先（Inspectorで変更可）")]
    [SerializeField] string gameOverSceneName = "GameOver";               // InGame（エネミー戦）敗北時
    [SerializeField] string gameClearSceneName = "GameClear";             // InGame（エネミー戦）勝利時
    [SerializeField] string inGame1v1ResultSceneName = "Judgment"; // InGame1v1（対人戦）の勝敗結果画面

    //=========================================
    // 内部処理変数
    //=========================================
    public float gameOverTime;      // ゲームオーバーに移行するまでの時間
    float playerChangeTimer;        // プレイヤーが倒されてからの経過時間

    Player.Status player1Status;    // プレイヤー1の状態管理変数
    Player.Status player2Status;    // プレイヤー2の状態管理変数

    string currentScene = null;     // 現在のシーン名

    //-----------------------------------------------------------------------
    // 初期化
    //-----------------------------------------------------------------------
    void Start()
    {
        // 現在アクティブなシーンの名前を取得
        currentScene = SceneManager.GetActiveScene().name;
        Debug.Log("現在のシーン名: " + currentScene);

        // ★追加：敵（Enemy）と、敵用のHPバー・漢気ゲージを自動でアサインする
        AutoAssignEnemy();

        // シーン名に応じて表示/非表示を切り替え
        switch (currentScene)
        {
            case "InGame":
                // HPバーを現在出ているキャラクターに設定する
                if (E_HPbar != null && e1 != null) E_HPbar.value = e1.HP;
                break;

            case "InGame1v1":
                // HPバーを現在出ているキャラクターに設定する
                if (E_HPbar != null && p2 != null) E_HPbar.value = p2.HP;

                // プレイヤーのステータスをLiveにする
                player2Status = Player.Status.Live;

                // 前回対戦の結果が残っていると次の結果画面に誤表示されるため、
                // 対戦開始時に必ずリセットしておく
                MatchResult.Reset();
                break;
        }

        // HPバーを現在出ているキャラクターに設定する
        if (P_HPbar != null && p1 != null) P_HPbar.value = p1.HP;

        // プレイヤーのステータスをLiveにする
        player1Status = Player.Status.Live;

        // 漢気ゲージUIの初期化(0本分の状態から開始)
        Enemy_UpdateKankiGauge();

        // プレイヤーの状態経過時間タイマー
        playerChangeTimer = 0.0f;

        // 効果音再生用のAudioClipを取得・再生設定
        BGM_Lv1 = GetComponent<AudioSource>();
        if (BGM_Lv1 != null)
        {
            if (BGM != null) BGM_Lv1.clip = BGM;
            BGM_Lv1.loop = true;

            // BGM再生
            BGM_Lv1.Play();
        }
    }

    //-----------------------------------------------------------------------
    // 更新処理
    //-----------------------------------------------------------------------
    void Update()
    {
        // キーボードが接続されているかチェック
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            // F1 キーが押された瞬間を検知
            OnDebugF1KeyPressed();
        }

        // シーン名に応じて勝敗チェック
        switch (currentScene)
        {
            case "InGame":
                CheckGameResult(player1Status);
                CheckGameResult(player2Status);
                break;

            case "InGame1v1":
                // 1v1はplayer1Status・player2Statusの両方を見て勝敗を判定する
                CheckGameResult1V1();
                break;
        }
    }

    //=======================================================================
    // 勝敗チェック＆シーン遷移タイマー処理（Updateから呼出：InGame＝エネミー戦用）
    //=======================================================================
    private void CheckGameResult(Player.Status status)
    {
        // 状態がDeadならゲームオーバーシーンを読み込む
        if (status == Player.Status.Dead)
        {
            playerChangeTimer += Time.deltaTime; // 経過時間を加える
            if (playerChangeTimer >= gameOverTime)
            {
                SceneManager.LoadScene(gameOverSceneName);
                playerChangeTimer = 0.0f; // 経過時間をリセット
            }
        }
        // 状態がWinならゲームクリアシーンを読み込む
        else if (status == Player.Status.Win)
        {
            playerChangeTimer += Time.deltaTime; // 経過時間を加える
            if (playerChangeTimer >= gameOverTime)
            {
                SceneManager.LoadScene(gameClearSceneName);
                playerChangeTimer = 0.0f; // 経過時間をリセット
            }
        }
    }

    //=======================================================================
    // 勝敗チェック＆シーン遷移タイマー処理（Updateから呼出：InGame1v1＝対人戦用）
    //
    // player1Status・player2Statusのそれぞれを見て、どちらかがDead（敗北）に
    // なった時点で、もう一方の勝ちとしてMatchResultに書き込み、結果画面へ遷移する。
    // ※ SettestStatus(string playerName, Player.Status ps) 経由で、
    //    死んだ本人のPlayerNameに対応するステータスだけが更新される前提。
    //=======================================================================
    private void CheckGameResult1V1()
    {
        if (player1Status == Player.Status.Dead)
        {
            playerChangeTimer += Time.deltaTime;
            if (playerChangeTimer >= gameOverTime)
            {
                MatchResult.LastWinner = MatchResult.Winner.Player2; // 1Pが敗北＝2Pの勝ち
                SceneManager.LoadScene(inGame1v1ResultSceneName);
                playerChangeTimer = 0.0f;
            }
        }
        else if (player2Status == Player.Status.Dead)
        {
            playerChangeTimer += Time.deltaTime;
            if (playerChangeTimer >= gameOverTime)
            {
                MatchResult.LastWinner = MatchResult.Winner.Player1; // 2Pが敗北＝1Pの勝ち
                SceneManager.LoadScene(inGame1v1ResultSceneName);
                playerChangeTimer = 0.0f;
            }
        }
    }

    //=======================================================================
    // UI更新処理
    //=======================================================================

    // プレイヤーのHPを表示
    public void Player_ReduceHP(int hp, string PlayerName)
    {
        // ★追加：外部へ「攻撃が当たってHPが減った」ことを通知
        OnPlayerHpReduced?.Invoke(PlayerName, hp);

        if (PlayerName == "P1") P1_ReduceHP(hp);
        if (PlayerName == "P2") P2_ReduceHP(hp);
    }

    // p1側のHPの表示を更新する
    public void P1_ReduceHP(int hp)
    {
        if (P_HPbar == null || p1 == null)
        {
            Debug.LogError("GameMNGのP_HPbarまたはp1がInspectorで未設定です。");
            return;
        }
        P_HPbar.value = p1.HP;
    }

    // p2側のHPの表示を更新する
    public void P2_ReduceHP(int hp)
    {
        if (E_HPbar == null || p2 == null)
        {
            Debug.LogError("GameMNGのE_HPbarまたはp2がInspectorで未設定です。");
            return;
        }
        E_HPbar.value = p2.HP;
    }

    // エネミー側のHPを表示する
    public void Enemy_ReduceHP(int hp)
    {
        if (E_HPbar == null)
        {
            Debug.LogError("GameMNGのE_HPbarがInspectorで未設定です。HPバーのSliderをアサインしてください。");
            return;
        }
        if (e1 == null)
        {
            Debug.LogError("GameMNGのe1（Enemy）がInspectorで未設定です。Enemyオブジェクトをアサインしてください。");
            return;
        }
        E_HPbar.value = e1.HP;
    }

    // エネミー側の漢気ゲージ(2本分)を表示更新する
    // Enemy.csのAddKankiGauge/ReduceKankiGaugeが呼ばれた際に呼び出される想定
    public void Enemy_UpdateKankiGauge()
    {
        if (e1 == null)
        {
            Debug.LogError("GameMNGのe1（Enemy）がInspectorで未設定です。Enemyオブジェクトをアサインしてください。");
            return;
        }
        if (E_KankiGaugeCircle1 == null || E_KankiGaugeCircle2 == null)
        {
            Debug.LogError("GameMNGのE_KankiGaugeCircle1またはE_KankiGaugeCircle2がInspectorで未設定です。漢気ゲージ用のKankiGaugeCircleをアサインしてください。");
            return;
        }
        // 1本目・2本目それぞれの充填率(0〜1)を円形ゲージへ反映
        E_KankiGaugeCircle1.SetRatio(e1.GetGaugeFillRatio(0));
        E_KankiGaugeCircle2.SetRatio(e1.GetGaugeFillRatio(1));

        // ★追加：Inspectorのスイッチ(enableLegacyKankiGaugeBar)がONの時だけ、
        //   旧バー式ゲージ(Slider)も同時に更新する。Sliderが未アサインでもエラーにはせずスキップする。
        if (enableLegacyKankiGaugeBar)
        {
            if (E_KankiGaugeBar1 != null) E_KankiGaugeBar1.value = e1.GetGaugeFillRatio(0);
            if (E_KankiGaugeBar2 != null) E_KankiGaugeBar2.value = e1.GetGaugeFillRatio(1);
        }
    }

    // プレイヤー側(1P・2P)の漢気ゲージ(2本分ずつ)を表示更新する
    // Player.csのAddKankiGauge/ReduceKankiGaugeが呼ばれた際に呼び出される想定。
    // p1・p2のどちらが呼び出した場合でも、両方のゲージをまとめて最新値に更新する
    // （Enemy_UpdateKankiGauge()と同じ設計）。
    public void Player_UpdateKankiGauge()
    {
        if (p1 != null)
        {
            if (P1_KankiGaugeCircle1 != null) P1_KankiGaugeCircle1.SetRatio(p1.GetGaugeFillRatio(0));
            if (P1_KankiGaugeCircle2 != null) P1_KankiGaugeCircle2.SetRatio(p1.GetGaugeFillRatio(1));
        }
        if (p2 != null)
        {
            if (P2_KankiGaugeCircle1 != null) P2_KankiGaugeCircle1.SetRatio(p2.GetGaugeFillRatio(0));
            if (P2_KankiGaugeCircle2 != null) P2_KankiGaugeCircle2.SetRatio(p2.GetGaugeFillRatio(1));
        }

        // ★追加：Inspectorのスイッチ(enableLegacyKankiGaugeBar)がONの時だけ、
        //   旧バー式ゲージ(Slider)も同時に更新する。Sliderが未アサインでもエラーにはせずスキップする。
        if (enableLegacyKankiGaugeBar)
        {
            if (p1 != null)
            {
                if (P1_KankiGaugeBar1 != null) P1_KankiGaugeBar1.value = p1.GetGaugeFillRatio(0);
                if (P1_KankiGaugeBar2 != null) P1_KankiGaugeBar2.value = p1.GetGaugeFillRatio(1);
            }
            if (p2 != null)
            {
                if (P2_KankiGaugeBar1 != null) P2_KankiGaugeBar1.value = p2.GetGaugeFillRatio(0);
                if (P2_KankiGaugeBar2 != null) P2_KankiGaugeBar2.value = p2.GetGaugeFillRatio(1);
            }
        }
    }

    // ★追加：GameInputManagerなどから、実行時に生成したP1/P2（プレハブ）とカメラを登録するための公開メソッド。
    //   プレハブはシーン上のオブジェクトを参照できないため、p1/p2をInspectorで設定していても
    //   生成されたプレイヤーとは別物になる。生成後にこのメソッドで差し替え、
    //   HPバー・漢気ゲージの表示を生成したプレイヤーの現在値に合わせ直す。
    //   ・null を渡した引数は、既存の設定を変更しない。
    //   ・HPバー/漢気ゲージのUI自体（Slider/KankiGaugeCircle）はシーン上のものをそのまま使う。
    public void SetPlayers(Player player1, Player player2, FightingCameraController camera = null)
    {
        if (player1 != null)
        {
            p1 = player1;
            PlayerTransform = player1.transform; // 勝利時カメラのズーム対象
        }
        if (player2 != null)
        {
            p2 = player2;
            EnemyTransform = player2.transform;  // 1P敗北＝2P勝利時のズーム対象
        }
        if (camera != null) cameraController = camera;

        // ★追加：HPバーが未設定なら自動でアサインする
        AutoAssignHPBars();

        // HPバーを、生成したプレイヤーの最大HP・現在HPに合わせる
        SyncHpBar(P_HPbar, p1);
        SyncHpBar(E_HPbar, p2);

        // ★追加：漢気ゲージ(円形)が未設定なら自動でアサインする
        AutoAssignKankiGauges();

        // 漢気ゲージ(円形)を、生成したプレイヤーの現在値に合わせる
        Player_UpdateKankiGauge();
    }

    // ★追加：HPバーのSliderの範囲と現在値を、プレイヤーのmaxHP・HPに合わせる
    void SyncHpBar(Slider bar, Player player)
    {
        if (bar == null || player == null) return;
        if (autoSetHPBarMax && player.maxHP > 0)
        {
            bar.minValue = 0;
            bar.maxValue = player.maxHP;
        }
        bar.value = player.HP;
    }

    // ★追加：P_HPbar / E_HPbar が未設定なら、シーン内のSliderから自動で探してアサインする。
    //   1) 名前にhpBarNameKeyword（既定："hp"）を含むSliderを候補にする（漢気ゲージ用のSliderは除外）
    //   2) 名前に "P1"/"1P" を含む候補 → P_HPbar、"P2"/"2P" を含む候補 → E_HPbar
    //   3) まだ空いていれば、残りの候補を画面の左右の位置で振り分ける（左 → P_HPbar、右 → E_HPbar）
    void AutoAssignHPBars()
    {
        if (!autoAssignHPBars) return;
        if (P_HPbar != null && E_HPbar != null) return;

        string keyword = string.IsNullOrEmpty(hpBarNameKeyword) ? "hp" : hpBarNameKeyword.ToLowerInvariant();

        var candidates = new List<Slider>();
        foreach (var slider in FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (slider == P_HPbar || slider == E_HPbar) continue;
            if (IsLegacyKankiBar(slider)) continue;
            if (slider.name.ToLowerInvariant().Contains(keyword)) candidates.Add(slider);
        }

        // 2) 名前のヒントで振り分け
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            string n = candidates[i].name;
            if (P_HPbar == null && HasPlayerHint(n, 1))
            {
                P_HPbar = candidates[i];
                candidates.RemoveAt(i);
            }
            else if (E_HPbar == null && (HasPlayerHint(n, 2) || HasEnemyHint(n)))
            {
                E_HPbar = candidates[i];
                candidates.RemoveAt(i);
            }
        }

        // 3) 残りは画面上の左右の位置で振り分ける
        candidates.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        if (P_HPbar == null && candidates.Count > 0)
        {
            P_HPbar = candidates[0];
            candidates.RemoveAt(0);
        }
        if (E_HPbar == null && candidates.Count > 0)
        {
            E_HPbar = candidates[candidates.Count - 1];
            candidates.RemoveAt(candidates.Count - 1);
        }

        if (P_HPbar == null || E_HPbar == null)
        {
            Debug.LogWarning($"[GameMNG] HPバーを自動で見つけられませんでした（P_HPbar={(P_HPbar != null ? P_HPbar.name : "null")} / " +
                             $"E_HPbar={(E_HPbar != null ? E_HPbar.name : "null")}）。" +
                             $"名前に'{keyword}'を含むSliderを置くか、Inspectorで直接アサインしてください。");
        }
        else
        {
            Debug.Log($"[GameMNG] HPバーを自動アサイン：P_HPbar={P_HPbar.name} / E_HPbar={E_HPbar.name}");
        }
    }

    // 旧バー式の漢気ゲージ用Sliderかどうか（HPバーの自動探索から除外するため）
    bool IsLegacyKankiBar(Slider s)
    {
        return s == P1_KankiGaugeBar1 || s == P1_KankiGaugeBar2
            || s == P2_KankiGaugeBar1 || s == P2_KankiGaugeBar2
            || s == E_KankiGaugeBar1 || s == E_KankiGaugeBar2;
    }

    // ★追加：P1/P2の漢気ゲージ(KankiGaugeCircle)のうち未設定のものを、シーンから自動でアサインする。
    //   1) 自分と親の名前（階層パス）に "P1"/"1P" の目印を含むものはP1用、"P2"/"2P" を含むものはP2用にする
    //      （P1とP2の両方を含む／どちらも含まないものは、ここでは振り分けない）
    //   2) 各プレイヤー内では、オブジェクト名の昇順で1本目・2本目に割り当てる（..._1 → 1本目、..._2 → 2本目）
    //   3) 名前で振り分けられなかったものが、ちょうど4本だけ残っている場合に限り、
    //      画面の左側2本をP1、右側2本をP2とする（敵用など無関係なゲージの巻き込みを防ぐため、4本ちょうどの時のみ）
    void AutoAssignKankiGauges()
    {
        if (!autoAssignKankiGauges) return;

        bool allAssigned = P1_KankiGaugeCircle1 != null && P1_KankiGaugeCircle2 != null
                        && P2_KankiGaugeCircle1 != null && P2_KankiGaugeCircle2 != null;
        if (allAssigned) return;

        var p1Group = new List<KankiGaugeCircle>();
        var p2Group = new List<KankiGaugeCircle>();
        var others = new List<KankiGaugeCircle>();

        foreach (var circle in FindObjectsByType<KankiGaugeCircle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsAssignedKankiCircle(circle)) continue;

            string path = GetHierarchyPath(circle.transform);
            bool isP1 = HasPlayerHint(path, 1);
            bool isP2 = HasPlayerHint(path, 2);
            if (!isP1 && !isP2 && HasEnemyHint(path)) continue; // 敵用は AutoAssignEnemy 側で扱う

            if (isP1 && !isP2) p1Group.Add(circle);
            else if (isP2 && !isP1) p2Group.Add(circle);
            else others.Add(circle);
        }

        // 3) 名前で振り分けられなかったものが、ちょうど4本の時だけ左右の位置で振り分ける
        if (others.Count == 4 && p1Group.Count == 0 && p2Group.Count == 0)
        {
            others.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            p1Group.Add(others[0]); p1Group.Add(others[1]);
            p2Group.Add(others[2]); p2Group.Add(others[3]);
        }

        // 2) 名前の昇順で1本目・2本目に割り当てる
        Comparison<KankiGaugeCircle> byName = (a, b) => string.CompareOrdinal(a.name, b.name);
        p1Group.Sort(byName);
        p2Group.Sort(byName);

        AssignNextKankiCircle(ref P1_KankiGaugeCircle1, p1Group);
        AssignNextKankiCircle(ref P1_KankiGaugeCircle2, p1Group);
        AssignNextKankiCircle(ref P2_KankiGaugeCircle1, p2Group);
        AssignNextKankiCircle(ref P2_KankiGaugeCircle2, p2Group);

        if (P1_KankiGaugeCircle1 == null || P1_KankiGaugeCircle2 == null
            || P2_KankiGaugeCircle1 == null || P2_KankiGaugeCircle2 == null)
        {
            Debug.LogWarning("[GameMNG] 漢気ゲージ(KankiGaugeCircle)を一部自動で見つけられませんでした。" +
                             "名前（または親の名前）に P1 / P2 を含めるか、Inspectorで直接アサインしてください。");
        }
        else
        {
            Debug.Log($"[GameMNG] 漢気ゲージを自動アサイン：P1=({P1_KankiGaugeCircle1.name}, {P1_KankiGaugeCircle2.name}) / " +
                      $"P2=({P2_KankiGaugeCircle1.name}, {P2_KankiGaugeCircle2.name})");
        }
    }

    // 空いているスロットに、グループの先頭の1本を割り当てる（既にアサイン済みのスロットは何もしない）
    static void AssignNextKankiCircle(ref KankiGaugeCircle slot, List<KankiGaugeCircle> group)
    {
        if (slot != null || group.Count == 0) return;
        slot = group[0];
        group.RemoveAt(0);
    }

    // 既にどこかのスロット（敵用を含む）にアサイン済みのKankiGaugeCircleか
    bool IsAssignedKankiCircle(KankiGaugeCircle c)
    {
        return c == P1_KankiGaugeCircle1 || c == P1_KankiGaugeCircle2
            || c == P2_KankiGaugeCircle1 || c == P2_KankiGaugeCircle2
            || c == E_KankiGaugeCircle1 || c == E_KankiGaugeCircle2;
    }

    // ルートから自分までの名前をつないだ文字列。親の名前（例：P1_UI）も振り分けのヒントにする
    static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // 名前の中に「プレイヤー番号の目印（P1 / 1P など）」があるか。
    //   "Group1" のように単語の途中に偶然 "p1" が現れるものは除外する。
    //   ・小文字／大文字どちらでも可（p1_hp, P1_HP）だが、直前が英字の場合は大文字の "P" のみ可（HPbarP1 などのキャメルケース）
    //   ・直後が数字（P10など）の場合は別物として除外
    static bool HasPlayerHint(string text, int playerNumber)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string n = playerNumber.ToString();
        string pattern = $@"(?<![A-Za-z])([Pp]{n}|{n}[Pp])(?![0-9])|(?<=[a-z])(P{n}|{n}P)(?![0-9a-z])";
        return Regex.IsMatch(text, pattern);
    }

    // ★追加：外部（Enemyを生成するスクリプト等）から、生成したEnemyを登録するための公開メソッド。
    //   e1を差し替えたうえで、敵用のHPバー・漢気ゲージも自動アサインし、表示を現在値に合わせる。
    public void SetEnemy(Enemy enemy)
    {
        if (enemy != null)
        {
            e1 = enemy;
            EnemyTransform = enemy.transform; // プレイヤー敗北＝敵勝利時のズーム対象
        }
        AutoAssignEnemy();
        if (e1 != null)
        {
            if (E_HPbar != null) E_HPbar.value = e1.HP;
            Enemy_UpdateKankiGauge();
        }
    }

    // ★追加：敵（Enemy）まわりの未設定項目を自動でアサインする。
    //   1) e1（Enemy）が未設定なら、シーン内のEnemyを探して設定する（見つからなければ何もしない）
    //   2) Enemyがいる場合のみ、E_HPbar（名前に Enemy / E_ を含むSlider）・
    //      E_KankiGaugeCircle1/2（自分か親の名前に Enemy / E_ を含むもの。名前の昇順で1本目・2本目）を探す
    void AutoAssignEnemy()
    {
        if (!autoAssignEnemy) return;

        if (e1 == null)
        {
            Enemy found = FindAnyObjectByType<Enemy>(FindObjectsInactive.Include);
            if (found != null)
            {
                e1 = found;
                if (EnemyTransform == null) EnemyTransform = found.transform;
                Debug.Log($"[GameMNG] Enemyを自動アサイン：e1={found.name}");
            }
        }

        // シーンにEnemyがいない（対人戦など）なら、敵用UIの探索はしない
        if (e1 == null) return;

        // 敵用のHPバー（P_HPbar/E_HPbarの自動アサインと同じ処理。名前に Enemy / E_ を含むものがE_HPbarになる）
        AutoAssignHPBars();

        // 敵用の漢気ゲージ
        if (E_KankiGaugeCircle1 != null && E_KankiGaugeCircle2 != null) return;

        var group = new List<KankiGaugeCircle>();
        foreach (var circle in FindObjectsByType<KankiGaugeCircle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsAssignedKankiCircle(circle)) continue;

            string path = GetHierarchyPath(circle.transform);
            if (HasEnemyHint(path) && !HasPlayerHint(path, 1) && !HasPlayerHint(path, 2)) group.Add(circle);
        }
        group.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        AssignNextKankiCircle(ref E_KankiGaugeCircle1, group);
        AssignNextKankiCircle(ref E_KankiGaugeCircle2, group);

        if (E_KankiGaugeCircle1 == null || E_KankiGaugeCircle2 == null)
        {
            Debug.LogWarning("[GameMNG] 敵用の漢気ゲージ(KankiGaugeCircle)を一部自動で見つけられませんでした。" +
                             "名前（または親の名前）に Enemy または E_ を含めるか、Inspectorで直接アサインしてください。");
        }
    }

    // 名前の中に「敵の目印（Enemy / E_ ）」があるか。"Slider_Enemy" "E_HPbar" などが該当。
    static bool HasEnemyHint(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return Regex.IsMatch(text, @"(?i:enemy)|(?<![A-Za-z])E_");
    }

    // ド根性復活のタイマーとカウントを表示する
    public void PlayerUI(float Timer, int Cnt)
    {
        // 今後実装予定
    }

    //=======================================================================
    // ステータス・カメラ制御
    //=======================================================================

    // 他のC#スクリプトから呼び出す変数
    // 旧シグネチャ：呼び出し元が1P/2Pどちらでも player1Status を書き換える。
    // InGame（対エネミー戦）モードなど、プレイヤーが1人しかいない場面での互換性のために残している。
    public void SettestStatus(Player.Status ps)
    {
        player1Status = ps;

        // 勝敗が決まったら、勝った方にカメラをズームする
        if (cameraController != null)
        {
            if (player1Status == Player.Status.Win)
            {
                // プレイヤーが勝利
                cameraController.FocusOnTarget(PlayerTransform);
            }
            else if (player1Status == Player.Status.Dead)
            {
                // プレイヤーが敗北＝敵の勝利
                cameraController.FocusOnTarget(EnemyTransform);
            }
        }
    }

    // 新シグネチャ：PlayerNameで「どのプレイヤーの状態か」を明示する。
    // InGame1v1（1P vs 2P対戦）モードで、player.cs側から必ずこちらを呼ぶこと。
    // これが無いと、2Pが死んだ時にもplayer1Statusが上書きされてしまい、
    // 勝敗判定が逆転する不具合の原因になる。
    public void SettestStatus(string playerName, Player.Status ps)
    {
        if (playerName == "P1")
        {
            player1Status = ps;
        }
        else if (playerName == "P2")
        {
            player2Status = ps;
        }
        else
        {
            Debug.LogWarning($"[GameMNG] SettestStatus: 不明なPlayerName '{playerName}' が渡されました。PlayerNameがP1/P2に設定されているか確認してください。");
        }
    }

    //=======================================================================
    // デバッグ機能
    //=======================================================================

    // デバッグキーが押された時の処理
    void OnDebugF1KeyPressed()
    {
        Debug.Log("デバッグキー(F1)が押されました！");

        // ここにデバッグ用の処理を書く（例: HP全回復、アイテム付与など）
    }
}
