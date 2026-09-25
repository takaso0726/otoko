using System;
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
