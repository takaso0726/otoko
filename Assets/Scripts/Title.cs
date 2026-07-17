using UnityEngine;
using UnityEngine.InputSystem; 
using UnityEngine.SceneManagement;

public class Title : MonoBehaviour
{
    //変数宣言
    AudioSource se;
    public AudioClip Titlese;
    float CntTimer;
    bool flag;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //効果音再生用のAudioClipを取得
        se = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {
        if (flag)
        {
            //経過時間を加える
            CntTimer += Time.deltaTime;
            //経過時間がgameOverTIme以上になったら
            if (CntTimer >= 0.7f)
            {
                //インゲームシーンを読み込む
                SceneManager.LoadScene("InGame");

            }
        }
    }

    // 三回押されたとき遷移する
    public void OnTextThriceClicked()
    {
        // シーンの遷移をする
        TriggerSceneTransition();
    }

    // シーン遷移を開始する共通の処理
    private void TriggerSceneTransition()
    {
        if (!flag) // まだ遷移が始まっていなければ
        {
            se.PlayOneShot(Titlese); // 効果音を鳴らす
            flag = true;             // 遷移開始フラグをONにする（これでUpdate内のタイマーが動き出す）
        }
    }
}
