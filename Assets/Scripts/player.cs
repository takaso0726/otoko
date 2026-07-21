using UnityEngine;
using UnityEngine.InputSystem;

//=====================================================
// ★このスクリプトはPlayerInputコンポーネントとセットで使用します。
//   PlayerInputの Behavior は「Send Messages」に設定してください。
//   Actions には PlayerControls.inputactions（Playerマップ）を割り当ててください。
//   誰の入力かはPlayerInputManagerがデバイス単位で自動的に振り分けます。
//=====================================================
// プレイヤーキャラクターの移動・攻撃・被弾・復活などの一連の挙動を管理するメインスクリプト
//
// 【今回のバグ修正（被弾判定まわり）】
// 症状：①攻撃すると自分にもダメージが入る／②相手（P2）の攻撃が当たったり当たらなかったりする
//
// 原因は2つ複合していた：
//   (A) 攻撃用ヒットボックス（手足の子コライダー）は Player 本体の Rigidbody に
//       属する複合コライダーとして扱われるため、P1の手がP2の胴体に触れると
//       「P1側のOnTriggerEnter」と「P2側のOnTriggerEnter」が両方発火してしまい、
//       攻撃した側(P1)まで「被弾した」扱いになっていた。
//   (B) EnemyPlayer()→TakeDamage() の呼び出し経路が「相手のenemyPlayer(=自分)の
//       HPを減らす」というたすき掛けになっており、直後の HP -= enemyPlayer.atk と
//       合わせて同一ヒットで二重にダメージが入っていた。
//
// 対策：
//   (A) 「今トリガーに触れてきたコライダーが“相手の攻撃用ヒットボックス”であるか」
//       を allHitboxes 配列で判定し、それが true の時だけ被弾処理を行う。
//       自分のヒットボックスが相手の体（Player_Collider）に触れただけの場合は
//       この対象では何もしない（それは相手側のOnTriggerEnterで処理される）。
//   (B) EnemyPlayer()/TakeDamage() の自傷ロジックを削除し、ダメージ適用は
//       OnTriggerEnter内の1箇所だけに一本化した。
[RequireComponent(typeof(PlayerInput))]
public class Player : MonoBehaviour
{
    // "ManagerObject"のGameMNGへの参照。毎回GameObject.Findするとタイポや
    // 非アクティブ状態でnullを返しやすく、そのままGetComponentするとNullReferenceExceptionになるため、
    // Startで一度だけ探してキャッシュし、以降はこれを使い回す。
    GameMNG gameMNG;

    // 外部（GameMNG等）に見せるおおまかな状態。既存の呼び出し互換のため維持
    public enum Status
    {
        Neutral,    //待機(ニュートラル)
        Attack,     //攻撃
        Stand,      //仁王立ち
        Throw,      //投げ(つかみ)
        Live,       //生存
        Reborn,     //復活
        Dead,       //死亡
        Win,        //勝利
    };

    // 内部の行動制御用ステート。今どの行動をしているかをこれ1つで管理する
    private enum PlayerState
    {
        Idle,       // 待機
        Move,       // 前後移動
        Crouch,     // しゃがみ
        Punch,      // パンチ（空中では飛び蹴りになる）
        Kick,       // 通常キック
        UpKick,     // 上キック
        DownKick,   // 下キック
        Guard,      // 仁王立ち
        Throw,      // 投げ
        KnockedDown,// ダウン中（根性復活チャレンジ中）
        Dead,       // 死亡（復活失敗）
    }

    //=====================================================
    // ★名前
    //=====================================================
    public string PlayerName;
    public string PLayerTagName;
    //=====================================================
    // ★移動・向き
    //=====================================================
    [Header("移動設定")]
    public float moveSpeed = 3f;             // 移動速度
    public float turnSpeed = 15f;            // 向きを変える速さ
    [SerializeField] float moveInputThreshold = 0.03f;    // 左右移動と判定するスティックの入力量
    [SerializeField] float crouchInputThreshold = -0.43f; // しゃがみ／下キックと判定するスティックの下入力量
    [SerializeField] float upKickInputThreshold = 0.25f;  // 上キックと判定するスティックの上入力量

    //=====================================================
    // ★ジャンプ
    //=====================================================
    [Header("ジャンプ設定")]
    public Vector3 force;                    // ジャンプ時にRigidbodyへ加える力
    // true = 地上にいてジャンプ可能な状態／false = 空中にいる状態
    // （名前は旧版から変えず互換性を保っているが、意味は「接地フラグ」に近い）
    public bool Jumpflag = true;

