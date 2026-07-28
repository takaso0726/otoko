using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

//=====================================================
// ★このスクリプトはPlayerInputコンポーネントとセットで使用します。
//   PlayerInputの Behavior は「Send Messages」に設定してください。
//   Actions には PlayerControls.inputactions（Playerマップ）を割り当ててください。
//   誰の入力かはPlayerInputManagerがデバイス単位で自動的に振り分けます。
//=====================================================
// プレイヤーキャラクターの移動・攻撃・被弾・復活などの一連の挙動を管理するメインスクリプト
[RequireComponent(typeof(PlayerInput))]
public class Player : MonoBehaviour
{
    // GameMNGへの参照。毎回探すと負荷やタイミング次第でnullを返しやすいため、
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
    // ★デバッグ
    //=====================================================
    [Header("デバッグ設定")]
    [SerializeField] bool enableDebugLog = true; // trueの間だけ本スクリプト内のDebug.Logを出力する（Debug.LogErrorは不具合検知のため常時出力）

    // 本スクリプト内のDebug.Log呼び出しはすべてこのメソッド経由にする。
    // enableDebugLogをfalseにすればインスペクターから一括でログ出力を止められる。
    void DLog(string message)
    {
        if (enableDebugLog) Debug.Log(message);
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

    [Header("擬音演出（ドカン・ドドン等）")]
    public HitEffectData punchHitEffectData;   // パンチ（空中攻撃含む）被弾時に出す擬音の設定
    public HitEffectData kickHitEffectData;    // キック（通常/上/下すべて共通）被弾時に出す擬音の設定
    public HitEffectData guardHitEffectData;   // 仁王立ちガード成功時に出す擬音の設定（未設定なら出さない）
    public HitEffectData mashHitEffectData;    // 根性復活の連打1回ごとに出す擬音の設定（未設定なら出さない）
    public HitEffectData missHitEffectData;    // 攻撃が空振りした時に出す擬音の設定（未設定なら出さない）

    //=====================================================
    // ★ヒットストップ設定
    //=====================================================
    [Header("ヒットストップ設定")]
    [SerializeField] bool enableHitStop = true;             // ヒットストップ機能を使うかどうか
    [SerializeField] float hitStopDuration = 0.08f;         // 通常被弾時に少しだけ動けなくなる時間（秒）
    [SerializeField] float guardHitStopDuration = 0.05f;    // 仁王立ちガード成功時に少しだけ動けなくなる時間（秒）
    [SerializeField] bool freezeAnimatorDuringHitStop = true; // ヒットストップ中はアニメーションも一瞬止めるか

    private float hitStopTimer = 0f;   // 残りヒットストップ時間。0より大きい間は入力処理をすべてスキップする
    private bool pendingHeadHitAnimation = false; // ヒットストップ終了時にHeadHitアニメーションを再生するか（ガード成功時はfalse）

    //=====================================================
    // ★仁王立ち（ガード）成功エフェクト設定
    //=====================================================
    [Header("仁王立ち成功エフェクト設定")]
    [SerializeField] bool enableGuardSuccessEffect = true;   // ガード成功時にエフェクトを出すかどうか
    [SerializeField] ParticleSystem guardSuccessEffectPrefab; // ガード成功時に生成するパーティクル（未設定なら出さない）
    [SerializeField] Vector3 guardSuccessEffectOffset = new Vector3(0f, 1.0f, 0f); // 生成位置のオフセット（プレイヤー基準）
    [SerializeField] float guardSuccessEffectLifetime = 1.0f; // 生成したエフェクトを破棄するまでの時間（秒）

    //=====================================================
    // ★ガード成功による攻撃力上昇中の持続エフェクト設定
    //   ガード成功で攻撃力が上昇している間（＝次の自分の攻撃が当たるまで）、
    //   プレイヤーに追従してループ再生し続けるエフェクト。
    //=====================================================
    [Header("攻撃力上昇中エフェクト設定")]
    [SerializeField] bool enableGuardBuffEffect = true;        // 攻撃力上昇中のエフェクトを出すかどうか
    [SerializeField] ParticleSystem guardBuffEffectPrefab;     // 上昇中に出し続けるパーティクル（Loopオン推奨・未設定なら出さない）
    [SerializeField] Vector3 guardBuffEffectOffset = new Vector3(0f, 1.0f, 0f); // 生成位置のオフセット（プレイヤー基準）

    private bool isGuardBuffed = false;               // 現在ガード成功による攻撃力上昇中かどうか
    private ParticleSystem activeGuardBuffEffect;      // 生成中の攻撃力上昇エフェクトの参照（多重生成防止・停止処理用）
    //=====================================================
    // ---- 漢気ゲージ(必殺技ゲージ) ----
    //=====================================================
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
    Animator currentanimator;                               //現在のアニメーションを管理する変数
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

    // ★追加：擬音演出（パンチ/キックで表示を分ける）用に、
    //   自分が今どの攻撃をしているかを外部（被弾した相手）から問い合わせるためのプロパティ。
    //   currentStateはprivateなので、これ経由でしか攻撃種別が見えないようにしている。
    //   AttackType自体はPlayer/Enemy共通の独立ファイル(AttackType.cs)で定義している。
    public AttackType CurrentAttackType
    {
        get
        {
            switch (currentState)
            {
                case PlayerState.Punch: return AttackType.Punch;
                case PlayerState.Kick: return AttackType.Kick;
                case PlayerState.UpKick: return AttackType.UpKick;
                case PlayerState.DownKick: return AttackType.DownKick;
                default: return AttackType.None;
            }
        }
    }

    // ★追加：攻撃種別に応じて、自分が持っているHitEffectDataのどれを使うかを返す。
    //   被弾した相手側から「あなたの攻撃はどの擬音を使う？」と問い合わせられるためのメソッド。
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

    // ★追加：空振り演出用。攻撃を出した瞬間にfalseにし、相手にヒットした瞬間trueにする。
    //   攻撃モーション終了時にまだfalseなら「空振り」とみなす。
    private bool attackLandedThisAttack = false;

    // ★追加：被弾した相手側から「あなたの攻撃、当たりましたよ」と通知してもらうための公開メソッド。
    public void NotifyAttackLanded()
    {
        attackLandedThisAttack = true;

        // ガード成功で上昇していた攻撃力は「次の攻撃が当たるまで」の効果なので、
        // 自分の攻撃が実際に相手へ命中したこのタイミングでバフとエフェクトを終了する。
        if (isGuardBuffed)
        {
            isGuardBuffed = false;
            StopGuardBuffEffect();
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    // 各種コンポーネント・子オブジェクトの当たり判定の取得と初期化を行う
    void Start()
    {
        //男気ゲージに使用する。素のatkを参照するために代入
        baseAtk = atk;
        // 自分にアタッチされているPlayerInputコンポーネントを取得
        playerInput = GetComponent<PlayerInput>();
        // どのプレイヤー番号・どのデバイスが紐づいているかログ出力（デバッグ用）
        DLog($"{gameObject.name} : PlayerIndex={playerInput.playerIndex} / Device={(playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "なし")}");

        rb = GetComponent<Rigidbody>();		//PlayerのRigidbodyを取得
        animator = GetComponent<Animator>();
        se = GetComponent<AudioSource>();

        // GameMNGを名前に依存しない方法で探してキャッシュしておく。
        // （オブジェクト名が"ManagerObject"や"GameMNG"でなくても正しく取得できるようにする）
        // 見つからない場合はここでハッキリ警告を出し、以降のNullReferenceExceptionを防ぐ。
        gameMNG = FindAnyObjectByType<GameMNG>();
        if (gameMNG == null)
        {
            Debug.LogError("シーン内にGameMNGコンポーネントを持つGameObjectが見つかりません。" +
                "配置し忘れ／非アクティブ状態になっていないか確認してください。");
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

        // 全身の攻撃用当たり判定コライダーを一括でOFFにする
        DisableAllHitboxes();

        //ステータスを初期化する
        currentState = PlayerState.Idle;
        Player_status = Status.Live;

        // ★デバッグ用：自分のヒットボックスが正しく取得できているか確認
        foreach (var hb in allHitboxes)
        {
            DLog($"[{gameObject.name}] hitbox取得: {(hb != null ? hb.name + " / owner=" + hb.transform.root.name : "null!")}");
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

            // 連打1回ごとの擬音演出（「グッ！」「ンン！」等）。
            // 攻撃者がいないので、常にプレイヤーの正面方向を基準にHitEffectData側のBase Angleで散らす。
            if (HitEffectSpawner.Instance != null && mashHitEffectData != null)
            {
                HitEffectSpawner.Instance.SpawnAtDirection(mashHitEffectData, transform.position, transform.forward);
            }

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
        // ヒットストップ処理を最優先で消化する。動けない間は他の入力・状態処理を一切行わない。
        if (hitStopTimer > 0f)
        {
            hitStopTimer -= Time.deltaTime;
            if (hitStopTimer <= 0f)
            {
                EndHitStop();
            }
            ClearInputIntents();
            return;
        }

        // HP判定・死亡処理は、コントローラーの有無に関係なく常に実行する
        if (HP <= 0)
        {
            if (currentState != PlayerState.Dead)
            {
                HandleKnockedDown();
            }
            ClearInputIntents();
            return;
        }
        // ここから下は「操作入力の受付」なので、コントローラー未割り当てなら止める
        if (playerInput != null && !playerInput.enabled)
        {
            ClearInputIntents();
            return;
        }

        bool isFree = currentState == PlayerState.Idle
                   || currentState == PlayerState.Move
                   || currentState == PlayerState.Crouch;
        if (isFree)
        {
            HandleFreeInput();
        }
        else
        {
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
    // ヒットストップ
    //-----------------------------------------------------
    // durationの間だけ、そのフレームから入力・状態更新を止めて「少しだけ動けない」状態にする。
    // すでにヒットストップ中の場合は、残り時間を延ばしすぎないよう長い方の時間を採用する。
    // playHeadHitAnimation: ヒットストップが終わった瞬間に"HeadHit"（被弾）アニメーションを再生するか。
    //   通常被弾はtrue、仁王立ちガード成功時はfalse（ガードのポーズを崩したくないため）を渡す。
    void StartHitStop(float duration, bool playHeadHitAnimation = true)
    {
        if (!enableHitStop || duration <= 0f) return;

        hitStopTimer = Mathf.Max(hitStopTimer, duration);
        pendingHeadHitAnimation = playHeadHitAnimation;

        if (freezeAnimatorDuringHitStop && animator != null)
        {
            // ★修正：ここで即座にHeadHitを再生すると、直後にspeedを0にするせいで
            //   1フレーム目の姿勢のまま止まってしまい「アニメーションが再生された」ようには見えなかった。
            //   なので今は「その場でアニメーションを一時停止させる」だけにし、
            //   実際にHeadHitを再生するのはヒットストップが終わるタイミング（EndHitStop）に任せる。
            animator.speed = 0f;
        }

        DLog($"[{PlayerName}] ヒットストップ開始 duration={duration}秒");
    }

    // ヒットストップ終了時にアニメーション速度を元に戻し、必要ならHeadHitアニメーションを再生する
    void EndHitStop()
    {
        if (freezeAnimatorDuringHitStop && animator != null)
        {
            animator.speed = 1f;

            // ヒットストップで一瞬止めていたぶん、止まった直後に被弾（HeadHit）アニメーションを再生する。
            // ガード成功時のヒットストップ（pendingHeadHitAnimation=false）ではここは実行されない。
            if (pendingHeadHitAnimation)
            {
                animator.Play("HeadHit", 0, 0.0f);
            }
        }

        DLog($"[{PlayerName}] ヒットストップ終了");
    }

    //-----------------------------------------------------
    // ガード成功時の攻撃力上昇エフェクト（次の攻撃が当たるまで出し続ける）
    //-----------------------------------------------------

    // 攻撃力上昇エフェクトを開始する。すでに出ている場合は再生し直すだけで多重生成はしない。
    void StartGuardBuffEffect()
    {
        if (!enableGuardBuffEffect || guardBuffEffectPrefab == null) return;

        if (activeGuardBuffEffect != null)
        {
            // 連続でガード成功した場合はそのまま再生を継続（作り直さない）
            if (!activeGuardBuffEffect.isPlaying) activeGuardBuffEffect.Play();
            return;
        }

        activeGuardBuffEffect = Instantiate(
            guardBuffEffectPrefab,
            transform.position + guardBuffEffectOffset,
            Quaternion.Euler(-90f, 0f, 0f),
            transform); // プレイヤーに追従させるため子オブジェクトにする
        activeGuardBuffEffect.transform.localPosition = guardBuffEffectOffset;
        activeGuardBuffEffect.Play();

        DLog($"[{PlayerName}] 攻撃力上昇エフェクト開始");
    }

    // 攻撃力上昇エフェクトを停止する（次の攻撃が当たった時に呼ばれる）
    void StopGuardBuffEffect()
    {
        if (activeGuardBuffEffect == null) return;

        // 新規パーティクルの発生だけ止め、出ている分は自然にフェードアウトさせる
        activeGuardBuffEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(activeGuardBuffEffect.gameObject, activeGuardBuffEffect.main.startLifetime.constantMax + 0.5f);
        activeGuardBuffEffect = null;

        DLog($"[{PlayerName}] 攻撃力上昇エフェクト終了");
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
        attackLandedThisAttack = false; // 空振り判定用にリセット

        if (Jumpflag)
        {
            //弱攻撃(パンチ)
            animator.SetTrigger("Punch");
            RightHand.enabled = true;
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
        attackLandedThisAttack = false; // 空振り判定用にリセット

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
            DLog("投げ成功");
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
            DLog("投げ成功");
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

        // 攻撃系の状態（パンチ/キック/上キック/下キック）が、
        // 一度も命中通知(NotifyAttackLanded)を受けないまま終了したら「空振り」とみなす
        if (CurrentAttackType != AttackType.None && !attackLandedThisAttack)
        {
            if (HitEffectSpawner.Instance != null && missHitEffectData != null)
            {
                //出す位置の調整
                Vector3 transkunn = transform.position;
                transkunn.y += 2.0f;
                HitEffectSpawner.Instance.SpawnAtDirection(missHitEffectData, transkunn, transform.forward);
            }
        }

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
            DLog($"[{PlayerName}] 根性復活失敗。HP={HP}のままDead状態へ移行。");
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

        // ★修正：相手が「対人戦のPlayer(enemyPlayer)」か「対CPU戦のEnemy(enemy)」かを
        //   それぞれ判定する。以前はenemyPlayerしか見ておらず、enemy(CPU)の攻撃が
        //   一切ヒット判定されずHPが減らないバグの原因になっていた。
        bool isEnemyPlayerAttack = enemyPlayer != null
            && enemyPlayer.AttackHitboxes != null
            && System.Array.Exists(enemyPlayer.AttackHitboxes, hb => hb == collision);

        bool isEnemyAttack = !isEnemyPlayerAttack && enemy != null
            && enemy.AttackHitboxes != null
            && System.Array.Exists(enemy.AttackHitboxes, hb => hb == collision);

        // どちらの攻撃用ヒットボックスでもなければ、このイベントは無視する
        //   （＝自分のヒットボックスが相手の体に当たっただけの、攻撃側視点のイベント等）
        if (!isEnemyPlayerAttack && !isEnemyAttack)
        {
            return;
        }

        //今回被弾させてきた相手の攻撃力を取得（対人戦かCPU戦かで参照先を切り替える）
        int attackerAtk = isEnemyPlayerAttack ? enemyPlayer.atk : enemy.atk;

        //ここまで来たら「敵の攻撃用ヒットボックスが自分の体に当たった」＝正真正銘の被弾
        if (isGuarding)
        {
            // 仁王立ち（ガード）中に被弾した場合の処理
            atk += attackerAtk;                  // ガード成功で自分の攻撃力に敵の攻撃力を上乗せする（次の自分の攻撃が当たるまで持続）
            DLog("漢!!");
            se.PlayOneShot(MenBlock_se);       // ガード成功の効果音を再生
            HP -= attackerAtk / 2;                // ガード中はダメージを半減させる

            // 攻撃力上昇中であることを示すエフェクトを出し続ける（次に自分の攻撃が当たるまで）
            isGuardBuffed = true;
            StartGuardBuffEffect();

            // ガード成功時のヒットストップ（少しだけ動けなくする）。
            // ガードのポーズを崩したくないのでHeadHitアニメーションは再生しない。
            StartHitStop(guardHitStopDuration, false);

            guardComboCount++;
            if (fightingCamera != null)
            {
                fightingCamera.OnGuardImpact(transform, guardComboCount);
            }

            // ガード成功の擬音演出（攻撃者→自分の方向を基準に、空白地帯へ表示）
            Transform guardAttackerTf = isEnemyPlayerAttack ? enemyPlayer.transform : enemy.transform;
            if (HitEffectSpawner.Instance != null && guardHitEffectData != null)
            {
                HitEffectSpawner.Instance.Spawn(guardHitEffectData, guardAttackerTf.position, transform.position);
            }

            // ガード成功専用エフェクト（インスペクターで設定したパーティクルを生成）
            if (enableGuardSuccessEffect && guardSuccessEffectPrefab != null)
            {
                ParticleSystem guardSuccessEffect = Instantiate(
                    guardSuccessEffectPrefab,
                    transform.position + guardSuccessEffectOffset,
                    Quaternion.Euler(-90f, 0f, 0f));
                guardSuccessEffect.Play();
                Destroy(guardSuccessEffect.gameObject, guardSuccessEffectLifetime);
                DLog($"[{PlayerName}] ガード成功エフェクトを再生");
            }

            // ガードされてもヒットはヒット（空振りではない）なので攻撃側に通知
            if (isEnemyPlayerAttack) enemyPlayer.NotifyAttackLanded();
            else if (enemy != null) enemy.NotifyAttackLanded();
        }
        else
        {
            // ガードしていない状態で被弾した場合の処理
            guardComboCount = 0;
            animator.SetTrigger("Hit");

            // 通常被弾時のヒットストップ（少しだけ動けなくする）
            StartHitStop(hitStopDuration);

            Vector3 hitPoint = collision.ClosestPoint(collision.transform.position);
            ParticleSystem hitParticle = Instantiate(Hit_particle, hitPoint, Quaternion.Euler(-90f, 0f, 0f));
            hitParticle.Play();
            Destroy(hitParticle.gameObject, 1.0f);

            // 「ドカン」「ドドン」等の擬音演出。
            // 攻撃者に今の攻撃タイプ(パンチ/キック)を問い合わせて、対応する擬音データを選ぶ。
            // 角度・距離はHitEffectData側（Inspector）で調整する。
            Transform attackerTf = isEnemyPlayerAttack ? enemyPlayer.transform : enemy.transform;
            HitEffectData selectedHitEffect = null;

            if (isEnemyPlayerAttack)
            {
                // 対人戦：相手Playerの現在の攻撃タイプに応じたHitEffectDataを取得
                selectedHitEffect = enemyPlayer.GetHitEffectDataFor(enemyPlayer.CurrentAttackType);
                enemyPlayer.NotifyAttackLanded(); // 空振りではなく命中したことを攻撃側へ通知
            }
            else if (enemy != null)
            {
                // 対CPU戦：Enemy側の現在の攻撃タイプに応じたHitEffectDataを取得
                selectedHitEffect = enemy.GetHitEffectDataFor(enemy.CurrentAttackType);
                enemy.NotifyAttackLanded(); // 空振りではなく命中したことを攻撃側へ通知
            }

            if (HitEffectSpawner.Instance != null && selectedHitEffect != null)
            {
                HitEffectSpawner.Instance.Spawn(selectedHitEffect, attackerTf.position, hitPoint);
            }

            HP -= attackerAtk;
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

        //攻撃力を初期値に戻す（一度使ったらリセット）
        if (isEnemyPlayerAttack) enemyPlayer.atk = 10;
        else enemy.atk = 10;

        if (HP < 0) HP = 0;

        //デバッグログ：誰からどれだけダメージを受けてHPがいくつになったか
        string attackerName = isEnemyPlayerAttack ? enemyPlayer.PlayerName : "Enemy(CPU)";
        DLog($"[{PlayerName}] {attackerName}から被弾。ダメージ={attackerAtk}{(isGuarding ? "(ガード半減)" : "")} / 残りHP={HP}");
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

        //デバッグログ：ダメージ量と残りHP
        DLog($"[{PlayerName}] damege()呼び出し。ダメージ={n} / 残りHP={HP}");

        // ★元コードのまま維持。"Enemy_ReduceHP"という名前だが実際にはプレイヤー自身のHPを渡している。
        //   GameMNG側の実装次第では意図通りかもしれないが、要確認。
        if (gameMNG != null)
        {
            gameMNG.Player_ReduceHP(HP, PlayerName);
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

    //==============================================
    // ---- 漢気ゲージ操作 ----
    //==============================================
    //プレイヤーに攻撃(パンチ・キック等)が当たった時にPlayer側から呼び出す想定の公開メソッド。
    //※注意: 現在はUpdate()内でプレイヤーHPの減少を自動検知してゲージを増やしているため、
    //   このメソッドを別途呼び出すと二重加算になります。Player.cs側から明示的に呼ぶ場合は、
    //   Update()内のHP減少検知処理を削除するかコメントアウトしてください。
    public void NotifyAttackLandedOnPlayer()
    {
        AddKankiGauge(gaugeGainOnHit);
    }

    //漢気ゲージを増やす(上限は1本分の合計値でクランプ)
    public void AddKankiGauge(float amount)
    {
        float max = kankiGaugePerBar * kankiGaugeBarCount;
        kankiGauge = Mathf.Clamp(kankiGauge + amount, 0f, max);
        UpdateAtkByGauge();

        //ゲージUI(2本分のSlider)を更新
        if (gameMNG != null)
        {
            gameMNG.Player_UpdateKankiGauge();
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
}