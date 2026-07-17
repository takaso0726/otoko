using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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
    Player.Status playerStatus;

    [Header("勝敗カメラ演出")]
    //プレイヤーのTransform（勝利時にカメラがズームする対象）
    public Transform PlayerTransform;
    //敵のTransform（プレイヤー敗北＝敵勝利時にカメラがズームする対象）
    public Transform EnemyTransform;
    //シーン中のカメラコントローラー
    public FightingCameraController cameraController;

    //初期化
    void Start()
    {
        
        //ド根性復活のタイマー
       // Player_Timer_Text.text = "0";
        //ド根性復活の回数
       // Player_Cnt_Text.text = "0";
        //わからんけどなんかのタイマーとカウント
        PTimer = 0;
        PCnt = 0;

        //HPバーのパーセント
        P_HPbar.value = p1.HP;
        E_HPbar.value = p2.HP;


        playerChangeTimer = 0.0f;
        //プレイヤーのステータス
        playerStatus = Player.Status.Live;

        //効果音再生用のAudioClipを取得
        BGM_Lv1 = GetComponent<AudioSource>();

        BGM_Lv1.loop = true;

        //BGM再生
        BGM_Lv1.Play();
    }

    //更新処理
    void Update()
    {
        //Playerの状態がDeadなら
        if(playerStatus == Player.Status.Dead)
        {
            //経過時間を加える
            playerChangeTimer += Time.deltaTime;
            //経過時間がgameOverTIme以上になったら
            if(playerChangeTimer >= gameOverTime)
            {
                //ゲームオーバーシーンを読み込む
                SceneManager.LoadScene("GameOver");
                //経過時間をリセット
                playerChangeTimer = 0.0f;
            }
        }
        else if(playerStatus == Player.Status.Win)
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
            
    }

    //プレイヤーのHPを表示
    public void Player_ReduceHP(int hp, string PlayerName)
    {
        //HPを表示
        //PlayerHP_Text.text = p1.ToString();
        if (PlayerName == "P1") { P_HPbar.value = p1.HP; }
        if (PlayerName == "P2") { E_HPbar.value = p2.HP; }
    }
    //エネミー側のHPを表示する
    public void Enemy_ReduceHP(int hp)
    {
        //HPを表示
        //EnemyHP_Text.text = e1.ToString();
        E_HPbar.value = e1.HP;
    }
    //ド根性復活のタイマーとカウントを表示する
    public void PlayerUI(float Timer,int Cnt)
    {
        //ド根性復活のタイマーとカウントを表示する
        //Player_Timer_Text.text = PTimer.ToString();
        //Player_Cnt_Text.text = PCnt.ToString();
    }

    //他のC#スクリプトから呼び出す変数
    public void SettestStatus(Player.Status ps)
    {
        playerStatus = ps;

        //勝敗が決まったら、勝った方にカメラをズームする
        if (cameraController != null)
        {
            if (playerStatus == Player.Status.Win)
            {
                //プレイヤーが勝利
                cameraController.FocusOnTarget(PlayerTransform);
            }
            else if (playerStatus == Player.Status.Dead)
            {
                //プレイヤーが敗北＝敵の勝利
                cameraController.FocusOnTarget(EnemyTransform);
            }
        }
    }
}