    //=====================================================
    // ★体力・攻撃力・状態
    //=====================================================
    [Header("ステータス")]
    public int HP = 100;                     // 体力
    public int atk = 10;                     // 攻撃力
    public Player.Status Player_status;        // 外部から参照される、プレイヤーの現在の大まかな状態

    //=====================================================
    // ★各アクションの持続時間（Inspectorで調整可能）
    //=====================================================
    [Header("アクション時間設定（秒）")]
    [SerializeField] float punchDuration = 0.5f;    // パンチ（空中攻撃含む）の拘束時間
    [SerializeField] float kickDuration = 0.6f;     // 通常キックの拘束時間
    [SerializeField] float upKickDuration = 0.7f;   // 上キックの拘束時間
    [SerializeField] float downKickDuration = 0.5f; // 下キックの拘束時間
    [SerializeField] float guardDuration = 0.5f;    // 仁王立ちの拘束時間
    [SerializeField] float throwDuration = 1.5f;    // 投げの拘束時間

    //=====================================================
    // ★根性復活（ダウン後の復活チャレンジ）設定
    //=====================================================
    [Header("復活（根性）設定")]
    [SerializeField] float rebornTimeLimit = 5.0f;  // 復活チャレンジの制限時間
    [SerializeField] int rebornHp = 30;             // 復活成功時に回復するHP
    [SerializeField] int mashThresholdBase = 11;    // 必要連打数の基準値
    [SerializeField] int mashThresholdStep = 3;     // 復活回数が増えるごとに必要連打数が増える量

    //=====================================================
    // ★しゃがみ時のコライダー変化量
    //=====================================================
    [Header("しゃがみ時のコライダー設定")]
    [SerializeField] float standHeight = 2.0f;
    [SerializeField] Vector3 standCenter = new Vector3(0, 1.0f, 0);
    [SerializeField] float crouchHeight = 0.65f;
    [SerializeField] Vector3 crouchCenter = new Vector3(0, 0.5f, 0);

    //=====================================================
    // ★エフェクト・効果音
    //=====================================================
    [Header("エフェクト・SE")]
    public AudioClip MenBlock_se;            // 仁王立ちガード成功時の効果音
    public ParticleSystem Men_particle;      // 仁王立ち用のパーティクル
    public ParticleSystem Hit_particle;      // ヒット時用のパーティクル

    //=====================================================
    // ★外部参照
    //=====================================================
    [Header("参照")]
    public Enemy enemy;                              // 対戦相手（敵）
    public Player enemyPlayer;
    public Animator animator;                         // プレイヤーのAnimator
    public FightingCameraController fightingCamera;   // 演出用カメラ

    //=====================================================
    // ★内部状態
    //=====================================================
    private PlayerState currentState = PlayerState.Idle;   // 現在の行動状態
    private float stateTimer;                               // 現在の行動が終わるまでの残り時間（秒）

    Rigidbody rb;
    AudioSource se;
    PlayerInput playerInput;
    Vector2 moveInput;                        // 左スティックの現在値（OnMoveで更新され続ける）

    // ボタン入力の「意図」フラグ。OnXコールバックで立てて、Update内で1回だけ消費してクリアする
    bool wantJump;
    bool wantPunch;
    bool wantKick;
    bool wantGuard;
    bool wantThrow;

    bool isGuarding;                 // 仁王立ち中かどうか（被弾処理の分岐に使う）
    int guardComboCount;             // 仁王立ちで連続して耐えた回数
    bool rebornCamStarted;           // 根性復活のクローズアップカメラを開始済みか
    bool canThrow = true;            // 投げの多重発生を防ぐフラグ

    int rebornCount = 1;             // 復活回数のカウント
    float rebornTimer;               // 復活チャレンジの経過時間
    int mashCount;                   // 復活チャレンジ中のボタン連打回数

    //当たり判定の子オブジェクト
    CapsuleCollider Head;
    CapsuleCollider RightArm, RightForeArm, RightHand, RightFoot, RightUpLeg, RightLeg;
    CapsuleCollider LeftArm, LeftForeArm, LeftHand, LeftFoot, LeftUpLeg, LeftLeg;
    CapsuleCollider Player_Collider;          // 本体（胴体）のコライダー。しゃがみ時にサイズ変更する
    CapsuleCollider[] allHitboxes;            // 全ての攻撃用当たり判定をまとめて操作するための配列

