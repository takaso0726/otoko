using System.Collections.Generic;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    //変数宣言
    //int rand;           //乱数用変数
    float ActionTimer;  //タイマー
    public int atk;
    public int HP = 100;   // ★宣言時に初期化。Start()の実行順序に関わらず、GameMNG側から参照した時点で正しい値になるようにする
    public Player player;
    bool Menflag;
    public Animator animator;
    Rigidbody rb;                 //Rigidbody型の変数
    public Vector3 force;
    //行動状態
    public enum Status
    {
        Neutral,    //待機(ニュートラル)
        Attack,     //攻撃
        Stand,      //仁王立ち
        Throw,      //投げ(つかみ)
        nockback,       //やられ
        Live,       //生存
        Reborn,     //復活
        Dead,       //死亡
        Win,        //勝利
    };

    public Enemy.Status Enemy_Status;

    CapsuleCollider Enemy_Collider;

    // 全ての攻撃用当たり判定をまとめた配列（Playerの被弾判定から参照できるようにする）
    CapsuleCollider[] allHitboxes;
    // ★追加：Player側から「このコライダーはEnemyの攻撃用ヒットボックスか？」を
    //   問い合わせるための公開プロパティ。PlayerのOnTriggerEnterでの被弾判定に使う。
    public CapsuleCollider[] AttackHitboxes => allHitboxes;

    public ParticleSystem Men_particle;     //仁王立ち用のパーティクル
    public ParticleSystem Hit_particle;     //ヒット時用のパーティクル

    public FightingCameraController cameraController;
    private int currentHP = 100;

    // GameMNGへの参照。毎回GameObject.Findするとオブジェクト名の相違や
    // タイミングでnullを返し、そのままGetComponentするとNullReferenceExceptionになるため、
    // Startで一度だけ探してキャッシュし、以降はこれを使い回す。
    GameMNG gameMNG;

    // ---- CPU AI 用 ----
    // 行動の種類。ランダム値(1〜7)の代わりにこの列挙体で意図を表す
    private enum ActionType
    {
        Approach,   //接近
        Retreat,    //後退
        Kick,       //キック攻撃
        Punch,      //パンチ攻撃
        Throw,      //投げ
        Crouch,     //しゃがみ(回避寄り)
        Stand,      //仁王立ち(フェイント/カウンター狙い)
        Idle,       //何もしない(様子見)
    }

    // 距離の閾値。この値を境に「近距離/中距離/遠距離」を切り替える
    [Header("CPU AI設定")]
    public float nearRange = 1.8f;
    public float midRange = 3.5f;

    // ---- CPU 難易度 ----
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard
    }

    [Header("CPU難易度設定")]
    [Tooltip("この項目だけでCPUの強さ(反応速度・ミス率・与ダメージなど)がまとめて決まります")]
    public Difficulty difficulty = Difficulty.Normal;

    // ★変更: 以下の細かいパラメータは difficulty の選択に応じて自動決定されるため、
    //   Inspectorから直接編集できないよう public を外し private 化した。
    //   (Editor上で選べるのは上の difficulty プルダウンのみになる)

    // 行動を判断する間隔(秒)。小さいほど反応が速い
    private float reactionInterval = 1.0f;

    // 判断を誤ってランダムな行動を取ってしまう確率(0〜1)。低難易度ほど高い
    private float mistakeChance = 0.1f;

    // プレイヤーの攻撃に気づいて防御的な行動を選べる確率(0〜1)。低難易度ほど見逃しやすい
    private float defenseAwareness = 0.7f;

    // 投げ攻撃などCPU側の与ダメージにかかる倍率
    private float damageMultiplier = 1.0f;

    void Awake()
    {
        // シーン内のカメラコントローラーを自動取得したい場合
        if (cameraController == null)
            cameraController = FindAnyObjectByType<FightingCameraController>();

        // difficultyの選択に応じてパラメータを自動設定する
        ApplyDifficultyPreset();
    }

    // ★追加: 外部(難易度選択UIなど)から難易度を切り替えたい場合に呼び出す。
    //   Awake()より後のタイミングでも、このメソッドを呼べばパラメータが反映される。
    public void SetDifficulty(Difficulty newDifficulty)
    {
        difficulty = newDifficulty;
        ApplyDifficultyPreset();
    }

    // 難易度に応じてAIのパラメータをまとめて設定する
    private void ApplyDifficultyPreset()
    {
        switch (difficulty)
        {
            case Difficulty.Easy:
                reactionInterval = 1.4f;   // 判断が遅い
                mistakeChance = 0.35f;     // ミスが多い
                defenseAwareness = 0.3f;   // 攻撃されても気づきにくい
                damageMultiplier = 0.8f;   // 与ダメージ控えめ
                break;

            case Difficulty.Normal:
                reactionInterval = 1.0f;
                mistakeChance = 0.12f;
                defenseAwareness = 0.65f;
                damageMultiplier = 1.0f;
                break;

            case Difficulty.Hard:
                reactionInterval = 0.6f;   // 判断が速い
                mistakeChance = 0.0f;      // ミスしない
                defenseAwareness = 0.95f;  // 攻撃をほぼ見逃さない
                damageMultiplier = 1.2f;   // 与ダメージ高め
                break;
        }
    }

    //当たり判定の子オブジェクト
    CapsuleCollider Head;
    CapsuleCollider RightArm;
    CapsuleCollider RightForeArm;
    CapsuleCollider RightHand;
    CapsuleCollider RightFoot;
    CapsuleCollider RightUpLeg;
    CapsuleCollider RightLeg;
    CapsuleCollider LeftArm;
    CapsuleCollider LeftForeArm;
    CapsuleCollider LeftHand;
    CapsuleCollider LeftFoot;
    CapsuleCollider LeftUpLeg;
    CapsuleCollider LeftLeg;

    float InitRotate;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
        ActionTimer = 0.0f;
        //rand = Random.Range(1, 6);
        rb = GetComponent<Rigidbody>();		//EnemyのRigidbodyを取得
        atk = 10;
        HP = 100;
        Menflag = false;
        Enemy_Status = Status.Neutral;
        animator = GetComponent<Animator>();

        //<当たり判定の子オブジェクトの取得>
        //頭の当たり判定
        Head = GameObject.Find("Head").gameObject.GetComponent<CapsuleCollider>();

        // 自分自身をカメラの追従対象に登録
        cameraController.RegisterTarget(transform);

        //右腕の当たり判定
        RightArm = GameObject.Find("RightArm").gameObject.GetComponent<CapsuleCollider>();
        //右前腕の当たり判定
        RightForeArm = GameObject.Find("RightForeArm").gameObject.GetComponent<CapsuleCollider>();
        //右手の当たり判定
        RightHand = GameObject.Find("RightHand").gameObject.GetComponent<CapsuleCollider>();

        //右足の当たり判定
        RightFoot = GameObject.Find("RightFoot").gameObject.GetComponent<CapsuleCollider>();
        //右ふとももの当たり判定
        RightUpLeg = GameObject.Find("RightUpLeg").gameObject.GetComponent<CapsuleCollider>();
        //右ふくらはぎの当たり判定
        RightLeg = GameObject.Find("RightLeg").gameObject.GetComponent<CapsuleCollider>();

        //左腕の当たり判定
        LeftArm = GameObject.Find("LeftArm").gameObject.GetComponent<CapsuleCollider>();
        //左前腕の当たり判定
        LeftForeArm = GameObject.Find("LeftForeArm").gameObject.GetComponent<CapsuleCollider>();
        //左手の当たり判定
        LeftHand = GameObject.Find("LeftHand").gameObject.GetComponent<CapsuleCollider>();

        //左足の当たり判定
        LeftFoot = GameObject.Find("LeftFoot").gameObject.GetComponent<CapsuleCollider>();
        //左ふとももの当たり判定
        LeftUpLeg = GameObject.Find("LeftUpLeg").gameObject.GetComponent<CapsuleCollider>();
        //左ふくらはぎの当たり判定
        LeftLeg = GameObject.Find("LeftLeg").gameObject.GetComponent<CapsuleCollider>();

        Enemy_Collider = gameObject.GetComponent<CapsuleCollider>();

        // シーン内のGameMNGを名前に依存しない方法で取得しておく。
        // （"ManagerObject"という名前のオブジェクトが実際には存在しないシーンでも動くようにする）
        gameMNG = FindAnyObjectByType<GameMNG>();
        if (gameMNG == null)
        {
            Debug.LogError("シーン内にGameMNGコンポーネントを持つGameObjectが見つかりません。配置を確認してください。");
        }

        // 一括参照用の配列にまとめておく（Player側の被弾判定で使用）
        allHitboxes = new[]
        {
            Head, RightArm, RightForeArm, RightHand, RightFoot, RightUpLeg, RightLeg,
            LeftArm, LeftForeArm, LeftHand, LeftFoot, LeftUpLeg, LeftLeg
        };

        AtkHitboxOFF();
        InitRotate = transform.rotation.y;

    }

    // Update is called once per frame
    void Update()
    {
        // ★追加: プレイヤーが背後に回り込んだら振り向く。
        //   AIの行動判断(reactionInterval)とは別に、毎フレームチェックして即座に反応させる。
        FaceTowardsPlayerIfBehind();

        //タイマー加算
        ActionTimer += Time.deltaTime;

        //★修正: 固定値(1.0f)ではなく難易度ごとのreactionIntervalを使う。
        //   これにより Easy/Normal/Hard で「判断の速さ」に実際の差が出るようになる。
        if (ActionTimer >= reactionInterval)
        {
            AtkHitboxOFF();
            Menflag = false;
            Enemy_Status = Status.Neutral;

            if(animator.GetBool("Crouch"))
            {
                animator.SetBool("Crouch", false);
                //当たり判定を上げる
                Enemy_Collider.height = 2.0f;
                Enemy_Collider.center = new Vector3(0, 1.0f, 0);
            }

            // ==== ここからがCPUの「判断」部分 ====
            // プレイヤーとの距離、プレイヤーが攻撃中かどうかを見て行動を選ぶ
            float distance = Mathf.Abs(player.transform.position.z - transform.position.z);
            bool playerIsAttacking = player.Player_status == Player.Status.Attack;

            // 難易度が低いほど、攻撃の気配に気づけないことがある(見逃し)
            bool noticedAttack = playerIsAttacking && (Random.value <= defenseAwareness);

            // ★修正: noticedAttackを実際にChooseActionへ渡す。
            //   気づけなかった場合は「相手は攻撃していない」ものとして扱うため、
            //   低難易度ほど防御行動(しゃがみ/後退)を選ばず攻め込んでしまう。
            ActionType action = ChooseAction(distance, noticedAttack);

            // 難易度が低いほど、判断を誤ってランダムな行動を取ってしまうことがある
            if (Random.value < mistakeChance)
            {
                System.Array values = System.Enum.GetValues(typeof(ActionType));
                action = (ActionType)values.GetValue(Random.Range(0, values.Length));
            }

            DoAction(action);
        }


        /*
        //タックル--封印--
        if(HP <= 10)
        {
            if ((Mathf.Sqrt((transform.position.z - Player.transform.position.z) * (transform.position.z - Player.transform.position.z))) < 3.0f && Enemy_Status == Status.Neutral)
            {
                //当たり判定ON
                LeftForeArm.enabled = true;
                LeftHand.enabled = true;
                RightForeArm.enabled = true;
                RightHand.enabled = true;

                if (flag)
                {
                    //アニメーションのため向き調整(回転)
                    transform.Rotate(0, 90f, 0);
                    //アニメーション再生(タックル)
                    animator.SetTrigger("Tackle");
                    //エネミーのステータスを[攻撃]に変更
                    Enemy_Status = Status.Attack;

                    //フラグを降ろす
                    flag = false;
                }
            }
            else
            {
                //プレイヤーに向かって移動
                transform.Translate(0.0f, 0.0f, 1.0f);
                //animator.SetTrigger("Run");
                Enemy_Status = Status.Neutral;
                AtkHitboxOFF();
            }
        }


        */
    }

       
    // ★追加: プレイヤーが自分の背後(正面と逆側)に回り込んでいたら、その場でプレイヤーの方を向く。
    //   Translate(0,0,±)はローカル座標基準で移動するため、向きを直すことで
    //   以降のApproach/Retreatも自然にプレイヤー側へ進むようになる。
    private void FaceTowardsPlayerIfBehind()
    {
        if (player == null) return;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0.0f; // 水平方向のみで判定(高さは無視)

        if (toPlayer.sqrMagnitude < 0.0001f) return; // ほぼ同じ位置なら判定しない

        toPlayer.Normalize();

        // 自分の正面ベクトルとプレイヤー方向の内積。
        // 0より小さい = プレイヤーは正面より後ろ側にいる(背後に回り込まれた)
        float facingDot = Vector3.Dot(transform.forward, toPlayer);

        if (facingDot < 0.0f)
        {
            // ★変更: プレイヤーの厳密な方向へ向き直す(LookRotation)のではなく、
            //   正面をプレイヤー側へ180度反転させるだけにする(2D格闘ゲーム的な「振り向き」)
            transform.Rotate(0.0f, 180.0f, 0.0f);
        }
    }

    // 距離とプレイヤーの状態から、次に取る行動を重み付き抽選で決める
    private ActionType ChooseAction(float distance, bool playerIsAttacking)
    {
        List<(ActionType action, float weight)> table = new List<(ActionType, float)>();

        if (distance > midRange)
        {
            // 遠距離: 基本は接近。フェイントや様子見はほぼせず、積極的に詰める
            table.Add((ActionType.Approach, 0.85f));
            table.Add((ActionType.Stand, 0.1f));
            table.Add((ActionType.Idle, 0.05f));
        }
        else if (distance > nearRange)
        {
            // 中距離: 接近しつつ、相手が攻撃中なら距離を取る
            if (playerIsAttacking)
            {
                // 相手が攻撃中でも下がりきらず、しゃがみや反撃キックで押し返す
                table.Add((ActionType.Retreat, 0.25f));
                table.Add((ActionType.Crouch, 0.35f));
                table.Add((ActionType.Kick, 0.3f));
                table.Add((ActionType.Idle, 0.1f));
            }
            else
            {
                table.Add((ActionType.Approach, 0.65f));
                table.Add((ActionType.Kick, 0.25f));
                table.Add((ActionType.Retreat, 0.05f));
                table.Add((ActionType.Idle, 0.05f));
            }
        }
        else
        {
            // 近距離: 攻撃の主戦場。相手が攻撃中なら防御寄りの行動を優先
            if (playerIsAttacking)
            {
                // 引かずに殴り返す比率を上げる(しゃがみ回避は残しつつ最小限)
                table.Add((ActionType.Crouch, 0.2f));
                table.Add((ActionType.Retreat, 0.1f));
                table.Add((ActionType.Punch, 0.35f));
                table.Add((ActionType.Kick, 0.35f));
            }
            else
            {
                table.Add((ActionType.Punch, 0.35f));
                table.Add((ActionType.Kick, 0.5f));
                table.Add((ActionType.Throw, 0.05f));
                table.Add((ActionType.Stand, 0.05f));
                table.Add((ActionType.Retreat, 0.05f));
            }
        }

        float total = 0f;
        foreach (var entry in table) total += entry.weight;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var entry in table)
        {
            cumulative += entry.weight;
            if (roll <= cumulative)
                return entry.action;
        }

        return ActionType.Idle; // 保険(基本ここには来ない)
    }

    // 選ばれた行動を実際に実行する(以前の switch(rand) の中身を移植)
    private void DoAction(ActionType action)
    {
        switch (action)
        {
            case ActionType.Stand:
                //仁王立ち
                Enemy_Status = Status.Stand;
                Menflag = true;
                ActionTimer = 0.0f;

                // パーティクルシステムのインスタンスを生成する。
                ParticleSystem newParticle = Instantiate(Men_particle, new Vector3(transform.position.x, transform.position.y + 1.0f, transform.position.z),
                    Quaternion.Euler(-90.0f, 0.0f, 0.0f));
                // パーティクルを発生させる。
                newParticle.Play();
                // ※第一引数をnewParticleだけにするとコンポーネントしか削除されない。
                Destroy(newParticle.gameObject, 1.0f);
                break;

            case ActionType.Kick:
                //攻撃(キック)
                animator.SetTrigger("Kick");
                Enemy_Status = Status.Attack;

                //当たり判定をON
                RightFoot.enabled = true;
                RightLeg.enabled = true;
                RightUpLeg.enabled = true;

                //タイマーをリセット
                ActionTimer = -reactionInterval;
                break;

            case ActionType.Punch:
                //攻撃(パンチ)
                animator.SetTrigger("Punch");
                Enemy_Status = Status.Attack;

                //当たり判定をON
                RightHand.enabled = true;

                //タイマーをリセット
                ActionTimer = -reactionInterval;
                break;

            case ActionType.Approach:
                //移動(前進) ※大胆さを出すため歩幅を拡大
                transform.Translate(0.0f, 0.0f, 0.2f);
                ActionTimer = 0.0f;
                break;

            case ActionType.Retreat:
                //移動(後退) ※下がる歩幅は控えめにして引きすぎないようにする
                transform.Translate(0.0f, 0.0f, -0.08f);
                ActionTimer = 0.0f;
                break;

            case ActionType.Crouch:
                //しゃがみ
                animator.SetBool("Crouch", true);
                //当たり判定を下げる
                Enemy_Collider.height = 0.65f;
                Enemy_Collider.center = new Vector3(0, 0.5f, 0);

                //しゃがみの硬直は判断間隔の半分
                ActionTimer = reactionInterval * 0.5f;
                break;

            case ActionType.Throw:
                //つかみ
                animator.SetTrigger("Throw");
                //投げは硬直なし(すぐ次の判断に移れる)
                ActionTimer = reactionInterval;

                //投げれるか距離でチェック(距離と相手の状態で判断)
                if (player.Player_status != Player.Status.Attack && (player.transform.position.z - transform.position.z < 1.75f))
                {
                    Debug.Log("投げ成功");
                    player.transform.Translate(0.0f, 0.0f, -0.0025f);
                    player.animator.SetTrigger("Thrown");
                    player.damege(Mathf.RoundToInt(5 * damageMultiplier));
                }
                break;

            case ActionType.Idle:
                //何もしない(様子見)
                ActionTimer = 0.0f;
                break;
        }
    }

    //接触判定を行い、他のGameObjectと当たった時に呼び出される関数
    private void OnTriggerEnter(Collider collision)
    {
        //当たった対象物の[tag]がPAttack (プレイヤーによる攻撃)だった場合は処理する
        if (collision.gameObject.CompareTag("PAttack"))
        {        
            //体力を減らす
            
            //体力を表示
            Debug.Log("エネミーのHP : " + HP);
            if(Menflag)
            {
                atk += player.atk;

                HP -= player.atk / 2;
            }
            else
            {
                //ヒット時のアニメーション再生
                animator.SetTrigger("Hit");
                //衝突位置を取得
                Vector3 HitPoint = collision.ClosestPoint(collision.transform.position);
                // パーティクルシステムのインスタンスを生成する。
                ParticleSystem HitParticle = Instantiate(Hit_particle, HitPoint, Quaternion.Euler(-90.0f, 0.0f, 0.0f));
                // パーティクルを発生させる。
                HitParticle.Play();
                // ※第一引数をHitParticleだけにするとコンポーネントしか削除されない。
                Destroy(HitParticle.gameObject, 1.0f);

                //プレイヤーのHPを減らす
                HP -= player.atk;
            }
            player.atk = 10;

            if (gameMNG != null)
            {
                gameMNG.Enemy_ReduceHP(HP);

                if (HP <= 0)
                {
                    //マネージャーに「勝利状態」を設定する
                    gameMNG.SettestStatus(Player.Status.Win);
                }
            }
            else
            {
                Debug.LogError("gameMNGがnullのためHP表示・勝敗判定を更新できません。GameMNGコンポーネントの配置を確認してください。");
            }
        }
    }

    void AtkHitboxOFF()
    {
        //全ての攻撃用当たり判定をOFF
        LeftHand.enabled = false;
        LeftForeArm.enabled = false;
        LeftArm.enabled = false;
        Head.enabled = false;
        RightArm.enabled = false;
        RightForeArm.enabled = false;
        RightHand.enabled = false;
        RightFoot.enabled = false;
        RightUpLeg.enabled = false;
        RightLeg.enabled = false;
        LeftArm.enabled = false;
        LeftForeArm.enabled = false;
        LeftHand.enabled = false;
        LeftFoot.enabled = false;
        LeftUpLeg.enabled = false;
        LeftLeg.enabled = false;
    }

    public void damege(int n)
    {
        HP -= n;
        if (gameMNG != null)
        {
            gameMNG.Enemy_ReduceHP(HP);
        }
        else
        {
            Debug.LogError("gameMNGがnullのためHP表示を更新できません。GameMNGコンポーネントの配置を確認してください。");
        }
    }

    public void TakeDamage(int damage)
    {
        currentHP -= damage;
        if (currentHP <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // 撃破時にカメラの追従対象から除外
        cameraController.UnregisterTarget(transform);

        // 退場演出など
        Destroy(gameObject, 1.5f);
    }
}





