using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMNG : MonoBehaviour
{
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

    [Header("漢気ゲージ表示(敵側・2本分)")]
    [Tooltip("Sliderのmin=0, max=1で設定してください(GetGaugeFillRatioが0〜1を返すため)")]
    public Slider P1_KankiGaugeBar; // player1の漢気ゲージ1本目
    public Slider P2_KankiGaugeBar; // player2の漢気ゲージ1本目
    public Slider E_KankiGaugeBar1; // 敵の漢気ゲージ1本目
    public Slider E_KankiGaugeBar2; // 敵の漢気ゲージ2本目

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

            case "InGame1V1":
                // HPバーを現在出ているキャラクターに設定する
                if (E_HPbar != null && p2 != null) E_HPbar.value = p2.HP;

                // プレイヤーのステータスをLiveにする
                player2Status = Player.Status.Live;
                break;
        }

        // HPバーを現在出ているキャラクターに設定する
        if (P_HPbar != null && p1 != null) P_HPbar.value = p1.HP;

        // プレイヤーのステータスをLiveにする
        player1Status = Player.Status.Live;

        // 漢気ゲージUIの初期化(0本分の状態から開始)
        // ※Enemy_UpdateKankiGauge()はe1(Enemy)を参照するため、e1が存在するとき(対CPU戦)のみ呼ぶ。
        //   InGame1V1(対人戦)ではe1が未設定のため、無条件に呼ぶとエラーになる。
        if (e1 != null) Enemy_UpdateKankiGauge();
        Player_UpdateKankiGauge();

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

            case "InGame1V1":
                CheckGameResult(player1Status);
                break;
        }
    }

    //=======================================================================
    // 勝敗チェック＆シーン遷移タイマー処理（Updateから呼出）
    //=======================================================================
    private void CheckGameResult(Player.Status status)
    {
        // 状態がDeadならゲームオーバーシーンを読み込む
        if (status == Player.Status.Dead)
        {
            playerChangeTimer += Time.deltaTime; // 経過時間を加える
            if (playerChangeTimer >= gameOverTime)
            {
                SceneManager.LoadScene("GameOver");
                playerChangeTimer = 0.0f; // 経過時間をリセット
            }
        }
        // 状態がWinならゲームクリアシーンを読み込む
        else if (status == Player.Status.Win)
        {
            playerChangeTimer += Time.deltaTime; // 経過時間を加える
            if (playerChangeTimer >= gameOverTime)
            {
                SceneManager.LoadScene("GameClear");
                playerChangeTimer = 0.0f; // 経過時間をリセット
            }
        }
    }

    //=======================================================================
    // UI更新処理
    //=======================================================================

    // プレイヤーのHPを表示
    public void Player_ReduceHP(int hp, string PlayerName)
    {
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
        if (E_KankiGaugeBar1 == null || E_KankiGaugeBar2 == null)
        {
            Debug.LogError("GameMNGのE_KankiGaugeBar1またはE_KankiGaugeBar2がInspectorで未設定です。漢気ゲージ用のSliderをアサインしてください。");
            return;
        }
        // 1本目・2本目それぞれの充填率(0〜1)をSliderへ反映
        E_KankiGaugeBar1.value = e1.GetGaugeFillRatio(0);
        E_KankiGaugeBar2.value = e1.GetGaugeFillRatio(1);
    }

    // Playerの漢気ゲージ(両方)を表示更新する
    // Enemy.csのAddKankiGauge/ReduceKankiGaugeが呼ばれた際に呼び出される想定
    public void Player_UpdateKankiGauge()
    {
        if (p1 == null)
        {
            Debug.LogError("GameMNGのp1（Player）がInspectorで未設定です。Playerオブジェクトをアサインしてください。");
            return;
        }
        if (P1_KankiGaugeBar == null || P2_KankiGaugeBar == null)
        {
            Debug.LogError("GameMNGのP1_KankiGaugeBarまたはP2_KankiGaugeBarがInspectorで未設定です。漢気ゲージ用のSliderをアサインしてください。");
            return;
        }
        // 1本目・2本目それぞれの充填率(0〜1)をSliderへ反映
        P1_KankiGaugeBar.value = p1.GetGaugeFillRatio(0);
        // p2は1v1モードでのみ使用するため、対CPU戦（InGame）などp2未設定の構成ではスキップする
        if (p2 != null)
        {
            P2_KankiGaugeBar.value = p2.GetGaugeFillRatio(0);
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