    // ★追加：他プレイヤーから「このコライダーは自分の攻撃用ヒットボックスか？」を
    //   問い合わせるための公開プロパティ。OnTriggerEnterでの攻撃/被弾の区別に使う。
    public CapsuleCollider[] AttackHitboxes => allHitboxes;
    // ★追加：本体（胴体）コライダーの公開プロパティ。同様にOnTriggerEnterで使う。
    public CapsuleCollider BodyCollider => Player_Collider;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    // 各種コンポーネント・子オブジェクトの当たり判定の取得と初期化を行う
    void Start()
    {
        // 自分にアタッチされているPlayerInputコンポーネントを取得
        playerInput = GetComponent<PlayerInput>();
        // どのプレイヤー番号・どのデバイスが紐づいているかログ出力（デバッグ用）
        Debug.Log($"{gameObject.name} : PlayerIndex={playerInput.playerIndex} / Device={(playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "なし")}");

        rb = GetComponent<Rigidbody>();		//PlayerのRigidbodyを取得
        animator = GetComponent<Animator>();
        se = GetComponent<AudioSource>();

        // "ManagerObject"を探してGameMNGをキャッシュしておく。
        // 見つからない場合はここでハッキリ警告を出し、以降のNullReferenceExceptionを防ぐ。
        GameObject managerObj = GameObject.Find("GameMNG");
        if (managerObj == null)
        {
            Debug.LogError("シーン内に「ManagerObject」という名前のアクティブなGameObjectが見つかりません。" +
                "名前のスペルミス／非アクティブ状態／配置し忘れがないか確認してください。");
        }
        else
        {
            gameMNG = managerObj.GetComponent<GameMNG>();
            if (gameMNG == null)
            {
                Debug.LogError("「ManagerObject」にGameMNGコンポーネントが付いていません。");
            }
        }

        //<当たり判定の子オブジェクトの取得>
        Head = FindHitbox("P-Head");
        RightArm = FindHitbox("P-RightArm");
        RightForeArm = FindHitbox("P-RightForeArm");
        RightHand = FindHitbox("P-RightHand");
        RightFoot = FindHitbox("P-RightFoot");
        RightUpLeg = FindHitbox("P-RightUpLeg");
        RightLeg = FindHitbox("P-RightLeg");
        LeftArm = FindHitbox("P-LeftArm");
        LeftForeArm = FindHitbox("P-LeftForeArm");
        LeftHand = FindHitbox("P-LeftHand");
        LeftFoot = FindHitbox("P-LeftFoot");
        LeftUpLeg = FindHitbox("P-LeftUpLeg");
        LeftLeg = FindHitbox("P-LeftLeg");
        Player_Collider = GetComponent<CapsuleCollider>();

        // 一括ON/OFF操作用の配列にまとめておく
        allHitboxes = new[]
        {
            Head, RightArm, RightForeArm, RightHand, RightFoot, RightUpLeg, RightLeg,
            LeftArm, LeftForeArm, LeftHand, LeftFoot, LeftUpLeg, LeftLeg,
        };

        DisableAllHitboxes();

        HP = 100;
        atk = 10;
        currentState = PlayerState.Idle;
        Player_status = Status.Live;

        // ★デバッグ用：自分のヒットボックスが正しく取得できているか確認
        foreach (var hb in allHitboxes)
        {
            Debug.Log($"[{gameObject.name}] hitbox取得: {(hb != null ? hb.name + " / owner=" + hb.transform.root.name : "null!")}");
        }
    }

    // 指定した名前の子オブジェクトからCapsuleColliderを取得するヘルパー
    CapsuleCollider FindHitbox(string objectName)
    {
        Transform t = FindDeepChild(transform, objectName);
        if (t == null)
        {
            Debug.LogError($"[{gameObject.name}] ヒットボックス '{objectName}' が自分の階層内に見つかりません");
            return null;
        }
        return t.GetComponent<CapsuleCollider>();
    }

    // transformの子孫を再帰的に探索し、名前が一致するTransformを返す
    Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    //=====================================================
    // ★Input Actionsのコールバック（PlayerInputのBehavior=Send Messagesで自動的に呼ばれる）
    //   ここでは「ボタンが押された」という意図フラグを立てるだけにし、
    //   実際にどう行動へ反映するかはUpdate側で判断する。
    //=====================================================