//backup
//Enemy-ランダムアタック
/*
   //タイマー加算
        ActionTimer += Time.deltaTime;

        if(ActionTimer >= 0.7f)
        {
            AtkHitboxOFF();
            Menflag = false;

            switch (rand)
            {
                case 1:
                    //仁王立ち
                    Menflag = true;
                    ActionTimer = 0.0f;
                    rand = Random.Range(1, 6);
                    break;


                case 2:
                    //攻撃(キック)
                    animator.SetTrigger("Kick");

                    //当たり判定をON
                    LeftFoot.enabled = true;
                    LeftLeg.enabled = true;
                    LeftUpLeg.enabled = true;

                    //タイマーをリセット
                    ActionTimer = -1.0f;
                    rand = Random.Range(1, 6);
                    break;

                case 3:
                    //移動(前進)
                    transform.Translate(0.0f, 0.0f, 0.5f);
                    //タイマーをリセット
                    ActionTimer = 0.0f;
                    rand = Random.Range(1, 6);
                    break;

                case 4:
                    //攻撃(パンチ)
                    animator.SetTrigger("Punch");

                    //当たり判定をON
                    RightHand.enabled = true;

                    //タイマーをリセット
                    ActionTimer = -1.0f;
                    rand = Random.Range(1, 6);
                    break;

                case 5:
                    //移動(前進)
                    transform.Translate(0.0f, 0.0f, 0.5f);
                    //タイマーをリセット
                    ActionTimer = 0.0f;
                    rand = Random.Range(1, 6);
                    break;



            }
        }
*/