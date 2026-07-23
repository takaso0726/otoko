using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMNG : MonoBehaviour
{
    //変数宣言
    public Text PlayerHP_Text;
    public Text EnemyHP_Text;
   // public Text Player_Timer_Text;
    //public Text Player_Cnt_Text;

    //外部参照
    public Player p1;
    public Player p2;
    public Enemy  e1;

    public Slider P_HPbar;
    public Slider E_HPbar;

    float PTimer;
    int PCnt;

    AudioSource BGM_Lv1;
    public AudioClip BGM;

    //ゲームオーバーに移行するまでの時間
    public float gameOverTime;
    //プレイヤーが倒されてからの経過時間
    float playerChangeTimer;
    //プレイヤーの状態
    Player.Status player1Status;
    Player.Status player2Status;
    [Header("勝敗カメラ演出")]
    //プレイヤーのTransform（勝利時にカメラがズームする対象）
    public Transform PlayerTransform;
    //敵のTransform（プレイヤー敗北＝敵勝利時にカメラがズームする対象）
    public Transform EnemyTransform;
    //シーン中のカメラコントローラー
    public FightingCameraController cameraController;

    string currentScene = null;
    //初期化
    void Start()
    {
        // 現在アクティブなシーンの名前を取得
        currentScene = SceneManager.GetActiveScene().name;
        Debug.Log("現在のシーン名: " + currentScene);

        // シーン名に応じて表示/非表示を切り替え
        switch (currentScene)
        {
            case "InGame":

                //HPバーを現在出ているキャラクターに設定する
                E_HPbar.value = e1.HP;
                break;
            case "InGame1V1":

                //HPバーを現在出ているキャラクターに設定する
                E_HPbar.value = p2.HP;

                //プレイヤーのステータスをLiveにする
                player2Status = Player.Status.Live;
                break;
        }
        //HPバーを現在出ているキャラクターに設定する
        P_HPbar.value = p1.HP;

        //プレイヤーのステータスをLiveにする
        player1Status = Player.Status.Live;

        //プレイヤーの状態経過時間タイマー
        playerChangeTimer = 0.0f;
        
       
        //効果音再生用のAudioClipを取得
        BGM_Lv1 = GetComponent<AudioSource>();
        BGM_Lv1.loop = true;

        //BGM再生
        BGM_Lv1.Play();
    }

    //更新処理
    void Update()
    {

        // シーン名に応じて表示/非表示を切り替え
        switch (currentScene)
        {
            case "InGame":
                //Player1の状態がDeadなら
                if (player1Status == Player.Status.Dead)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameOver");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                //Player1の状態がWinなら
                else if (player1Status == Player.Status.Win)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameClear");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                //Player2の状態がDeadなら
                if (player2Status == Player.Status.Dead)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameOver");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                //Player2の状態がWinなら
                else if (player2Status == Player.Status.Win)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameClear");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                
                break;
            case "InGame1V1":
                //Player1の状態がDeadなら
                if (player1Status == Player.Status.Dead)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameOver");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                //Player1の状態がWinなら
                else if (player1Status == Player.Status.Win)
                {
                    //経過時間を加える
                    playerChangeTimer += Time.deltaTime;
                    //経過時間がgameOverTIme以上になったら
                    if (playerChangeTimer >= gameOverTime)
                    {
                        //ゲームオーバーシーンを読み込む
                        SceneManager.LoadScene("GameClear");
                        //経過時間をリセット
                        playerChangeTimer = 0.0f;
                    }
                }
                break;
        }
        
            
    }

    //プレイヤーのHPを表示
    public void Player_ReduceHP(int hp, string PlayerName)
    {
        //HPを表示
        //PlayerHP_Text.text = p1.ToString();
        if (PlayerName == "P1")
        {
            if (P_HPbar == null || p1 == null)
            {
                Debug.LogError("GameMNGのP_HPbarまたはp1がInspectorで未設定です。");
                return;
            }
            P_HPbar.value = p1.HP;
        }
        if (PlayerName == "P2")
        {
            if (E_HPbar == null || p2 == null)
            {
                Debug.LogError("GameMNGのE_HPbarまたはp2がInspectorで未設定です。");
                return;
            }
            E_HPbar.value = p2.HP;
        }
    }
    //エネミー側のHPを表示する
    public void Enemy_ReduceHP(int hp)
    {
        //HPを表示
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
    //p1側のHPの表示を更新する
    public void P1_ReduceHP(int hp)
    {
        //HPを表示
        if (P_HPbar == null || p1 == null)
        {
            Debug.LogError("GameMNGのP_HPbarまたはp1がInspectorで未設定です。");
            return;
        }
        P_HPbar.value = p1.HP;
    }
    //p2側のHPの表示を更新する
    public void P2_ReduceHP(int hp)
    {
        //HPを表示
        if (E_HPbar == null || p2 == null)
        {
            Debug.LogError("GameMNGのE_HPbarまたはp2がInspectorで未設定です。");
            return;
        }
        E_HPbar.value = p2.HP;
    }
    //ド根性復活のタイマーとカウントを表示する
    public void PlayerUI(float Timer,int Cnt)
    {
    }

    //他のC#スクリプトから呼び出す変数
    public void SettestStatus(Player.Status ps)
    {
        player1Status = ps;

        //勝敗が決まったら、勝った方にカメラをズームする
        if (cameraController != null)
        {
            if (player1Status == Player.Status.Win)
            {
                //プレイヤーが勝利
                cameraController.FocusOnTarget(PlayerTransform);
            }
            else if (player1Status == Player.Status.Dead)
            {
                //プレイヤーが敗北＝敵の勝利
                cameraController.FocusOnTarget(EnemyTransform);
            }
        }
    }
}