    // 左スティック：Move（継続的な値なので、押した/離したではなく現在値を保持するだけ）
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    // ジャンプボタン押下時のコールバック
    public void OnJump(InputValue value)
    {
        if (value.isPressed) wantJump = true;
    }

    // パンチボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打判定としてこのボタンを使う
    public void OnPunch(InputValue value)
    {
        if (!value.isPressed) return;

        if (HP <= 0)
        {
            // ダウン中は復活チャレンジの連打カウントとして加算するだけ
            mashCount++;
            return;
        }

        wantPunch = true;
    }

    // キックボタン押下時のコールバック（通常／上／下の分岐はUpdate側で行う）
    public void OnKick(InputValue value)
    {
        if (value.isPressed) wantKick = true;
    }

    // 仁王立ちボタン押下時のコールバック
    public void OnStand(InputValue value)
    {
        if (value.isPressed) wantGuard = true;
    }

    // 投げボタン押下時のコールバック
    public void OnThrow(InputValue value)
    {
        if (value.isPressed) wantThrow = true;
    }

    // Update is called once per frame
    // 毎フレームの更新処理。HPが尽きていれば復活チャレンジへ、
    // そうでなければ現在の状態に応じて「拘束中のタイマー消化」か「新しい行動の受付」を行う。
    void Update()
    {
        // ★追加：PlayerInputが無効化されている（＝DualPlayerDeviceAssignerによって
        //   コントローラーが割り当てられなかった側）場合は、Update自体を丸ごとスキップする。
        //   これが無いと、無効化される直前にOnMoveで受け取った古いmoveInputの値が
        //   残り続け、以降入力が来なくなってもそのまま動き続けてしまう。
        if (playerInput != null && !playerInput.enabled) return;

        if (HP <= 0)
        {
            HandleKnockedDown();
            ClearInputIntents();
            return;
        }

        bool isFree = currentState == PlayerState.Idle
                   || currentState == PlayerState.Move
                   || currentState == PlayerState.Crouch;
        if (isFree)
        {
            // 拘束のない状態（Idle/Move/Crouch）での入力処理
            HandleFreeInput();
        }
        else
        {
            // 攻撃・ガード・投げなど拘束時間のある行動中はタイマーだけを進める
            TickBusyState();
        }

        ClearInputIntents();
    }

    // 1フレームで消費しなかった意図フラグを毎フレーム末尾でクリアする
    void ClearInputIntents()
    {
        wantJump = false;
        wantPunch = false;
        wantKick = false;
        wantGuard = false;
        wantThrow = false;
    }

    //-----------------------------------------------------
    // 拘束のない状態（Idle/Move/Crouch）での入力処理
    //-----------------------------------------------------
    // 優先度：ジャンプ ＞ 投げ ＞ パンチ ＞ キック ＞ 仁王立ち ＞ 移動
    void HandleFreeInput()
    {
        bool isCrouchInput = moveInput.y <= crouchInputThreshold;

        // --- しゃがみの見た目切り替え（アナログ値なので毎フレーム判定）---
        if (isCrouchInput && currentState != PlayerState.Crouch)
        {
            EnterCrouch();
        }
        else if (!isCrouchInput && currentState == PlayerState.Crouch)
        {
            ExitCrouch();
        }

        // --- アクションボタン ---
        if (wantJump && Jumpflag)
        {
            DoJump();
            return;
        }
        if (wantThrow)
        {
            EnterThrow();
            return;
        }
        if (wantPunch)
        {
            EnterPunch();
            return;
        }
        if (wantKick)
        {
            EnterKick(isCrouchInput);
            return;
        }
        if (wantGuard)
        {
            EnterGuard();
            return;
        }

        // --- 移動（ボタン入力が無い時、かつしゃがみ入力でない時のみ）---
        if (!isCrouchInput)
        {
            if (moveInput.x >= moveInputThreshold)
            {
                Move(Vector3.forward);
            }
            else if (moveInput.x <= -moveInputThreshold)
            {
                Move(Vector3.back);
            }
            else if (currentState == PlayerState.Move)
            {
                currentState = PlayerState.Idle;
            }
        }
    }

