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
    // ★追加：Player側の押しのけ処理（CPU戦でのすり抜け防止）が、Enemyの胴体の大きさを参照するための公開プロパティ。
    public CapsuleCollider BodyCollider => Enemy_Collider;

    public ParticleSystem Men_particle;     //仁王立ち用のパーティクル
    public ParticleSystem Hit_particle;     //ヒット時用のパーティクル

    [Header("擬音演出（ドカン・ドドン等）")]
    public HitEffectData punchHitEffectData;   // パンチ被弾時に出す擬音の設定
    public HitEffectData kickHitEffectData;    // キック被弾時に出す擬音の設定
    public HitEffectData guardHitEffectData;   // 仁王立ちガード成功時に出す擬音の設定（未設定なら出さない）
    public HitEffectData missHitEffectData;    // 攻撃が空振りした時に出す擬音の設定（未設定なら出さない）

    // ★追加：空振り演出用。攻撃を出した瞬間にfalseにし、Playerにヒットした瞬間trueにする。
    private bool attackLandedThisAttack = false;

    // ★追加：被弾したPlayer側から「あなたの攻撃、当たりましたよ」と通知してもらうための公開メソッド。
    public void NotifyAttackLanded()
    {
        attackLandedThisAttack = true;
    }

    // ★追加：擬音演出用に、自分が今どの攻撃をしているかを外部（被弾したPlayer側）から
    //   問い合わせるための状態。DoAction()でPunch/Kickを選んだ時に更新する。
    private ActionType currentAttackAction = ActionType.Idle;

    public AttackType CurrentAttackType
    {
        get
        {
            switch (currentAttackAction)
            {
                case ActionType.Punch: return AttackType.Punch;
                case ActionType.Kick: return AttackType.Kick;
                default: return AttackType.None;
            }
        }
    }

    // ★追加：攻撃種別に応じて、自分が持っているHitEffectDataのどれを使うかを返す。
    //   被弾したPlayer側から「あなたの攻撃はどの擬音を使う？」と問い合わせられるためのメソッド。
    public HitEffectData GetHitEffectDataFor(AttackType type)
    {
        switch (type)
        {
            case AttackType.Punch:
                return punchHitEffectData;
            case AttackType.Kick:
            case AttackType.UpKick:
            case AttackType.DownKick:
                return kickHitEffectData;
            default:
                return null;
        }
    }

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

    // ★追加: 向き合い設定（Player.csの「自動向き合い設定」に対応）
    [Header("向き合い設定")]
    [Tooltip("ONにすると、常にプレイヤーの方向へ自動で向きを変える。")]
    public bool enableAutoFacing = true;
    [Tooltip("向きを変える速さ。大きいほど素早く振り向く。")]
    public float turnSpeed = 15f;
    [Tooltip("プレイヤーとのZ軸の距離がこの値より小さい時は向きを変えない（真横で重なった時のブレ防止）。")]
    public float autoFaceDeadZone = 0.05f;
    [Tooltip("プレイヤーが反対側へ回り込んだ時、このZ距離以上離れて初めて向きを反転させる（密着中の反転防止）。")]
    public float autoFaceFlipThreshold = 1.0f;
    private bool autoFaceSnapped; // 最初に向き合わせる時だけ補間なしで即座に向くための記録
    private float autoFaceSign = 1f; // 現在向いている（向こうとしている）Z方向。密着中に判定が揺れても、確定するまで変えない

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

    // ---- 漢気ゲージ(必殺技ゲージ) ----
    [Header("漢気ゲージ設定")]
    [Tooltip("ゲージ1本分の最大値")]
    public float kankiGaugePerBar = 100f;
    [Tooltip("ゲージの本数(2本分)")]
    public int kankiGaugeBarCount = 2;
    [Tooltip("プレイヤーに攻撃を当てた時に増える量")]
    public float gaugeGainOnHit = 15f;
    [Tooltip("後退(敵から離れる)した時に減る量")]
    public float gaugeLossOnRetreat = 5f;
    [Tooltip("ゲージ1本(満タン)につき上昇する攻撃力の倍率。0.25なら1本で+25%")]
    public float atkPowerPerBar = 0.25f;

    // 現在の合計ゲージ量(0 〜 kankiGaugePerBar * kankiGaugeBarCount)
    private float kankiGauge = 0f;
    // ゲージによる補正がかかる前の、素の攻撃力
    private int baseAtk;
    // プレイヤーのHPが「攻撃が当たって減った」ことを検知するための、直前フレームのHP
    private int lastPlayerHP;

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
        baseAtk = atk;      //漢気ゲージによる攻撃力補正の基準値として保持
        kankiGauge = 0f;
        HP = 100;
        Menflag = false;
        Enemy_Status = Status.Neutral;
        animator = GetComponent<Animator>();

        ResolvePlayerIfMissing(true); // ★追加：Inspectorで未設定でも、シーン内のPlayerを自動で探す

        //プレイヤーの攻撃命中検知(HP減少検知)用の初期値
        if (player != null)
        {
            lastPlayerHP = player.HP;
        }

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
        ResolvePlayerIfMissing(false); // ★追加：player参照が空なら0.5秒おきに自動で探し直す

        // ★追加: プレイヤーのHPが前フレームより減っていたら、
        //   自分(Enemy)の攻撃(パンチ・キック・投げ等)が当たったとみなして漢気ゲージを増やす。
        //   Player.csを直接編集しなくても、パンチ/キック含む全ての攻撃命中を検知できる。
        if (player != null)
        {
            if (player.HP < lastPlayerHP)
            {
                AddKankiGauge(gaugeGainOnHit);
            }
            lastPlayerHP = player.HP;
        }

        // ★変更: 常にプレイヤーの方向を向く（Player.AutoFaceOpponentと同じ方式）。
        //   AIの行動判断(reactionInterval)とは別に、毎フレーム即座に反応させる。
        FaceTowardsPlayer();

        //タイマー加算
        ActionTimer += Time.deltaTime;

        //★修正: 固定値(1.0f)ではなく難易度ごとのreactionIntervalを使う。
        //   これにより Easy/Normal/Hard で「判断の速さ」に実際の差が出るようになる。
        if (ActionTimer >= reactionInterval)
        {
            // 攻撃系の行動（パンチ/キック）が、一度も命中通知(NotifyAttackLanded)を
            // 受けないまま次の判断に入ったら「空振り」とみなす
            if ((currentAttackAction == ActionType.Punch || currentAttackAction == ActionType.Kick)
                && !attackLandedThisAttack)
            {
                if (HitEffectSpawner.Instance != null && missHitEffectData != null)
                {
                    HitEffectSpawner.Instance.SpawnAtDirection(missHitEffectData, transform.position, transform.forward);
                }
            }

            AtkHitboxOFF();
            Menflag = false;
            Enemy_Status = Status.Neutral;
            currentAttackAction = ActionType.Idle; // 新しい行動を選ぶ前に攻撃タイプ情報もリセット

            if(animator.GetBool("Crouch"))
            {
                animator.SetBool("Crouch", false);
                //当たり判定を上げる
                Enemy_Collider.height = 2.0f;
                Enemy_Collider.center = new Vector3(0, 1.0f, 0);
            }

            // ★追加：プレイヤーがまだ見つからない間は行動判断を保留する（NullReferenceException防止）
            if (player == null)
            {
                ActionTimer = 0.0f;
                return;
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

       
    // ★追加：playerが未設定（またはシーン再読み込み・プレハブ化で参照切れ）の時に、シーン内から自動で探す。
    //   Inspectorで設定されていればそちらが常に優先される（何もしない）。
    //   リザルト画面用のPlayer(IsResultMode)は対戦相手ではないので除外する。
    private float playerResolveTimer = 0f;

    private void ResolvePlayerIfMissing(bool immediate)
    {
        if (player != null) return;

        if (!immediate)
        {
            playerResolveTimer -= Time.unscaledDeltaTime;
            if (playerResolveTimer > 0f) return;
        }
        playerResolveTimer = 0.5f;

        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (!candidate.gameObject.activeInHierarchy || candidate.IsResultMode) continue;

            player = candidate;
            lastPlayerHP = player.HP;
            Debug.LogWarning($"[Enemy] playerが未設定だったため、シーン内の'{candidate.gameObject.name}'を自動で設定しました。" +
                             "Inspectorで設定し直すことをおすすめします。", this);
            return;
        }
    }

    // ★変更: 常にプレイヤーの方向へ向きを変える（Player.csのAutoFaceOpponentと同じ考え方）。
    //   ・向きは「プレイヤーが+Z側にいれば+Z、-Z側にいれば-Z」の2択（間合いはZ軸で決まる仕様に合わせている）。
    //   ・Translate(0,0,±)はローカル座標基準で動くため、向きが合っていればApproach/Retreatも自然に動く。
    //   ・最初の1回だけは補間せず即座に向き合わせる（開始直後にゆっくり回る見た目を防ぐ）。
    //   ・以前は「背後に回られた時だけ180度反転」だったため、初期の向きがZ軸とズレていると向き合わなかった。
    private void FaceTowardsPlayer()
    {
        if (!enableAutoFacing) return;
        if (player == null) return;
        if (HP <= 0) return; // 倒れた後は向きを変えない

        float deltaZ = player.transform.position.z - transform.position.z;
        if (Mathf.Abs(deltaZ) < autoFaceDeadZone) return; // ほぼ同じ位置：今の向きを維持（向きのブレ防止）

        float desiredSign = Mathf.Sign(deltaZ);

        if (!autoFaceSnapped)
        {
            autoFaceSign = desiredSign;
            transform.rotation = Quaternion.LookRotation(new Vector3(0.0f, 0.0f, autoFaceSign), Vector3.up);
            autoFaceSnapped = true;
            return;
        }

        // 密着中はZの左右関係が不安定なので、プレイヤーが反対側へ autoFaceFlipThreshold 以上回り込むまで向きを変えない
        if (desiredSign != autoFaceSign && Mathf.Abs(deltaZ) >= autoFaceFlipThreshold)
        {
            autoFaceSign = desiredSign;
        }

        Quaternion targetRotation = Quaternion.LookRotation(new Vector3(0.0f, 0.0f, autoFaceSign), Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
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
                currentAttackAction = ActionType.Kick; // 擬音演出の判定用に記録
                attackLandedThisAttack = false; // 空振り判定用にリセット

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
                currentAttackAction = ActionType.Punch; // 擬音演出の判定用に記録
                attackLandedThisAttack = false; // 空振り判定用にリセット

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

                //敵(プレイヤー)から離れる行動なので漢気ゲージを減らす
                ReduceKankiGauge(gaugeLossOnRetreat);
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

                    //※漢気ゲージはUpdate()内のプレイヤーHP減少検知で自動的に加算されるため、ここでは呼ばない
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

                // 仁王立ちガード成功の擬音演出（自分の guardHitEffectData を使用）
                Vector3 guardHitPoint = collision.ClosestPoint(collision.transform.position);
                if (HitEffectSpawner.Instance != null && guardHitEffectData != null)
                {
                    HitEffectSpawner.Instance.Spawn(guardHitEffectData, player.transform.position, guardHitPoint);
                }

                // ガードされてもヒットはヒット（空振りではない）なので攻撃側へ通知
                if (player != null) player.NotifyAttackLanded();
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

                // 「ドカン」「ドドン」等の擬音演出。
                // 攻撃者(player)に今の攻撃タイプ(パンチ/キック)を問い合わせて、対応する擬音データを選ぶ。
                if (HitEffectSpawner.Instance != null && player != null)
                {
                    HitEffectData selectedHitEffect = player.GetHitEffectDataFor(player.CurrentAttackType);
                    if (selectedHitEffect != null)
                    {
                        HitEffectSpawner.Instance.Spawn(selectedHitEffect, player.transform.position, HitPoint);
                    }
                }

                // 空振りではなく命中したことをPlayer側(攻撃者)へ通知
                if (player != null) player.NotifyAttackLanded();

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

    // ---- 漢気ゲージ操作 ----

    //プレイヤーに攻撃(パンチ・キック等)が当たった時にPlayer側から呼び出す想定の公開メソッド。
    //※注意: 現在はUpdate()内でプレイヤーHPの減少を自動検知してゲージを増やしているため、
    //   このメソッドを別途呼び出すと二重加算になります。Player.cs側から明示的に呼ぶ場合は、
    //   Update()内のHP減少検知処理を削除するかコメントアウトしてください。
    public void NotifyAttackLandedOnPlayer()
    {
        AddKankiGauge(gaugeGainOnHit);
    }

    //漢気ゲージを増やす(上限は2本分の合計値でクランプ)
    public void AddKankiGauge(float amount)
    {
        float max = kankiGaugePerBar * kankiGaugeBarCount;
        kankiGauge = Mathf.Clamp(kankiGauge + amount, 0f, max);
        UpdateAtkByGauge();

        //ゲージUI(2本分のSlider)を更新
        if (gameMNG != null)
        {
            gameMNG.Enemy_UpdateKankiGauge();
        }
    }

    //漢気ゲージを減らす(0未満にはならない)
    public void ReduceKankiGauge(float amount)
    {
        kankiGauge = Mathf.Clamp(kankiGauge - amount, 0f, kankiGaugePerBar * kankiGaugeBarCount);
        UpdateAtkByGauge();

        //ゲージUI(2本分のSlider)を更新
        if (gameMNG != null)
        {
            gameMNG.Enemy_UpdateKankiGauge();
        }
    }

    //ゲージの満タン本数に応じて攻撃力を再計算する
    private void UpdateAtkByGauge()
    {
        int filledBars = Mathf.FloorToInt(kankiGauge / kankiGaugePerBar);
        atk = Mathf.RoundToInt(baseAtk * (1f + atkPowerPerBar * filledBars));
    }

    //UI(Sliderなど)から参照するための、指定した本数目のゲージの充填率(0〜1)を返す
    //barIndex: 0 = 1本目, 1 = 2本目
    public float GetGaugeFillRatio(int barIndex)
    {
        float barStart = barIndex * kankiGaugePerBar;
        float filled = Mathf.Clamp(kankiGauge - barStart, 0f, kankiGaugePerBar);
        return filled / kankiGaugePerBar;
    }

    //現在のゲージ合計値(生の値)を取得したい場合用
    public float GetKankiGauge()
    {
        return kankiGauge;
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