    // プレイヤーを指定方向へ移動させ、その方向を向かせる
    void Move(Vector3 worldDirection)
    {
        currentState = PlayerState.Move;
        // ※Space.Worldを指定し、向きが変わっても常に世界座標の指定方向へ移動するようにする
        transform.Translate(worldDirection * moveSpeed * Time.deltaTime, Space.World);
        FaceDirection(worldDirection);
    }

    // 指定した世界座標方向へプレイヤーの向きを滑らかに回転させる
    void FaceDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    // しゃがみ開始処理。コライダーを低くし、しゃがみアニメーションを再生する
    void EnterCrouch()
    {
        currentState = PlayerState.Crouch;
        Player_Collider.height = crouchHeight;
        Player_Collider.center = crouchCenter;
        animator.SetBool("Crouch", true);
    }

    // しゃがみ終了処理。コライダーを立ち姿勢に戻す
    void ExitCrouch()
    {
        Player_Collider.height = standHeight;
        Player_Collider.center = standCenter;
        animator.SetBool("Crouch", false);
        currentState = PlayerState.Idle;
    }

    // ジャンプ処理。アニメーション再生とRigidbodyへの力の付与を行う
    void DoJump()
    {
        animator.SetTrigger("Jump");
        rb.AddForce(force);
        Jumpflag = false; // 空中に出たので再度ジャンプできないようにする
    }

    //-----------------------------------------------------
    // 拘束のある状態（攻撃・ガード・投げ）への遷移
    //-----------------------------------------------------

    // パンチ（弱攻撃）処理。地上か空中かでアニメーションと当たり判定を切り替える
    void EnterPunch()
    {
        ResetAttackTriggers();
        currentState = PlayerState.Punch;
        stateTimer = punchDuration;

        if (Jumpflag)
        {
            //弱攻撃(パンチ)
            animator.SetTrigger("Punch");
            RightFoot.enabled = true;
            RightLeg.enabled = true;
        }
        else
        {
            //空中攻撃
            animator.SetTrigger("Flying-kick");
            LeftFoot.enabled = true;
            LeftLeg.enabled = true;
            RightFoot.enabled = true;
        }
    }

    // キック処理。スティックの上下入力で通常／上／下キックに分岐する
    void EnterKick(bool isCrouchInput)
    {
        ResetAttackTriggers();

        if (isCrouchInput)
        {
            currentState = PlayerState.DownKick;
            stateTimer = downKickDuration;
            animator.SetTrigger("DownKick");
            RightFoot.enabled = true;
            RightLeg.enabled = true;
            
        }
        else if (moveInput.y > upKickInputThreshold)
        {
            currentState = PlayerState.UpKick;
            stateTimer = upKickDuration;
            animator.SetTrigger("UpKick");
            RightFoot.enabled = true;
            RightLeg.enabled = true;
        }
        else
        {
            currentState = PlayerState.Kick;
            stateTimer = kickDuration;
            animator.SetTrigger("Kick");
            RightFoot.enabled = true;
            RightUpLeg.enabled = true;
            RightLeg.enabled = true;
        }
    }

    // 仁王立ち（ガード）処理。ガードフラグを立て、演出用パーティクルを再生する
    void EnterGuard()
    {
        currentState = PlayerState.Guard;
        stateTimer = guardDuration;
        isGuarding = true;

        ParticleSystem newParticle = Instantiate(
            Men_particle,
            transform.position + Vector3.up,
            Quaternion.Euler(-90f, 0f, 0f));
        newParticle.Play();
        Destroy(newParticle.gameObject, 1.0f);
    }

    // 投げ（掴み）処理。敵との距離・状態を判定し、条件を満たせば投げを成立させる
    void EnterThrow()
    {
        currentState = PlayerState.Throw;
        stateTimer = throwDuration;
        animator.SetTrigger("Throw-start");
        if (enemyPlayer != null &&
            enemyPlayer.Player_status != Status.Attack &&
            (enemyPlayer.transform.position.z - transform.position.z < 1.75f) &&
            canThrow)
        {
            Debug.Log("投げ成功");
            enemyPlayer.transform.Translate(0f, 0f, -0.0025f);      // 敵を少し引き寄せる
            enemyPlayer.animator.SetTrigger("Thrown");              // 敵に投げられアニメーションを再生させる
            enemyPlayer.damege(5);                                  // 敵に固定ダメージ5を与える
            canThrow = false;                                       // 一度成功したら再度投げが発動しないようにする
        }

        if (enemy != null &&
            enemyPlayer.Player_status != Status.Attack &&
            (enemyPlayer.transform.position.z - transform.position.z < 1.75f) &&
            canThrow)
        {
            Debug.Log("投げ成功");
            enemyPlayer.transform.Translate(0f, 0f, -0.0025f);     // 敵を少し引き寄せる
            enemyPlayer.animator.SetTrigger("Thrown");             // 敵に投げられアニメーションを再生させる
            enemyPlayer.damege(5);                                 // 敵に固定ダメージ5を与える
            canThrow = false;                                // 一度成功したら再度投げが発動しないようにする
        }
    }

    // 攻撃系トリガーの予約をすべてクリアする（前の攻撃予約が残って誤発火するのを防ぐ）
    void ResetAttackTriggers()
    {
        animator.ResetTrigger("Punch");
        animator.ResetTrigger("Flying-kick");
        animator.ResetTrigger("Kick");
        animator.ResetTrigger("Jump");
    }

    // 拘束中の行動（攻撃・ガード・投げ）のタイマーを進め、時間切れになったらIdleへ戻す
    void TickBusyState()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        DisableAllHitboxes();
        isGuarding = false;
        canThrow = true;
        currentState = PlayerState.Idle;
    }

    //-----------------------------------------------------
    // 復活チャレンジ（ダウン中の処理）
    //-----------------------------------------------------
    // HPが0になった際に毎フレーム呼ばれる、根性復活（ボタン連打による復活）の処理
    void HandleKnockedDown()
    {
        currentState = PlayerState.KnockedDown;

        //gameMNG.PlayerUI(rebornTimer, mashCount);

        rebornTimer += Time.deltaTime;
        Player_status = Status.Reborn;
        if (gameMNG != null) gameMNG.SettestStatus(Status.Reborn);

        //ダウンした瞬間、一度だけ顔・拳へのクローズアップカメラを開始する
        if (!rebornCamStarted && fightingCamera != null)
        {
            fightingCamera.StartRebornCloseUp(transform);
            rebornCamStarted = true;
        }

        // 復活に必要な連打回数のしきい値（復活回数が増えるほど厳しくなる）
        int mashThreshold = mashThresholdBase + mashThresholdStep * rebornCount;

        if (rebornTimer < rebornTimeLimit)
        {
            if (mashCount <= mashThreshold)
            {
                //連打の進捗に応じて復活レベルを算出し、カメラを徐々に引かせる
                if (fightingCamera != null && mashThreshold > 0)
                {
                    float progress = (float)mashCount / mashThreshold;
                    int level = Mathf.Clamp(Mathf.FloorToInt(progress * fightingCamera.rebornMaxLevel), 0, fightingCamera.rebornMaxLevel);
                    fightingCamera.SetRebornLevel(level);
                }
            }
            else
            {
                //復活成功
                HP = rebornHp;
                rebornCount++;
                mashCount = 0;
                rebornTimer = 0f;
                currentState = PlayerState.Idle;
                Player_status = Status.Live;
                //UIにHPを反映させるように指示
                if (gameMNG != null) gameMNG.Player_ReduceHP(HP, PlayerName);
                //根性復活成功！咆哮して立ち上がる漢を中心に、カメラが180度高速で回り込む
                if (fightingCamera != null)
                {
                    fightingCamera.SetRebornLevel(fightingCamera.rebornMaxLevel);
                    fightingCamera.TriggerRebornStandUpOrbit(transform);
                }
                rebornCamStarted = false; //次回のダウンに備えてリセット
            }
        }
        else
        {
            //制限時間内に復活できず力尽きた
            currentState = PlayerState.Dead;
            Player_status = Status.Dead;
            if (gameMNG != null) gameMNG.SettestStatus(Status.Dead);

            if (fightingCamera != null)
            {
                fightingCamera.ClearReborn();
            }
            rebornCamStarted = false;
        }
    }

    //-----------------------------------------------------
    // 衝突・トリガー
    //-----------------------------------------------------

    // 物理的な衝突が発生した時に呼ばれる。地面との接触判定（着地）に使用
    void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.CompareTag("Ground"))
        {
            Jumpflag = true;
        }
    }

    // トリガー判定の当たり判定に何かが接触した時に呼ばれる。敵の攻撃を受けた時の処理を行う
    //
    // ★修正済み：この処理は「相手の攻撃用ヒットボックスが自分に触れた場合」だけに
    //   限定する。以前は「相手プレイヤーのタグを持つ何か」に触れただけで反応していたため、
    //   自分の攻撃ヒットボックスが相手の体に当たった瞬間、攻撃側の自分にもこの
    //   イベントが飛んできて、誤って自分自身にダメージが入っていた。
    void OnTriggerEnter(Collider collision)
    {
        //地面に当たっている場合は無視
        if (collision.gameObject.CompareTag("Ground")) return;

        //自分自身のタグ、またはすでに倒れている場合は無視
        if (collision.gameObject.tag == this.PLayerTagName || HP <= 0) return;

        if (enemyPlayer == null) return;

        // ★追加：触れてきたコライダーが「敵プレイヤーの攻撃用ヒットボックス」で
        //   なければ、このイベントは無視する。
        //   （＝自分のヒットボックスが敵の体に当たっただけの、攻撃側視点のイベント）
        if (!System.Array.Exists(enemyPlayer.AttackHitboxes, hb => hb == collision))
        {
            return;
        }

        //ここまで来たら「敵の攻撃用ヒットボックスが自分の体に当たった」＝正真正銘の被弾
        if (isGuarding)
        {
            // 仁王立ち（ガード）中に被弾した場合の処理
            atk += enemyPlayer.atk;                  // ガード成功で自分の攻撃力に敵の攻撃力を上乗せする
            Debug.Log("漢!!");
            se.PlayOneShot(MenBlock_se);       // ガード成功の効果音を再生
            HP -= enemyPlayer.atk / 2;                // ガード中はダメージを半減させる

            guardComboCount++;
            if (fightingCamera != null)
            {
                fightingCamera.OnGuardImpact(transform, guardComboCount);
            }
        }
        else
        {
            // ガードしていない状態で被弾した場合の処理
            guardComboCount = 0;
            animator.SetTrigger("Hit");

            Vector3 hitPoint = collision.ClosestPoint(collision.transform.position);
            ParticleSystem hitParticle = Instantiate(Hit_particle, hitPoint, Quaternion.Euler(-90f, 0f, 0f));
            hitParticle.Play();
            Destroy(hitParticle.gameObject, 1.0f);

            HP -= enemyPlayer.atk;
        }

        //UIにHPを減らすように指示
        if (gameMNG != null)
        {
            gameMNG.Player_ReduceHP(HP, PlayerName);
        }
        else
        {
            Debug.LogError("gameMNGがnullのためHP表示を更新できません。ManagerObjectの配置を確認してください。");
        }
        enemyPlayer.atk = 10;   // 敵の攻撃力を初期値に戻す（一度使ったらリセット）

        if (HP < 0) HP = 0;
    }

    //-----------------------------------------------------
    // 当たり判定ユーティリティ
    //-----------------------------------------------------

    // 全身の攻撃用当たり判定コライダーを一括でOFFにする
    void DisableAllHitboxes()
    {
        foreach (var hitbox in allHitboxes)
        {
            hitbox.enabled = false;
        }
    }

    //-----------------------------------------------------
    // 外部から呼ばれるダメージ処理
    //-----------------------------------------------------
    // 外部（敵など）から呼び出される、プレイヤーがダメージを受けるための公開メソッド
    // n: 受けるダメージ量
    public void damege(int n)
    {
        HP -= n;
        if (HP < 0) HP = 0;
        // ★元コードのまま維持。"Enemy_ReduceHP"という名前だが実際にはプレイヤー自身のHPを渡している。
        //   GameMNG側の実装次第では意図通りかもしれないが、要確認。
        if (gameMNG != null)
        {
            gameMNG.Enemy_ReduceHP(HP);
            //※開発中
            //相手のプレイヤーの型を取得してその型のに適したUIの表示を変更する予定。
            //gameMNG.Player_ReduceHP(HP, Enemyplayer);
        }
        else
        {
            Debug.LogError("gameMNGがnullのためHP表示を更新できません。ManagerObjectの配置を確認してください。");
        }
    }

    // ★削除：EnemyPlayer() / TakeDamage() は OnTriggerEnter と組み合わさって
    //   二重ダメージ・自傷ダメージの原因になっていたため撤去した。
    //   ダメージ適用は OnTriggerEnter 内の1箇所に一本化している。
    //   もし「攻撃側が能動的に相手へダメージを与える」設計に変更したい場合は、
    //   OnTriggerEnter側のダメージ処理をこちらに移し替える形で作り直すこと。
}