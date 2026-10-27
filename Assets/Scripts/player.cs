using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
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
        Throw,      // 投げ：掴んでいる側（掴み始め～投げ飛ばすまでの一連の拘束）
        Grabbed,    // ★追加：投げ：掴まれている側（脱出を試みている間の拘束）
        Thrown,     // ★追加：投げ：投げ飛ばされて放物線上を飛んでいる間（着地するまで）
        Special,    // 必殺技（漢気ゲージ消費技）
        KnockedDown,// ダウン中（根性復活チャレンジ中）
        Dead,       // 死亡（復活失敗）
        Stunned,    // ★追加：やられ状態（被弾後、攻撃ごとに設定したフレーム数だけ行動不能）
    }

    //=====================================================
    // ★デバッグ
    //=====================================================
    [Header("デバッグ設定")]
    [SerializeField] bool enableDebugLog = true; // trueの間だけ本スクリプト内のDebug.Logを出力する（Debug.LogErrorは不具合検知のため常時出力）

    // ★変更：F2/F4/F5/F6の各デバッグキーを、キャラクターごとにInspectorでON/OFF選択できるようにする。
    //   以前はF4/F5がPlayerName=="P1"固定、F6は専用のisAlwaysGuardDebugTargetという別名の変数だったが、
    //   命名を統一し、4つとも同じ仕組み（チェックボックスON＝そのキーの対象）で扱う。
    [Header("デバッグキー 有効設定（キャラクターごとに選択可能）")]
    [Tooltip("ONにすると、このキャラクターでF2キー（漢気ゲージデバッグログのON/OFF切替）が使えます。")]
    [SerializeField] bool enableF2DebugKey = true;
    [Tooltip("ONにすると、このキャラクターでF3キー（エフェクト発生ログのON/OFF切替）が使えます。")]
    [SerializeField] bool enableF3DebugKey = true;
    [Tooltip("ONにすると、このキャラクターでF4キー（ダウン中／Dead中からの強制復活）が使えます。")]
    [SerializeField] bool enableF4DebugKey = false;
    [Tooltip("ONにすると、このキャラクターでF5キー（HPを強制的に0にする）が使えます。")]
    [SerializeField] bool enableF5DebugKey = false;
    [Tooltip("ONにすると、このキャラクターでF6キー（常時仁王立ちデバッグモード）が使えます。")]
    [SerializeField] bool enableF6DebugKey = false;
    [Tooltip("ONにすると、このキャラクターでF7キー（漢気ゲージを満タンにする）が使えます。")]
    [SerializeField] bool enableF7DebugKey = false;
    [Tooltip("ONにすると、このキャラクターでF8キー（HPを最大値まで回復する）が使えます。")]
    [SerializeField] bool enableF8DebugKey = false;
    [Tooltip("ONにすると、このキャラクターでF10キー（やられ状態中に連続で攻撃が当たった回数の画面表示ON/OFF）が使えます。")]
    [SerializeField] bool enableF10DebugKey = false;
    [Tooltip("F10で表示するデバッグ文字（やられ状態／相手の技のクールタイム）のフォントサイズ。")]
    [SerializeField, Range(8, 48)] int f10DebugFontSize = 18;

    //=====================================================
    // ★追加：矢印キー（↑←→↓）で任意のアニメーションを再生する設定
    //   ・各キーを押した瞬間に、Inspectorで指定したアニメーションを1回再生する。
    //   ・「Use Trigger」ON：AnimatorのTriggerパラメータ名を指定（例：Punch / Kick / UpKick / DownKick / waza）
    //   ・「Use Trigger」OFF：Animatorのステート名を指定して直接再生（例：HeadHit）
    //   ・currentState（状態機械）は変更しないので、あくまで見た目の再生・確認用。当たり判定やダメージは発生しない。
    //   ・ダウン中／ヒットストップ中／掴まれ中／やられ中など、自由に動けない状態では再生しない。
    //=====================================================
    [System.Serializable]
    public class ArrowAnimationSetting
    {
        [Tooltip("再生するアニメーション名。Use TriggerがONならTriggerパラメータ名、OFFならAnimatorのステート名。空なら何もしない。")]
        public string animationName = "";
        [Tooltip("ON：animationNameをTriggerとして発火する／OFF：animationNameのステートを頭から直接再生する")]
        public bool useTrigger = true;
    }

    [Header("矢印キーでアニメーション再生")]
    [Tooltip("ONにすると、このキャラクターで矢印キー（↑←→↓）によるアニメーション再生が使えます。\n" +
             "※PlayerInputのキーボード操作で矢印キーを移動(Move)に割り当てている場合、移動も同時に入力される点に注意。")]
    [SerializeField] bool enableArrowKeyAnimation = false;
    [Tooltip("ONにすると、このキャラクターに割り当てられたゲームパッドの十字キー（上下左右）でも、下の同じ設定のアニメーションを再生できます。\n" +
             "※PlayerInputで十字キーをMoveに割り当てている場合、移動も同時に入力される点に注意。")]
    [SerializeField] bool enableDpadAnimation = false;
    [SerializeField] ArrowAnimationSetting upArrowAnimation = new ArrowAnimationSetting { animationName = "Anim-Up", useTrigger = true };       // ↑キー
    [SerializeField] ArrowAnimationSetting leftArrowAnimation = new ArrowAnimationSetting { animationName = "Anim-Left", useTrigger = true };       // ←キー
    [SerializeField] ArrowAnimationSetting rightArrowAnimation = new ArrowAnimationSetting { animationName = "Anim-Right", useTrigger = true };     // →キー
    [SerializeField] ArrowAnimationSetting downArrowAnimation = new ArrowAnimationSetting { animationName = "Anim-Down", useTrigger = true };   // ↓キー

    // 本スクリプト内のDebug.Log呼び出しはすべてこのメソッド経由にする。
    // enableDebugLogをfalseにすればインスペクターから一括でログ出力を止められる。
    void DLog(string message)
    {
        if (enableDebugLog) Debug.Log(message);
    }

    // ★追加：漢気ゲージ専用のデバッグログ。F2キーでON/OFFをトグルする。
    //   増減が多くログが流れやすいため、通常のenableDebugLogとは別に管理する。
    private bool kankiGaugeDebugLogEnabled = false;

    // 漢気ゲージ関連（増減・後退判定）のログはこのメソッド経由にする。
    // F2でトグルした時だけ出力される（enableDebugLogの影響は受けない）。
    void GaugeDLog(string message)
    {
        if (kankiGaugeDebugLogEnabled) Debug.Log(message);
    }

    // ★追加：エフェクト発生専用のデバッグログ。F3キーでON/OFFをトグルする。
    //   ヒット・ガード・必殺技・漢気ゲージ充填などのエフェクトが「生成された瞬間」にだけ出力される。
    //   通常のenableDebugLogとは独立して管理する（F2の漢気ゲージログと同じ方式）。
    private bool effectDebugLogEnabled = false;

    // エフェクト発生ログはすべてこのメソッド経由にする。F3でトグルした時だけ出力される。
    void EffectDLog(string message)
    {
        if (effectDebugLogEnabled) Debug.Log($"[EFFECT] {message}");
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

    //=====================================================
    // ★追加：自動向き合い設定
    //   相手（対人戦はenemyPlayer／対CPU戦はenemy）の方向へ、常に自動で向きを変える。
    //   ONの間は「移動方向を向く」処理を行わないため、後退する時も相手の方を向いたまま下がる。
    //   向きはZ軸（P1/P2の間合いが変化する軸）だけで判定する。
    //=====================================================
    [Header("自動向き合い設定")]
    [Tooltip("ONにすると、常に相手の方向へ自動で向きを変える（移動方向は向かない）。OFFにすると従来どおり移動方向を向く。")]
    [SerializeField] bool enableAutoFacing = true;
    [Tooltip("相手とのZ軸の距離がこの値より小さい時は向きを変えない（真横で重なった時に左右へ向きがブレ続けるのを防ぐ）。")]
    [SerializeField] float autoFaceDeadZone = 0.05f;
    [Tooltip("【CPU戦のみ有効】相手が反対側へ回り込んだ時、このZ距離以上離れて初めて向きを反転させる。密着中の押し合い・ノックバック等で左右が一瞬入れ替わっても反転しないようにするための値。")]
    [SerializeField] float autoFaceFlipThreshold = 1.0f;
    [Tooltip("ONにすると、攻撃・投げ・必殺技の最中は向きを変えず、終わってから相手へ向き直す。OFF（既定）なら技の最中でも常に相手を向く。")]
    [SerializeField] bool lockFacingDuringAction = false;

    //=====================================================
    // ★追加：リザルト画面モード
    //   ONの間は、攻撃ボタン（パンチ／キック）以外の操作を受け付けず、移動も制限する。
    //   ・スティック入力は無視（左右移動・しゃがみ・上下キックの分岐もすべて無効）
    //   ・ジャンプ／仁王立ち／投げ／必殺技／L1は無効
    //   ・被弾・ダメージ判定は行わない（攻撃は見た目だけ）
    //   ・向きの自動変更、押しのけ合い、移動範囲のクランプも行わない（配置した位置のまま動かない）
    //   パンチ／キックが実際に出るたびに ResultAttackPressed イベントが発行される（ひび割れ演出などが購読する）。
    //   攻撃のクールダウン（攻撃モーション中）に押したボタンは無視され、イベントも発行されない。
    //=====================================================
    [Header("リザルト画面モード")]
    [Tooltip("ONにすると、攻撃ボタン（パンチ／キック）以外の操作と移動を無効にする。通常はOFF。\n" +
             "下のAuto Detect Result SceneがONなら、InGame1V1ResultControllerのあるシーンでは自動的にONになる。")]
    [SerializeField] bool isResultMode = false;
    [Tooltip("ONにすると、シーン内にInGame1V1ResultControllerがある時（＝リザルト画面）、自動でリザルト画面モードにする。")]
    [SerializeField] bool autoDetectResultScene = true;

    // リザルト画面モード中に、パンチ／キックが実際に出た瞬間（EnterPunch／EnterKick）に発行される。
    // 攻撃のクールダウン中（攻撃モーション中）のボタン押下では発行されない＝発行間隔は攻撃のクールダウンに一致する。
    public static event System.Action<Player> ResultAttackPressed;

    public bool IsResultMode => isResultMode;

    // ★追加：リザルト画面で、このPlayerの攻撃ボタン入力を受け付けるか。
    //   InGame1V1ResultControllerが「勝者以外の操作は無効」にする時、勝者以外のPlayerをfalseにする。
    bool resultInputEnabled = true;
    public void SetResultInputEnabled(bool enabled) { resultInputEnabled = enabled; }

    // ★追加：このPlayerに紐づいているPlayerInput（1P=playerIndex 0／2P=1、使用デバイスの取得に使う）
    public PlayerInput PlayerInputComponent => playerInput != null ? playerInput : GetComponent<PlayerInput>();

    // リザルト画面モードのON/OFFを切り替える（InGame1V1ResultControllerからも呼ばれる）
    public void SetResultMode(bool enable)
    {
        isResultMode = enable;
        if (!enable) return;

        // 入力の残りを消し、移動・しゃがみ状態からも抜けておく
        moveInput = Vector2.zero;
        ClearInputIntents();
        if (currentState == PlayerState.Move || currentState == PlayerState.Crouch)
        {
            StopMoveAnimation();
            currentState = PlayerState.Idle;
        }
    }
    [SerializeField] float moveInputThreshold = 0.03f;    // 左右移動と判定するスティックの入力量
    [SerializeField] float crouchInputThreshold = -0.43f; // しゃがみ／下キックと判定するスティックの下入力量
    [SerializeField] float upKickInputThreshold = 0.25f;  // 上キックと判定するスティックの上入力量

    // ★追加：前進／後退アニメーション（frontMove / BackMove）の再生速度を、
    //   スティックの倒し具合（moveInput.xの絶対値）に応じて変化させるための範囲設定。
    //   Animator側で"MoveSpeedMultiplier"という名前のFloatパラメータを作成し、
    //   frontMove/BackMoveステートのSpeedにこのパラメータを乗算設定しておくこと（設定方法は後述）。
    [Header("移動アニメーション速度設定")]
    [SerializeField] float minMoveAnimSpeed = 0.4f;  // スティックを少しだけ倒した時の再生速度倍率
    [SerializeField] float maxMoveAnimSpeed = 1.4f;  // スティックを最大まで倒した時の再生速度倍率

    // ★追加：実際の移動速度（moveSpeed）もアナログ入力量に応じて可変にするための倍率設定。
    //   スティックを軽く倒した時にmoveSpeedが0近くまで下がってしまうと「動いていないように見える」ため、
    //   最低でもmoveSpeedのminMoveSpeedRatio倍は出るようにし、最大までいくとmoveSpeedそのまま(=1.0倍)にする。
    [SerializeField] [Range(0f, 1f)] float minMoveSpeedRatio = 0.4f; // 入力が最小(閾値ギリギリ)の時のmoveSpeedに対する割合

    //=====================================================
    // ★移動範囲制限（ワールド座標）
    //   ステージ外に出ないよう、X/Y/Z各軸ごとにワールド座標の移動可能範囲を制限する。
    //   通常の移動(Move)だけでなく、ジャンプ等でRigidbodyが動かした結果もLateUpdateで一括して制限する。
    //=====================================================
    [Header("移動範囲制限設定（ワールド座標）")]
    [Tooltip("ワールド座標の各軸の最小値（左端・下端・奥側の限界）")]
    [SerializeField] Vector3 minPosition = new Vector3(-10f, -10f, -10f);
    [Tooltip("ワールド座標の各軸の最大値（右端・上端・手前側の限界）")]
    [SerializeField] Vector3 maxPosition = new Vector3(10f, 10f, 10f);

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
    public int HP = 100;                     // 体力（現在値）
    public int maxHP = 100;                  // 体力の最大値（F8での全回復・復活時の回復割合の基準になる）
    public int atk = 10;                     // 現在の攻撃力（直近に出した攻撃の種類・漢気ゲージ・ガード成功で自動計算される。Inspectorの値は初期表示用）
    public Player.Status Player_status;        // 外部から参照される、プレイヤーの現在の大まかな状態

    //=====================================================
    // ★各アクションの持続時間（Inspectorで調整可能）
    //=====================================================
    [Header("アクション時間設定（フレーム：60フレーム=1秒）")]
    // ※パンチ／キック／上キック／下キックの拘束時間は、下の「攻撃ごとの設定」（各技のヘッダー）へ移動した。
    [SerializeField] int guardFrames = 30;    // 仁王立ちの拘束フレーム数
    [SerializeField] int throwFrames = 90;    // 投げの拘束フレーム数

    //=====================================================
    // ★攻撃の種類ごとの攻撃力（Inspectorで個別に調整可能）
    //   ここに新しい技を追加する場合は、対応するフィールドを増やし、
    //   GetAttackPower() の switch にもケースを追加すること。
    //=====================================================
    // ※パンチ／キック／上キック／下キック／必殺技の攻撃力は、下の「攻撃ごとの設定」（各技のヘッダー）へ移動した。
    [Header("投げの攻撃力")]
    [SerializeField] int throwAtk = 5;     // 投げ（つかみ）成立時の固定ダメージ
    [Tooltip("投げ成立で飛ばされた相手が、着地した後に【やられ状態（行動不能）】になるフレーム数（60F=1秒）。" +
             "着地した瞬間から数え始める。0だとやられ状態にならない。\n" +
             "※投げ不成立（掴みタイムアウト）で互いに吹き飛んだ場合は対象外。")]
    [SerializeField] int throwStunFrames = 30;

    //=====================================================
    // ★追加：投げ（掴み）仕様変更に伴う詳細設定
    //   掴み成立→(掴み状態中に脱出できなければ)→投げ飛ばし、という2段階の流れになる。
    //   throwFramesは「掴んでから実際に投げ飛ばすまでの拘束時間」として引き続き使用する
    //   （Animatorの"Throw-start"モーションの長さと合わせること）。
    //=====================================================
    [Header("投げ（掴み）詳細設定")]
    [Tooltip("HPが0の時に脱出（掴みを振りほどく）までに必要なフレーム数（60F=1秒）。値が大きいほど脱出しにくい＝投げられやすい。")]
    [SerializeField] int escapeFramesAtZeroHp = 90;
    [Tooltip("HPが満タンの時に脱出（掴みを振りほどく）までに必要なフレーム数（60F=1秒）。値が小さいほど脱出しやすい＝投げられにくい。")]
    [SerializeField] int escapeFramesAtFullHp = 24;
    [Tooltip("掴まれている間、ボタンやスティックを入力する度に、脱出に必要な残り時間からさらに追加で短縮するフレーム数。")]
    [SerializeField] int escapeReductionFramesPerInput = 9;
    [Tooltip("投げが成立した瞬間、相手に加える水平方向の初速。攻撃側の移動スティックの倒し方向（前/後）で向きが決まる。")]
    [SerializeField] float throwHorizontalSpeed = 6f;
    [Tooltip("投げが成立した瞬間、相手に加える上方向の初速。大きいほど放物線の弧が高くなる。")]
    [SerializeField] float throwUpSpeed = 5f;
    [Tooltip("★変更：掴み拘束時間が終わった時点で、掴んでいる側が方向スティックを入力していなかった場合の「投げ不成立」時に、" +
             "お互いを後方（相手から離れる方向）へ吹き飛ばす水平方向の初速。投げ成立時(throwHorizontalSpeed)より弱めが目安。")]
    [SerializeField] float grabFailHorizontalSpeed = 3f;
    [Tooltip("★追加：投げ不成立時に、お互いを後方へ吹き飛ばす上方向の初速。大きいほど放物線の弧が高くなる。")]
    [SerializeField] float grabFailUpSpeed = 3f;
    [Tooltip("★追加：掴みが成立してから、掴みモーション(Throw-start)を再生し続ける時間。この時間が過ぎたらアニメーションを一時停止し、" +
             "投げ成立(Throw-release)／投げ不成立(Throw-whiff)の瞬間まで掴んだポーズで止め続ける。" +
             "※下のenableThrowStartPlayがONの時だけ使われる。OFFの時は、手が相手に接触した瞬間にアニメーションを即座に止める。")]
    [SerializeField] int throwStartPlayFrames = 12;
    [Tooltip("★追加：ONにすると、手が相手に接触してから上のthrowStartPlayFramesフレームだけ掴みモーションを再生し続け、その後に止める。" +
             "OFF（既定）の時は、手が相手に接触した瞬間にアニメーションを即座に止める。")]
    [SerializeField] bool enableThrowStartPlay = false;
    [Tooltip("★追加：掴んでいる間に方向入力（前／後）をした時は、拘束時間の終了を待たずに即座に投げ飛ばす。\n" +
             "投げ飛ばした後、掴んでいた側が次の行動に移れるようになるまでの硬直フレーム数（60F=1秒）。\n" +
             "※拘束時間の残りがこれより短ければ、残り時間のほうが使われる。")]
    [SerializeField] int throwReleaseRecoveryFrames = 20;
    [Tooltip("★追加：掴みが成立してから、方向入力による即時投げを受け付け始めるまでのフレーム数（60F=1秒）。\n" +
             "この間は入力しても投げに移行せず、掴んだ状態を保つ。この間の入力は先行入力としては扱わず、\n" +
             "経過後も入力し続けている（押しっぱなし）場合に投げが決まる。")]
    [SerializeField] int throwInputStartDelayFrames = 10;
    [Tooltip("★追加：投げ動作の間、当たり判定をONにする部位。ここでONになった部位が相手の体に接触した瞬間に掴みが成立する。\n" +
             "既定は左手・左前腕。※投げ中にONになるコライダーは、すべて「掴みの接触」として扱われ、ダメージは発生しない。")]
    [SerializeField] HitboxSetting throwHitbox = new HitboxSetting { leftHand = true, leftForeArm = true };

    //=====================================================
    // ★ガード（仁王立ち）成功時の攻撃力上昇設定
    //   ★変更：以前は「相手の攻撃力を自分の攻撃力に加算」していたが、
    //   「次に与える技のダメージ倍率を上げる」方式に変更した。
    //=====================================================
    [Header("ガード成功時の攻撃力上昇設定")]
    [Tooltip("ガード成功時、次に自分が与える技のダメージ倍率。1.5なら次の技のダメージが1.5倍になる。\n" +
             "（漢気ゲージによる攻撃力補正とは別に、さらに掛け算される）\n" +
             "倍率は次の攻撃が相手に命中するまで持続する。1以下なら上昇なし。\n" +
             "★技を出す（命中する）前に続けてガードに成功すると、成功するたびにこの倍率が掛け算で重なっていく（1.5なら 1.5 → 2.25 → 3.375…）。")]
    [SerializeField] float guardSuccessNextAttackMultiplier = 1.5f;
    [Tooltip("ガード成功を重ねた時の、倍率の上限。0以下なら上限なし。")]
    [SerializeField] float guardSuccessMaxMultiplier = 0f;

    //=====================================================
    // ★ガード（仁王立ち）失敗時のペナルティ設定
    //   「失敗」＝相手の攻撃を一度も受け止めないまま、仁王立ちの拘束時間が終わった（空ガード）場合。
    //=====================================================
    [Header("ガード失敗時のペナルティ設定")]
    [Tooltip("ガード失敗後、漢気ゲージの増加量にかかる倍率。0.5なら半減。1なら影響なし。")]
    [Range(0f, 1f)]
    [SerializeField] float guardFailGaugeGainRate = 0.5f;
    [Tooltip("ガード失敗後、漢気ゲージの増加率が下がる時間（フレーム数。60F=1秒）。0なら下がらない。")]
    [SerializeField] int guardFailGaugePenaltyFrames = 180;
    [Tooltip("ガード失敗直後に受けるダメージにかかる倍率。1.5なら1.5倍。1なら影響なし。")]
    [SerializeField] float guardFailDamageMultiplier = 1.5f;
    [Tooltip("ガード失敗直後、受けるダメージが増える時間（フレーム数。60F=1秒）。0なら増えない。")]
    [SerializeField] int guardFailDamagePenaltyFrames = 60;

    private bool guardBlockedThisGuard = false; // 今の仁王立ちで、相手の攻撃を1回でも受け止めたか（失敗判定用）
    private int guardFailGaugeTimer = 0;        // 漢気ゲージ増加率ダウンの残りフレーム数
    private int guardFailDamageTimer = 0;       // 被ダメージ増加の残りフレーム数

    // ★追加：F10デバッグ表示用。直前に受けたダメージの記録
    private int lastDamageTaken = 0;            // 実際に減ったダメージ量（ガード半減・失敗ペナルティ反映後）
    private int lastDamageBase = 0;             // ペナルティ倍率を掛ける前のダメージ量
    private string lastDamageKind = "なし";     // 直前のダメージの種類（通常被弾／ガード(半減)／damege()）

    void RecordDamage(int damage, int baseDamage, string kind)
    {
        lastDamageTaken = damage;
        lastDamageBase = baseDamage;
        lastDamageKind = kind;
    }

    // ガード失敗ペナルティの残りフレームを消化する（Updateの先頭から毎フレーム呼ぶ）
    void TickGuardFailPenalty()
    {
        if (guardFailGaugeTimer > 0) guardFailGaugeTimer = Mathf.Max(guardFailGaugeTimer - frameStep, 0);
        if (guardFailDamageTimer > 0) guardFailDamageTimer = Mathf.Max(guardFailDamageTimer - frameStep, 0);
    }

    // 仁王立ちが空振り（1回も受け止めずに終了）した時に呼ぶ
    void ApplyGuardFailPenalty()
    {
        guardFailGaugeTimer = Mathf.Max(guardFailGaugePenaltyFrames, 0);
        guardFailDamageTimer = Mathf.Max(guardFailDamagePenaltyFrames, 0);
        DLog($"[{PlayerName}] ガード失敗：漢気ゲージ増加率×{guardFailGaugeGainRate}を{guardFailGaugeTimer}F／被ダメージ×{guardFailDamageMultiplier}を{guardFailDamageTimer}F");
    }

    //=====================================================
    // ★ガード（仁王立ち）成功時の、相手へのスタン設定
    //=====================================================
    [Header("ガード成功時の相手スタン設定")]
    [Tooltip("仁王立ちでガードに成功した時、攻撃してきた相手を行動不能（スタン）にするフレーム数（60F=1秒）。" +
             "この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。0だとスタンさせない。\n" +
             "※相手が必殺技・投げ（掴み中）・ダウン中などの場合はスタンさせない。")]
    [SerializeField] int guardSuccessStunFrames = 30;

    //=====================================================
    // ★根性復活（ダウン後の復活チャレンジ）設定
    //=====================================================
    [Header("復活（根性）設定")]
    [SerializeField] int rebornTimeLimitFrames = 300;  // 復活チャレンジの制限フレーム数（300F=5秒）
    [Range(0f, 1f)]
    [SerializeField] float rebornHpRatio = 0.3f;    // 復活成功時に回復するHPの割合（maxHPに対する割合。0.3なら最大HPの3割まで回復）
    [SerializeField] int mashThresholdBase = 11;    // 必要連打数の基準値
    [Tooltip("ダウン中の連打に使えるボタンの切り替え。\n" +
             "ON：どのボタン（ジャンプ・パンチ・キック・仁王立ち・投げ・L1）を押しても連打1回として数える。\n" +
             "OFF：パンチボタン（Bボタン）だけを連打として数える（他のボタンは無視）。")]
    [SerializeField] bool mashWithAnyButton = true;  // ★追加：ONならどのボタンでも連打OK／OFFならBボタン（パンチ）のみ
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
    // ★追加：行動SE。パンチ・キック・投げは、まず空振りかどうかを判定し、空振りならmissSeだけ／そうでなければその行動のSEを鳴らす。
    //   ジャンプ・ジャンプ着地・投げ飛ばし（投げ成立）は空振り判定をせず、そのまま鳴らす。
    //   Inspectorに、MP3等のAudioClipをドラッグ＆ドロップして設定する。未設定の技は鳴らさない。
    //   再生には、このGameObjectに AudioSource コンポーネントが必要。
    //=====================================================
    [Header("行動SE（パンチ・キック等。MP3のAudioClipを割り当て）")]
    [Tooltip("投げで相手を掴めた時のSE。掴めなかった時はMiss Seが鳴る。")]
    [SerializeField] AudioClip throwSe;
    [Tooltip("ジャンプした時のSE")]
    [SerializeField] AudioClip jumpSe;
    [Tooltip("投げが成立し、相手を投げ飛ばした瞬間のSE（掴めた瞬間のThrow Seとは別）")]
    [SerializeField] AudioClip throwLaunchSe;
    [Tooltip("ジャンプして地面に着地した時のSE（ジャンプ以外で空中に出た時の着地では鳴らない）")]
    [SerializeField] AudioClip landSe;
    [Tooltip("パンチが空振りでなかった（命中した）時のSE。空振りの時はMiss Seが鳴る。")]
    [SerializeField] AudioClip punchSe;
    [Tooltip("空中攻撃（飛び蹴り）が空振りでなかった（命中した）時のSE。")]
    [SerializeField] AudioClip flyingKickSe;
    [Tooltip("通常キックが空振りでなかった（命中した）時のSE。")]
    [SerializeField] AudioClip kickSe;
    [Tooltip("上キックが空振りでなかった（命中した）時のSE。")]
    [SerializeField] AudioClip upKickSe;
    [Tooltip("下キックが空振りでなかった（命中した）時のSE。")]
    [SerializeField] AudioClip downKickSe;
    [Tooltip("攻撃（パンチ・キック・上キック・下キック）が当たらなかった時、または投げで相手を掴めなかった時のSE。空振りの時は、この音だけを鳴らす。")]
    [SerializeField] AudioClip missSe;
    [Tooltip("空振りと判定するまでの、攻撃開始からのフレーム数（60F=1秒）。この時間が経っても未命中ならmissSeを鳴らす。\n0＝攻撃モーションが終わった時に鳴らす。\n※空振りと確定した後に命中しても、その攻撃の技SEは鳴らない。")]
    [SerializeField] int missSeJudgeFrames = 0;
    [Tooltip("行動SEの音量（0～1）")]
    [SerializeField, Range(0f, 1f)] float actionSeVolume = 1f;

    [Header("擬音演出（ドカン・ドドン等）")]
    public HitEffectData punchHitEffectData;   // パンチ（空中攻撃含む）被弾時に出す擬音の設定
    public HitEffectData kickHitEffectData;    // キック（通常/上/下すべて共通）被弾時に出す擬音の設定
    public HitEffectData guardHitEffectData;   // 仁王立ちガード成功時に出す擬音の設定（未設定なら出さない）
    public HitEffectData mashHitEffectData;    // 根性復活の連打1回ごとに出す擬音の設定（未設定なら出さない）
    public HitEffectData missHitEffectData;    // 攻撃が空振りした時に出す擬音の設定（未設定なら出さない）

    // ★追加：被弾擬音を「どのキャラの向き」の反対側に出すか
    public enum HitEffectFacingBasis
    {
        Attacker, // 攻撃したキャラの向き（左を向いて攻撃したら右へ／右を向いて攻撃したら左へ）
        Victim,   // 被弾したキャラの向き（左を向いている側が殴られたら右へ／右なら左へ）
    }
    [Tooltip("被弾した時の擬音を、攻撃したキャラ／被弾したキャラのどちらの向きの反対側に出すか。")]
    [SerializeField] HitEffectFacingBasis hitEffectFacingBasis = HitEffectFacingBasis.Attacker;

    //=====================================================
    // ★ヒットストップ設定
    //=====================================================
    [Header("ヒットストップ設定")]
    [SerializeField] bool enableHitStop = true;             // ヒットストップ機能を使うかどうか
    [Tooltip("CPU(Enemy)の攻撃を受けた時など、攻撃側に個別設定が無い場合に使う、通常被弾時のヒットストップのフレーム数（60F=1秒）。\n" +
             "対人戦でPlayerの攻撃を受けた時は、攻撃者側の「攻撃ごとの設定」のヒットストップ時間が使われる。")]
    [SerializeField] int hitStopFrames = 5;         // 通常被弾時（CPU等の攻撃側設定が無い場合）に少しだけ動けなくなるフレーム数
    [SerializeField] int guardHitStopFrames = 3;    // 仁王立ちガード成功時に少しだけ動けなくなるフレーム数
    [SerializeField] bool freezeAnimatorDuringHitStop = true; // ヒットストップ中はアニメーションも一瞬止めるか

    private int hitStopTimer = 0;   // 残りヒットストップフレーム数。0より大きい間は入力処理をすべてスキップする
    private bool pendingHeadHitAnimation = false; // ヒットストップ終了時に被弾アニメーションを再生するか（ガード成功時はfalse）

    // ★追加：被弾アニメーションの二重再生を防ぐための設定。
    //   以前は、被弾した瞬間にTrigger（Hit／Hit-front／Hit-back）で再生を始めたうえで、
    //   ヒットストップ終了時にHeadHitステートを頭から再生し直していたため、被弾アニメーションが2回再生されていた。
    //   今は、被弾アニメーションを1つだけ選んで「ヒットストップが終わった瞬間に1回だけ」再生する。
    // ★変更：既定をON（トリガーOFF）にした。被弾時にHit／Hit-front／Hit-backのTriggerは一切立てず、
    //   HeadHitステートを「ヒットストップ終了時（ヒットストップ無しならその場）に1回だけ」直接再生する。
    //   ※Inspectorでこの項目のチェックを外していた場合は、シリアライズ済みの値(OFF)が優先されるので、チェックを入れ直すこと。
    [Tooltip("ON（既定）：被弾Trigger（Hit／Hit-front／Hit-back）は立てず、HeadHitステートを1回だけ直接再生する。\n" +
             "OFF：被弾アニメーションをTrigger（Hit／Hit-front／Hit-back）で、ヒットストップ終了時に1回だけ再生する。")]
    [SerializeField] bool playHeadHitStateDirectly = true;
    private bool pendingHitTrigger = false;       // 予約中の被弾Triggerがあるか
    private bool pendingHitHasDirection = false;  // 攻撃者の方向が分かっているか（Hit-front／Hit-backを立てるか）
    private bool pendingHitFromFront = false;     // 攻撃者が自分の前方にいたか
    private bool hasPendingKnockback = false;    // ヒットストップ終了時にノックバックを与えるか
    private Vector3 pendingKnockbackVelocity;    // 与えるノックバックの初速（水平＋上方向）

    //=====================================================
    // ★攻撃ごとの設定（攻撃の種類ごと）
    //   各技のヘッダーの下に、次の順で並べている：
    //     ① 攻撃力 … この技が当たった時に相手へ与えるダメージ（漢気ゲージ補正の前の素の値）
    //     ② 攻撃後のクールダウン（拘束時間）… 攻撃を出した後、次の行動ができるまでの時間
//        ★空振り時(〇〇MissFrames)と命中時(〇〇HitFrames)を別々に設定できる。
//        どちらも「攻撃を出した瞬間からの合計時間」。命中した瞬間に、残り時間が命中時の値へ切り替わる。
    //     ③ ヒットストップ … この技を当てられた相手が止まる時間
    //     ④ ノックバック … この技を当てられた相手が飛ぶ量（攻撃者から離れる方向＋上方向）
    //     ⑤ やられ状態の時間 … この技を当てられた相手が行動不能になるフレーム数（60F=1秒）。0ならやられ状態にならない。
    //   ③④⑤は「攻撃する側」のInspectorで調整する。
    //   ※仁王立ちガードで防がれた時は、ヒットストップはguardHitStopFrames、ノックバックは無し。
    //   ※新しい技を追加する場合は、対応するフィールドを増やし、GetAttackPower() /
    //     GetCurrentHitStopFrames() / GetCurrentKnockbackSetting() / GetMultiHitSetting() の
    //     switchにもケースを追加すること。
    //=====================================================
    [System.Serializable]
    public class KnockbackSetting
    {
        [Tooltip("相手を後方（攻撃者から離れる方向）へ飛ばす水平方向の初速。大きいほど遠くまで飛ぶ。0で水平方向には飛ばない。")]
        public float horizontalSpeed = 3f;
        [Tooltip("相手を上方向へ飛ばす初速。大きいほど高く浮く。0で浮かない。" +
                 "1未満だと実際にはほとんど浮かないので、浮かせたい時は1以上を目安に。")]
        public float upSpeed = 0f;
    }

    //=====================================================
    // ★追加：技ごとの「当たり判定（どの部位のコライダーをONにするか）」設定
    //   各技（パンチ／空中攻撃／通常・上・下キック／必殺技／投げ）のInspectorに1つずつ用意してあり、
    //   チェックを入れた部位のコライダーが、その技を出している間だけONになる（OFFに戻すのは従来どおり技の終了時）。
    //   既定値は、これまでコード内に固定で書いていた部位と同じ（既存キャラの挙動は変わらない）。
    //   ※ヤンキー用のP-Head追加は、下のenableHeadHitboxOnAttackで全技にまとめて追加することもできる。
    //=====================================================
    [System.Serializable]
    public class HitboxSetting
    {
        [Tooltip("P-Head（頭）")] public bool head;
        [Tooltip("P-RightArm（右上腕）")] public bool rightArm;
        [Tooltip("P-RightForeArm（右前腕）")] public bool rightForeArm;
        [Tooltip("P-RightHand（右手）")] public bool rightHand;
        [Tooltip("P-RightUpLeg（右もも）")] public bool rightUpLeg;
        [Tooltip("P-RightLeg（右すね）")] public bool rightLeg;
        [Tooltip("P-RightFoot（右足）")] public bool rightFoot;
        [Tooltip("P-LeftArm（左上腕）")] public bool leftArm;
        [Tooltip("P-LeftForeArm（左前腕）")] public bool leftForeArm;
        [Tooltip("P-LeftHand（左手）")] public bool leftHand;
        [Tooltip("P-LeftUpLeg（左もも）")] public bool leftUpLeg;
        [Tooltip("P-LeftLeg（左すね）")] public bool leftLeg;
        [Tooltip("P-LeftFoot（左足）")] public bool leftFoot;
    }

    [Header("ヤンキー用：頭（P-Head）の当たり判定設定")]
    [Tooltip("ON：下の各技の「当たり判定」設定に関係なく、パンチ（空中攻撃含む）・キック（通常／上／下）・必殺技を出している間、P-Headの当たり判定も必ずONにする。ヤンキーのみONにしてください。\n" +
             "OFF：各技の「当たり判定」のチェックボックス設定どおり（P-Headは、その技のheadにチェックを入れた時だけONになる）。\n" +
             "※投げ（掴み）には適用されない。投げでP-Headを使いたい場合は、投げの「当たり判定」のheadにチェックを入れる。")]
    [SerializeField] bool enableHeadHitboxOnAttack = false;

    [Header("パンチ（空中攻撃含む）")]
    [Tooltip("① パンチの攻撃力（ダメージ量）")]
    [SerializeField] int punchAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束フレーム数。60F=1秒）。パンチを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [SerializeField] int punchMissFrames = 30;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束フレーム数。60F=1秒）。パンチを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] int punchHitFrames = 30;
    [Tooltip("⑤ パンチを当てた時の、相手の【やられ状態（行動不能）】のフレーム数（60F=1秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] int punchStunFrames = 18;
    [Tooltip("③ パンチを当てた時の、相手のヒットストップのフレーム数（60F=1秒）。0だと止まらない。")]
    [SerializeField] int punchHitStopFrames = 5;
    [Tooltip("④ パンチを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting punchKnockback = new KnockbackSetting { horizontalSpeed = 2.5f, upSpeed = 0f };
    [Tooltip("⑥ パンチ（地上）を出している間、当たり判定をONにする部位。チェックした部位のコライダーが、パンチ中だけONになる。")]
    [SerializeField] HitboxSetting punchHitbox = new HitboxSetting { rightHand = true };
    [Tooltip("⑥ 空中攻撃（パンチ入力を空中で出した時のFlying-kick）を出している間、当たり判定をONにする部位。")]
    [SerializeField] HitboxSetting flyingKickHitbox = new HitboxSetting { leftFoot = true, leftLeg = true, rightFoot = true };


    [Header("通常キック")]
    [Tooltip("① 通常キックの攻撃力（ダメージ量）")]
    [SerializeField] int kickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束フレーム数。60F=1秒）。キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [SerializeField] int kickMissFrames = 36;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束フレーム数。60F=1秒）。キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] int kickHitFrames = 36;
    [Tooltip("⑤ 通常キックを当てた時の、相手の【やられ状態（行動不能）】のフレーム数（60F=1秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] int kickStunFrames = 24;
    [Tooltip("③ 通常キックを当てた時の、相手のヒットストップのフレーム数（60F=1秒）。0だと止まらない。")]
    [SerializeField] int kickHitStopFrames = 5;
    [Tooltip("④ 通常キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting kickKnockback = new KnockbackSetting { horizontalSpeed = 3.5f, upSpeed = 0f };
    [Tooltip("⑥ 通常キックを出している間、当たり判定をONにする部位。チェックした部位のコライダーが、キック中だけONになる。")]
    [SerializeField] HitboxSetting kickHitbox = new HitboxSetting { rightFoot = true, rightUpLeg = true, rightLeg = true };

    [Header("上キック")]
    [Tooltip("① 上キックの攻撃力（ダメージ量）")]
    [SerializeField] int upKickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束フレーム数。60F=1秒）。上キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [SerializeField] int upKickMissFrames = 42;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束フレーム数。60F=1秒）。上キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] int upKickHitFrames = 42;
    [Tooltip("⑤ 上キックを当てた時の、相手の【やられ状態（行動不能）】のフレーム数（60F=1秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] int upKickStunFrames = 30;
    [Tooltip("③ 上キックを当てた時の、相手のヒットストップのフレーム数（60F=1秒）。0だと止まらない。")]
    [SerializeField] int upKickHitStopFrames = 5;
    [Tooltip("④ 上キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting upKickKnockback = new KnockbackSetting { horizontalSpeed = 2f, upSpeed = 5f };
    [Tooltip("⑥ 上キックを出している間、当たり判定をONにする部位。チェックした部位のコライダーが、上キック中だけONになる。")]
    [SerializeField] HitboxSetting upKickHitbox = new HitboxSetting { rightFoot = true, rightLeg = true };

    [Header("下キック")]
    [Tooltip("① 下キックの攻撃力（ダメージ量）")]
    [SerializeField] int downKickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束フレーム数。60F=1秒）。下キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [SerializeField] int downKickMissFrames = 30;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束フレーム数。60F=1秒）。下キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] int downKickHitFrames = 30;
    [Tooltip("⑤ 下キックを当てた時の、相手の【やられ状態（行動不能）】のフレーム数（60F=1秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] int downKickStunFrames = 24;
    [Tooltip("③ 下キックを当てた時の、相手のヒットストップのフレーム数（60F=1秒）。0だと止まらない。")]
    [SerializeField] int downKickHitStopFrames = 5;
    [Tooltip("④ 下キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting downKickKnockback = new KnockbackSetting { horizontalSpeed = 3f, upSpeed = 0f };
    [Tooltip("⑥ 下キックを出している間、当たり判定をONにする部位。チェックした部位のコライダーが、下キック中だけONになる。")]
    [SerializeField] HitboxSetting downKickHitbox = new HitboxSetting { rightFoot = true, rightLeg = true };


    [Header("必殺技（waza）")]
    [Tooltip("① 必殺技（左足の攻撃判定）が相手に命中した時のダメージ量。パンチ・キック等と同じ仕組みで、" +
             "漢気ゲージによる攻撃力補正（atkPowerPerBar）もこの値を基準に上乗せされる。")]
    [SerializeField] int specialAtk = 20;
    [Tooltip("③ 必殺技を当てた時の、相手のヒットストップのフレーム数（60F=1秒）。多段ヒットごとに毎回かかる点に注意。0だと止まらない。\n" +
             "※② 必殺技のクールダウン（拘束時間）は、下の「必殺技設定」のspecialFramesで調整する（waza アニメーションの長さと合わせる必要があるため）。")]
    [SerializeField] int specialHitStopFrames = 5;
    [Tooltip("④ 必殺技を当てた時の、相手のノックバック量。多段ヒットするため、既定は0（飛ばさない）。")]
    [SerializeField] KnockbackSetting specialKnockback = new KnockbackSetting { horizontalSpeed = 0f, upSpeed = 0f };
    [Tooltip("⑤ 必殺技を当てた時の、相手の【やられ状態（行動不能）】のフレーム数（60F=1秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] int specialStunFrames = 30;
    [Tooltip("⑥ 必殺技（waza）を出している間、当たり判定をONにする部位。既定は左足（もも・すね・足）の3箇所。")]
    [SerializeField] HitboxSetting specialHitbox = new HitboxSetting { leftFoot = true, leftUpLeg = true, leftLeg = true };

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
    private float guardBuffMultiplier = 1f;            // 現在のガード成功による累積ダメージ倍率（ガード成功のたびに掛け算で増える）
    private ParticleSystem activeGuardBuffEffect;      // 生成中の攻撃力上昇エフェクトの参照（多重生成防止・停止処理用）
    //=====================================================
    // ---- 漢気ゲージ(必殺技ゲージ) ----
    //=====================================================
    [Header("漢気ゲージ設定")]
    [Tooltip("ゲージ1本分の最大値")]
    public float kankiGaugePerBar = 100f;
    [Tooltip("ゲージの本数(2本分)")]
    public int kankiGaugeBarCount = 2;
    [Tooltip("相手に攻撃を当てた（ダメージを与えた）時に増える量")]
    public float gaugeGainOnHit = 15f;
    [Tooltip("仁王立ちでガードに成功した時に増える量")]
    public float gaugeGainOnGuard = 10f;
    [Tooltip("後退（相手から離れる方向へ移動）している間、1秒あたりに減る量")]
    public float gaugeLossOnRetreat = 5f;
    [Tooltip("ゲージ1本(満タン)につき上昇する基礎攻撃力倍率。0.25なら1本で+25%（実際のダメージ＝基礎攻撃力倍率×技の攻撃力）")]
    public float atkPowerPerBar = 0.25f;

    // 現在の合計ゲージ量(0 ? kankiGaugePerBar * kankiGaugeBarCount)
    private float kankiGauge = 0f;

    // ゲージによる補正がかかる前の、素の攻撃力
    private int baseAtk;

    //=====================================================
    // ★追加：漢気ゲージが1本以上たまっている間、自身の中心に出し続けるエフェクト設定
    //   ゲージ1本分(kankiGaugePerBar)以上 → 表示開始／1本分未満になった → 停止（自然にフェードアウト）
    //=====================================================
    [Header("漢気ゲージ充填中エフェクト設定")]
    [SerializeField] bool enableKankiChargeEffect = true;          // ゲージ充填中エフェクトを出すかどうか
    [Tooltip("ゲージが1本以上たまっている間、出し続けるパーティクル（Loopオン推奨・未設定なら出さない）")]
    [SerializeField] ParticleSystem kankiChargeEffectPrefab;       //ゲージが溜まっている時のエフェクト(上部分)
    [SerializeField] ParticleSystem kankiChargeEffectPrefab_Ground;//ゲージが溜まっている時のエフェクト(下部分)
    [Tooltip("プレイヤー基準の生成位置。キャラクターの体の中心あたりになるよう調整してください。")]
    [SerializeField] Vector3 kankiChargeEffectOffset = new Vector3(0f, 0.0f, 0f);

    [Tooltip("ONにすると、パーティクルの発生位置がプレイヤーと一緒に動く（Local）。OFFだと発生済みの粒は移動時にその場へ置き去りになる（World）。")]
    [SerializeField] bool kankiChargeEffectFollowLocal = true;
    private ParticleSystem activeKankiChargeEffect; // 生成中のエフェクト参照（多重生成防止・停止処理用）
    private ParticleSystem activeKankiChargeEffect_Ground; // 生成中のエフェクト参照（多重生成防止・停止処理用）

    //=====================================================
    // ★追加：漢気復活（ダウン中に漢気ゲージを消費して、連打せずに即座に復活する）設定
    //   ダウン中（復活チャレンジの制限時間内）にL1ボタンを押すと、漢気ゲージを消費してその場で復活する。
    //   復活後は一定時間（reviveAtkBuffFrames）、攻撃力が上昇し、その間は専用エフェクトを出し続ける。
    //=====================================================
    [Header("漢気復活設定（ゲージ消費の即時復活）")]
    [Tooltip("ONにすると、ダウン中にL1ボタンで漢気ゲージを消費して即座に復活できる。")]
    [SerializeField] bool enableKankiRevive = true;
    [Tooltip("漢気復活で消費する漢気ゲージの本数。1ならゲージ1本分(kankiGaugePerBar)を消費する。消費分のゲージがたまっていない時は復活できない。")]
    [SerializeField] int kankiReviveBarCost = 1;
    [Tooltip("復活後の攻撃力の倍率。1.5なら攻撃力が1.5倍（+50%）、2なら2倍。1以下にすると上昇しない。漢気ゲージ本数による補正と掛け合わせて計算される。")]
    [SerializeField] float reviveAtkMultiplier = 1.5f;
    [Tooltip("復活後に攻撃力上昇が続くフレーム数（60F=1秒。600F=10秒）。この間は攻撃力上昇エフェクトも出続ける。")]
    [SerializeField] int reviveAtkBuffFrames = 600;

    [Header("漢気復活エフェクト設定")]
    [Tooltip("復活した瞬間に1回だけ出すパーティクル（未設定なら出さない）")]
    [SerializeField] ParticleSystem kankiReviveEffectPrefab;
    [SerializeField] ParticleSystem kankiReviveEffectPrefab_Ground;
    [Tooltip("プレイヤー基準の生成位置")]
    [SerializeField] Vector3 kankiReviveEffectOffset = new Vector3(0f, 1.0f, 0f);
    [Tooltip("生成した復活エフェクトを破棄するまでの時間（秒）")]
    [SerializeField] float kankiReviveEffectLifetime = 2.0f;
    [Tooltip("攻撃力上昇中に出し続けるパーティクル（未設定なら出さない）。Loopの設定に関わらず上昇中は自動でループさせ、上昇が終わると止まる。")]
    [SerializeField] ParticleSystem reviveBuffEffectPrefab;
    [SerializeField] ParticleSystem reviveBuffEffectPrefab_Ground;
    [Tooltip("プレイヤー基準の生成位置")]
    [SerializeField] Vector3 reviveBuffEffectOffset = new Vector3(0f, 1.0f, 0f);

    private bool wantKankiRevive = false;           // ダウン中にL1が押された（漢気復活の要求）
    private bool isReviveBuffed = false;            // 漢気復活による攻撃力上昇中かどうか
    private int reviveBuffTimer = 0;             // 攻撃力上昇の残りフレーム数
    private ParticleSystem activeReviveBuffEffect;  // 生成中の攻撃力上昇エフェクトの参照（多重生成防止・停止処理用）
    private ParticleSystem activeReviveBuffEffect_Ground;  // 生成中の攻撃力上昇エフェクトの参照（多重生成防止・停止処理用）

    //=====================================================
    // ---- 必殺技（漢気ゲージ消費） ----
    //=====================================================
    [Header("必殺技設定")]
    [Tooltip("必殺技を発動するために必要な漢気ゲージ量。デフォルトはゲージ満タン(2本分)。")]
    public float specialRequiredGauge = 200f;

    [Tooltip("必殺技発動で消費する漢気ゲージ量。")]
    public float specialGaugeCost = 200f;

    // ※必殺技の攻撃力(specialAtk)は、「攻撃ごとの設定」の「必殺技（waza）」ヘッダーへ移動した。

    [Tooltip("必殺技発動中の拘束フレーム数(60F=1秒)。既定値は180フレーム（3秒）。\n" +
             "この間は攻撃・ガード・投げ・ジャンプ・必殺技の再発動は一切できず、移動のみ可能。\n" +
             "また、この間は相手からの攻撃を一切受け付けない（無敵）。\n" +
             "「waza」トリガーで再生される必殺技アニメーションの長さと同じフレーム数に合わせること。")]
    [SerializeField] int specialFrames = 180;

    [Header("必殺技演出（任意設定）")]
    [Tooltip("必殺技発動時に鳴らす効果音（未設定なら鳴らさない）")]
    [SerializeField] AudioClip specialSe;
    [Tooltip("必殺技発動時に生成するパーティクル（未設定なら出さない）")]
    [SerializeField] ParticleSystem specialEffectPrefab;

    [SerializeField] Vector3 specialEffectOffset = new Vector3(0f, 1.0f, 0f);
    [SerializeField] float specialEffectLifetime = 1.5f;

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
    private int stateTimer;                               // 現在の行動が終わるまでの残りフレーム数
    private int currentAttackMissFrames;                // ★追加：今出している通常攻撃の「空振り時」拘束時間（命中時に経過時間を求めるために保持）

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
    bool wantSpecial;   // L1（必殺技ボタン）

    bool isGuarding;                 // 仁王立ち中かどうか（被弾処理の分岐に使う）

    // ★追加：しゃがみの見た目（コライダー・アニメーター）をスティック下入力に直結させて追跡するフラグ。
    //   currentState（状態機械）とは独立して管理する。詳細はSyncCrouchVisual()のコメントを参照。
    bool isCrouchVisual;

    // ★追加：frontMove/BackMoveのTriggerを「移動を開始した瞬間・方向が切り替わった瞬間」だけ
    //   発火させるための直近の移動方向記録。毎フレームSetTriggerすると同じアニメが再生し直され続けてしまう。
    enum MoveDirection { None, Forward, Backward }
    MoveDirection lastMoveDir = MoveDirection.None;
    int guardComboCount;             // 仁王立ちで連続して耐えた回数
    bool autoFaceSnapped;            // ★追加：最初に相手を認識した時に、補間なしで即座に向き合わせたか
    float autoFaceSign = 1f;         // ★追加：現在向いている（向こうとしている）Z方向。密着中に判定が揺れても、確定するまで変えない
    bool hasMoveBackwardParam;       // ★追加：Animatorに"MoveBackward"(Bool)が存在するか（存在する時だけ値を送る）
    bool hasGuardParam;              // ★変更：Animatorに"Guard"(Trigger)が存在するか（存在する時だけトリガーを立てる）
    bool guardAnimApplied;           // ★変更：直前フレームでGuard状態だったか（Guardに入った瞬間だけトリガーを立てるための記録）
    const string GuardParam = "Guard"; // ★仁王立ちアニメーション用のAnimatorパラメータ名（Trigger）
    bool hasGuardSuccessParam;       // ★追加：Animatorに"Guard-success"(Trigger)が存在するか（存在する時だけ立てる）
    const string GuardSuccessParam = "Guard-success"; // ★仁王立ち成功（ガード成功）アニメーション用のAnimatorパラメータ名（Trigger）
    // ★追加：F6キーでON/OFFする、常時仁王立ちデバッグモードの現在の状態
    private bool debugAlwaysGuard = false;
    bool rebornCamStarted;           // 根性復活のクローズアップカメラを開始済みか

    //=====================================================
    // ★追加：ダウン関連のアニメーション（開始／ダウン中／復活／復活失敗）
    //   ・ダウン開始：Trigger。ダウンした瞬間に1回だけ立てる
    //   ・ダウン中　：Bool。ダウンしている間ずっとtrue、復活／死亡で必ずfalseに戻す
    //   ・復活　　　：Trigger。復活成功（連打・漢気復活・F4デバッグ復活）の瞬間に立てる
    //   ・復活失敗　：Trigger。制限時間内に復活できず力尽きた（Dead）瞬間に立てる
    //   パラメータ名はInspectorで変更できる。空にするとその演出は何もしない。
    //   Animatorに存在しない名前を指定した場合も警告を出さずに無視する。
    //=====================================================
    [Header("ダウン時のアニメーション（Animatorのパラメータ名）")]
    [Tooltip("ダウンした瞬間に1回だけ立てるTrigger名。")]
    [SerializeField] string downStartTrigger = "Down-start";
    [Tooltip("ダウンしている間ずっとtrueにするBool名（復活／死亡でfalseに戻る）。")]
    [SerializeField] string downBoolParam = "Down";
    [Tooltip("根性復活・漢気復活に成功した瞬間に立てるTrigger名。")]
    [SerializeField] string downReviveTrigger = "Down-revive";
    [Tooltip("制限時間内に復活できず力尽きた（Dead）瞬間に立てるTrigger名。")]
    [SerializeField] string downFailTrigger = "Down-fail";
    bool downAnimPlayed;             // ダウン中で、ダウン開始アニメーションを開始済みか（復活／死亡でfalseに戻る）

    //=====================================================
    // ★追加：復活後の停止／同時ダウン時のUI
    //=====================================================
    [Header("復活後の停止（自分・相手ともに動けない）")]
    [Tooltip("復活に成功した瞬間から、自分・相手ともに動けなくなるフレーム数（60F=1秒）。\n" +
             "この間、カメラは復活したキャラの正面の引きの画を映す。")]
    [SerializeField] int rebornStandUpFreezeFrames = 180;
    [Tooltip("ONにすると、復活後の停止中は相手（P2／CPU）を非表示にする。復活演出のカメラに相手が映り込むのを防ぐ。\n" +
             "同時ダウン後の復活はカメラ演出をしないので、非表示にもしない。")]
    [SerializeField] bool hideOpponentDuringRebornFreeze = true;

    bool downWasDual;                                                                   // 今回のダウン中に、一度でも同時ダウン（両者HP0）になったか
    readonly System.Collections.Generic.List<UnityEngine.Renderer> hiddenOpponentRenderers
        = new System.Collections.Generic.List<UnityEngine.Renderer>();                  // 復活停止中に非表示にしている相手のRenderer

    [Header("同時ダウン時のUI（P1・P2の両方がダウンした時）")]
    [Tooltip("同時ダウン中は、カメラの引き（クローズアップ）をせず通常カメラのままにし、\n" +
             "制限時間・ボタン画像・残り連打数をP1は画面左、P2は画面右に表示する。")]
    [SerializeField] bool enableDualDownLayout = true;
    [Tooltip("同時ダウン時、P1のUIを表示する横位置（画面幅に対する割合。0.25＝左半分の中央）")]
    [SerializeField, Range(0f, 1f)] float dualDownLeftXRatio = 0.25f;
    [Tooltip("同時ダウン時、P2のUIを表示する横位置（画面幅に対する割合。0.75＝右半分の中央）")]
    [SerializeField, Range(0f, 1f)] float dualDownRightXRatio = 0.75f;
    bool canThrow = true;            // 投げの多重発生を防ぐフラグ
    bool throwReaching;              // ★追加：投げ動作中で、まだ掴みが成立しておらず手の当たり判定の接触を待っている間（接触した瞬間にfalseになる）

    // ★追加：投げ（掴み）仕様変更用の相互参照・タイマー
    Player grabbedTarget;            // 自分が今掴んでいる相手（掴む側の時だけ使用。掴んでいなければnull）
    Player grabbingPlayer;           // 自分を今掴んでいる相手（掴まれる側の時だけ使用。掴まれていなければnull）
    int grabHoldTimer;             // 掴み成立後、掴みモーションを再生し続ける残りフレーム数（0になったら一時停止）
    bool grabHoldFrozen;             // 掴みモーションを一時停止中か（trueの間animator.speed=0）
    bool grabStickWasActive;         // 掴まれ中、前フレームでスティックが倒れていたか（押しっぱなしを連打扱いにしないための判定用）
    int escapeTimer;               // 掴まれている間の、脱出に必要な残りフレーム数（0になったら脱出成功）

    int rebornCount = 1;             // 復活回数のカウント
    int rebornTimer;               // 復活チャレンジの経過フレーム数
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

    // ★追加：攻撃種別に応じた「基準攻撃力」を返す。
    //   漢気ゲージやガード上昇分は含まない、Inspectorで設定した素の値。
    //   新しい技を追加したときはここにもケースを追加すること。
    int GetAttackPower(AttackType type)
    {
        switch (type)
        {
            case AttackType.Punch: return punchAtk;
            case AttackType.Kick: return kickAtk;
            case AttackType.UpKick: return upKickAtk;
            case AttackType.DownKick: return downKickAtk;
            default: return punchAtk; // 想定外の場合のフォールバック
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

    //=====================================================
    // ★多段ヒット設定（攻撃の種類ごと）
    //   1回の攻撃（パンチ・通常キック・上キック・下キック）で、相手に何回まで
    //   ダメージを与えられるかを、技ごとに個別にInspectorから設定できるようにする。
    //   ・allowMultiHit = false（既定）：1回の攻撃につき1ヒットのみ（多段ヒット防止）
    //   ・allowMultiHit = true：maxHitCountで指定した回数までヒットを許可
    //=====================================================
    [System.Serializable]
    public class MultiHitSetting
    {
        [Tooltip("OFF（既定）：この技は1回の攻撃で相手に当たるのは1回だけになります（多段ヒット防止）。\n" +
                 "ON：この技は1回の攻撃で複数回ヒットを許可します（多段ヒット可能）。回数は下のmaxHitCountで設定します。")]
        public bool allowMultiHit = false;
        [Tooltip("多段ヒットを許可する場合、1回の攻撃で最大何回までヒット判定を有効にするか。\n" +
                 "allowMultiHitがOFFの間はこの値に関わらず常に1回として扱われます。")]
        [Min(1)] public int maxHitCount = 1;
    }

    [Header("多段ヒット設定（攻撃の種類ごと）")]
    [Tooltip("パンチ（空中攻撃含む）の多段ヒット設定")]
    [SerializeField] MultiHitSetting punchMultiHit = new MultiHitSetting();
    [Tooltip("通常キックの多段ヒット設定")]
    [SerializeField] MultiHitSetting kickMultiHit = new MultiHitSetting();
    [Tooltip("上キックの多段ヒット設定")]
    [SerializeField] MultiHitSetting upKickMultiHit = new MultiHitSetting();
    [Tooltip("下キックの多段ヒット設定")]
    [SerializeField] MultiHitSetting downKickMultiHit = new MultiHitSetting();
    [Tooltip("必殺技（waza／左足攻撃）の多段ヒット設定。既定でON（180フレーム＝3秒間で最大5回まで命中可能）。")]
    [SerializeField] MultiHitSetting specialMultiHit = new MultiHitSetting { allowMultiHit = true, maxHitCount = 5 };
    [Tooltip("必殺技が命中してから、次のヒットが有効になるまでのクールタイム（フレーム）。多段ヒットの連続ヒット間隔を制御する。")]
    [SerializeField] int specialHitCooldownFrames = 6;

    // 現在の攻撃で、既に何回ヒットが確定したか。EnterPunch/EnterKick等、新しい攻撃の開始時に0へリセットする。
    private int hitCountThisAttack = 0;
    private AudioClip pendingAttackSe;            // ★追加：今の攻撃で「空振りでなければ鳴らす」技のSE（命中が確定した瞬間に鳴らす。空振りなら破棄）
    private bool throwGrabbedThisThrow = false;  // ★追加：今の投げ動作で相手を掴めたか（掴めなかった＝空振り）
    private bool jumpLandSeArmed = false;        // ★追加：ジャンプで空中に出た後、まだ着地SEを鳴らしていないか
    private bool wasInThrowForMissSe = false;    // ★追加：前フレームで投げ動作の最中だったか
    private bool wasInAttackForMissSe = false; // ★追加：前フレームで攻撃（パンチ/キック系）の最中だったか（攻撃が途中で中断された時の空振りSE判定用）
    private bool missSePlayedThisAttack = false; // ★追加：今の攻撃で空振りSEを鳴らし済みか（二重再生防止）

    // 必殺技が最後にヒットを確定したフレーム（CurrentFrame）。specialHitCooldownFramesの間隔判定に使用する。
    // 新しい攻撃の開始時（ResetMultiHitCount）に十分過去の値へリセットし、前回の攻撃のクールタイムを引きずらないようにする。
    private int lastSpecialHitFrame = -999999;

    // 攻撃の開始時（EnterPunch/EnterKick等）に呼び出し、多段ヒットのカウントをリセットする。
    void ResetMultiHitCount()
    {
        hitCountThisAttack = 0;
        lastSpecialHitFrame = -999999;
        missSePlayedThisAttack = false;
        pendingAttackSe = null; // 新しい攻撃の開始時に、前の攻撃の保留SEを破棄する
    }

    // 攻撃の種類（AttackType）に応じた多段ヒット設定を返す。
    // 新しい技を追加した場合は、対応するMultiHitSettingフィールドを増やし、ここにもケースを追加すること。
    MultiHitSetting GetMultiHitSetting(AttackType type)
    {
        switch (type)
        {
            case AttackType.Punch: return punchMultiHit;
            case AttackType.Kick: return kickMultiHit;
            case AttackType.UpKick: return upKickMultiHit;
            case AttackType.DownKick: return downKickMultiHit;
            default: return punchMultiHit; // 想定外の場合のフォールバック
        }
    }

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今出している技のノックバック量」を問い合わせるための公開メソッド。
    //   必殺技中はspecialKnockback、それ以外はCurrentAttackTypeに応じた設定を返す。
    //   攻撃中でない場合（想定外）はnullを返す＝ノックバックなし。
    //   新しい技を追加した場合は、対応するKnockbackSettingフィールドを増やし、ここにもケースを追加すること。
    public KnockbackSetting GetCurrentKnockbackSetting()
    {
        if (currentState == PlayerState.Special) return specialKnockback;

        switch (CurrentAttackType)
        {
            case AttackType.Punch: return punchKnockback;
            case AttackType.Kick: return kickKnockback;
            case AttackType.UpKick: return upKickKnockback;
            case AttackType.DownKick: return downKickKnockback;
            default: return null;
        }
    }

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今出している技のヒットストップのフレーム数（60F=1秒）」を問い合わせるための公開メソッド。
    //   必殺技中はspecialHitStopFrames、それ以外はCurrentAttackTypeに応じた値を返す。
    //   攻撃中でない場合（想定外）は、呼び出し側のフォールバックで扱えるよう負の値(-1)を返す。
    //   新しい技を追加した場合は、対応するフィールドを増やし、ここにもケースを追加すること。
    public int GetCurrentHitStopFrames()
    {
        if (currentState == PlayerState.Special) return specialHitStopFrames;

        switch (CurrentAttackType)
        {
            case AttackType.Punch: return punchHitStopFrames;
            case AttackType.Kick: return kickHitStopFrames;
            case AttackType.UpKick: return upKickHitStopFrames;
            case AttackType.DownKick: return downKickHitStopFrames;
            default: return -1;
        }
    }

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今出している技のやられ状態のフレーム数」を問い合わせるための公開メソッド。
    //   必殺技中はspecialStunFrames、それ以外はCurrentAttackTypeに応じた値を返す。攻撃中でない場合は0（やられ状態にしない）。
    //   新しい技を追加した場合は、対応するフィールドを増やし、ここにもケースを追加すること。
    public int GetCurrentStunFrames()
    {
        if (currentState == PlayerState.Special) return specialStunFrames;

        switch (CurrentAttackType)
        {
            case AttackType.Punch: return punchStunFrames;
            case AttackType.Kick: return kickStunFrames;
            case AttackType.UpKick: return upKickStunFrames;
            case AttackType.DownKick: return downKickStunFrames;
            default: return 0;
        }
    }

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今回の接触を有効なヒットとして扱ってよいか」を
    //   攻撃側（自分＝このPlayerインスタンス）に問い合わせるための公開メソッド。
    //   現在出している技ごとのMultiHitSettingを参照して判定する。
    //   ・必殺技(Special)中はspecialMultiHitを参照する（CurrentAttackTypeには含まれないため専用に分岐）。
    //   ・それ以外はCurrentAttackType（パンチ/キック等）に応じたMultiHitSettingを参照する。
    //   ・多段ヒット無効時：1回目の呼び出しのみtrueを返し、以降は同じ攻撃の間ずっとfalseを返す。
    //   ・多段ヒット有効時：maxHitCountで設定した回数までtrueを返す。
    //   trueを返した場合のみ、呼び出し側（被弾した相手）はダメージ処理を実行すること。
    public bool TryRegisterHit()
    {
        bool isSpecial = currentState == PlayerState.Special;
        MultiHitSetting setting = isSpecial ? specialMultiHit : GetMultiHitSetting(CurrentAttackType);
        int limit = setting.allowMultiHit ? Mathf.Max(1, setting.maxHitCount) : 1;

        if (hitCountThisAttack >= limit)
        {
            GaugeDLog($"[{PlayerName}] 多段ヒット制限により今回の接触は無効（技={(isSpecial ? "Special" : CurrentAttackType.ToString())} / 既に{hitCountThisAttack}回ヒット済み / 上限{limit}回）");
            return false;
        }

        // ★追加：必殺技は、命中してから次のヒットが有効になるまでspecialHitCooldownFramesフレームのクールタイムを設ける。
        //   多段ヒット許可中でも、同一フレーム／連続フレームでの連続ヒットを防ぐための時間的な間引き。
        if (isSpecial && CurrentFrame - lastSpecialHitFrame < specialHitCooldownFrames)
        {
            GaugeDLog($"[{PlayerName}] 必殺技クールタイム中により今回の接触は無効（前回ヒットから{CurrentFrame - lastSpecialHitFrame}フレーム / クールタイム{specialHitCooldownFrames}フレーム）");
            return false;
        }

        hitCountThisAttack++;
        if (isSpecial) lastSpecialHitFrame = CurrentFrame;
        return true;
    }

    // ★追加：攻撃種別ごとの「命中時」クールダウン（攻撃開始からの合計フレーム数）を返す。
    //   新しい技を追加したときはここにもケースを追加すること。
    int GetHitFrames(AttackType type)
    {
        switch (type)
        {
            case AttackType.Punch: return punchHitFrames;
            case AttackType.Kick: return kickHitFrames;
            case AttackType.UpKick: return upKickHitFrames;
            case AttackType.DownKick: return downKickHitFrames;
            default: return 0;
        }
    }

    // ★追加：被弾した相手側から「あなたの攻撃、当たりましたよ」と通知してもらうための公開メソッド。
    public void NotifyAttackLanded()
    {
        // ★追加：空振りではなかった（命中した）ので、保留していた技のSEを鳴らす。空振りが確定済みなら鳴らさない。
        if (pendingAttackSe != null && !missSePlayedThisAttack) PlayActionSe(pendingAttackSe);
        pendingAttackSe = null;

        // ★追加：この攻撃で最初の命中だった場合、クールダウンを「空振り時」から「命中時」の長さへ切り替える。
        //   （必殺技や投げなど、CurrentAttackTypeがNoneの状態では対象外）
        if (!attackLandedThisAttack && CurrentAttackType != AttackType.None)
        {
            int elapsed = currentAttackMissFrames - stateTimer; // 攻撃開始からの経過フレーム数
            int newTimer = Mathf.Max(GetHitFrames(CurrentAttackType) - elapsed, 0);
            DLog($"[{PlayerName}] 命中によりクールダウン切替：残り{stateTimer}F → {newTimer}F（命中時={GetHitFrames(CurrentAttackType)}F / 経過={elapsed}F）");
            stateTimer = newTimer;
        }

        attackLandedThisAttack = true;

        // 相手にダメージを与えた（攻撃を命中させた）ので、漢気ゲージを増やす
        AddKankiGauge(gaugeGainOnHit);

        // ガード成功で上昇していた攻撃力は「次の攻撃が当たるまで」の効果なので、
        // 自分の攻撃が実際に相手へ命中したこのタイミングでバフとエフェクトを終了する。
        if (isGuardBuffed)
        {
            isGuardBuffed = false;
            guardBuffMultiplier = 1f; // 技が当たったので累積倍率をリセット
            StopGuardBuffEffect();
        }
    }

    // ★追加：相手（enemyPlayer / enemy）の参照が切れている時に、シーン内から自動で探し直す。
    //   プレハブはシーン上のオブジェクトへの参照を保持できないため、プレハブ化して置き直したり、
    //   相手側を置き直したりすると、Inspectorで設定していたenemyPlayer/enemyが空(None/Missing)になり、
    //   OnTriggerEnterの「相手の攻撃か？」判定が常にfalseになって当たり判定が消えたように見える。
    //   ・enemyPlayerもenemyも未設定（または破棄済み）の時だけ、0.5秒おきに探す（設定済みなら何もしない）。
    //   ・まず自分以外のPlayerを探し、いなければCPUのEnemyを探す。
    //   ・Inspectorで設定されていればそちらが常に優先される。
    private float opponentResolveTimer = 0f;

    void ResolveOpponentIfMissing()
    {
        // UnityのObjectの==は、破棄済み(Missing)の参照もnull扱いにする
        if (enemyPlayer != null || enemy != null) return;

        opponentResolveTimer -= Time.unscaledDeltaTime;
        if (opponentResolveTimer > 0f) return;
        opponentResolveTimer = 0.5f;

        // 自分以外のPlayer（対人戦の相手）を探す。PLayerTagNameが自分と違うものを優先する
        Player found = null;
        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (candidate == this || !candidate.gameObject.activeInHierarchy) continue;
            if (found == null || (found.PLayerTagName == PLayerTagName && candidate.PLayerTagName != PLayerTagName))
            {
                found = candidate;
            }
        }

        if (found != null)
        {
            enemyPlayer = found;
            Debug.LogWarning($"[{PlayerName}] enemyPlayerが未設定だったため、シーン内の'{found.gameObject.name}'を自動で設定しました。" +
                             "プレハブはシーン上のオブジェクトを参照できないため、Inspectorで設定し直すことをおすすめします。", this);
            return;
        }

        // 対人戦の相手がいなければ、CPU(Enemy)を探す
        Enemy foundEnemy = FindAnyObjectByType<Enemy>();
        if (foundEnemy != null)
        {
            enemy = foundEnemy;
            Debug.LogWarning($"[{PlayerName}] enemyが未設定だったため、シーン内の'{foundEnemy.gameObject.name}'を自動で設定しました。" +
                             "Inspectorで設定し直すことをおすすめします。", this);
        }
    }

    // ★追加：リザルト画面（InGame1V1ResultControllerがあるシーン）なら、自動でリザルト画面モードにする
    void Awake()
    {
        if (autoDetectResultScene && !isResultMode
            && FindAnyObjectByType<InGame1V1ResultController>(FindObjectsInactive.Include) != null)
        {
            isResultMode = true;
            DLog($"[{PlayerName}] リザルト画面を検知したため、リザルト画面モードで起動します");
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    // 各種コンポーネント・子オブジェクトの当たり判定の取得と初期化を行う
    void Start()
    {
        //男気ゲージに使用する、補正前の素の攻撃力。
        //攻撃の種類ごとに変わるため、実際の値は各攻撃発生時（EnterPunch/EnterKick）に設定し直す。
        //ここでは戦闘開始直後の表示用にパンチの攻撃力で初期化しておく。
        baseAtk = GetAttackPower(AttackType.Punch);
        UpdateAtkByGauge();
        // 自分にアタッチされているPlayerInputコンポーネントを取得
        playerInput = GetComponent<PlayerInput>();
        // どのプレイヤー番号・どのデバイスが紐づいているかログ出力（デバッグ用）
        DLog($"{gameObject.name} : PlayerIndex={playerInput.playerIndex} / Device={(playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "なし")}");

        rb = GetComponent<Rigidbody>();		//PlayerのRigidbodyを取得
        animator = GetComponent<Animator>();
        se = GetComponent<AudioSource>();

        // ★追加：後退用アニメーションの切り替えに使う"MoveBackward"(Bool)がAnimatorにあるか調べておく。
        //   無い場合はSetBoolを呼ばない（毎フレーム警告が出るのを防ぐ）。
        if (animator != null)
        {
            foreach (var param in animator.parameters)
            {
                if (param.name == "MoveBackward" && param.type == AnimatorControllerParameterType.Bool)
                {
                    hasMoveBackwardParam = true;
                    break;
                }
            }

            // ★変更：仁王立ち用の"Guard"(Trigger)・ガード成功用の"Guard-success"(Trigger)がAnimatorにあるか調べる。
            //   無ければ警告を1回だけ出し、そのアニメーションの再生は行わない。
            foreach (var param in animator.parameters)
            {
                if (param.name == GuardParam && param.type == AnimatorControllerParameterType.Trigger)
                {
                    hasGuardParam = true;
                }
                else if (param.name == GuardSuccessParam && param.type == AnimatorControllerParameterType.Trigger)
                {
                    hasGuardSuccessParam = true;
                }
            }
            if (!hasGuardParam)
            {
                Debug.LogWarning($"[{PlayerName}] Animatorに'{GuardParam}'(Trigger)パラメータがありません。仁王立ちのアニメーションは再生されません。", this);
            }
            if (!hasGuardSuccessParam)
            {
                Debug.LogWarning($"[{PlayerName}] Animatorに'{GuardSuccessParam}'(Trigger)パラメータがありません。仁王立ち成功のアニメーションは再生されません。", this);
            }
        }

        // GameMNGを名前に依存しない方法で探してキャッシュしておく。
        // （オブジェクト名が"ManagerObject"や"GameMNG"でなくても正しく取得できるようにする）
        // 見つからない場合はここでハッキリ警告を出し、以降のNullReferenceExceptionを防ぐ。
        gameMNG = FindAnyObjectByType<GameMNG>();
        if (gameMNG == null && !isResultMode) // リザルト画面にはGameMNGが無いのが普通なのでエラーにしない
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

    // 当たり判定の部位（ここに挙げた名前のオブジェクトが P[番号]Attack になる）
    static readonly string[] AttackPartNames =
    {
        "P-LeftUpLeg", "P-LeftLeg", "P-LeftFoot",
        "P-RightUpLeg", "P-RightLeg", "P-RightFoot",
        "P-LeftArm", "P-LeftForeArm", "P-LeftHand",
        "P-Head",
        "P-RightArm", "P-RightForeArm", "P-RightHand",
    };

    // プレイヤー番号（"P1" / "P2"）に応じて、tagとlayerを設定する
    //   一番上の親 ... tag = P[番号]       / layer = P[番号]
    //   各部位     ... tag = P[番号]Attack / layer = P[番号]
    // ※Start()より前（生成直後）に呼ばれても動くよう、キャッシュ済みのhitbox変数は使わず名前で探す
    public void ApplyPlayerTagAndLayer(string playerLabel)
    {
        string bodyTag = playerLabel;              // 例: P1
        string attackTag = playerLabel + "Attack"; // 例: P1Attack

        int layer = LayerMask.NameToLayer(playerLabel);
        if (layer < 0)
        {
            Debug.LogWarning($"[{gameObject.name}] Layer '{playerLabel}' が未登録です。" +
                             "Project Settings > Tags and Layers で追加してください。layerは変更しません。", this);
        }

        // 一番上の親
        GameObject root = transform.root.gameObject;
        TrySetTag(root, bodyTag);
        if (layer >= 0) root.layer = layer;

        // 各部位（gameObject.layerは子へ伝播しないので、1つずつ設定する）
        foreach (string partName in AttackPartNames)
        {
            Transform part = FindDeepChild(transform, partName);
            if (part == null)
            {
                Debug.LogWarning($"[{gameObject.name}] 部位 '{partName}' が見つからず、tag/layerを設定できませんでした。", this);
                continue;
            }

            TrySetTag(part.gameObject, attackTag);
            if (layer >= 0) part.gameObject.layer = layer;
        }

        DLog($"[{PlayerName}] tag/layerを設定：親={bodyTag} / 部位={attackTag} / layer={playerLabel}({layer})");
    }

    // 未登録のtagを代入するとUnityExceptionになるため、握りつぶさず警告に変える
    static void TrySetTag(GameObject go, string tagName)
    {
        try
        {
            go.tag = tagName;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"Tag '{tagName}' が未登録です。Project Settings > Tags and Layers で追加してください。", go);
        }
    }

    //=====================================================
    // ★Input Actionsのコールバック（PlayerInputのBehavior=Send Messagesで自動的に呼ばれる）
    //   ここでは「ボタンが押された」という意図フラグを立てるだけにし、
    //   実際にどう行動へ反映するかはUpdate側で判断する。
    //=====================================================

    // 左スティック：Move（継続的な値なので、押した/離したではなく現在値を保持するだけ）
    public void OnMove(InputValue value)
    {
        // ★追加：リザルト画面モード中はスティック入力をすべて無視する（移動・しゃがみ等を無効化）
        moveInput = isResultMode ? Vector2.zero : value.Get<Vector2>();
    }

    // ★追加：ダウン中（HP<=0）の「根性復活」の連打を1回分カウントする。
    //   どのボタンが押されても同じ処理になるよう、各ボタンのコールバックから呼ぶ共通処理。
    void RegisterMash()
    {
        // 復活チャレンジの連打カウントとして加算するだけ
        mashCount++;

        // 連打ボタン画像に「押された」ことを伝える。
        // 押した画像の延長はせず、TickMashButtonVisual()が「押していない画像」を必ず挟んでから次の押下として表示する。
        mashButtonPendingPress = true;

        // 連打1回ごとの擬音演出（「グッ！」「ンン！」等）。
        // 攻撃者がいないので、常にプレイヤーの正面方向を基準にHitEffectData側のBase Angleで散らす。
        if (HitEffectSpawner.Instance != null && mashHitEffectData != null)
        {
            HitEffectSpawner.Instance.SpawnBehindFacing(mashHitEffectData, transform.position, transform.forward); // 向きと反対側の斜め上へ
            EffectDLog($"[{PlayerName}] 連打擬音エフェクト発生 pos={transform.position}");
        }
    }

    // ジャンプボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする（どのボタンでも同じ）
    public void OnJump(InputValue value)
    {
        if (!value.isPressed) return;
        if (isResultMode) return; // ★リザルト画面モード：攻撃ボタン以外は無効

        if (HP <= 0)
        {
            if (mashWithAnyButton) RegisterMash(); // ★どのボタンでもOKの設定がONの時だけ連打として数える
            return;
        }

        wantJump = true;
    }

    // パンチボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打判定としてこのボタンを使う
    public void OnPunch(InputValue value)
    {
        if (!value.isPressed) return;

        // ★リザルト画面モード：攻撃として受け付ける（通知は、実際に攻撃が出るEnterPunch内で行う）
        if (isResultMode)
        {
            if (!resultInputEnabled) return; // ★勝者以外の操作は受け付けない
            wantPunch = true;
            return;
        }

        if (HP <= 0)
        {
            RegisterMash(); // ダウン中は復活チャレンジの連打としてカウントするだけ
            return;
        }

        wantPunch = true;
    }

    // キックボタン押下時のコールバック（通常／上／下の分岐はUpdate側で行う）
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする
    public void OnKick(InputValue value)
    {
        if (!value.isPressed) return;

        // ★リザルト画面モード：攻撃として受け付ける（通知は、実際に攻撃が出るEnterKick内で行う）
        if (isResultMode)
        {
            if (!resultInputEnabled) return; // ★勝者以外の操作は受け付けない
            wantKick = true;
            return;
        }

        if (HP <= 0)
        {
            if (mashWithAnyButton) RegisterMash(); // ★どのボタンでもOKの設定がONの時だけ連打として数える
            return;
        }

        wantKick = true;
    }

    // 仁王立ちボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする
    public void OnStand(InputValue value)
    {
        if (!value.isPressed) return;
        if (isResultMode) return; // ★リザルト画面モード：攻撃ボタン以外は無効

        if (HP <= 0)
        {
            if (mashWithAnyButton) RegisterMash(); // ★どのボタンでもOKの設定がONの時だけ連打として数える
            return;
        }

        wantGuard = true;
    }

    // 投げボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする
    public void OnThrow(InputValue value)
    {
        if (!value.isPressed) return;
        if (isResultMode) return; // ★リザルト画面モード：攻撃ボタン以外は無効

        if (HP <= 0)
        {
            if (mashWithAnyButton) RegisterMash(); // ★どのボタンでもOKの設定がONの時だけ連打として数える
            return;
        }

        wantThrow = true;
    }

    // ★追加：L1ボタン（必殺技）押下時のコールバック
    //   ※Input Actionsアセット側に"L1"という名前のActionを追加し、
    //     コントローラーのL1ボタンを割り当てておく必要があります。
    public void OnL1(InputValue value)
    {
        if (!value.isPressed) return;
        if (isResultMode) return; // ★リザルト画面モード：攻撃ボタン以外は無効

        // ★追加：ダウン中（HP<=0）のL1は必殺技ではなく「漢気復活」の要求として扱う
        //   （OnPunchがダウン中は連打カウントになるのと同じ仕組み）
        if (HP <= 0)
        {
            wantKankiRevive = true;
            // ★追加：L1も他のボタンと同じく連打1回分として数える（どのボタンでもOKの設定がONの時だけ。ゲージが足りなければ連打で復活を目指せる）
            if (mashWithAnyButton) RegisterMash();
            return;
        }

        wantSpecial = true;
    }

    //=====================================================
    // ★フレーム管理（60fps固定の「論理フレーム」）
    //   以前は「秒」で管理していた各種時間（攻撃後のクールダウン・やられ・ヒットストップ・投げ・復活など）を、
    //   すべて「フレーム数」で管理する。格闘ゲームの慣習どおり 60フレーム = 1秒。
    //   ・描画のfps（60Hz/144Hz等）に関係なく、Time.deltaTimeを溜めて1/60秒ごとに1フレームを進める。
    //   ・クロックはstaticで共有するので、P1・P2は常に同じタイミングでフレームが進む。
    //   ・各タイマーは「Updateの先頭で進んだフレーム数(FrameStep)」だけ減らす（描画が速い時は0のこともある）。
    //   ・移動速度・回転速度・押しのけ速度・漢気ゲージ減少量など「毎秒あたりの速度」は従来どおり秒基準のまま。
    //=====================================================
    public const int FramesPerSecond = 60;
    const float SecondsPerFrame = 1f / FramesPerSecond;
    static float frameAccumulator;          // まだフレームに換算していない端数の時間（秒）
    static int frameClockUnityFrame = -1;   // 最後にクロックを進めたUnityの描画フレーム番号（二重加算防止）
    static int frameStep;                   // 今回のUpdateで進んだ論理フレーム数（0以上）

    // ゲーム開始からの累計フレーム数（多段ヒットの間隔判定や、見た目の最低表示時間などに使う）
    public static int CurrentFrame { get; private set; }
    // 今回のUpdateで進んだ論理フレーム数
    public static int FrameStep => frameStep;
    // フレーム数を秒に換算する（UI表示用）
    public static float FramesToSeconds(int frames) => frames / (float)FramesPerSecond;

    // Play開始のたびにクロックを初期化する（Domain Reloadを切っている場合でも前回の値を引きずらないため）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFrameClock()
    {
        frameAccumulator = 0f;
        frameClockUnityFrame = -1;
        frameStep = 0;
        CurrentFrame = 0;
        rebornFreezeEndFrame = 0;
    }

    //=====================================================
    // ★追加：復活時の全員停止
    //   復活に成功した瞬間から、一定フレームの間、自分・相手ともに操作・状態更新を止める。
    //   その間カメラは、復活したキャラの正面の引きの画を映す（FightingCameraController.TriggerRebornStandUpHold）。
    //   staticなので、P1・P2のどちらが復活しても両方に効く。
    //   CPU(Enemy)側はEnemy.csのUpdate先頭で Player.IsRebornFreezing を見て、trueの間は何もせず return すること。
    //=====================================================
    static int rebornFreezeEndFrame;
    public static bool IsRebornFreezing => CurrentFrame < rebornFreezeEndFrame;

    // Updateの先頭で呼ぶ。同じ描画フレーム内で2人目以降のPlayerが呼んでも二重には進まない。
    static void AdvanceFrameClock()
    {
        if (frameClockUnityFrame == Time.frameCount) return;
        frameClockUnityFrame = Time.frameCount;

        frameAccumulator += Time.deltaTime;
        // 1e-4fは、60Hz固定の環境で浮動小数点の誤差により「0フレーム→次に2フレーム」とブレるのを防ぐための補正
        frameStep = Mathf.FloorToInt(frameAccumulator / SecondsPerFrame + 1e-4f);
        frameAccumulator -= frameStep * SecondsPerFrame;
        CurrentFrame += frameStep;
    }

    // Update is called once per frame
    // 毎フレームの更新処理。HPが尽きていれば復活チャレンジへ、
    // そうでなければ現在の状態に応じて「拘束中のタイマー消化」か「新しい行動の受付」を行う。
    void Update()
    {
        AdvanceFrameClock(); // ★追加：60fps固定の論理フレームを進める（各タイマーはframeStep分だけ減らす）

        // ★追加：復活後の停止が終わったら、非表示にしていた相手を再表示する
        if (hiddenOpponentRenderers.Count > 0 && !IsRebornFreezing) ShowOpponentAfterReborn();

        // ★追加：掴みポーズで止めている最中に、被弾やダウン等でThrow状態から外れた場合の保険として必ず再生を再開する
        if (grabHoldFrozen && currentState != PlayerState.Throw) ResumeGrabHoldAnimation();

        MaintainKankiChargeEffect(); // ★追加：漢気ゲージ1本以上の間、追従エフェクトを出し続ける
        if (!IsRebornFreezing)       // ★追加：復活後の停止中は、攻撃力上昇・ペナルティの残り時間も進めない
        {
            TickReviveBuff();        // ★追加：漢気復活後の攻撃力上昇の残り時間を消化する
            TickGuardFailPenalty();  // ★追加：ガード失敗ペナルティ（ゲージ増加率ダウン／被ダメージ増加）の残り時間を消化する
        }
        TickMashButtonVisual();      // ★追加：連打ボタン画像の押した／押していない切り替え
        if (!isResultMode) ResolveOpponentIfMissing();  // ★追加：プレハブ化・再配置等で相手(enemyPlayer/enemy)の参照が切れていたら自動で探し直す
        // ★修正：しゃがみの見た目（コライダー・アニメーター）は、
        //   currentState（状態機械）を経由せず、毎フレーム「スティック下入力の有無」だけで直接同期する。
        //   以前はEnterCrouch()内でcurrentStateとコライダー/アニメーターを同時に変更していたため、
        //   しゃがみ中にキック等のボタン入力でcurrentStateがCrouch以外へ上書きされると、
        //   その後スティックを離してもExitCrouch()が呼ばれず、しゃがみ見た目だけが残り続ける不具合があった。
        //   ダウン中／Dead中／掴まれ中／投げ吹き飛び中はコライダー変更で見た目が破綻するので対象外にする。
        if (currentState != PlayerState.KnockedDown && currentState != PlayerState.Dead
            && currentState != PlayerState.Grabbed && currentState != PlayerState.Thrown)
        {
            // ★変更：やられ状態中はスティック入力を無視する（しゃがみにしない）
            SyncCrouchVisual(currentState != PlayerState.Stunned && moveInput.y <= crouchInputThreshold);
        }

        // F2キーで漢気ゲージ専用デバッグログのON/OFFを切り替える（対象キャラクターはenableF2DebugKeyで選択）
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            if (enableF2DebugKey)
            {
                kankiGaugeDebugLogEnabled = !kankiGaugeDebugLogEnabled;
                Debug.Log($"[{PlayerName}] 漢気ゲージデバッグログ: {(kankiGaugeDebugLogEnabled ? "ON" : "OFF")}");
            }
            else
            {
                DLog($"[{PlayerName}] F2キーを検知しましたが、enableF2DebugKeyがOFFのため無視します。");
            }
        }

        // ★追加：F3キーでエフェクト発生ログのON/OFFを切り替える（対象キャラクターはenableF3DebugKeyで選択）
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            if (enableF3DebugKey)
            {
                effectDebugLogEnabled = !effectDebugLogEnabled;
                Debug.Log($"[{PlayerName}] エフェクト発生ログ: {(effectDebugLogEnabled ? "ON" : "OFF")}");
            }
            else
            {
                DLog($"[{PlayerName}] F3キーを検知しましたが、enableF3DebugKeyがOFFのため無視します。");
            }
        }

        // ★デバッグ：F5キーでHPを強制的に0にする
        //   F4の強制復活をテストする際、わざわざ相手の攻撃を受けてダウンさせる手間を省くためのキー。
        //   対象キャラクターはInspectorのenableF5DebugKeyで選択する。
        if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
        {
            if (enableF5DebugKey)
            {
                HP = 0;
                Debug.Log($"[{PlayerName}] [デバッグ]F5キーによりHPを強制的に0にしました");

                //UIにHPを反映させるように指示
                if (gameMNG != null)
                {
                    gameMNG.Player_ReduceHP(HP, PlayerName);
                }
            }
            else
            {
                // 対象外のPlayerインスタンスでもF5押下自体は検知されるため、
                // 意図的に無視していることが分かるようログを残す。
                DLog($"[{PlayerName}] F5キーを検知しましたが、enableF5DebugKeyがOFFのため無視します。");
            }
        }

        // ★F6キーで常時仁王立ち（ガード）デバッグモードをON/OFFする
        //   対象キャラクターはInspectorのenableF6DebugKeyチェックボックスで選択する。
        if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
        {
            if (enableF6DebugKey)
            {
                debugAlwaysGuard = !debugAlwaysGuard;
                Debug.Log($"[{PlayerName}] [デバッグ]常時仁王立ちモード: {(debugAlwaysGuard ? "ON" : "OFF")}");

                if (!debugAlwaysGuard)
                {
                    // OFFにした瞬間、通常状態へきちんと戻す（TickBusyStateの終了処理と同じ内容）
                    DisableAllHitboxes();
                    isGuarding = false;
                    canThrow = true;
                    currentState = PlayerState.Idle;
                    stateTimer = 0;
                }
            }
            else
            {
                // 対象外のPlayerインスタンスでもF6押下自体は検知されるため、
                // 意図的に無視していることが分かるようログを残す。
                DLog($"[{PlayerName}] F6キーを検知しましたが、enableF6DebugKeyがOFFのため無視します。");
            }
        }

        // ★デバッグ：F7キーで漢気ゲージを満タンにする
        //   必殺技の動作確認等、ゲージが貯まるまで待たずにすぐ試したい時のためのキー。
        //   対象キャラクターはInspectorのenableF7DebugKeyで選択する。
        if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
        {
            if (enableF7DebugKey)
            {
                float max = kankiGaugePerBar * kankiGaugeBarCount;
                kankiGauge = max;
                UpdateAtkByGauge();
                Debug.Log($"[{PlayerName}] [デバッグ]F7キーにより漢気ゲージを満タン（{max:F1}）にしました");

                //ゲージUIを更新
                if (gameMNG != null)
                {
                    gameMNG.Player_UpdateKankiGauge();
                }
            }
            else
            {
                // 対象外のPlayerインスタンスでもF7押下自体は検知されるため、
                // 意図的に無視していることが分かるようログを残す。
                DLog($"[{PlayerName}] F7キーを検知しましたが、enableF7DebugKeyがOFFのため無視します。");
            }
        }

        // ★デバッグ：F8キーでHPを最大値まで回復する
        //   被弾テストやダウン・復活テストを何度も繰り返す際、いちいち他の手段でHPを戻す手間を省くためのキー。
        //   対象キャラクターはInspectorのenableF8DebugKeyで選択する。
        //   ★追加：押した時点で根性復活チャレンジ中（KnockedDown）またはDead状態だった場合は、
        //     HPを回復するだけでなく、F4の強制復活と同様に生存状態まで復活させる。
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if (enableF8DebugKey)
            {
                if (currentState == PlayerState.KnockedDown || currentState == PlayerState.Dead)
                {
                    ForceRevive(maxHP, "F8");
                }
                else
                {
                    HP = maxHP;
                    Debug.Log($"[{PlayerName}] [デバッグ]F8キーによりHPを最大値（{maxHP}）まで回復しました");

                    //UIにHPを反映させるように指示
                    if (gameMNG != null)
                    {
                        gameMNG.Player_ReduceHP(HP, PlayerName);
                    }
                }
            }
            else
            {
                // 対象外のPlayerインスタンスでもF8押下自体は検知されるため、
                // 意図的に無視していることが分かるようログを残す。
                DLog($"[{PlayerName}] F8キーを検知しましたが、enableF8DebugKeyがOFFのため無視します。");
            }
        }

        // ★デバッグ：F10キーで「やられ状態中に連続で攻撃が当たった回数」の画面表示をON/OFFする
        //   対象キャラクターはInspectorのenableF10DebugKeyで選択する。
        if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
        {
            if (enableF10DebugKey)
            {
                showStunComboDebug = !showStunComboDebug;
                Debug.Log($"[{PlayerName}] [デバッグ]やられ連続ヒット数表示: {(showStunComboDebug ? "ON" : "OFF")}");
            }
            else
            {
                DLog($"[{PlayerName}] F10キーを検知しましたが、enableF10DebugKeyがOFFのため無視します。");
            }
        }

        // ヒットストップ処理を最優先で消化する。動けない間は他の入力・状態処理を一切行わない。
        if (hitStopTimer > 0)
        {
            hitStopTimer -= frameStep;
            if (hitStopTimer <= 0)
            {
                EndHitStop();
            }
            ClearInputIntents();
            return;
        }

        // ★追加：復活後の停止中は、自分・相手ともに入力・状態更新を一切行わない。
        //   ただしダウン中（HP0）のプレイヤーは止めない。同時ダウン中に片方だけ復活しても、
        //   もう一方の復活の制限時間（rebornTimer）と連打は止めずに進める。
        if (IsRebornFreezing && !(HP <= 0 && !isResultMode))
        {
            ClearInputIntents();
            return;
        }

        // HP判定・死亡処理は、コントローラーの有無に関係なく常に実行する
        if (HP <= 0 && !isResultMode) // ★リザルト画面モードではダウン／復活処理を行わない
        {
            // ★デバッグ：F4キーでダウン中／Dead中のキャラクターを強制的に復活させる
            //   対象キャラクターはInspectorのenableF4DebugKeyで選択する。
            //   ※以前はcurrentState != Dead を条件にしていたが、復活チャレンジの制限時間切れで
            //     Dead状態に入った後はF4を押しても一切反応しなくなっていたため、その条件を撤廃した。
            if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
            {
                if (enableF4DebugKey)
                {
                    ForceRebornDebug();
                    ClearInputIntents();
                    return;
                }
                else
                {
                    // 対象外のPlayerインスタンスでもF4押下自体は検知されるため、
                    // 意図的に無視していることが分かるようログを残す。
                    DLog($"[{PlayerName}] F4キーを検知しましたが、enableF4DebugKeyがOFFのため無視します。");
                }
            }

            // ★追加：ダウンしたら漢気復活による攻撃力上昇は解除する（エフェクトも止める）
            if (isReviveBuffed) EndReviveBuff();

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

        // ★追加：F6デバッグモードがONの間は、通常の入力処理を行わず毎フレーム強制的に仁王立ち状態を維持する
        if (debugAlwaysGuard)
        {
            currentState = PlayerState.Guard;
            isGuarding = true;
            ClearInputIntents();
            return;
        }

        if (!isResultMode) HandleArrowKeyAnimation(); // ★追加：矢印キーによるアニメーション再生（自由に動ける状態の時のみ）
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

        //エフェクトにプレイヤーの座標を渡す
        if(activeKankiChargeEffect_Ground != null)
        {
            Vector3 Player_pos = this.transform.position;
            Player_pos.x = 0.0f;
            Player_pos.y = 0.25f;

            activeKankiChargeEffect_Ground.transform.position = Player_pos;
        }
        if (activeReviveBuffEffect_Ground != null)
        {
            Vector3 Player_pos = this.transform.position;
            Player_pos.x = 0.0f;
            Player_pos.y = 0.25f;

            activeReviveBuffEffect_Ground.transform.position = Player_pos;
        }

    }

    // LateUpdate: Update内の移動処理やRigidbodyによる物理移動（ジャンプ等）がすべて反映された後に、
    //   ①P1・P2同士が重なっていたら押しのけ合い（ResolvePlayerOverlap）、
    //   ②最終的なワールド座標をminPosition?maxPositionの範囲内へ強制的に収める（ClampPositionWithinBounds）。
    //   Updateの最後ではなくLateUpdateで行うことで、物理演算(FixedUpdate)による移動分も確実に制限できる。
    void LateUpdate()
    {
        CheckMissSeOnAttackEnd(); // ★追加：攻撃が当たらないまま終わった／中断された時の空振りSE
        SyncGuardAnimation(); // ★追加：仁王立ち（Guard状態）の間だけ、仁王立ちアニメーションを再生する

        // ★リザルト画面モード：向きの自動変更・押しのけ・位置クランプを行わず、配置した位置のまま動かさない
        if (isResultMode) return;

        if (enemy != null)
        {
            // ★CPU戦：先に押しのけて位置を確定させてから、その位置を基準に相手の方向を向く
            ResolvePlayerOverlap();
            AutoFaceOpponent();
        }
        else
        {
            // 対人戦：従来どおりの順序
            AutoFaceOpponent(); // ★追加：常に相手の方向を向く
            ResolvePlayerOverlap();
        }
        ClampPositionWithinBounds();
    }

    //=====================================================
    // ★プレイヤー同士の押しのけ合い設定
    //   P1・P2の胴体コライダー(Player_Collider)同士が重なった時に、
    //   物理エンジン任せにせず自前で押しのけ合う処理。
    //   移動(Move)がRigidbody物理ではなくtransform.Translateで直接位置を動かしているため、
    //   Unity標準の衝突応答だけでは正しく押し返されず、すり抜けたり上に乗れてしまったりする。
    //   この処理は「XZ平面上の水平距離」だけを見て押し合うため、
    //   相手の真上に乗った状態でも横方向へ押し出され続け、結果的に滑り落ちるようになる。
    //=====================================================
    [Header("プレイヤー同士の押しのけ合い設定")]
    [Tooltip("ONにすると、P1・P2の胴体コライダーが重なった時にお互いを押しのけ合います。")]
    [SerializeField] bool enablePlayerPush = true;
    [Tooltip("押しのけ判定に使う、コライダー同士の間に余分に確保する隙間（大きいほど早めに反発し始める）")]
    [SerializeField] float pushSkinWidth = 0.02f;
    [Tooltip("1秒間に押しのけられる最大距離。大きいほど瞬時に離れ、小さいほどじわっと押し出される")]
    [SerializeField] float maxPushSpeed = 20f;

    // 胴体コライダー同士が重なっている時に、水平方向(Z軸)へ押し出す。
    //   ・対人戦(enemyPlayer)：P1・P2がお互いに半分ずつ押しのける。
    //   ・★追加：CPU戦(enemy)：Enemyは押しのけ処理を持たないため、Player側だけが重なり分を全て押し戻す。
    //     （以前はCPU戦が対象外で、プレイヤーがEnemyをすり抜けて左右が入れ替わり、自動向き合いで両者が反転していた）
    // 相手の真上に乗ってしまった場合でも、この水平方向の押し出しにより横へずれ落ち、重力で自然に滑り落ちる。
    void ResolvePlayerOverlap()
    {
        if (!enablePlayerPush) return;

        Transform otherTf;
        CapsuleCollider otherCol;
        bool otherPushesToo; // 相手側も同じ押しのけ処理をしているか（trueなら半分ずつ、falseなら自分が全部）

        if (enemy != null)
        {
            // CPU戦：enemyを優先（GetOpponentTransformと同じ優先順位）
            if (Player_status == Status.Dead) return;
            otherTf = enemy.transform;
            otherCol = enemy.GetComponent<CapsuleCollider>(); // Enemy側は自身の胴体CapsuleColliderをGetComponentで取得している（Enemy.csの変更不要）
            otherPushesToo = false;
        }
        else if (enemyPlayer != null)
        {
            // ダウン中・死亡中のプレイヤーは押しのけ合いの対象から外す
            if (Player_status == Status.Dead || enemyPlayer.Player_status == Status.Dead) return;
            otherTf = enemyPlayer.transform;
            otherCol = enemyPlayer.Player_Collider;
            otherPushesToo = true;
        }
        else
        {
            return;
        }

        if (Player_Collider == null || otherCol == null) return;

        CapsuleCollider myCol = Player_Collider;

        // 半径はワールドスケールを考慮する（通常は1のはずだが、念のためlossyScaleを反映）
        float myRadius = myCol.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float otherRadius = otherCol.radius * Mathf.Max(otherTf.lossyScale.x, otherTf.lossyScale.z);
        float minDistance = myRadius + otherRadius + pushSkinWidth;

        // --- 垂直方向(Y)の重なりチェック ---
        // お互いの高さ範囲が重なっていない（例：ジャンプで相手の頭上を飛び越えている最中）場合は、
        // 実際には接触していないので押しのけを行わない。
        float myBottom = transform.position.y + myCol.center.y - myCol.height * 0.5f;
        float myTop = transform.position.y + myCol.center.y + myCol.height * 0.5f;
        float otherBottom = otherTf.position.y + otherCol.center.y - otherCol.height * 0.5f;
        float otherTop = otherTf.position.y + otherCol.center.y + otherCol.height * 0.5f;

        bool verticalOverlap = myBottom < otherTop && otherBottom < myTop;
        if (!verticalOverlap) return;

        // --- 水平方向(Z軸。間合いはZ軸のみで変化する仕様のため)の重なりチェック ---
        float deltaZ = transform.position.z - otherTf.position.z;
        float distance = Mathf.Abs(deltaZ);

        if (distance >= minDistance) return; // 十分離れているので何もしない

        float overlap = minDistance - distance;
        // 対人戦は半分だけ押し出す（相手も同じ処理を自分自身に対して行うため、結果的に均等に離れる）。
        // CPU戦は相手が押し出さないので、重なり分を全て自分が押し戻す。
        float share = otherPushesToo ? 0.5f : 1f;
        float pushThisFrame = Mathf.Min(overlap * share, maxPushSpeed * Time.deltaTime);

        float pushDir;
        if (distance > 0.0001f)
        {
            pushDir = Mathf.Sign(deltaZ);
        }
        else
        {
            // 完全に同じZ座標で重なっている等、符号が決まらない場合は、
            // GameObjectのInstanceIDで押す方向を固定し、お互い矛盾なく逆方向へ分かれるようにする
            pushDir = gameObject.GetInstanceID() < otherTf.gameObject.GetInstanceID() ? -1f : 1f;
        }

        transform.position += new Vector3(0f, 0f, pushDir * pushThisFrame);
    }

    // ワールド座標のX/Y/Zをそれぞれ設定した範囲内にクランプする
    void ClampPositionWithinBounds()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minPosition.x, maxPosition.x);
        pos.y = Mathf.Clamp(pos.y, minPosition.y, maxPosition.y);
        pos.z = Mathf.Clamp(pos.z, minPosition.z, maxPosition.z);
        transform.position = pos;
    }

    // ★追加：矢印キーが押された瞬間に、対応するアニメーションを再生する（Updateから毎フレーム呼ばれる）
    void HandleArrowKeyAnimation()
    {
        if (animator == null) return;

        // キーボードの矢印キー
        if (enableArrowKeyAnimation && Keyboard.current != null)
        {
            Keyboard kb = Keyboard.current;
            if (kb.upArrowKey.wasPressedThisFrame) PlayArrowAnimation(upArrowAnimation, "↑");
            if (kb.leftArrowKey.wasPressedThisFrame) PlayArrowAnimation(leftArrowAnimation, "←");
            if (kb.rightArrowKey.wasPressedThisFrame) PlayArrowAnimation(rightArrowAnimation, "→");
            if (kb.downArrowKey.wasPressedThisFrame) PlayArrowAnimation(downArrowAnimation, "↓");
        }

        // ゲームパッドの十字キー（このPlayerInputに紐づいているパッドだけを見る。1P/2Pで混ざらない）
        if (enableDpadAnimation && playerInput != null)
        {
            foreach (InputDevice device in playerInput.devices)
            {
                Gamepad pad = device as Gamepad;
                if (pad == null) continue;

                if (pad.dpad.up.wasPressedThisFrame) PlayArrowAnimation(upArrowAnimation, "十字↑");
                if (pad.dpad.left.wasPressedThisFrame) PlayArrowAnimation(leftArrowAnimation, "十字←");
                if (pad.dpad.right.wasPressedThisFrame) PlayArrowAnimation(rightArrowAnimation, "十字→");
                if (pad.dpad.down.wasPressedThisFrame) PlayArrowAnimation(downArrowAnimation, "十字↓");
            }
        }
    }

    void PlayArrowAnimation(ArrowAnimationSetting setting, string keyLabel)
    {
        if (setting == null || string.IsNullOrEmpty(setting.animationName))
        {
            DLog($"[{PlayerName}] {keyLabel}キー：再生するアニメーション名が未設定です。");
            return;
        }

        // 移動アニメーションが残っていると、再生したアニメーションより移動が優先されてしまうので先に止める
        StopMoveAnimation();
        ResetAttackTriggers();

        if (setting.useTrigger)
        {
            animator.SetTrigger(setting.animationName);
        }
        else
        {
            animator.Play(setting.animationName, 0, 0f);
        }

        DLog($"[{PlayerName}] {keyLabel}キー：アニメーション再生 {setting.animationName}（{(setting.useTrigger ? "Trigger" : "State")}）");
    }

    // 1フレームで消費しなかった意図フラグを毎フレーム末尾でクリアする
    void ClearInputIntents()
    {
        wantJump = false;
        wantPunch = false;
        wantKick = false;
        wantGuard = false;
        wantThrow = false;
        wantSpecial = false;
        wantKankiRevive = false;
    }

    //-----------------------------------------------------
    // ダウン中の連打プロンプト表示
    //-----------------------------------------------------
    // ダウン（根性復活チャレンジ）中だけ、画面に大きく連打を促すメッセージを表示する。
    // ※OnGUIによる簡易実装のため、split-screen等で複数Playerが同時に描画される構成の場合は
    //   表示位置が重なる可能性がある。その場合はCanvas+UI Textでの実装に置き換えること。
    [Header("復活連打メッセージ設定")]
    [SerializeField] bool showMashPrompt = true;               // ダウン中に連打メッセージを表示するか
    [SerializeField] string mashPromptText = "ボタンを連打しろ！"; // 表示する文言（どのボタンでもOK）
    [SerializeField] int mashPromptFontSize = 64;               // 文字サイズ
    [SerializeField] Color mashPromptColor = Color.yellow;      // 文字色

    // ★追加：残り連打回数を画面中央に大きく表示するための設定。
    //   押すごとに（mashCountが増えるごとに）GetRemainingMashCount()の値が減っていく。
    [Header("残り連打回数表示設定")]
    [SerializeField] bool showMashRemainingCount = true;        // 残り連打回数の数字を表示するか
    [SerializeField] int mashRemainingFontSize = 150;            // 数字のフォントサイズ（Inspectorで調整可能）
    [SerializeField] Color mashRemainingColor = Color.white;    // 数字の色
    [SerializeField] float mashRemainingYRatio = 0.42f;         // 数字を表示するY位置（画面高さに対する割合。中央付近）

    // ★追加：連打を促すボタン画像（押していない時／押した時の2枚）を差し込むための設定。
    //   ダウン中に何かボタンを押すたびに「押した画像」へ切り替わり、少し経つと「押していない画像」へ戻る。
    //   ※Inspectorに画像（Texture）を割り当てると表示される。未設定なら何も表示しない。
    //   ※画像のImport Settingsは「Sprite (2D and UI)」のままでも、「Default」でもよい。
    [Header("復活連打ボタン画像設定")]
    [SerializeField] bool showMashButtonImage = true;            // 連打ボタン画像を表示するか
    [Tooltip("ボタンを押していない時の画像（mash_button_normal.png）をここに入れる")]
    [SerializeField] Texture2D mashButtonNormalImage;            // ★画像の差し込み口①：押していない時
    [Tooltip("ボタンを押した時の画像（mash_button_pressed.png）をここに入れる")]
    [SerializeField] Texture2D mashButtonPressedImage;           // ★画像の差し込み口②：押した時
    [Tooltip("「押した画像」を出すフレーム数。1回出したら、連打がどれだけ速くてもこの時間で必ず終わらせる。")]
    [SerializeField] int mashButtonPressedFrames = 4;
    [Tooltip("「押していない画像」を最低限出すフレーム数。押した画像の後に必ずこの時間だけ挟むので、連打中でも押しっぱなしに見えない。")]
    [SerializeField] int mashButtonReleaseMinFrames = 3;
    [Tooltip("画像の大きさ（画面の高さに対する割合）。0.22なら画面高さの22%の正方形で表示する。")]
    [SerializeField] float mashButtonSizeRatio = 0.22f;
    [Tooltip("画像を表示するX位置（画面幅に対する割合。0.5で中央）")]
    [SerializeField] float mashButtonXRatio = 0.5f;
    [Tooltip("画像を表示するY位置（画面高さに対する割合。数字より下に出すため0.72付近）")]
    [SerializeField] float mashButtonYRatio = 0.72f;

    bool mashButtonVisualPressed;   // 今「押した画像」を出しているか
    int mashButtonStateEndFrame;   // 今の見た目（押した／押していない）を最低限維持する終了フレーム（CurrentFrame）
    bool mashButtonPendingPress;    // 押された要求が溜まっている（押していない画像を挟んでから、次の押した画像にする）

    // ★追加：ダウン中（復活チャレンジ中）に、復活の制限時間（残り秒数）を画面上部に表示するための設定。
    //   制限時間は上の「復活（根性）設定」のrebornTimeLimitFramesで、残りフレーム = rebornTimeLimitFrames - rebornTimer。
    [Header("復活制限時間表示設定")]
    [Tooltip("ダウン中に、復活の制限時間（残り秒数）を画面上部に表示するか")]
    [SerializeField] bool showRebornTimeLimit = true;
    [Tooltip("残り秒数の前に付ける文言")]
    [SerializeField] string rebornTimeLabel = "復活まで";
    [SerializeField] int rebornTimeFontSize = 56;                        // 文字サイズ
    [SerializeField] Color rebornTimeColor = Color.white;                // 通常時の色
    [Tooltip("残り時間がこのフレーム数以下になったら、色を変えて点滅させる")]
    [SerializeField] int rebornTimeLowThresholdFrames = 90;
    [SerializeField] Color rebornTimeLowColor = new Color(1f, 0.2f, 0.2f, 1f); // 残りわずかの時の色
    [Tooltip("文字の下に、残り時間に応じて短くなるバーを表示するか")]
    [SerializeField] bool showRebornTimeBar = true;
    [Tooltip("バーの長さ（画面幅に対する割合）")]
    [SerializeField] float rebornTimeBarWidthRatio = 0.4f;
    [Tooltip("表示する位置（画面上端からの距離。画面高さに対する割合）。大きくしすぎると連打メッセージと重なる。")]
    [SerializeField] float rebornTimeYRatio = 0.03f;

    // ★追加：復活の制限時間（残り秒数）を画面上部に表示する。OnGUIから、ダウン中だけ呼ばれる。
    void DrawRebornTimeLimit(float blink, float centerX, bool dualLayout)
    {
        int remainingFrames = Mathf.Max(rebornTimeLimitFrames - rebornTimer, 0);
        float remaining = FramesToSeconds(remainingFrames); // 画面表示は見やすいよう秒に換算する
        float ratio = rebornTimeLimitFrames > 0 ? Mathf.Clamp01((float)remainingFrames / rebornTimeLimitFrames) : 0f;
        bool isLow = remainingFrames <= rebornTimeLowThresholdFrames;

        // 残りわずかの時は色を変えて、明滅させる
        Color baseColor = isLow ? rebornTimeLowColor : rebornTimeColor;
        float alpha = isLow ? blink : 1f;
        Color textColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

        string text = $"{rebornTimeLabel} {remaining:F1}";

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = rebornTimeFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = textColor;

        float width = 800f;
        float height = rebornTimeFontSize * 1.4f;
        float top = Screen.height * rebornTimeYRatio;
        Rect rect = new Rect(centerX - width / 2f, top, width, height);

        // 縁取り（黒）を少しずらして重ね描きし、背景が明るくても読めるようにする
        GUIStyle outlineStyle = new GUIStyle(style)
        {
            normal = { textColor = new Color(0f, 0f, 0f, alpha) }
        };
        Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
        foreach (var offset in offsets)
        {
            GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height), text, outlineStyle);
        }
        GUI.Label(rect, text, style);

        // 残り時間に応じて右から短くなるバー
        if (showRebornTimeBar)
        {
            float barWidth = Screen.width * rebornTimeBarWidthRatio * (dualLayout ? 0.5f : 1f); // 同時ダウン時は半分の画面に収める
            float barHeight = Mathf.Max(Screen.height * 0.015f, 8f);
            float barX = centerX - barWidth / 2f;
            float barY = top + height + 4f;

            Color prevColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.6f); // 背景（枠）
            GUI.DrawTexture(new Rect(barX - 3f, barY - 3f, barWidth + 6f, barHeight + 6f), Texture2D.whiteTexture);

            GUI.color = textColor;                    // 残り時間分のバー
            GUI.DrawTexture(new Rect(barX, barY, barWidth * ratio, barHeight), Texture2D.whiteTexture);

            GUI.color = prevColor;
        }
    }

    // ★追加：連打ボタン画像の「押した／押していない」を毎フレーム切り替える。
    //   押した画像は mashButtonPressedFrames で必ず終わらせ、
    //   その後に mashButtonReleaseMinFrames だけ「押していない画像」を必ず挟む。
    //   連打が速くて押下要求が溜まっていても、その間は押していない画像を見せるので、押しっぱなしに見えない。
    void TickMashButtonVisual()
    {
        // ダウン中以外は状態をリセットしておく（次のダウン時に前回の押下が残らないようにする）
        if (currentState != PlayerState.KnockedDown)
        {
            downWasDual = false; // ダウン中でなければ同時ダウンの記録は不要
            if (downAnimPlayed)
            {
                // ★追加：復活／死亡の処理を通らずにダウンを抜けた場合の保険。ダウン中Boolが残らないようにする
                downAnimPlayed = false;
                SetDownBool(downBoolParam, false);
            }
            mashButtonVisualPressed = false;
            mashButtonPendingPress = false;
            return;
        }

        int now = CurrentFrame;

        if (mashButtonVisualPressed)
        {
            // 押した画像は決められた時間で必ず終わらせ、押していない画像へ戻す
            if (now >= mashButtonStateEndFrame)
            {
                mashButtonVisualPressed = false;
                mashButtonStateEndFrame = now + mashButtonReleaseMinFrames;
            }
        }
        else
        {
            // 押していない画像を最低限の時間出し終えていて、押された要求があれば、次の「押した画像」にする
            if (mashButtonPendingPress && now >= mashButtonStateEndFrame)
            {
                mashButtonPendingPress = false;
                mashButtonVisualPressed = true;
                mashButtonStateEndFrame = now + mashButtonPressedFrames;
            }
        }
    }

    void OnGUI()
    {
        DrawStunComboDebug(); // ★追加：F10のやられ連続ヒット数表示

        if (!showMashPrompt && !showMashRemainingCount && !showMashButtonImage && !showRebornTimeLimit) return;
        if (currentState != PlayerState.KnockedDown) return;

        // 明滅させて視認性・緊張感を出す
        float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 10f);

        // ★追加：同時ダウン中は、P1＝画面左／P2＝画面右にUIを分けて表示する。
        //   個別に変わる部分（制限時間・ボタン画像・残り連打数）は各自の側に、
        //   2人で同じ内容の文言（mashPromptText）は画面中央に1つだけ表示する。
        bool dualDown = IsDualKnockedDown();
        bool rightSide = IsRightSidePlayer();
        float uiCenterX = Screen.width * 0.5f;
        if (dualDown) uiCenterX = Screen.width * (rightSide ? dualDownRightXRatio : dualDownLeftXRatio);

        // ★追加：復活の制限時間（残り秒数）を画面上部に表示する
        if (showRebornTimeLimit)
        {
            DrawRebornTimeLimit(blink, uiCenterX, dualDown);
        }

        if (showMashPrompt && (!dualDown || !rightSide)) // 同時ダウン時は中央に1つだけ（P1側のインスタンスが描く）
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = mashPromptFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            Color baseColor = mashPromptColor;
            style.normal.textColor = new Color(baseColor.r, baseColor.g, baseColor.b, blink);

            float width = 800f;
            float height = 120f;
            Rect rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.15f, width, height);

            // 縁取り（黒）を少しずらして重ね描きし、背景が明るくても読めるようにする
            GUIStyle outlineStyle = new GUIStyle(style)
            {
                normal = { textColor = new Color(0f, 0f, 0f, blink) }
            };
            Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
            foreach (var offset in offsets)
            {
                GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height), mashPromptText, outlineStyle);
            }

            GUI.Label(rect, mashPromptText, style);
        }

        if (showMashRemainingCount)
        {
            int remaining = GetRemainingMashCount();
            string remainingText = remaining.ToString();

            GUIStyle numberStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = mashRemainingFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            numberStyle.normal.textColor = mashRemainingColor;

            float width = 400f;
            float height = mashRemainingFontSize * 1.4f;
            Rect rect = new Rect(
                uiCenterX - width / 2f,
                Screen.height * mashRemainingYRatio - height / 2f,
                width, height);

            // 縁取り（黒）を少しずらして重ね描きし、背景が明るくても読めるようにする
            GUIStyle outlineStyle = new GUIStyle(numberStyle)
            {
                normal = { textColor = Color.black }
            };
            Vector2[] offsets = { new Vector2(-3, -3), new Vector2(3, -3), new Vector2(-3, 3), new Vector2(3, 3) };
            foreach (var offset in offsets)
            {
                GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height), remainingText, outlineStyle);
            }

            GUI.Label(rect, remainingText, numberStyle);
        }

        // ★追加：連打ボタン画像（押した瞬間だけ「押した画像」、それ以外は「押していない画像」）
        if (showMashButtonImage)
        {
            bool pressed = mashButtonVisualPressed;
            Texture2D tex = (pressed && mashButtonPressedImage != null) ? mashButtonPressedImage : mashButtonNormalImage;

            if (tex != null)
            {
                float size = Screen.height * mashButtonSizeRatio;
                float buttonCenterX = dualDown ? uiCenterX : Screen.width * mashButtonXRatio;
                Rect rect = new Rect(
                    buttonCenterX - size / 2f,
                    Screen.height * mashButtonYRatio - size / 2f,
                    size, size);

                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            }
        }
    }

    //-----------------------------------------------------
    // ヒットストップ
    //-----------------------------------------------------
    // ★追加：被弾アニメーション（Trigger）を予約する。実際に立てるのはFirePendingHitAnimation()。
    void RequestHitAnimation(bool hasDirection, bool fromFront)
    {
        pendingHitTrigger = true;
        pendingHitHasDirection = hasDirection;
        pendingHitFromFront = fromFront;
    }

    // ★追加：予約していた被弾Triggerを、ここで1回だけ立てる。
    //   この一撃でHPが0になった場合は被弾アニメーションを再生しない（直後にダウン開始のアニメーションが再生されるため）。
    void FirePendingHitAnimation()
    {
        if (!pendingHitTrigger) return;
        pendingHitTrigger = false;

        if (animator == null || HP <= 0) return;

        animator.SetTrigger("Hit");
        if (pendingHitHasDirection)
        {
            // 反対側の未消費フラグが残っていると、次の被弾で誤ったアニメーションに遷移しうるので先に消す
            animator.ResetTrigger(pendingHitFromFront ? "Hit-back" : "Hit-front");
            animator.SetTrigger(pendingHitFromFront ? "Hit-front" : "Hit-back");
            DLog($"[{PlayerName}] 被弾アニメーション再生：攻撃者は{(pendingHitFromFront ? "前方" : "後方")}（{(pendingHitFromFront ? "Hit-front" : "Hit-back")}）");
        }
    }

    // ★追加：Triggerを使わず、HeadHitステートを頭から1回だけ直接再生する。
    //   この一撃でHPが0になった場合は再生しない（直後にダウン開始のアニメーションが再生されるため）。
    void PlayHeadHitDirect()
    {
        if (animator == null || HP <= 0) return;
        animator.Play("HeadHit", 0, 0.0f);
    }

    // framesフレームの間だけ、そのフレームから入力・状態更新を止めて「少しだけ動けない」状態にする。
    // すでにヒットストップ中の場合は、残り時間を延ばしすぎないよう長い方の時間を採用する。
    // playHeadHitAnimation: ヒットストップが終わった瞬間に"HeadHit"（被弾）アニメーションを再生するか。
    //   通常被弾はtrue、仁王立ちガード成功時はfalse（ガードのポーズを崩したくないため）を渡す。
    void StartHitStop(int frames, bool playHeadHitAnimation = true)
    {
        if (!enableHitStop || frames <= 0) return;

        hitStopTimer = Mathf.Max(hitStopTimer, frames);
        pendingHeadHitAnimation = playHeadHitAnimation;

        if (freezeAnimatorDuringHitStop && animator != null)
        {
            // ★修正：ここで即座にHeadHitを再生すると、直後にspeedを0にするせいで
            //   1フレーム目の姿勢のまま止まってしまい「アニメーションが再生された」ようには見えなかった。
            //   なので今は「その場でアニメーションを一時停止させる」だけにし、
            //   実際にHeadHitを再生するのはヒットストップが終わるタイミング（EndHitStop）に任せる。
            animator.speed = 0f;
        }

        DLog($"[{PlayerName}] ヒットストップ開始 {frames}フレーム");
    }

    // ヒットストップ終了時にアニメーション速度を元に戻し、必要ならHeadHitアニメーションを再生する
    void EndHitStop()
    {
        if (freezeAnimatorDuringHitStop && animator != null)
        {
            // ★追加：掴みポーズで止めている最中は、ヒットストップが終わっても止めたままにする
            animator.speed = grabHoldFrozen ? 0f : 1f;
        }

        // ヒットストップで一瞬止めていたぶん、止まった直後に被弾アニメーションを「1回だけ」再生する。
        // ガード成功時のヒットストップ（pendingHeadHitAnimation=false）ではここは実行されない。
        if (pendingHeadHitAnimation && animator != null)
        {
            if (playHeadHitStateDirectly) PlayHeadHitDirect();
            else FirePendingHitAnimation();
        }

        // ★追加：ヒットストップ中は相手もアニメーションも止まっているので、
        //   止まっている間に滑らないよう、ヒットストップが終わった瞬間にノックバックを開始する。
        if (hasPendingKnockback)
        {
            hasPendingKnockback = false;
            ApplyKnockback(pendingKnockbackVelocity);
        }

        DLog($"[{PlayerName}] ヒットストップ終了");
    }

    //-----------------------------------------------------
    // やられ状態（行動不能）
    //-----------------------------------------------------
    // 被弾すると、攻撃側の「やられ状態の時間」の間だけ行動不能（Stunned）になる。
    // この間は入力を一切受け付けない（Updateでisfree扱いにならず、TickBusyStateで時間だけ消化される）。
    // やられ中に再度攻撃が当たると、連続ヒット数が増え、残り時間はmax(残り, 新しい攻撃の時間)に延長される。
    private int stunComboCount = 0;          // 今のやられ状態中に連続で当たった回数（0=やられ中ではない）
    private int lastStunComboCount = 0;      // 直前に終わったやられ状態での連続ヒット数
    private bool showStunComboDebug = false; // F10で切り替える、連続ヒット数の画面表示ON/OFF
    private int stunBarTotalFrames = 0;      // ★追加：F10表示用。今のやられ状態の合計フレーム数（延長されたら増える）。消化済み部分を□で残すために使う

    void StunDLog(string message)
    {
        if (showStunComboDebug) Debug.Log($"[STUN] {message}");
    }

    // 被弾時に呼ぶ。frames=やられ状態のフレーム数、willBeKnockedDown=この一撃でHPが0になるか
    void ApplyStun(int frames, bool willBeKnockedDown)
    {
        // 掴み・投げ・ダウン・死亡・必殺技（無敵）中は、やられ状態に置き換えない
        if (currentState == PlayerState.Grabbed || currentState == PlayerState.Thrown
            || currentState == PlayerState.KnockedDown || currentState == PlayerState.Dead
            || currentState == PlayerState.Special) return;
        if (currentState == PlayerState.Throw && grabbedTarget != null) return; // 相手を掴んでいる最中

        // ★追加：仁王立ち（ガード）中の側には、相手の攻撃のスタン時間を一切反映しない。
        //   ガード成功時は攻撃者側だけがApplyGuardedStunでスタンし、ガードした側はスタンしない。
        if (currentState == PlayerState.Guard || isGuarding) return;

        bool alreadyStunned = currentState == PlayerState.Stunned;

        // この一撃でダウンする場合は、やられ状態にはせず、コンボだけ確定させる（以降はダウン処理に任せる）
        if (willBeKnockedDown)
        {
            if (alreadyStunned) stunComboCount++;
            FinishStunCombo();
            return;
        }

        if (alreadyStunned)
        {
            stunComboCount++;
            int prevTimer = stateTimer;
            stateTimer = Mathf.Max(stateTimer, frames);
            stunBarTotalFrames += Mathf.Max(stateTimer - prevTimer, 0); // 延長された分だけバーの全長も伸ばす
        }
        else
        {
            if (frames <= 0) return; // 0フレーム設定の技はやられ状態にならない
            stunComboCount = 1;
            EnterStunned(frames);
        }

        StunDLog($"[{PlayerName}] 連続ヒット={stunComboCount} / やられ残り={stateTimer}フレーム");
    }

    // ★追加：自分の攻撃が相手の仁王立ち（ガード）に防がれた時に、相手側から呼ばれる。
    //   ガードされた側（攻撃者＝自分）を、指定フレーム数だけ行動不能（Stunned）にする。
    //   通常の被弾（ApplyStun）と違い「連続ヒット数」は増やさない（被弾コンボではないため）。
    //   すでにやられ状態の場合は、残りフレーム数と指定フレーム数の長い方に延長する。
    public void ApplyGuardedStun(int frames)
    {
        if (frames <= 0) return; // 0フレーム設定ならスタンさせない

        // 掴み・投げ・ダウン・死亡・必殺技（無敵）中は、やられ状態に置き換えない（ApplyStunと同じ除外条件）
        if (currentState == PlayerState.Grabbed || currentState == PlayerState.Thrown
            || currentState == PlayerState.KnockedDown || currentState == PlayerState.Dead
            || currentState == PlayerState.Special) return;
        if (currentState == PlayerState.Throw && grabbedTarget != null) return; // 相手を掴んでいる最中

        if (currentState == PlayerState.Stunned)
        {
            int prevTimer = stateTimer;
            stateTimer = Mathf.Max(stateTimer, frames);
            stunBarTotalFrames += Mathf.Max(stateTimer - prevTimer, 0); // 延長された分だけバーの全長も伸ばす
        }
        else
        {
            EnterStunned(frames);
        }

        DLog($"[{PlayerName}] 相手の仁王立ちガードに防がれ、{frames}フレームスタン（残り{stateTimer}フレーム）");
    }

    // 実行中の行動を中断してやられ状態に入る
    void EnterStunned(int frames)
    {
        StopMoveAnimation();
        ResetAttackTriggers();
        DisableAllHitboxes();   // 攻撃中だった場合、その当たり判定を消す
        isGuarding = false;
        canThrow = true;
        throwReaching = false;  // 掴み待ち（まだ掴めていない投げ動作）だった場合は中断

        currentState = PlayerState.Stunned;
        stateTimer = frames;
        stunBarTotalFrames = frames; // F10表示用：バーの全長を初期化

        DLog($"[{PlayerName}] やられ状態に入りました（{frames}フレーム）");
    }

    // やられ状態の連続ヒット数を確定する（やられ状態が終わった時・ダウンした時に呼ぶ）
    void FinishStunCombo()
    {
        if (stunComboCount <= 0) return;
        lastStunComboCount = stunComboCount;
        StunDLog($"[{PlayerName}] やられ状態終了。連続ヒット数={lastStunComboCount}");
        stunComboCount = 0;
    }

    // ★追加：F10表示用。今「通常攻撃（パンチ／キック／上キック／下キック）」のクールダウン中なら、その残り／全長フレームを返す。
    //   全長は、命中済みなら命中時の合計フレーム、空振り中なら空振り時の合計フレーム（攻撃を出した瞬間からの合計時間）。
    public bool TryGetAttackCooldown(out string attackName, out int remainFrames, out int totalFrames)
    {
        attackName = "";
        remainFrames = 0;
        totalFrames = 0;

        switch (currentState)
        {
            case PlayerState.Punch: attackName = "パンチ"; break;
            case PlayerState.Kick: attackName = "キック"; break;
            case PlayerState.UpKick: attackName = "上キック"; break;
            case PlayerState.DownKick: attackName = "下キック"; break;
            default: return false;
        }

        remainFrames = Mathf.Max(stateTimer, 0);
        int total = attackLandedThisAttack ? GetHitFrames(CurrentAttackType) : currentAttackMissFrames;
        totalFrames = Mathf.Max(total, remainFrames);
        return true;
    }

    // F10デバッグ表示：
    //   ・黄色の文字…やられ状態の情報（残りフレーム／連続ヒット数）
    //   ・水色の文字…相手が出した通常攻撃のクールタイム情報（相手が攻撃中でなければ「なし」）
    //   ・その下に、フレームバーを「やられ状態（黄）」「相手の技のクールタイム（水色）」の順に上下で隣り合わせに並べて表示する。
    //     ■（塗りつぶし）＝残りフレーム／□（枠だけ）＝消化済み。1マス＝1フレームで、5フレームごとに少し間を空ける。
    //     ★変更：2本のバーは同じ大きさ・同じ間隔のマスで描くため、フォントの字幅の違いに左右されず、上下で同じフレームの位置が揃う。
    //     P1：左端を起点に右から左へ減る／P2：右端を起点に左から右へ減る（□が左から増える）。
    void DrawStunComboDebug()
    {
        if (!showStunComboDebug) return;

        // P2は画面右側に表示する。PlayerInputのindexが1、またはPlayerNameが"P2"ならP2とみなす。
        int index = playerInput != null ? playerInput.playerIndex : 0;
        bool isRightSide = index == 1 || PlayerName == "P2";
        float width = Screen.width * 0.45f;
        float x = isRightSide ? Screen.width - width - 20f : 20f;
        float y = Screen.height * 0.3f;

        // ---- 数値情報（文字）----
        bool stunned = currentState == PlayerState.Stunned;
        int stunRemain = stunned ? Mathf.Max(stateTimer, 0) : 0;
        int stunTotal = stunned ? Mathf.Max(stunBarTotalFrames, stunRemain) : 0;
        string stateText = stunned ? $"やられ中（残り{stunRemain}F）" : "通常";
        string stunText = $"[{PlayerName}] やられ状態\n状態: {stateText}\n連続ヒット数: {stunComboCount}\n直前のコンボ: {lastStunComboCount} ヒット";
        y += DrawOutlinedLabel(new Rect(x, y, width, 0f), stunText, Color.yellow, isRightSide) + 6f;

        int cdRemain = 0, cdTotal = 0;
        string cooldownText = "相手の技: なし";
        if (enemyPlayer != null && enemyPlayer.TryGetAttackCooldown(out string atkName, out cdRemain, out cdTotal))
        {
            cooldownText = $"相手の技: {atkName}（クールタイム残り{cdRemain}F）";
        }
        else
        {
            cdRemain = 0;
            cdTotal = 0;
        }
        y += DrawOutlinedLabel(new Rect(x, y, width, 0f), cooldownText, Color.cyan, isRightSide) + 6f;

        // ---- 仁王立ち上昇倍率／ダメージ量（緑）----
        string buffText = isGuardBuffed
            ? $"仁王立ち上昇倍率: ×{Mathf.Max(1f, guardBuffMultiplier):F2}（上昇中）"
            : "仁王立ち上昇倍率: ×1.00（なし）";
        string penaltyText = guardFailDamageTimer > 0
            ? $"／ガード失敗ペナルティ中：被ダメージ×{guardFailDamageMultiplier:F2}（残り{guardFailDamageTimer}F）"
            : "";
        string damageText = lastDamageTaken > 0 || lastDamageKind != "なし"
            ? $"直前に受けたダメージ: {lastDamageTaken}（{lastDamageKind}" + (lastDamageBase != lastDamageTaken ? $"／元{lastDamageBase}" : "") + "）"
            : "直前に受けたダメージ: なし";
        string guardInfoText = $"{buffText}\n現在の攻撃力: {atk}\n{damageText}{penaltyText}";
        y += DrawOutlinedLabel(new Rect(x, y, width, 0f), guardInfoText, Color.green, isRightSide) + 8f;

        // ---- フレームバー（上：やられ状態／下：相手の技のクールタイム）----
        float cell = f10DebugFontSize * 0.8f;   // 1フレームぶんのマスの大きさ（フォントサイズに連動）
        float pitch = cell + 3f;                // マスの間隔
        float groupGap = cell * 0.5f;           // 5フレームごとの追加の間
        float rowHeight = cell + 6f;

        // 1行に収まるフレーム数（5の倍数）。収まらない分は次の行へ折り返す。上下のバーは同じ行位置で隣り合わせに描く。
        int perRow = 5;
        while (BarSlotX(perRow + 5, pitch, groupGap) - groupGap <= width) perRow += 5;
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(stunTotal, cdTotal) / (float)perRow));

        for (int r = 0; r < rows; r++)
        {
            DrawFrameBarRow(x, y, width, isRightSide, r, perRow, stunRemain, stunTotal, cell, pitch, groupGap, Color.yellow);
            y += rowHeight;
            DrawFrameBarRow(x, y, width, isRightSide, r, perRow, cdRemain, cdTotal, cell, pitch, groupGap, Color.cyan);
            y += rowHeight + 4f;
        }
    }

    // バーの「何マス目(0始まり)」の起点側からの距離。5マスごとにgroupGapだけ間を空ける。
    static float BarSlotX(int slot, float pitch, float groupGap)
    {
        return slot * pitch + (slot / 5) * groupGap;
    }

    // フレームバーの1行ぶん（r行目）を描く。remain個の■（塗り）と、total-remain個の□（枠のみ）。
    // P1は左端が起点、P2は右端が起点（□が反対側＝左側から増えていく）。
    void DrawFrameBarRow(float x, float y, float width, bool anchorRight, int r, int perRow,
                         int remain, int total, float cell, float pitch, float groupGap, Color color)
    {
        Color prev = GUI.color;
        for (int i = r * perRow; i < Mathf.Min(total, (r + 1) * perRow); i++)
        {
            float offset = BarSlotX(i - r * perRow, pitch, groupGap);
            float cx = anchorRight ? x + width - offset - cell : x + offset;
            Rect cellRect = new Rect(cx, y, cell, cell);

            // 黒い縁取り
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cellRect.x - 1f, cellRect.y - 1f, cell + 2f, cell + 2f), Texture2D.whiteTexture);

            // 色付きの本体
            GUI.color = color;
            GUI.DrawTexture(cellRect, Texture2D.whiteTexture);

            // 消化済みフレームは中をくり抜いて「□」にする
            if (i >= remain)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(new Rect(cellRect.x + 2f, cellRect.y + 2f, cell - 4f, cell - 4f), Texture2D.whiteTexture);
            }
        }
        GUI.color = prev;
    }

    // ★追加：縁取り付きのラベルを描画し、使用した高さを返す（文字が折り返しても下段と重ならないよう、高さは内容から計算する）。
    //   rect.heightは無視して、f10DebugFontSizeと幅から算出する。
    float DrawOutlinedLabel(Rect rect, string text, Color color, bool rightAlign)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = f10DebugFontSize,
            fontStyle = FontStyle.Bold,
            alignment = rightAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft, // P2は右端に寄せる
            wordWrap = true,
        };
        style.normal.textColor = color;
        GUIStyle outline = new GUIStyle(style);
        outline.normal.textColor = Color.black;

        float height = style.CalcHeight(new GUIContent(text), rect.width);
        rect.height = height;

        Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
        foreach (var o in offsets)
        {
            GUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), text, outline);
        }
        GUI.Label(rect, text, style);
        return height;
    }

    //-----------------------------------------------------
    // 被弾時のノックバック
    //-----------------------------------------------------
    // ノックバックを要求する。ヒットストップが有効な場合はその終了時（EndHitStop）に、
    // 無効な場合はその場ですぐにApplyKnockbackを実行する。
    // ※StartHitStopの早期return条件（enableHitStopがfalse、またはヒットストップ時間が0以下）と揃えてある。
    //   appliedHitStopFrames: 今回の被弾で実際にStartHitStopへ渡したヒットストップのフレーム数。
    void RequestKnockback(Vector3 velocity, int appliedHitStopFrames)
    {
        // ★追加：攻撃が当たった瞬間に、相手(自分)の今の速度をXYZすべて0にする。
        //   前の被弾の吹き飛び中・落下中・移動中の勢いが残ったまま、ノックバックが上乗せされるのを防ぐ。
        //   （ヒットストップ中に前の勢いのまま滑ってしまうのも防ぐ。実際にノックバックを与えるのはその後）
        ZeroRigidbodyVelocity();

        if (enableHitStop && appliedHitStopFrames > 0)
        {
            hasPendingKnockback = true;
            pendingKnockbackVelocity = velocity;
        }
        else
        {
            ApplyKnockback(velocity);
        }
    }

    // ★追加：Rigidbodyの速度（線形速度）をXYZすべて0にする。
    //   速度のプロパティ名がUnity 6以降はlinearVelocity、それ以前はvelocityで異なるため、バージョンで切り替える。
    //   代入で直接0にするので、同じフレームに何度呼んでも結果は同じ（AddForceで打ち消す方式のような二重適用は起きない）。
    void ZeroRigidbodyVelocity()
    {
        if (rb == null) return;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
    }

    // 実際にRigidbodyへ初速を与える。LaunchByThrow()と同じくAddForce(VelocityChange)を使う
    // （rb.velocity / rb.linearVelocity のUnityバージョン差を避けるため）。
    // ★currentStateは変更しない（通常被弾は元々状態遷移しないため、それに合わせている）。
    void ApplyKnockback(Vector3 velocity)
    {
        if (rb == null) return;

        // ★追加：ノックバックを加える直前に、今の速度をすべて0にしてから加える。
        //   ヒットストップ中に重力などで付いた速度が混ざらず、与えた初速そのものがノックバックになる。
        ZeroRigidbodyVelocity();
        rb.AddForce(velocity, ForceMode.VelocityChange);

        // しっかり浮くほどの上方向速度がある場合は空中扱いにして、飛んでいる間の空中ジャンプを防ぐ。
        // 着地時はOnCollisionEnterでtrueに戻る。
        // （ほとんど浮かない小さな値でfalseにすると、着地イベントが来ずfalseのままになる恐れがあるためしきい値を設けている）
        if (velocity.y >= 1f) Jumpflag = false;

        DLog($"[{PlayerName}] ノックバック velocity={velocity}");
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
        EffectDLog($"[{PlayerName}] 攻撃力上昇(ガードバフ)エフェクト開始 pos={activeGuardBuffEffect.transform.position}");
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
        EffectDLog($"[{PlayerName}] 攻撃力上昇(ガードバフ)エフェクト終了");
    }

    //-----------------------------------------------------
    // 拘束のない状態（Idle/Move/Crouch）での入力処理
    //-----------------------------------------------------
    // 優先度：必殺技 ＞ ジャンプ ＞ 投げ ＞ パンチ ＞ キック ＞ 仁王立ち ＞ 移動
    void HandleFreeInput()
    {
        bool isCrouchInput = moveInput.y <= crouchInputThreshold;

        // --- しゃがみの状態遷移（見た目の同期は毎フレームUpdate冒頭のSyncCrouchVisual()が別途担当）---
        if (isCrouchInput && currentState != PlayerState.Crouch)
        {
            EnterCrouch();
        }
        else if (!isCrouchInput && currentState == PlayerState.Crouch)
        {
            ExitCrouch();
        }

        // --- アクションボタン ---
        if (wantSpecial)
        {
            if (CanUseSpecial())
            {
                EnterSpecial();
                return;
            }
            GaugeDLog($"[{PlayerName}] 漢気ゲージが足りず必殺技を発動できません（現在{kankiGauge}/{specialRequiredGauge}）");
        }
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
                lastMoveDir = MoveDirection.None; // 次に動き出した時、必ずTriggerが再発火するようにリセット
            }
        }
        //動いていないときはアニメーターをMoveをfalseにする
        if(PlayerState.Idle == currentState)
        {
            animator.SetBool("Move", false);
        }
    }

    // ★追加：アクション（パンチ・キック・ガード・投げ・必殺技・ジャンプ）へ遷移する際に、
    //   Animatorの"Move"boolをOFFにするための共通処理。
    //   これを呼ばないと、移動中にアクションボタンを押した瞬間、状態(currentState)側は
    //   正しくアクションへ切り替わっているのに、Animator側だけ"Move"=trueが残り続け、
    //   移動アニメーションが優先されているように見えてしまう。
    //   合わせてlastMoveDirもリセットし、アクション終了後に再度動き出した時、
    //   Move()側のTrigger発火判定（currentState != Moveの分岐）が正しく効くようにする。
    void StopMoveAnimation()
    {
        animator.SetBool("Move", false);
        if (hasMoveBackwardParam) animator.SetBool("MoveBackward", false);
        lastMoveDir = MoveDirection.None;
    }

    // プレイヤーを指定方向へ移動させ、その方向を向かせる
    void Move(Vector3 worldDirection)
    {
        bool isForward = worldDirection == Vector3.forward;
        MoveDirection newDir = isForward ? MoveDirection.Forward : MoveDirection.Backward;

        // ★追加：移動を開始した瞬間、または移動方向が切り替わった瞬間だけTriggerを発火する
        //   （毎フレーム発火させると同じアニメーションが再生し直され続けてカクつくため）
        if (currentState != PlayerState.Move || newDir != lastMoveDir)
        {
            animator.SetBool("Move", true);
            lastMoveDir = newDir;
        }

        // ★追加：スティックの倒し具合（0?1）に応じて、移動アニメーションの再生速度を変化させる
        //   moveInput.xの絶対値をそのまま「移動量」として扱い、minMoveAnimSpeed?maxMoveAnimSpeedの範囲へ変換する
        float inputMagnitude = Mathf.Clamp01(Mathf.Abs(moveInput.x));
        float animSpeed = Mathf.Lerp(minMoveAnimSpeed, maxMoveAnimSpeed, inputMagnitude);
        animator.SetFloat("MoveSpeedMultiplier", animSpeed);

        // ★追加：実際の移動速度（moveSpeed）も同じinputMagnitudeを使って可変にする。
        //   入力が閾値ギリギリ（0扱い）でもminMoveSpeedRatio分は必ず動き、最大まで倒すとmoveSpeedそのまま出る。
        float speedRatio = Mathf.Lerp(minMoveSpeedRatio, 1f, inputMagnitude);
        float actualMoveSpeed = moveSpeed * speedRatio;

        currentState = PlayerState.Move;
        // ※Space.Worldを指定し、向きが変わっても常に世界座標の指定方向へ移動するようにする
        transform.Translate(worldDirection * actualMoveSpeed * Time.deltaTime, Space.World);

        // ★変更：自動向き合いがONの間は、移動方向ではなく相手の方向を向く（LateUpdateのAutoFaceOpponentが担当）。
        //   従来の「移動方向を向く」挙動は、自動向き合いをOFFにした時だけ使う。
        if (!enableAutoFacing) FaceDirection(worldDirection);

        // ★追加：相手から離れる方向（後退）かどうかをAnimatorへ伝える。
        //   相手の方を向いたまま下がるため、Animator側で後退モーションへ分岐させたい場合に使う（任意）。
        if (hasMoveBackwardParam) animator.SetBool("MoveBackward", IsRetreating(worldDirection));

        // 漢気ゲージ：相手から離れる方向へ移動している間は減らす
        ApplyGaugeLossIfRetreating(worldDirection);
    }

    // 相手の現在位置を基準に「今の移動方向が相手から離れる方向かどうか」を判定し、
    // 離れる方向であれば漢気ゲージを1秒あたりgaugeLossOnRetreatだけ減らす。
    void ApplyGaugeLossIfRetreating(Vector3 worldDirection)
    {
        Transform opponentTf = GetOpponentTransform();
        if (opponentTf == null)
        {
            GaugeDLog($"[{PlayerName}] 後退判定スキップ：enemyPlayer/enemyのどちらもInspectorで未設定です。");
            return;
        }

        Vector3 toOpponent = opponentTf.position - transform.position;
        toOpponent.y = 0f;
        if (toOpponent.sqrMagnitude < 0.0001f) return;

        // 移動方向と「相手への方向」の内積が負＝相手から離れる方向へ動いている
        float dot = Vector3.Dot(worldDirection.normalized, toOpponent.normalized);
        GaugeDLog($"[{PlayerName}] 後退判定：dot={dot:F2}（負なら後退とみなす）");
        if (dot < 0f)
        {
            ReduceKankiGauge(gaugeLossOnRetreat * Time.deltaTime);
        }
    }

    // ★追加：指定した相手が、自分の向いている方向に対して前方にいるか（true）後方にいるか（false）を返す。
    //   高さ(Y)は無視し、XZ平面上で自分の正面(transform.forward)と相手への方向の内積で判定する
    //   （後退判定ApplyGaugeLossIfRetreating()や投げの向き判定と同じ考え方）。
    //   ほぼ同じ位置に重なっていて方向が決められない場合は、前方として扱う。
    bool IsOpponentInFront(Transform opponentTf)
    {
        Vector3 toOpponent = opponentTf.position - transform.position;
        toOpponent.y = 0f;
        if (toOpponent.sqrMagnitude < 0.0001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return true;

        return Vector3.Dot(forward.normalized, toOpponent.normalized) >= 0f;
    }

    // 対人戦(enemyPlayer)／対CPU戦(enemy)どちらの場合でも、相手のTransformを取得する
    // ★変更：enemy(CPU)が設定されているならCPU戦とみなし、enemyを優先する。
    //   以前はenemyPlayerを先に見ていたため、CPU戦でenemyPlayerに別のPlayer（古い参照やシーン内の別Player）が
    //   入っていると、Enemyではなくそのオブジェクトを基準に向きが決まり、プレイヤーだけ反転することがあった。
    Transform opponentLogged; // ★追加：どの相手を基準にしているかをログに出すための記録
    Transform GetOpponentTransform()
    {
        Transform tf = null;
        if (enemy != null) tf = enemy.transform;
        else if (enemyPlayer != null) tf = enemyPlayer.transform;

        if (tf != opponentLogged)
        {
            opponentLogged = tf;
            if (tf != null) Debug.Log($"[{PlayerName}] 向き合いの基準にする相手: '{tf.gameObject.name}'", this);
        }
        return tf;
    }

    // ★追加：相手の方向へ自動で向きを変える（LateUpdateから毎フレーム呼ばれる）。
    //   ・向きは「相手が+Z側にいれば+Z、-Z側にいれば-Z」の2択（P1/P2の間合いはZ軸で決まる仕様に合わせている）。
    //   ・相手がジャンプで頭上を飛び越えて左右が入れ替わった場合も、自動で向き直る。
    //   ・掴まれ中／投げ飛ばされ中／ダウン中／死亡中は、演出を崩さないよう向きを変えない。
    //   ・最初に相手を認識した1回だけは補間せず即座に向き合わせる（開始直後にゆっくり回る見た目を防ぐ）。
    void AutoFaceOpponent()
    {
        if (!enableAutoFacing) return;
        if (HP <= 0) return;

        switch (currentState)
        {
            case PlayerState.Grabbed:
            case PlayerState.Thrown:
            case PlayerState.KnockedDown:
            case PlayerState.Dead:
                return;
        }

        if (lockFacingDuringAction)
        {
            switch (currentState)
            {
                case PlayerState.Punch:
                case PlayerState.Kick:
                case PlayerState.UpKick:
                case PlayerState.DownKick:
                case PlayerState.Throw:
                case PlayerState.Special:
                    return;
            }
        }

        Transform opponentTf = GetOpponentTransform();
        if (opponentTf == null) return;

        float deltaZ = opponentTf.position.z - transform.position.z;
        if (Mathf.Abs(deltaZ) < autoFaceDeadZone) return; // ほぼ同じ位置：今の向きを維持

        float desiredSign = Mathf.Sign(deltaZ);

        if (!autoFaceSnapped)
        {
            autoFaceSign = desiredSign;
            transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, autoFaceSign), Vector3.up);
            autoFaceSnapped = true;
            return;
        }

        // ★追加：向きの切り替えにヒステリシスを持たせる。
        //   相手とほぼ密着している間はZの左右関係が不安定（押しのけ・ノックバック・攻撃の踏み込み等）なので、
        //   相手が反対側へ autoFaceFlipThreshold 以上回り込んだ時だけ、向きを切り替える。
        //   ★CPU戦(enemyが設定されている時)限定。対人戦は閾値0（＝従来どおり毎フレーム即座に相手の方向へ）。
        bool isCpuBattle = enemy != null;
        float flipThreshold = isCpuBattle ? autoFaceFlipThreshold : 0f;
        if (desiredSign != autoFaceSign && Mathf.Abs(deltaZ) >= flipThreshold)
        {
            autoFaceSign = desiredSign;
            if (isCpuBattle) Debug.Log($"[{PlayerName}] 向きを切り替え: {(autoFaceSign > 0f ? "+Z" : "-Z")}方向へ（相手とのZ差={deltaZ:F2}, 状態={currentState}）", this);
        }

        FaceDirection(new Vector3(0f, 0f, autoFaceSign));
    }

    // ★追加：指定した移動方向が「相手から離れる方向（後退）」かどうかを返す。
    bool IsRetreating(Vector3 worldDirection)
    {
        Transform opponentTf = GetOpponentTransform();
        if (opponentTf == null) return false;

        float deltaZ = opponentTf.position.z - transform.position.z;
        if (Mathf.Abs(deltaZ) < 0.0001f) return false;

        return Mathf.Sign(deltaZ) != Mathf.Sign(worldDirection.z);
    }

    // 指定した世界座標方向へプレイヤーの向きを滑らかに回転させる
    void FaceDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    // ★追加：しゃがみの見た目（コライダー・アニメーター）を、スティック下入力の有無だけで同期する。
    //   currentStateが何であっても（攻撃中・ガード中等でも）この判定だけで見た目が決まるため、
    //   「入力を離したのにしゃがみっぱなしになる」不具合が起きなくなる。
    // ★追加：右向き(+Z)か左向き(-Z)かを返す。
    //   右入力＝Move(Vector3.forward)なので、+Z側を向いていれば右向きとみなす。
    //   Slerpで回転途中でもZ成分の符号で判定できる。
    bool IsFacingRight()
    {
        return transform.forward.z >= 0f;
    }

    void SyncCrouchVisual(bool isCrouchInput)
    {
        if (isCrouchInput == isCrouchVisual) return; // 前フレームから変化なし

        if (isCrouchInput)
        {
            Player_Collider.height = crouchHeight;
            Player_Collider.center = crouchCenter;

            // ★追加：しゃがむ瞬間の向きをAnimatorへ渡す（"Crouch"より先にセットすること）。
            //   Animator側で CrouchFacingRight の値によって右向き/左向きのしゃがみアニメーションへ分岐させる。
            bool facingRight = IsFacingRight();
            animator.SetBool("CrouchFacingRight", facingRight);
            DLog($"[{PlayerName}] しゃがみ開始：{(facingRight ? "右向き" : "左向き")}");
        }
        else
        {
            Player_Collider.height = standHeight;
            Player_Collider.center = standCenter;
        }
        animator.SetBool("Crouch", isCrouchInput);
        isCrouchVisual = isCrouchInput;
    }

    // しゃがみ開始処理（状態機械のcurrentStateのみ管理。見た目はSyncCrouchVisual()が別途担当）
    void EnterCrouch()
    {
        // ★修正：左右移動入力が入ったままスティックを下に倒してしゃがみへ遷移した際、
        //   Moveアニメーション(bool)がONのまま残り、しゃがみアニメーションが正しく
        //   表示されない（＝しゃがめないように見える）不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Crouch;
    }

    // しゃがみ終了処理（状態機械のcurrentStateのみ管理。見た目はSyncCrouchVisual()が別途担当）
    void ExitCrouch()
    {
        currentState = PlayerState.Idle;
    }

    // ジャンプ処理。アニメーション再生とRigidbodyへの力の付与を行う
    void DoJump()
    {
        // ★修正：移動中にジャンプへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   ジャンプよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        animator.ResetTrigger("Land"); // ★追加：前回の着地フラグが未消費で残っていた場合に、次の着地で誤再生しないよう消す
        animator.SetTrigger("Jump");
        PlayActionSe(jumpSe);
        jumpLandSeArmed = true; // ★追加：次に着地した時に着地SEを鳴らす
        rb.AddForce(force);
        Jumpflag = false; // 空中に出たので再度ジャンプできないようにする
    }

    //-----------------------------------------------------
    // 拘束のある状態（攻撃・ガード・投げ）への遷移
    //-----------------------------------------------------

    // ★追加：行動SEを1回再生する共通処理。clip未設定なら何もしない。
    //   PlayOneShotなので、連打しても前の音を途切れさせずに重ねて鳴らせる。
    void PlayActionSe(AudioClip clip)
    {
        if (clip == null) return;
        if (se == null)
        {
            Debug.LogWarning($"[{PlayerName}] AudioSourceコンポーネントが無いため、SEを再生できません。このGameObjectにAudioSourceを追加してください。", this);
            return;
        }
        se.PlayOneShot(clip, actionSeVolume);
    }

    // ★追加：攻撃開始時に、その技のSEを「保留」する。まず空振りかどうかを判定し、空振りでなければ（命中したら）NotifyAttackLandedで鳴らす。
    //   リザルト画面は命中判定を行わないので、保留せずその場で鳴らす。
    void SetAttackSe(AudioClip clip)
    {
        if (isResultMode) { PlayActionSe(clip); return; }
        pendingAttackSe = clip;
    }

    // ★追加：空振りSEを鳴らす共通処理。1回の攻撃につき1回だけ鳴る。その技のSEは鳴らさない。リザルト画面（攻撃が当たらない仕様）では鳴らさない。
    void PlayMissSe()
    {
        if (isResultMode || missSePlayedThisAttack) return;
        missSePlayedThisAttack = true;
        DLog($"[{PlayerName}] 空振り判定：{CurrentAttackType} / 空振りSE={(missSe != null ? missSe.name : "未設定")}");
        pendingAttackSe = null; // ★空振りが確定したので、その技のSEは鳴らさない（空振りSEだけを鳴らす）
        PlayActionSe(missSe);
    }

    // ★追加：攻撃（パンチ/キック/上キック/下キック）の状態から抜けた瞬間に、一度も命中していなければ空振りSEを鳴らす。
    //   時間切れでの終了だけでなく、被弾・やられ状態・掴まれ・ダウン等で攻撃が途中で中断された場合も確実に拾うため、
    //   Updateの途中returnの影響を受けないLateUpdateから毎フレーム呼ぶ。
    void CheckMissSeOnAttackEnd()
    {
        bool inThrow = currentState == PlayerState.Throw;
        bool inAction = CurrentAttackType != AttackType.None || inThrow;
        if (wasInAttackForMissSe && !inAction)
        {
            // 直前まで投げ動作なら「掴めたか」、攻撃なら「命中したか」で空振りを判定する
            bool landed = wasInThrowForMissSe ? throwGrabbedThisThrow : attackLandedThisAttack;
            if (!landed) PlayMissSe();
        }
        wasInAttackForMissSe = inAction;
        wasInThrowForMissSe = inThrow;
    }

    // パンチ（弱攻撃）処理。地上か空中かでアニメーションと当たり判定を切り替える
    void EnterPunch()
    {
        // ★リザルト画面モード：パンチが実際に出たことを通知する（クールダウン中は、ここへ来ないので通知されない）
        if (isResultMode) ResultAttackPressed?.Invoke(this);

        // ★修正：移動中にパンチへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   パンチよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        ResetAttackTriggers();
        currentState = PlayerState.Punch;
        stateTimer = punchMissFrames;
        currentAttackMissFrames = punchMissFrames;
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        // パンチの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映して攻撃力を確定する
        baseAtk = GetAttackPower(AttackType.Punch);
        UpdateAtkByGauge();

        if (Jumpflag)
        {
            //弱攻撃(パンチ)
            animator.SetTrigger("Punch");
            SetAttackSe(punchSe);
            EnableAttackHitboxes(punchHitbox);
        }
        else
        {
            //空中攻撃
            animator.SetTrigger("Flying-kick");
            SetAttackSe(flyingKickSe);
            EnableAttackHitboxes(flyingKickHitbox);
        }
    }

    // キック処理。スティックの上下入力で通常／上／下キックに分岐する
    void EnterKick(bool isCrouchInput)
    {
        // ★リザルト画面モード：キックが実際に出たことを通知する（クールダウン中は、ここへ来ないので通知されない）
        if (isResultMode) ResultAttackPressed?.Invoke(this);

        // ★修正：移動中にキックへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   キックよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        ResetAttackTriggers();
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        if (isCrouchInput)
        {
            currentState = PlayerState.DownKick;
            stateTimer = downKickMissFrames;
            currentAttackMissFrames = downKickMissFrames;
            animator.SetTrigger("DownKick");
            SetAttackSe(downKickSe);
            EnableAttackHitboxes(downKickHitbox);

            // 下キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.DownKick);
            UpdateAtkByGauge();
        }
        else if (moveInput.y > upKickInputThreshold)
        {
            currentState = PlayerState.UpKick;
            stateTimer = upKickMissFrames;
            currentAttackMissFrames = upKickMissFrames;
            animator.SetTrigger("UpKick");
            SetAttackSe(upKickSe);
            DLog($"[{PlayerName}] UpKick発動：Triggerを立てました（moveInput.y={moveInput.y:F2}）");
            DebugUpKickAnimator(); // ★デバッグ：Animatorが実際にどのステートへ遷移したかを確認する
            EnableAttackHitboxes(upKickHitbox);

            // 上キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.UpKick);
            UpdateAtkByGauge();
        }
        else
        {
            currentState = PlayerState.Kick;
            stateTimer = kickMissFrames;
            currentAttackMissFrames = kickMissFrames;
            animator.SetTrigger("Kick");
            SetAttackSe(kickSe);
            EnableAttackHitboxes(kickHitbox);

            // 通常キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.Kick);
            UpdateAtkByGauge();
        }
    }

    // ★追加：技ごとの「当たり判定」設定（HitboxSetting）に従って、攻撃用コライダーをONにする。
    //   ONにするだけで、OFFに戻すのは従来どおりDisableAllHitboxes()（技の終了時など）が担当する。
    //   applyYankeeHead=true（既定）の時は、enableHeadHitboxOnAttack（ヤンキー用）がONならP-Headも必ずONにする。
    void EnableAttackHitboxes(HitboxSetting setting, bool applyYankeeHead = true)
    {
        if (setting == null) return;
        TurnOnHitbox(Head, setting.head || (applyYankeeHead && enableHeadHitboxOnAttack));
        TurnOnHitbox(RightArm, setting.rightArm);
        TurnOnHitbox(RightForeArm, setting.rightForeArm);
        TurnOnHitbox(RightHand, setting.rightHand);
        TurnOnHitbox(RightUpLeg, setting.rightUpLeg);
        TurnOnHitbox(RightLeg, setting.rightLeg);
        TurnOnHitbox(RightFoot, setting.rightFoot);
        TurnOnHitbox(LeftArm, setting.leftArm);
        TurnOnHitbox(LeftForeArm, setting.leftForeArm);
        TurnOnHitbox(LeftHand, setting.leftHand);
        TurnOnHitbox(LeftUpLeg, setting.leftUpLeg);
        TurnOnHitbox(LeftLeg, setting.leftLeg);
        TurnOnHitbox(LeftFoot, setting.leftFoot);
    }

    // コライダーが取得できていて、onがtrueの時だけONにする（見つからない場合はStart()でエラーログが出ている）
    static void TurnOnHitbox(CapsuleCollider hitbox, bool on)
    {
        if (on && hitbox != null) hitbox.enabled = true;
    }

    // ★デバッグ：UpKickのTriggerを立てた後、Animatorが実際に何を再生しているかをログに出す。
    //   ・"UpKick"パラメータがAnimatorに存在するか／Trigger型か
    //   ・0.15秒後に再生されているクリップ名、遷移中かどうか、animator.speed、レイヤー数
    //   enableDebugLogがONの時だけ動く。原因が分かったら呼び出しごと削除してよい。
    void DebugUpKickAnimator()
    {
        if (!enableDebugLog || animator == null) return;

        bool found = false;
        foreach (var p in animator.parameters)
        {
            if (p.name == "UpKick")
            {
                found = true;
                DLog($"[{PlayerName}] UpKickパラメータ確認：型={p.type}（Triggerなら正常）");
            }
        }
        if (!found)
        {
            Debug.LogError($"[{PlayerName}] AnimatorにUpKickというパラメータが存在しません。名前の変更・削除がないか確認してください。", this);
        }

        StartCoroutine(LogAnimatorStateAfter(0.15f));
    }

    System.Collections.IEnumerator LogAnimatorStateAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        var clips = animator.GetCurrentAnimatorClipInfo(0);
        string clipName = clips.Length > 0 ? clips[0].clip.name : "(なし)";
        float clipLength = clips.Length > 0 ? clips[0].clip.length : 0f;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        DLog($"[{PlayerName}] UpKick発動{delay}秒後：再生中クリップ={clipName}（クリップ長={clipLength:F2}秒） / ステートのSpeed={st.speed} / Multiplier(パラメータ)の現在値={st.speedMultiplier} / 遷移中={animator.IsInTransition(0)} / animator.speed={animator.speed} / レイヤー数={animator.layerCount}");
    }

    // ★変更：Animatorの"Guard"(Trigger)を、Guard状態に「入った瞬間」だけ立てる。
    //   Guardへ入る／出る経路は複数あるため（通常のガード、被弾、掴まれ、F6デバッグの切り替え等）、
    //   個々の箇所で出し入れせず、状態だけを見て毎フレーム同期する。
    //   Boolと違いトリガーは「出る」合図を持たないため、Guardから抜けた時は未消費のトリガーを消すだけにし、
    //   仁王立ちステートから抜ける遷移はAnimator側（Exit Time等）で設定する。
    void SyncGuardAnimation()
    {
        if (!hasGuardParam || animator == null) return;

        bool inGuard = currentState == PlayerState.Guard;
        if (inGuard == guardAnimApplied) return;

        guardAnimApplied = inGuard;

        if (inGuard)
        {
            // Guardに入った瞬間：仁王立ちアニメーションを1回発火する
            animator.SetTrigger(GuardParam);
        }
        else
        {
            // Guardを抜けた：未消費の"Guard"トリガーが残って後から誤再生されないよう消しておく
            animator.ResetTrigger(GuardParam);
            // ガードを抜けた後に、未消費のGuard-successトリガーが残って後から誤再生されないよう消しておく
            if (hasGuardSuccessParam) animator.ResetTrigger(GuardSuccessParam);
        }
    }

    // 仁王立ち（ガード）処理。ガードフラグを立て、演出用パーティクルを再生する
    void EnterGuard()
    {
        // ★修正：移動中にガードへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   ガードよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Guard;
        stateTimer = guardFrames;
        isGuarding = true;
        guardBlockedThisGuard = false; // 今回の仁王立ちでまだ受け止めていない（終了時もfalseなら失敗）

        ParticleSystem newParticle = Instantiate(
            Men_particle,
            transform.position + Vector3.up,
            Quaternion.Euler(-90f, 0f, 0f));
        newParticle.Play();
        Destroy(newParticle.gameObject, 1.0f);
        EffectDLog($"[{PlayerName}] 仁王立ちエフェクト発生 pos={newParticle.transform.position}");
    }

    // 投げ（掴み）処理。★仕様変更：この時点ではまだ投げ飛ばさず、
    //   手の当たり判定が接触した相手を「掴まれ状態(Grabbed)」へ移行させるだけにする。
    //   実際に相手を投げ飛ばす（放物線状に飛ばす）のは、この掴み拘束(throwFrames)が終わる瞬間
    //   （TickBusyState→ResolveThrowLaunch）であり、掴んでいる間の移動スティック入力で方向が決まる。
    void EnterThrow()
    {
        // ★修正：移動中に投げへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   投げよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Throw;
        stateTimer = throwFrames;
        throwGrabbedThisThrow = false;   // ★追加：まだ掴めていない（掴めないまま終われば空振り）
        missSePlayedThisAttack = false;
        pendingAttackSe = null;
        animator.SetTrigger("Throw-start"); // 掴み（ホールド）モーション。飛ばす瞬間は別トリガー"Throw-release"を使う
        grabbedTarget = null;

        // ★変更：掴みの成立を「間合い(Z距離)の判定」から「手の当たり判定の接触」へ変更した。
        //   投げ動作の間、左手(P-LeftHand)・左前腕(P-LeftForeArm)の当たり判定をONにしておき、
        //   相手の体に触れた瞬間に掴みが成立する（相手側のOnTriggerEnter → OnGrabHitboxContact）。
        //   触れないまま投げ動作(throwFrames)が終われば空振りで、掴めなかった時と同じく何も起きずにIdleへ戻る。
        throwReaching = true;
        grabHoldFrozen = false;
        EnableAttackHitboxes(throwHitbox, false); // 投げはヤンキー用P-Head一括追加の対象外（throwHitboxのheadで個別に設定）
        DLog($"[{PlayerName}] 投げ開始：投げの当たり判定設定の部位をON（接触した相手を掴む）");
    }

    // ★追加：今が投げ動作中で、colliderが「掴み用の当たり判定（throwHitboxで選んだ部位。既定は左手／左前腕）」かどうかを返す。
    //   相手側のOnTriggerEnterから、「これは通常の攻撃ヒットではなく掴みの接触だ」と区別するために使う。
    //   ※掴み成立後（throwReachingがfalse）でも、同じ物理フレームに残りの手のイベントが届くことがあるため、
    //     throwReachingは見ずに、投げ状態(Throw)であることだけで判定する（ダメージ処理へ流れないようにするため）。
    public bool IsGrabHitbox(Collider c)
    {
        return currentState == PlayerState.Throw && allHitboxes != null && System.Array.Exists(allHitboxes, hb => hb == c);
    }

    // ★追加：投げ動作中の手の当たり判定が相手(target)の体に接触した時に、相手側のOnTriggerEnterから呼ばれる。
    //   ここで初めて掴みを成立させ、以降は従来どおりの投げ処理（相手をGrabbedへ→脱出判定→
    //   拘束時間が終わったらResolveThrowLaunchで投げ飛ばす）に入る。
    public void OnGrabHitboxContact(Player target)
    {
        // 既に掴んだ後の2つ目の手の接触や、掴めない状態（ジャンプ中等）の相手への接触は何もしない
        if (currentState != PlayerState.Throw || !throwReaching || grabbedTarget != null) return;
        if (!canThrow || target == null || !target.CanBeGrabbed()) return;

        DLog($"[{PlayerName}] 掴み成立（手が接触）：{target.PlayerName}");
        throwGrabbedThisThrow = true;
        PlayActionSe(throwSe); // ★追加：空振りではなく掴めたので、投げのSEを鳴らす
        throwReaching = false;
        canThrow = false;                      // 一度成立したら再度多重に発動しないようにする
        grabbedTarget = target;

        // 手の当たり判定は役目を終えたのでOFFにする（以降の接触でダメージ判定へ流れないようにする）
        DisableAllHitboxes();

        // 接触した瞬間から数えてthrowFrames後に投げ飛ばす（脱出判定の猶予もこの間）
        stateTimer = throwFrames;

        target.transform.Translate(0f, 0f, -0.0025f); // 敵を少し引き寄せる（既存の演出を踏襲）
        target.EnterGrabbed(this);                    // 相手を掴まれ状態へ移行させる（脱出タイマーは相手のHPから決まる）

        // ★アニメーションの止め方はInspectorのenableThrowStartPlayで切り替える。
        //   止めたアニメーションは、投げ成立／不成立の瞬間にResumeGrabHoldAnimation()で再開される。
        if (enableThrowStartPlay && throwStartPlayFrames > 0)
        {
            // ON：接触後throwStartPlayFramesフレームだけ再生を続け、その後に止める（TickBusyStateで残り時間を監視）
            grabHoldTimer = throwStartPlayFrames;
            grabHoldFrozen = false;
        }
        else
        {
            // OFF：接触した瞬間にアニメーションを止める
            grabHoldTimer = 0;
            grabHoldFrozen = true;
            if (animator != null) animator.speed = 0f;
        }
    }

    // ★追加：外部（掴んでくる相手）から「今、掴まれる（投げられる）ことができる状態か」を問い合わせるための公開メソッド。
    //   Player_status(Attack)は他の処理でほとんど更新されておらず信頼できないため、
    //   実際の行動状態であるcurrentStateを基準に判定する。
    public bool CanBeGrabbed()
    {
        // ★追加：ジャンプ中（空中）の相手は掴めない。Jumpflag==true が「接地している」状態を表す。
        //   （通常ジャンプ・被弾ノックバックで浮いている間はfalseになる）
        return HP > 0
            && Jumpflag
            && currentState != PlayerState.Grabbed
            && currentState != PlayerState.Thrown
            && currentState != PlayerState.KnockedDown
            && currentState != PlayerState.Dead;
    }

    // ★追加：現在のHPから、掴まれた際に脱出（振りほどき）に必要な時間を算出する。
    //   HPが高いほど短時間で脱出でき、HPが低いほど脱出に時間がかかる（＝投げられやすい）仕様。
    int CalculateEscapeFrames()
    {
        float hpRatio = maxHP > 0 ? Mathf.Clamp01((float)HP / maxHP) : 0f;
        return Mathf.RoundToInt(Mathf.Lerp(escapeFramesAtZeroHp, escapeFramesAtFullHp, hpRatio));
    }

    // ★追加：相手に掴まれた瞬間に、相手側(grabber)から呼び出される。
    //   ここではまだダメージは発生させず（ダメージは実際に投げ飛ばされた瞬間に発生させる）、
    //   脱出のための拘束状態(Grabbed)に入るだけにする。
    public void EnterGrabbed(Player grabber)
    {
        StopMoveAnimation();
        DisableAllHitboxes(); // 掴まれた瞬間に自分の攻撃判定は念のため止めておく
        isGuarding = false;

        currentState = PlayerState.Grabbed;
        grabbingPlayer = grabber;
        escapeTimer = CalculateEscapeFrames();
        // 掴まれた瞬間にスティックを倒していても、それを「抵抗入力」とは数えない
        grabStickWasActive = Mathf.Abs(moveInput.x) >= moveInputThreshold
                          || Mathf.Abs(moveInput.y) >= moveInputThreshold;

        animator.SetTrigger("Grabbed"); // ★要Animator追加：掴まれ中（もがき）専用のアニメーション。"Thrown"（飛んでいる間）とは別にする

        DLog($"[{PlayerName}] 掴まれた！脱出に必要なフレーム数={escapeTimer}（HP={HP}/{maxHP}）");
    }

    // ★追加：今、投げの方向入力（前／後のスティック入力）をしているか。
    //   掴んでいる側がこれをtrueにしている間は、掴まれている側は脱出できない（入力した時点で投げが確定するため）。
    public bool IsInputtingThrowDirection => Mathf.Abs(moveInput.x) >= moveInputThreshold;

    // ★追加：掴んでいる側が「今この瞬間に投げを確定させられる」か。
    //   掴みが成立してからthrowInputStartDelayFramesフレーム以上経過していて、かつ方向入力をしている場合にtrue。
    //   （掴み成立時にstateTimer=throwFramesへ設定しているので、経過フレーム = throwFrames - stateTimer）
    bool CanCommitThrowNow =>
        currentState == PlayerState.Throw && grabbedTarget != null
        && IsInputtingThrowDirection
        && (throwFrames - stateTimer) >= throwInputStartDelayFrames;

    // ★追加：掴まれている間、毎フレーム呼ばれる。時間経過に加え、
    //   ボタン（既存の意図フラグ）かスティックの入力があるたびに追加で脱出時間を短縮する。
    void TickGrabbed()
    {
        escapeTimer -= frameStep;

        // ★修正：スティックは「倒した瞬間」だけを入力として数える。
        //   以前は倒している間ずっと毎フレーム短縮されていたため、押しっぱなしで一瞬で脱出でき、
        //   HPによる脱出時間の差がほぼ意味を持たなくなっていた。
        bool stickActive = Mathf.Abs(moveInput.x) >= moveInputThreshold
                        || Mathf.Abs(moveInput.y) >= moveInputThreshold;
        bool stickPressedNow = stickActive && !grabStickWasActive;
        grabStickWasActive = stickActive;

        bool mashed = wantPunch || wantKick || wantGuard || wantJump || wantThrow || wantSpecial
                   || stickPressedNow;
        if (mashed)
        {
            escapeTimer -= escapeReductionFramesPerInput;
        }

        // ★変更：掴んでいる側が方向入力をしている間は脱出させない（その入力で掴んだ側のUpdateが即座に投げを確定させる）。
        //   Update順がP1/P2で入れ替わっても、同じフレームに「脱出」と「投げ」が競合しないようにするため。
        bool grabberCommittingThrow = grabbingPlayer != null && grabbingPlayer.CanCommitThrowNow;
        if (escapeTimer <= 0 && !grabberCommittingThrow)
        {
            EscapeFromGrab();
        }
    }

    // ★追加：掴みからの脱出に成功した時の処理。自分をIdleへ戻し、掴んでいた相手の投げ動作も中断させる。
    void EscapeFromGrab()
    {
        DLog($"[{PlayerName}] 掴みから脱出成功！");

        currentState = PlayerState.Idle;
        animator.SetTrigger("Grab-escape"); // ★要Animator追加（任意）：脱出演出用トリガー。未設定でも動作に支障はない

        if (grabbingPlayer != null)
        {
            grabbingPlayer.NotifyGrabEscaped();
            grabbingPlayer = null;
        }
    }

    // ★追加：掴んでいた相手に脱出された時、掴んでいる側から呼び出される。
    //   投げ拘束(Throw)を即座に打ち切ってIdleへ戻す。
    public void NotifyGrabEscaped()
    {
        if (currentState != PlayerState.Throw) return; // 既に自分の掴み拘束が終わっていれば何もしない

        DLog($"[{PlayerName}] 掴んでいた相手に逃げられた");

        grabbedTarget = null;
        DisableAllHitboxes();
        isGuarding = false;
        canThrow = true;
        currentState = PlayerState.Idle;
        ResumeGrabHoldAnimation();           // ★追加：止めていたアニメーションを再開してからトリガーを上げる（速度0のままだと遷移しないため）
        animator.SetTrigger("Throw-whiff"); // 投げ失敗（逃げられた）演出用トリガー
    }

    // ★追加：掴み拘束(throwFrames)が時間切れになった＝相手が脱出できなかった時に、
    //   TickBusyState()から呼ばれる。掴んでいる側が方向入力をしていれば実際にダメージを与えて
    //   放物線状に投げ飛ばす。方向入力が無かった場合は「投げ不成立」として扱い、
    //   （脱出できた時と同様に）お互いを後方へ押し出すだけにする。
    void ResolveThrowLaunch()
    {
        if (grabbedTarget == null) return; // 掴み自体が不成立（空振り）だった場合は何もしない

        bool hasDirectionInput = Mathf.Abs(moveInput.x) >= moveInputThreshold;

        if (!hasDirectionInput)
        {
            // ★変更：方向入力が無いまま拘束時間切れ＝投げ不成立。
            //   ダメージは発生させないが、お互いを相手から離れる方向へ放物線を描いて吹き飛ばす。
            DLog($"[{PlayerName}] 方向入力が無いまま拘束時間切れ。投げ不成立扱いで{grabbedTarget.PlayerName}と共に後方へ吹き飛ぶ");

            // ★変更：止めていた掴みモーションを再開する（速度0のままだとThrow-whiffへ遷移できない）。
            //   投げ失敗のトリガー"Throw-whiff"は、掴んでいた側・掴まれていた側とも
            //   PushBackFromGrabFailure()→LaunchByThrow()の中で上げる（二重にトリガーを立てて競合しないため）。
            ResumeGrabHoldAnimation();

            grabbedTarget.ReleaseFromGrabWithoutThrow(this); // 相手側もGrabbedを終了させ、放物線で後方へ吹き飛ばす
            PushBackFromGrabFailure(grabbedTarget);          // 自分も相手から離れる方向へ放物線で後方へ吹き飛ぶ

            grabbedTarget = null;
            return;
        }

        DLog($"[{PlayerName}] 投げ成立！{grabbedTarget.PlayerName}を投げ飛ばす");
        PlayActionSe(throwLaunchSe); // ★追加：相手を投げ飛ばした瞬間のSE

        ResumeGrabHoldAnimation();            // ★追加：止めていた掴みモーションを再開してからトリガーを上げる
        animator.SetTrigger("Throw-release"); // 投げ成立時だけ上げる「投げ飛ばす」モーション用トリガー

        grabbedTarget.damege(throwAtk); // ダメージは掴み成立時ではなく、実際に投げが決まった瞬間に与える

        Vector3 launchDir = GetThrowDirectionFromStick();

        // ★追加：後ろ投げ（自分の向きと逆方向へ投げ飛ばした場合）は、お互いの向きを反転させる。
        //   相手が自分の背後へ飛んでいくため、そのままだと背中を向け合ったままになってしまう。
        //   向きの判定は投げる前の自分の向き(transform.forward)で行う。
        bool isBackThrow = Vector3.Dot(launchDir, transform.forward) < 0f;

        grabbedTarget.LaunchByThrow(launchDir, throwHorizontalSpeed, throwUpSpeed, "Thrown", throwStunFrames);

        if (isBackThrow)
        {
            DLog($"[{PlayerName}] 後ろ投げのため、{grabbedTarget.PlayerName}と互いの向きを反転");
            FlipFacing();
            grabbedTarget.FlipFacing();
        }

        grabbedTarget = null;
    }

    // ★追加：投げ不成立（掴んだ側の無入力タイムアウト）時に、掴まれていた側から呼び出される。
    //   Grabbed状態を終了してIdleへ戻し、掴んでいた相手から離れる方向へ後方へ押し出される。
    public void ReleaseFromGrabWithoutThrow(Player grabber)
    {
        if (currentState != PlayerState.Grabbed) return; // 既にGrabbedでなければ何もしない

        grabbingPlayer = null;

        // ★変更：以前はここでIdleへ戻してから瞬間的に押し出していたが、
        //   PushBackFromGrabFailure()がLaunchByThrow()を流用してThrown状態へ遷移させ、
        //   放物線を描いて吹き飛ばした上で着地時に自動でIdleへ戻すようにした。
        PushBackFromGrabFailure(grabber);

        DLog($"[{PlayerName}] 投げ不成立のため掴みが解け、後方へ吹き飛んだ");
    }

    // ★変更：投げ不成立時に、相手(other)から離れる方向（Z軸）へ、放物線を描いて吹き飛ぶ。
    //   以前は transform.Translate による瞬間的な押し出しだったが、仕様変更により
    //   LaunchByThrow()を流用して実際にThrown状態へ遷移させ、重力に任せて放物線を描かせた上で
    //   着地時（OnCollisionEnter→LandFromThrow）に自動でIdleへ復帰させるようにした。
    //   速度は投げ成立時(throwHorizontalSpeed/throwUpSpeed)より弱いgrabFailHorizontalSpeed/grabFailUpSpeedを使う。
    void PushBackFromGrabFailure(Player other)
    {
        Vector3 awayDirection = GetAwayDirectionFrom(other);

        // ★変更：投げ不成立時は、掴んでいた側・掴まれていた側ともに"Throw-whiff"トリガーを上げる
        //   （以前は専用の"Grab-fail-fly"を使っていた。Animator側でThrow-whiffから吹き飛びモーションへ遷移させること）。
        LaunchByThrow(awayDirection, grabFailHorizontalSpeed, grabFailUpSpeed, "Throw-whiff");
    }

    // ★追加：相手(other)から見て自分が離れるべき方向（Z軸、±Vector3.forward）を返すヘルパー。
    //   PushBackFromGrabFailure専用に、Z座標の位置関係から決定する（同一Z座標の場合はInstanceIDで決定的に振り分ける）。
    Vector3 GetAwayDirectionFrom(Player other)
    {
        float deltaZ = transform.position.z - other.transform.position.z;
        float dir;
        if (Mathf.Abs(deltaZ) > 0.0001f)
        {
            dir = Mathf.Sign(deltaZ);
        }
        else
        {
            dir = GetInstanceID() < other.GetInstanceID() ? -1f : 1f;
        }

        return new Vector3(0f, 0f, dir);
    }

    // ★追加：掴んでいる間の移動スティック入力から、投げ飛ばす水平方向を決定する。
    //   Move()と同じくX入力で前方/後方(Vector3.forward/back)を判定し、
    //   ニュートラルなら自分が向いている方向へ飛ばす。
    Vector3 GetThrowDirectionFromStick()
    {
        if (moveInput.x >= moveInputThreshold) return Vector3.forward;
        if (moveInput.x <= -moveInputThreshold) return Vector3.back;
        return transform.forward;
    }

    private int pendingThrowStunFrames = 0; // ★追加：投げで飛ばされた後、着地時に入るやられ状態のフレーム数

    // ★追加：投げ技によって、放物線状に吹き飛ばされる処理。掴んでいた相手から呼び出される。
    //   Rigidbodyに初速を与えるだけで、あとは重力(既存のJump同様の物理設定)に任せて放物線を描かせる。
    //   ★変更：投げ成立時（相手を飛ばす）だけでなく、投げ不成立時にお互いが後方へ吹き飛ぶ演出
    //   （PushBackFromGrabFailure）でも共用できるよう、再生するアニメーショントリガーを引数化した。
    //   省略時は従来通り"Thrown"（通常の投げ成立で飛ばされる側のモーション）を使う。
    public void LaunchByThrow(Vector3 horizontalDirection, float horizontalSpeed, float upSpeed, string animTrigger = "Thrown", int stunAfterLanding = 0)
    {
        // ★追加：着地後にやられ状態へ移行する時間を記録する（0なら着地後は通常復帰）。
        //   投げ不成立の吹き飛び(PushBackFromGrabFailure)は引数省略＝0なので、やられ状態にはならない。
        pendingThrowStunFrames = stunAfterLanding;

        StopMoveAnimation();
        DisableAllHitboxes();
        isGuarding = false;

        currentState = PlayerState.Thrown;
        grabbingPlayer = null;

        animator.SetTrigger(animTrigger); // 飛んでいる間の専用アニメーション（Grabbedとは別のモーション）

        if (rb != null)
        {
            // ★Note：rb.velocity / rb.linearVelocity はUnityのバージョンによってプロパティ名が異なるため、
            //   どちらの環境でも動くようにAddForce(ForceMode.VelocityChange)で速度を直接加算する方式にしている。
            //   掴まれている間は移動していない想定なので、既存の速度への加算でほぼ意図通りの初速になる。
            Vector3 launchVelocity = horizontalDirection.normalized * horizontalSpeed + Vector3.up * upSpeed;
            rb.AddForce(launchVelocity, ForceMode.VelocityChange);
        }

        DLog($"[{PlayerName}] 投げられて吹き飛んだ！（trigger={animTrigger}）");
    }

    // ★追加：向きをその場で180度反転させる（後ろ投げの後、お互いが再び向かい合うようにするため）。
    //   通常の向き変更(FaceDirection)は徐々に回るが、こちらは投げの瞬間に即座に反転させる。
    //   相手(grabbedTarget)側からも呼ぶため public。
    public void FlipFacing()
    {
        Vector3 flipped = -transform.forward;
        flipped.y = 0f;
        if (flipped.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(flipped.normalized, Vector3.up);
        autoFaceSign = flipped.z >= 0f ? 1f : -1f; // ★追加：反転後の向きをAutoFaceの確定方向にも反映（直後に元の向きへ戻されないように）
    }

    // ★追加：掴みポーズで止めていたアニメーションを再開する。
    //   animator.speedが0のままだとトリガーを上げても遷移しないため、Throw-release／Throw-whiffを上げる直前に必ず呼ぶ。
    void ResumeGrabHoldAnimation()
    {
        grabHoldFrozen = false;
        if (animator != null && hitStopTimer <= 0) animator.speed = 1f; // ヒットストップ中ならEndHitStopが戻す
    }

    // ★追加：投げで吹き飛ばされた後、地面に着地した瞬間にOnCollisionEnterから呼ばれる。
    //   Thrown状態を終えてIdleへ戻す。
    void LandFromThrow()
    {
        DLog($"[{PlayerName}] 投げから着地して復帰");
        currentState = PlayerState.Idle;

        // ★追加：投げ成立で飛ばされていた場合、着地した瞬間からやられ状態（行動不能）に入る。
        //   ダメージでHPが0になっている場合は、ダウン処理（HandleKnockedDown）に任せるため入らない。
        int landStun = pendingThrowStunFrames;
        pendingThrowStunFrames = 0;
        if (landStun > 0 && HP > 0)
        {
            stunComboCount = 1; // 投げを1ヒットとして数える（やられ中にさらに被弾すると加算される）
            EnterStunned(landStun);
            StunDLog($"[{PlayerName}] 投げ着地→やられ状態 {landStun}フレーム");
        }

        animator.SetTrigger("Thrown-land"); // ★要Animator追加（任意）：着地モーション用トリガー。未設定でも動作に支障はない
    }

    // 現在の漢気ゲージで必殺技を発動できるかどうか
    bool CanUseSpecial()
    {
        return kankiGauge >= specialRequiredGauge;
    }

    // 必殺技発動処理。左足（LeftFoot/LeftUpLeg/LeftLeg）の当たり判定をONにして、
    // その状態でspecialFramesフレーム（既定180F=3秒）だけ攻撃を行う。
    // この間は移動のみ可能（攻撃・ガード・投げ・ジャンプ・必殺技の再発動は不可）で、
    // かつ相手からの攻撃を一切受けない（無敵。OnTriggerEnter側で判定）。
    // 相手に左足の判定が当たった場合のみ、specialAtk（Inspector設定）のダメージを与える。
    void EnterSpecial()
    {
        // ★修正：移動中に必殺技へ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   必殺技よりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Special;
        stateTimer = specialFrames;
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        // ★Animator Controller側に"waza"という名前のTriggerパラメータを追加しておくこと。
        //   currentState=Specialの間、移動以外の入力(攻撃・ガード・投げ・ジャンプ・必殺技)は
        //   TickBusyState()側で受け付けないため、stateTimer(=specialFrames)が0になって
        //   PlayerState.Idleへ戻るまで次の技は出せない（＝アニメーションが終わるまで次の技を出せない）。
        //   specialFramesは"waza"アニメーションの実際の長さと一致させること。
        animator.SetTrigger("waza");

        // 左足（3箇所）の攻撃用当たり判定をON。オフに戻す処理はTickBusyState()の
        // 拘束時間終了処理（DisableAllHitboxes）で他の技と共通して行われる。
        EnableAttackHitboxes(specialHitbox);

        // 必殺技の攻撃力（Inspector設定値=specialAtk）を基準に、漢気ゲージ補正を反映して攻撃力を確定する
        baseAtk = specialAtk;
        UpdateAtkByGauge();

        if (se != null && specialSe != null)
        {
            se.PlayOneShot(specialSe);
        }

        if (specialEffectPrefab != null)
        {
            // ★修正：プレイヤーの子オブジェクトとして生成することで、必殺技中プレイヤーが移動しても
            //   エフェクトが追従し続けるようにする（親を指定しないと生成位置に固定されたままになる）。
            //   guardBuffEffectPrefabと同じ仕組み：生成後にlocalPositionを明示設定し、
            //   生成時のプレイヤーの向きに関わらずオフセットがズレないようにする。
            ParticleSystem fx = Instantiate(
                specialEffectPrefab,
                transform.position + specialEffectOffset,
                Quaternion.Euler(-90f, 0f, 0f),
                transform);
            fx.transform.localPosition = specialEffectOffset;
            fx.Play();
            Destroy(fx.gameObject, specialEffectLifetime);
            EffectDLog($"[{PlayerName}] 必殺技エフェクト発生 pos={fx.transform.position}");
        }

        // 自分の漢気ゲージを消費する
        ReduceKankiGauge(specialGaugeCost);

        DLog($"[{PlayerName}] 必殺技発動！（消費ゲージ={specialGaugeCost} / 攻撃力={atk} / 拘束フレーム数={specialFrames} / 無敵）");
    }

    // 攻撃系トリガーの予約をすべてクリアする（前の攻撃予約が残って誤発火するのを防ぐ）
    void ResetAttackTriggers()
    {
        animator.ResetTrigger("Punch");
        animator.ResetTrigger("Flying-kick");
        animator.ResetTrigger("Kick");
        animator.ResetTrigger("UpKick");   // ★追加：上キック/下キックのトリガー予約も消す
        animator.ResetTrigger("DownKick");
        animator.ResetTrigger("Jump");
    }

    // ★追加：必殺技(Special)中だけ許可される移動処理。
    //   通常のMove()はcurrentStateをPlayerState.Moveへ書き換えてしまい、それをやると
    //   「isFree扱い」になってTickBusyState()ではなくHandleFreeInput()側に処理が移ってしまい、
    //   必殺技の拘束時間・当たり判定・無敵状態が途中で解除されてしまう。
    //   そのためcurrentStateやアニメーターの"Move"パラメータには一切触れず、
    //   スティック入力に応じて位置と向きだけを更新する。
    void MoveDuringSpecial()
    {
        Vector3 worldDirection;
        if (moveInput.x >= moveInputThreshold)
        {
            worldDirection = Vector3.forward;
        }
        else if (moveInput.x <= -moveInputThreshold)
        {
            worldDirection = Vector3.back;
        }
        else
        {
            return; // 入力なし：移動しない
        }

        float inputMagnitude = Mathf.Clamp01(Mathf.Abs(moveInput.x));
        float speedRatio = Mathf.Lerp(minMoveSpeedRatio, 1f, inputMagnitude);
        float actualMoveSpeed = moveSpeed * speedRatio;

        transform.Translate(worldDirection * actualMoveSpeed * Time.deltaTime, Space.World);
        if (!enableAutoFacing) FaceDirection(worldDirection); // ★変更：自動向き合いONの間は相手を向いたまま
    }

    // 拘束中の行動（攻撃・ガード・投げ・必殺技）のタイマーを進め、時間切れになったらIdleへ戻す。
    // ただし必殺技(Special)中に限り、タイマー消化と並行して移動だけは受け付ける。
    void TickBusyState()
    {
        // ★追加：掴まれている間は専用の脱出判定（TickGrabbed）だけを行い、
        //   通常の拘束時間消化（stateTimer）とは別ロジックで終了条件を管理する。
        if (currentState == PlayerState.Grabbed)
        {
            TickGrabbed();
            return;
        }

        // ★追加：投げ飛ばされて飛んでいる間は、着地判定(OnCollisionEnter→LandFromThrow)で
        //   終了するため、ここでは何もしない（stateTimerも消化しない）。
        if (currentState == PlayerState.Thrown)
        {
            return;
        }

        // ★追加：やられ状態（行動不能）。時間だけ消化して、終わったらIdleへ戻る。
        if (currentState == PlayerState.Stunned)
        {
            stateTimer -= frameStep;
            if (stateTimer > 0) return;

            FinishStunCombo();
            currentState = PlayerState.Idle;
            DLog($"[{PlayerName}] やられ状態が終わりました");
            return;
        }

        if (currentState == PlayerState.Special)
        {
            MoveDuringSpecial();
        }

        // ★追加：掴み成立後、throwStartPlayFramesフレームだけ掴みモーションを再生したら、そのポーズで止める
        if (currentState == PlayerState.Throw && grabbedTarget != null && !grabHoldFrozen)
        {
            grabHoldTimer -= frameStep;
            if (grabHoldTimer <= 0)
            {
                grabHoldFrozen = true;
                if (animator != null) animator.speed = 0f;
            }
        }

        // ★追加：掴んでいる間に方向入力（前／後）があったら、拘束時間の終了を待たずにその場で投げ飛ばす。
        //   以前は拘束時間が切れるまで投げが決まらず、その前に掴まれた側のHPで決まる脱出時間（特にHP満タン時は短い）が
        //   先に来て「入力しているのに脱出成功になる」ことがあった。入力を受けた時点で投げを確定させる。
        //   ★ただし、掴み成立からthrowInputStartDelayFramesフレームの間は入力を受け付けず、掴んだ状態を保つ。
        if (CanCommitThrowNow)
        {
            ResolveThrowLaunch(); // 方向入力ありなので必ず「投げ成立」になる（grabbedTargetはnullに戻る）
            stateTimer = Mathf.Min(stateTimer, throwReleaseRecoveryFrames); // 投げ飛ばしモーションぶんだけ硬直してからIdleへ戻る
            return;
        }

        stateTimer -= frameStep;

        // ★追加：空振りSE（攻撃開始からmissSeJudgeFramesフレーム経っても未命中なら鳴らす。0なら下の攻撃終了時に鳴らす）
        if (missSeJudgeFrames > 0 && !missSePlayedThisAttack && !attackLandedThisAttack
            && CurrentAttackType != AttackType.None
            && currentAttackMissFrames - stateTimer >= missSeJudgeFrames)
        {
            PlayMissSe();
        }

        if (stateTimer > 0) return;

        // 攻撃系の状態（パンチ/キック/上キック/下キック）が、
        // 一度も命中通知(NotifyAttackLanded)を受けないまま終了したら「空振り」とみなす
        if (CurrentAttackType != AttackType.None && !attackLandedThisAttack)
        {
            PlayMissSe(); // ★追加：空振りSE
            if (HitEffectSpawner.Instance != null && missHitEffectData != null)
            {
                //出す位置の調整
                Vector3 transkunn = transform.position;
                transkunn.y += 2.0f;
                HitEffectSpawner.Instance.SpawnBehindFacing(missHitEffectData, transkunn, transform.forward); // 向きと反対側の斜め上へ
                EffectDLog($"[{PlayerName}] 空振り擬音エフェクト発生 pos={transkunn}");
            }
        }

        // ★追加：相手を一度も掴めないまま投げ動作が終わったら空振り
        if (currentState == PlayerState.Throw && !throwGrabbedThisThrow) PlayMissSe();

        // ★追加：掴み拘束が時間切れになった＝相手が脱出できなかった場合、ここで実際に投げ飛ばす
        if (currentState == PlayerState.Throw)
        {
            ResolveThrowLaunch();
        }

        // ★追加：投げ不成立（方向入力なしのタイムアウト）で自分自身もPushBackFromGrabFailure()により
        //   Thrown状態へ遷移した場合は、ここで即座にIdleへ戻さない。
        //   放物線飛行中の状態管理はThrown用の分岐（このメソッド冒頭）と、
        //   着地判定(OnCollisionEnter→LandFromThrow)に委ねる。
        if (currentState == PlayerState.Thrown)
        {
            canThrow = true;
            return;
        }

        // ★追加：仁王立ちが、相手の攻撃を一度も受け止めないまま終わったら「ガード失敗」としてペナルティを与える
        if (currentState == PlayerState.Guard && !guardBlockedThisGuard)
        {
            ApplyGuardFailPenalty();
        }

        DisableAllHitboxes();
        isGuarding = false;
        canThrow = true;
        throwReaching = false; // ★追加：掴めないまま投げ動作が終わった場合も、掴み待ちを終える
        currentState = PlayerState.Idle;
    }

    //-----------------------------------------------------
    // 復活チャレンジ（ダウン中の処理）
    //-----------------------------------------------------
    // 復活に必要な連打回数のしきい値を計算する（復活回数が増えるほど厳しくなる）
    // ★追加：HandleKnockedDown()とOnGUI()の両方から参照するため共通メソッド化。
    int CurrentMashThreshold()
    {
        return mashThresholdBase + mashThresholdStep * rebornCount;
    }

    // 現在の残り連打回数（0未満にはならない）を計算する。
    // mashCountがmashThresholdを超えた瞬間に復活成功するため、必要な残り回数は
    // 「しきい値 + 1 - 現在の連打数」になる。
    int GetRemainingMashCount()
    {
        int mashThreshold = CurrentMashThreshold();
        return Mathf.Max(mashThreshold + 1 - mashCount, 0);
    }

    // ★追加：P1・P2が同時にダウンしている（両方HP0で、どちらもまだ死亡していない）か。
    //   同時ダウン中はカメラの引き（クローズアップ）をせず、UIを左右に分けて表示する。
    //   ※片方が先にダウンして、もう片方が後から0になる場合は同時ダウンとは扱わない（先に復活／死亡が決まるため）。
    bool IsDualKnockedDown()
    {
        if (!enableDualDownLayout || isResultMode || enemyPlayer == null) return false;
        return HP <= 0 && enemyPlayer.HP <= 0
            && currentState != PlayerState.Dead && enemyPlayer.currentState != PlayerState.Dead;
    }

    // ★追加：画面右側（P2側）のプレイヤーか。PlayerInputのindexが1、またはPlayerNameが"P2"ならP2とみなす。
    bool IsRightSidePlayer()
    {
        int index = playerInput != null ? playerInput.playerIndex : 0;
        return index == 1 || PlayerName == "P2";
    }

    // ★追加：Animatorにその名前・型のパラメータがあるか（無い名前でSetTrigger/SetBoolすると警告が出るため先に確認する）
    bool HasAnimatorParam(string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(paramName)) return false;
        foreach (var param in animator.parameters)
        {
            if (param.type == type && param.name == paramName) return true;
        }
        return false;
    }

    void SetDownTrigger(string paramName)
    {
        if (HasAnimatorParam(paramName, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(paramName);
    }

    void SetDownBool(string paramName, bool value)
    {
        if (HasAnimatorParam(paramName, AnimatorControllerParameterType.Bool)) animator.SetBool(paramName, value);
    }

    // ★追加：ダウン開始（ダウンした瞬間に1回だけ呼ぶ）。Triggerを立て、ダウン中Boolをtrueにする。
    //   移動・攻撃のアニメーションが残っているとダウンに遷移できないことがあるので、先に止める。
    void OnDownStart()
    {
        if (animator == null) return;

        StopMoveAnimation();
        ResetAttackTriggers();

        SetDownBool(downBoolParam, true);
        SetDownTrigger(downStartTrigger);
        DLog($"[{PlayerName}] ダウン開始アニメーション（{downStartTrigger} / {downBoolParam}=true）");
    }

    // ★追加：復活成功の瞬間に呼ぶ。ダウン中Boolをfalseに戻し、復活Triggerを立てる。
    void OnDownRevive()
    {
        downAnimPlayed = false;
        bool wasDual = downWasDual;
        downWasDual = false;
        StartRebornFreeze(!wasDual); // ★追加：復活の瞬間から、自分・相手ともに動けなくする（同時ダウン後でなければ相手を非表示にする）
        if (animator == null) return;

        SetDownBool(downBoolParam, false);
        SetDownTrigger(downReviveTrigger);
        DLog($"[{PlayerName}] 復活アニメーション（{downReviveTrigger} / {downBoolParam}=false）");
    }

    // ★追加：復活した瞬間のカメラ。
    //   同時ダウン後の復活：カメラが寄る・引く演出は一切せず、通常の追従カメラへ戻す。
    //   それ以外：後方へ回り込まず、復活したキャラ正面の引きの画を停止時間ぶん映す。
    void StartRebornStandUpCamera()
    {
        if (fightingCamera == null) return;

        if (downWasDual)
        {
            fightingCamera.ClearReborn();
            return;
        }

        fightingCamera.TriggerRebornStandUpHold(transform, FramesToSeconds(rebornStandUpFreezeFrames));
    }

    // ★追加：復活停止中、相手（P2またはCPU）の見た目を隠す。隠す前に有効だったRendererだけを記録し、後で元に戻す。
    void HideOpponentForReborn()
    {
        if (hiddenOpponentRenderers.Count > 0) return; // すでに隠している

        Component opponent = enemyPlayer != null ? (Component)enemyPlayer : (Component)enemy;
        if (opponent == null) return;

        foreach (UnityEngine.Renderer r in opponent.GetComponentsInChildren<UnityEngine.Renderer>(true))
        {
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            hiddenOpponentRenderers.Add(r);
        }
    }

    // ★追加：非表示にしていた相手のRendererを元に戻す。
    void ShowOpponentAfterReborn()
    {
        foreach (UnityEngine.Renderer r in hiddenOpponentRenderers)
        {
            if (r != null) r.enabled = true;
        }
        hiddenOpponentRenderers.Clear();
    }

    // ★追加：復活の瞬間から rebornStandUpFreezeFrames の間、自分・相手ともに動けなくする。
    //   動作の途中だった相手の攻撃は、当たり判定を消して待機に戻す（止まったまま当たり判定が残らないように）。
    void StartRebornFreeze(bool hideOpponent)
    {
        if (rebornStandUpFreezeFrames <= 0) return;

        rebornFreezeEndFrame = CurrentFrame + rebornStandUpFreezeFrames;
        CancelAttackForFreeze();
        if (enemyPlayer != null) enemyPlayer.CancelAttackForFreeze();
        if (hideOpponent && hideOpponentDuringRebornFreeze) HideOpponentForReborn(); // 復活演出のカメラに相手が映り込まないようにする

        DLog($"[{PlayerName}] 復活：{rebornStandUpFreezeFrames}フレーム、全員停止");
    }

    // ★追加：全員停止の開始時に、攻撃動作の途中だった場合だけ待機に戻す。
    void CancelAttackForFreeze()
    {
        if (currentState != PlayerState.Punch && currentState != PlayerState.Kick
            && currentState != PlayerState.UpKick && currentState != PlayerState.DownKick) return;

        DisableAllHitboxes();
        ResetAttackTriggers();
        StopMoveAnimation();
        stateTimer = 0;
        currentState = PlayerState.Idle;
    }

    // ★追加：復活失敗（Dead）の瞬間に呼ぶ。ダウン中Boolをfalseに戻し、復活失敗Triggerを立てる。
    void OnDownFail()
    {
        downAnimPlayed = false;
        if (animator == null) return;

        SetDownBool(downBoolParam, false);
        SetDownTrigger(downFailTrigger);
        DLog($"[{PlayerName}] 復活失敗アニメーション（{downFailTrigger} / {downBoolParam}=false）");
    }

    // HPが0になった際に毎フレーム呼ばれる、根性復活（ボタン連打による復活）の処理
    void HandleKnockedDown()
    {
        FinishStunCombo(); // ★追加：やられ中にダウンした場合、連続ヒット数を確定する
        currentState = PlayerState.KnockedDown;

        //gameMNG.PlayerUI(rebornTimer, mashCount);

        rebornTimer += frameStep;
        Player_status = Status.Reborn;
        if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Reborn);

        // ★追加：ダウンした瞬間、一度だけダウン開始アニメーション（Trigger）を立て、ダウン中Boolをtrueにする
        if (!downAnimPlayed)
        {
            downAnimPlayed = true;
            OnDownStart();
        }

        // ★追加：同時ダウン中はカメラの引き（クローズアップ）をしない。すでに始めていた場合は通常カメラへ戻す
        bool dualDown = IsDualKnockedDown();
        if (dualDown) downWasDual = true; // 復活時にカメラ演出をしないための記録
        if (dualDown && rebornCamStarted && fightingCamera != null)
        {
            fightingCamera.ClearReborn();
            rebornCamStarted = false;
        }

        //ダウンした瞬間、一度だけ顔・拳へのクローズアップカメラを開始する
        if (!dualDown && !rebornCamStarted && fightingCamera != null)
        {
            fightingCamera.StartRebornCloseUp(transform);
            rebornCamStarted = true;
        }

        // ★追加：漢気復活。制限時間内にL1が押され、漢気ゲージが足りていれば、連打せずに即座に復活する
        if (wantKankiRevive && rebornTimer < rebornTimeLimitFrames && TryKankiRevive())
        {
            return;
        }

        // 復活に必要な連打回数のしきい値（復活回数が増えるほど厳しくなる）
        int mashThreshold = CurrentMashThreshold();

        if (rebornTimer < rebornTimeLimitFrames)
        {
            if (mashCount <= mashThreshold)
            {
                //連打の進捗に応じて復活レベルを算出し、カメラを徐々に引かせる
                if (fightingCamera != null && mashThreshold > 0 && !dualDown)
                {
                    float progress = (float)mashCount / mashThreshold;
                    int level = Mathf.Clamp(Mathf.FloorToInt(progress * fightingCamera.rebornMaxLevel), 0, fightingCamera.rebornMaxLevel);
                    fightingCamera.SetRebornLevel(level);
                }
            }
            else
            {
                //復活成功（最大HPのrebornHpRatio割合まで回復）
                HP = Mathf.RoundToInt(maxHP * rebornHpRatio);
                rebornCount++;
                mashCount = 0;
                rebornTimer = 0;
                currentState = PlayerState.Idle;
                Player_status = Status.Live;
                //UIにHPを反映させるように指示
                if (gameMNG != null) gameMNG.Player_ReduceHP(HP, PlayerName);
                //根性復活成功！咆哮して立ち上がる漢を中心に、カメラが180度高速で回り込む
                if (fightingCamera != null)
                {
                    fightingCamera.SetRebornLevel(fightingCamera.rebornMaxLevel);
                    StartRebornStandUpCamera(); // 同時ダウン後はカメラ演出なし／それ以外は正面の引きを映す
                }
                rebornCamStarted = false; //次回のダウンに備えてリセット
                OnDownRevive(); // ★追加：復活アニメーション
            }
        }
        else
        {
            //制限時間内に復活できず力尽きた
            currentState = PlayerState.Dead;
            Player_status = Status.Dead;
            DLog($"[{PlayerName}] 根性復活失敗。HP={HP}のままDead状態へ移行。");
            if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Dead);

            if (fightingCamera != null)
            {
                fightingCamera.ClearReborn();
            }
            rebornCamStarted = false;
            OnDownFail(); // ★追加：復活失敗アニメーション
        }
    }

    //-----------------------------------------------------
    // ★追加：漢気復活（ダウン中に漢気ゲージを消費して、連打せずに即座に復活する）
    //-----------------------------------------------------
    // 漢気ゲージが足りていればゲージを消費して復活し、trueを返す。足りなければ何もせずfalseを返す。
    // 復活後の状態リセット・カメラ演出・UI更新は、通常の根性復活成功時（HandleKnockedDown）と同じ。
    bool TryKankiRevive()
    {
        if (!enableKankiRevive) return false;

        float cost = kankiGaugePerBar * Mathf.Max(1, kankiReviveBarCost);
        if (kankiGauge < cost)
        {
            DLog($"[{PlayerName}] 漢気復活できません：ゲージ不足（必要={cost:F1} / 現在={kankiGauge:F1}）");
            return false;
        }

        // 漢気ゲージを消費（UIの更新もこの中で行われる）
        ReduceKankiGauge(cost);

        // 最大HPのrebornHpRatio割合まで回復し、ダウン状態をリセットする
        HP = Mathf.RoundToInt(maxHP * rebornHpRatio);
        rebornCount++;
        mashCount = 0;
        rebornTimer = 0;
        currentState = PlayerState.Idle;
        Player_status = Status.Live;

        if (gameMNG != null)
        {
            gameMNG.Player_ReduceHP(HP, PlayerName);
            gameMNG.SettestStatus(PlayerName, Status.Live);
        }

        // 咆哮して立ち上がる漢を中心に、カメラが回り込む（通常の復活成功時と同じ演出）
        if (fightingCamera != null)
        {
            fightingCamera.SetRebornLevel(fightingCamera.rebornMaxLevel);
            StartRebornStandUpCamera(); // 同時ダウン後はカメラ演出なし／それ以外は正面の引きを映す
        }
        rebornCamStarted = false; // 次回のダウンに備えてリセット
        OnDownRevive(); // ★追加：復活アニメーション

        SpawnKankiReviveEffect(); // 復活した瞬間のエフェクト
        StartReviveBuff();        // 攻撃力上昇（＋上昇中エフェクト）を開始

        DLog($"[{PlayerName}] 漢気復活！（消費ゲージ={cost:F1} / 復活後HP={HP} / 攻撃力={atk} / 上昇フレーム数={reviveAtkBuffFrames}）");
        return true;
    }

    // 復活した瞬間に1回だけ出すエフェクト。プレイヤーの子オブジェクトとして生成し、一定時間後に破棄する。
    void SpawnKankiReviveEffect()
    {
        if (kankiReviveEffectPrefab == null && kankiReviveEffectPrefab_Ground)
        {
            Debug.LogWarning($"[{PlayerName}] kankiReviveEffectPrefabが未設定のため、漢気復活エフェクトを出せません。Inspectorで設定してください。", this);
            return;
        }

        ParticleSystem fx = Instantiate(
            kankiReviveEffectPrefab,
            transform.position + kankiReviveEffectOffset,
            Quaternion.Euler(-90f, 0f, 0f),
            transform);
        fx.transform.localPosition = kankiReviveEffectOffset;
        fx.Play();
        Destroy(fx.gameObject, kankiReviveEffectLifetime);

        ParticleSystem fx_Ground = Instantiate(
            kankiReviveEffectPrefab,
            transform.position + kankiReviveEffectOffset,
            Quaternion.Euler(-90f, 0f, 0f));
        fx_Ground.transform.localPosition = kankiReviveEffectOffset;
        fx_Ground.Play();
        Destroy(fx_Ground.gameObject, kankiReviveEffectLifetime);






        EffectDLog($"[{PlayerName}] 漢気復活エフェクト発生 pos={fx.transform.position}");
    }

    // 攻撃力上昇を開始する（すでに上昇中なら残り時間を延ばし直す）
    void StartReviveBuff()
    {
        isReviveBuffed = true;
        reviveBuffTimer = reviveAtkBuffFrames;
        UpdateAtkByGauge(); // 上昇倍率をatkへ反映
        StartReviveBuffEffect();
    }

    // 攻撃力上昇を終了する（時間切れ・ダウン時に呼ばれる）
    void EndReviveBuff()
    {
        if (!isReviveBuffed) return;

        isReviveBuffed = false;
        reviveBuffTimer = 0;
        UpdateAtkByGauge(); // 上昇倍率を外したatkへ戻す
        StopReviveBuffEffect();

        DLog($"[{PlayerName}] 漢気復活による攻撃力上昇が終了（攻撃力={atk}）");
    }

    // 毎フレームUpdateから呼ばれ、攻撃力上昇の残り時間を消化する
    void TickReviveBuff()
    {
        if (!isReviveBuffed) return;

        reviveBuffTimer -= frameStep;
        if (reviveBuffTimer <= 0) EndReviveBuff();
    }

    // 攻撃力上昇中のエフェクトを開始する。プレイヤーに追従し、上昇が終わるまでループし続ける。
    void StartReviveBuffEffect()
    {
        if (reviveBuffEffectPrefab == null)
        {
            Debug.LogWarning($"[{PlayerName}] reviveBuffEffectPrefabが未設定のため、攻撃力上昇中のエフェクトを出せません。Inspectorで設定してください。", this);
            return;
        }

        if (activeReviveBuffEffect != null)
        {
            if (!activeReviveBuffEffect.isPlaying) activeReviveBuffEffect.Play();
            return;
        }

        activeReviveBuffEffect = Instantiate(
            reviveBuffEffectPrefab,
            transform.position + reviveBuffEffectOffset,
            Quaternion.Euler(-90f, 0f, 0f),
            transform); // プレイヤーに追従させるため子オブジェクトにする
        activeReviveBuffEffect.transform.localPosition = reviveBuffEffectOffset;


        activeReviveBuffEffect_Ground = Instantiate(
            reviveBuffEffectPrefab_Ground,
            transform.position + reviveBuffEffectOffset,
            Quaternion.Euler(-90f, 0f, 0f)); // プレイヤーに追従させるため子オブジェクトにする
        activeReviveBuffEffect_Ground.transform.localPosition = reviveBuffEffectOffset;






        // Prefab側のLoop設定に関係なく、上昇中は出し続けるようループを強制する
        foreach (var ps in activeReviveBuffEffect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
        }

        activeReviveBuffEffect.Play();

        EffectDLog($"[{PlayerName}] 漢気復活の攻撃力上昇エフェクト開始 pos={activeReviveBuffEffect.transform.position}");
    }

    // 攻撃力上昇中のエフェクトを止める（新規発生だけ止め、出ている分は自然にフェードアウトさせる）
    void StopReviveBuffEffect()
    {
        if (activeReviveBuffEffect == null) return;

        activeReviveBuffEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(activeReviveBuffEffect.gameObject, activeReviveBuffEffect.main.startLifetime.constantMax + 0.5f);
        activeReviveBuffEffect = null;

        EffectDLog($"[{PlayerName}] 漢気復活の攻撃力上昇エフェクト終了");
    }

    // ★デバッグ用の強制復活処理（F4キー・対象はenableF4DebugKeyで選択）
    //   連打数や制限時間を無視して、通常の復活成功時と同じ処理を即座に実行する。
    void ForceRebornDebug()
    {
        //最大HPのrebornHpRatio割合まで回復（通常の復活成功時と同じ割合）
        ForceRevive(Mathf.RoundToInt(maxHP * rebornHpRatio), "F4");
    }

    // ★追加：デバッグキー共通の強制復活処理。
    //   指定したHPまで回復させつつ、根性復活成功時と同じ状態リセット・カメラ演出・UI更新を行う。
    //   hp: 復活後のHP　debugKeyName: ログ表示用のキー名（例："F4","F8"）
    void ForceRevive(int hp, string debugKeyName)
    {
        HP = hp;
        rebornCount++;
        mashCount = 0;
        rebornTimer = 0;
        currentState = PlayerState.Idle;
        Player_status = Status.Live;

        //UIにHPを反映させるように指示
        if (gameMNG != null) gameMNG.Player_ReduceHP(HP, PlayerName);
        if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Live);

        if (fightingCamera != null)
        {
            fightingCamera.SetRebornLevel(fightingCamera.rebornMaxLevel);
            StartRebornStandUpCamera(); // 同時ダウン後はカメラ演出なし／それ以外は正面の引きを映す
        }
        rebornCamStarted = false; //次回のダウンに備えてリセット
        OnDownRevive(); // ★追加：復活アニメーション

        Debug.Log($"[{PlayerName}] [デバッグ]{debugKeyName}キーにより強制復活しました（HP={HP}）");
    }

    //-----------------------------------------------------
    // 衝突・トリガー
    //-----------------------------------------------------

    // 物理的な衝突が発生した時に呼ばれる。地面との接触判定（着地）に使用
    void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.CompareTag("Ground"))
        {
            // ★追加：着地アニメーション用に、「空中にいた(Jumpflag=false)状態からの着地か」を先に控えておく。
            //   開始時にスポーンして床に触れただけ等、空中にいなかった場合はフラグを立てないため。
            bool wasAirborne = !Jumpflag;

            // ★追加：ジャンプして着地した時だけ着地SEを鳴らす（ノックバック・投げ等で空中に出た場合は鳴らさない）。
            bool playLandSe = wasAirborne && jumpLandSeArmed;
            jumpLandSeArmed = false;

            Jumpflag = true;

            // ★追加：投げで放物線状に吹き飛ばされている最中に地面へ着地したら、Thrown状態を終えて復帰する
            //   （この場合の着地アニメーションは、LandFromThrow内の"Thrown-land"が担当するので"Land"は立てない）
            if (currentState == PlayerState.Thrown)
            {
                LandFromThrow();
            }
            // ★追加：ジャンプ・ノックバック等で空中にいた状態から地面に着地したら、
            //   着地アニメーションを再生したいフラグ（Trigger "Land"）を立てる。
            //   ダウン中／死亡中は倒れたポーズを崩さないよう対象外にする。
            else if (wasAirborne && animator != null
                     && currentState != PlayerState.KnockedDown && currentState != PlayerState.Dead)
            {
                animator.SetTrigger("Land"); // ★要Animator追加：Triggerパラメータ"Land"
                if (playLandSe) PlayActionSe(landSe); // ★追加：ジャンプ着地SE
                DLog($"[{PlayerName}] 着地：Landフラグを立てました");
            }
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

        // ★リザルト画面モード：攻撃は見た目だけで、被弾・ダメージ・掴みの判定は行わない
        if (isResultMode) return;

        // すでに倒れている場合は無視
        // ★変更：以前は「自分のタグ(PLayerTagName)と同じタグのオブジェクトは無視」も行っていたが、削除した。
        //   自分自身のヒットボックスの除外は、この後の「相手(enemyPlayer/enemy)の攻撃用ヒットボックスか？」の
        //   参照比較で既に確実に行われているため不要。むしろ、GameInputManagerからプレハブで生成すると、
        //   同じキャラを選んだ場合などに1P/2Pのタグが同じになり、相手の攻撃まで無視して
        //   「当たり判定が消える」原因になっていた。
        if (HP <= 0) return;

        // ★追加：必殺技(waza)発動中は無敵。相手の攻撃用当たり判定が触れても一切反応しない
        //  （ダメージ・ヒットストップ・ガード演出等、何も発生させない）。
        if (currentState == PlayerState.Special) return;

        // ★変更：相手の攻撃判定かどうかは、触れたコライダーの「持ち主」を親からたどって直接取得する。
        //   タグやInspectorのenemyPlayer/enemy参照には一切依存しない。
        //   「相手のアタックボックス（攻撃用コライダー）が自分の体に触れた」時だけ被弾として扱う。
        Player attackerPlayer = collision.GetComponentInParent<Player>();
        Enemy attackerEnemy = attackerPlayer == null ? collision.GetComponentInParent<Enemy>() : null;

        // 自分自身の攻撃判定が自分の体に触れただけの場合は除外する（attackerPlayer == this）
        bool isEnemyPlayerAttack = attackerPlayer != null && attackerPlayer != this
            && attackerPlayer.AttackHitboxes != null
            && System.Array.Exists(attackerPlayer.AttackHitboxes, hb => hb == collision);

        bool isEnemyAttack = attackerEnemy != null
            && attackerEnemy.AttackHitboxes != null
            && System.Array.Exists(attackerEnemy.AttackHitboxes, hb => hb == collision);

        // 相手のアタックボックスでなければ無視（相手の体に自分の攻撃が当たった、攻撃側視点のイベント等）
        if (!isEnemyPlayerAttack && !isEnemyAttack)
        {
            return;
        }

        // ★追加：相手が投げ動作中に、その左手・左前腕の当たり判定が自分の体に触れた場合は、
        //   ダメージを与える通常の被弾ではなく「掴みの接触」として相手側に通知する（ダメージ・ヒットストップ等は発生させない）。
        //   掴めない状態（ジャンプ中等）だった場合も、通知先で無視されるだけで、ここでは被弾扱いにしない。
        if (isEnemyPlayerAttack && attackerPlayer.IsGrabHitbox(collision))
        {
            attackerPlayer.OnGrabHitboxContact(this);
            return;
        }

        // ★追加：多段ヒット防止／許可の判定。
        //   同じ攻撃（1回のパンチ・キック等）に対して、今回の接触を有効なヒットとして扱ってよいかを
        //   攻撃側（相手）のInspector設定（多段ヒット設定）に基づいて問い合わせる。
        //   falseが返った場合は、既に規定回数ヒット済みなのでこの接触は完全に無視する（ダメージ・演出も一切発生させない）。
        if (isEnemyPlayerAttack)
        {
            if (!attackerPlayer.TryRegisterHit()) return;
        }
        else if (isEnemyAttack)
        {
            // ※CPU(Enemy)側は現状、多段ヒット制御に未対応です。
            //   Enemy.csにも同様のTryRegisterHit()を実装すればCPU戦にも適用できます。
        }

        //今回被弾させてきた相手の攻撃力を取得（対人戦かCPU戦かで参照先を切り替える）
        int attackerAtk = isEnemyPlayerAttack ? attackerPlayer.atk : attackerEnemy.atk;

        int attackerAtkBase = attackerAtk; // F10表示用：ペナルティ倍率を掛ける前の値

        // ★追加：ガード失敗直後の一定時間は、受けるダメージが増える（ダウン判定・ガード時の半減などにもこの値が使われる）
        if (guardFailDamageTimer > 0)
        {
            attackerAtk = Mathf.RoundToInt(attackerAtk * Mathf.Max(guardFailDamageMultiplier, 0f));
        }

        //ここまで来たら「敵の攻撃用ヒットボックスが自分の体に当たった」＝正真正銘の被弾
        if (isGuarding)
        {
            // 仁王立ち（ガード）中に被弾した場合の処理
            // ★変更：ガード成功で、次に自分が与える技のダメージ倍率を上げる（guardSuccessNextAttackMultiplier倍）。
            //   （次の自分の攻撃が当たるまで持続。倍率はInspectorの「ガード成功時の攻撃力上昇設定」で調整可能）
            guardBlockedThisGuard = true; // 受け止めたので、この仁王立ちはガード失敗にならない
            DLog("漢!!");
            se.PlayOneShot(MenBlock_se);       // ガード成功の効果音を再生
            HP -= attackerAtk / 2;                // ガード中はダメージを半減させる
            RecordDamage(attackerAtk / 2, attackerAtkBase / 2, "ガード(半減)"); // F10表示用

            // ガードに成功したので、自分の漢気ゲージを増やす
            AddKankiGauge(gaugeGainOnGuard);

            // 攻撃力上昇中であることを示すエフェクトを出し続ける（次に自分の攻撃が当たるまで）
            // ★変更：技を出す前に続けてガードに成功した場合は、倍率を掛け算で重ねていく（倍々）。
            float stepMultiplier = Mathf.Max(1f, guardSuccessNextAttackMultiplier);
            guardBuffMultiplier = isGuardBuffed ? guardBuffMultiplier * stepMultiplier : stepMultiplier;
            if (guardSuccessMaxMultiplier > 0f) guardBuffMultiplier = Mathf.Min(guardBuffMultiplier, guardSuccessMaxMultiplier);
            isGuardBuffed = true;
            UpdateAtkByGauge(); // 倍率を反映して攻撃力を再計算する
            DLog($"[{PlayerName}] ガード成功：次の技のダメージ倍率×{guardBuffMultiplier:F2}（攻撃力={atk}）");
            StartGuardBuffEffect();

            // ★追加：仁王立ち成功アニメーション（Animatorの"Guard-success"トリガー）を再生する。
            //   ヒットストップ中はanimator.speedが0になり遷移しないが、トリガーは消費されるまで残るので、
            //   ヒットストップが終わった瞬間に再生が始まる。
            if (hasGuardSuccessParam && animator != null)
            {
                animator.SetTrigger(GuardSuccessParam);
            }

            // ガード成功時のヒットストップ（少しだけ動けなくする）。
            // ガードのポーズを崩したくないのでHeadHitアニメーションは再生しない。
            StartHitStop(guardHitStopFrames, false);

            guardComboCount++;
            if (fightingCamera != null)
            {
                fightingCamera.OnGuardImpact(transform, guardComboCount);
            }

            // ガード成功の擬音演出（攻撃者→自分の方向を基準に、空白地帯へ表示）
            Transform guardAttackerTf = isEnemyPlayerAttack ? attackerPlayer.transform : attackerEnemy.transform;
            if (HitEffectSpawner.Instance != null && guardHitEffectData != null)
            {
                HitEffectSpawner.Instance.SpawnBehindFacing(guardHitEffectData, transform.position, transform.forward); // 向きと反対側の斜め上へ
                EffectDLog($"[{PlayerName}] ガード成功擬音エフェクト発生 pos={transform.position}");
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
                EffectDLog($"[{PlayerName}] ガード成功パーティクル発生 pos={guardSuccessEffect.transform.position}");
                DLog($"[{PlayerName}] ガード成功エフェクトを再生");
            }

            // ガードされてもヒットはヒット（空振りではない）なので攻撃側に通知
            if (isEnemyPlayerAttack) attackerPlayer.NotifyAttackLanded();
            else if (attackerEnemy != null) attackerEnemy.NotifyAttackLanded();

            // ★追加：ガード成功で、攻撃してきた相手を guardSuccessStunFrames フレームだけスタン（行動不能）にする。
            //   ※NotifyAttackLanded()の後に呼ぶこと（命中によるクールダウン切替の後で、やられ状態へ上書きするため）。
            //   ※CPU(Enemy)が攻撃者の場合は未対応（Enemy.cs側にApplyGuardedStun()相当が必要）。
            if (isEnemyPlayerAttack) attackerPlayer.ApplyGuardedStun(guardSuccessStunFrames);
        }
        else
        {
            // ガードしていない状態で被弾した場合の処理
            guardComboCount = 0;
            // ★変更：被弾アニメーションはここでは再生せず「予約」し、ヒットストップが終わった瞬間に1回だけ再生する
            //   （即座にTriggerを立てたうえでHeadHitも再生し直していたため、二重に再生されていた）。
            //   "Hit-front" = 前から攻撃された（相手が自分の正面にいる）／"Hit-back" = 後ろから攻撃された（相手が背後にいる）。
            if (!playHeadHitStateDirectly)
            {
                Transform hitAttackerTf = isEnemyPlayerAttack ? attackerPlayer.transform : (attackerEnemy != null ? attackerEnemy.transform : null);
                bool hasHitDir = hitAttackerTf != null;
                bool hitFromFront = hasHitDir && IsOpponentInFront(hitAttackerTf);
                RequestHitAnimation(hasHitDir, hitFromFront);
            }

            // 通常被弾時のヒットストップ（少しだけ動けなくする）。
            // ★変更：対人戦では、攻撃者の「攻撃ごとの設定」のヒットストップ時間を使う。
            //   CPU(Enemy)の攻撃、または攻撃種別が取れない場合は、従来のhitStopFramesにフォールバックする。
            int appliedHitStop = hitStopFrames;
            if (isEnemyPlayerAttack)
            {
                int perAttackHitStop = attackerPlayer.GetCurrentHitStopFrames();
                if (perAttackHitStop >= 0) appliedHitStop = perAttackHitStop;
            }
            StartHitStop(appliedHitStop);
            if (hitStopTimer <= 0)
            {
                // ヒットストップが無い設定の時は、そのまま今再生する（トリガーOFFならHeadHitを直接再生）
                if (playHeadHitStateDirectly) PlayHeadHitDirect();
                else FirePendingHitAnimation();
            }

            // ★追加：ノックバック。攻撃者（相手Player）の技ごとの設定に従って、
            //   攻撃者から離れる方向（Z軸）＋上方向へ飛ばす。
            //   ※CPU(Enemy)の攻撃は未対応（Enemy.cs側にも同様のGetCurrentKnockbackSetting()が必要）。
            //   ※この一撃でHPが0になる場合は、ダウン処理と干渉しないようノックバックしない。
            if (isEnemyPlayerAttack && HP - attackerAtk > 0)
            {
                KnockbackSetting knockback = attackerPlayer.GetCurrentKnockbackSetting();
                if (knockback != null && (knockback.horizontalSpeed != 0f || knockback.upSpeed != 0f))
                {
                    Vector3 awayDirection = GetAwayDirectionFrom(attackerPlayer);
                    RequestKnockback(awayDirection * knockback.horizontalSpeed + Vector3.up * knockback.upSpeed, appliedHitStop);
                }
            }

            // ★追加：やられ状態（行動不能）。攻撃者の技ごとの「やられ状態の時間」だけ動けなくする。
            //   ※CPU(Enemy)の攻撃は未対応（Enemy.csにGetCurrentStunFrames()相当が必要）。
            if (isEnemyPlayerAttack)
            {
                ApplyStun(attackerPlayer.GetCurrentStunFrames(), HP - attackerAtk <= 0);
            }

            Vector3 hitPoint = collision.ClosestPoint(collision.transform.position);
            ParticleSystem hitParticle = Instantiate(Hit_particle, hitPoint, Quaternion.Euler(-90f, 0f, 0f));
            hitParticle.Play();
            Destroy(hitParticle.gameObject, 1.0f);
            EffectDLog($"[{PlayerName}] 被弾パーティクル発生 pos={hitPoint}");

            // 「ドカン」「ドドン」等の擬音演出。
            // 攻撃者に今の攻撃タイプ(パンチ/キック)を問い合わせて、対応する擬音データを選ぶ。
            // 角度・距離はHitEffectData側（Inspector）で調整する。
            Transform attackerTf = isEnemyPlayerAttack ? attackerPlayer.transform : attackerEnemy.transform;
            HitEffectData selectedHitEffect = null;

            if (isEnemyPlayerAttack)
            {
                // 対人戦：相手Playerの現在の攻撃タイプに応じたHitEffectDataを取得
                selectedHitEffect = attackerPlayer.GetHitEffectDataFor(attackerPlayer.CurrentAttackType);
                attackerPlayer.NotifyAttackLanded(); // 空振りではなく命中したことを攻撃側へ通知
            }
            else if (attackerEnemy != null)
            {
                // 対CPU戦：Enemy側の現在の攻撃タイプに応じたHitEffectDataを取得
                selectedHitEffect = attackerEnemy.GetHitEffectDataFor(attackerEnemy.CurrentAttackType);
                attackerEnemy.NotifyAttackLanded(); // 空振りではなく命中したことを攻撃側へ通知
            }

            if (HitEffectSpawner.Instance != null && selectedHitEffect != null)
            {
                Transform effectFacingTf = hitEffectFacingBasis == HitEffectFacingBasis.Attacker ? attackerTf : transform;
                HitEffectSpawner.Instance.SpawnBehindFacing(selectedHitEffect, hitPoint, effectFacingTf.forward); // 向きと反対側の斜め上へ
                EffectDLog($"[{PlayerName}] 被弾擬音エフェクト発生 pos={hitPoint}");
            }

            HP -= attackerAtk;
            RecordDamage(attackerAtk, attackerAtkBase, "通常被弾"); // F10表示用
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
        // ★修正：以前は "= 10" で固定値に戻しており、攻撃の種類ごとの攻撃力や
        //   漢気ゲージによる補正まで消えてしまっていた。ここではガード上昇分だけを
        //   取り消し、ゲージ補正込みの本来の攻撃力に戻す。
        if (isEnemyPlayerAttack) attackerPlayer.ResetAtkAfterHit();
        else if (attackerEnemy != null) attackerEnemy.atk = 10; // ※Enemy側は未対応。同様の仕組みが必要なら要相談。

        if (HP < 0) HP = 0;

        //デバッグログ：誰からどれだけダメージを受けてHPがいくつになったか
        string attackerName = isEnemyPlayerAttack ? attackerPlayer.PlayerName : "Enemy(CPU)";
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
        RecordDamage(n, n, "damege()（投げ等）"); // F10表示用
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
    // ★増減のタイミングまとめ：
    //   増加：NotifyAttackLanded()（攻撃命中時） / OnTriggerEnterのガード成功時
    //   減少：ApplyGaugeLossIfRetreating()（相手から離れる方向へ移動中、1秒あたり） / 必殺技発動時

    //漢気ゲージを増やす(上限は1本分の合計値でクランプ)
    public void AddKankiGauge(float amount)
    {
        float max = kankiGaugePerBar * kankiGaugeBarCount;
        float before = kankiGauge;

        // ★追加：ガード失敗後の一定時間は、漢気ゲージの増加量を下げる（増加のみ。guardFailGaugeGainRate=0.5なら半減）
        if (amount > 0f && guardFailGaugeTimer > 0) amount *= guardFailGaugeGainRate;

        kankiGauge = Mathf.Clamp(kankiGauge + amount, 0f, max);
        UpdateAtkByGauge();

        GaugeDLog($"[{PlayerName}] 漢気ゲージ増加：+{amount:F1}（{before:F1} → {kankiGauge:F1} / 上限{max:F1}）");

        //ゲージUIを更新（このPlayerインスタンス自身のゲージ表示を更新する）
        if (gameMNG != null)
        {
            gameMNG.Player_UpdateKankiGauge();
        }
        else
        {
            GaugeDLog($"[{PlayerName}] gameMNGがnullのため漢気ゲージUIを更新できません。");
        }
    }

    //漢気ゲージを減らす(0未満にはならない)
    public void ReduceKankiGauge(float amount)
    {
        float before = kankiGauge;
        kankiGauge = Mathf.Clamp(kankiGauge - amount, 0f, kankiGaugePerBar * kankiGaugeBarCount);
        UpdateAtkByGauge();

        GaugeDLog($"[{PlayerName}] 漢気ゲージ減少：-{amount:F1}（{before:F1} → {kankiGauge:F1}）");

        //ゲージUIを更新（このPlayerインスタンス自身のゲージ表示を更新する）
        // ★修正：以前はEnemy(CPU)用のUI更新メソッドを誤って呼んでおり、
        //   Player(P1/P2)の減少がゲージUIに反映されていなかった。
        if (gameMNG != null)
        {
            gameMNG.Player_UpdateKankiGauge();
        }
        else
        {
            GaugeDLog($"[{PlayerName}] gameMNGがnullのため漢気ゲージUIを更新できません。");
        }
    }

    //ゲージの満タン本数に応じて攻撃力を再計算する
    private void UpdateAtkByGauge()
    {
        int filledBars = Mathf.FloorToInt(kankiGauge / kankiGaugePerBar);
        // ★追加：漢気復活による攻撃力上昇中は倍率を掛ける（1未満にはならない）。
        //   ここに組み込むことで、攻撃のたびにatkが再計算されても上昇が消えない。
        float reviveMultiplier = isReviveBuffed ? Mathf.Max(1f, reviveAtkMultiplier) : 1f;
        // ★追加：ガード成功中（次の攻撃が当たるまで）は、次に与える技のダメージ倍率を掛ける（1未満にはならない）
        float guardMultiplier = isGuardBuffed ? Mathf.Max(1f, guardBuffMultiplier) : 1f;
        atk = Mathf.RoundToInt(baseAtk * (1f + atkPowerPerBar * filledBars) * reviveMultiplier * guardMultiplier);

        // ★追加：ゲージ量が変わるたびに、充填中エフェクトのON/OFFを判定し直す
        UpdateKankiChargeEffect(filledBars >= 1);
    }

    // ★追加：充填中エフェクトが何かの拍子に止まっていたら再生し直す（毎フレームUpdateから呼ぶ）。
    //   ゲージが1本以上ある間は出し続けるための保険。
    private void MaintainKankiChargeEffect()
    {
        if (!enableKankiChargeEffect) return;

        bool hasBar = Mathf.FloorToInt(kankiGauge / kankiGaugePerBar) >= 1;
        if (!hasBar) return;

        // 生成されていない（破棄された等）場合は作り直し、止まっている場合は再生し直す
        //上部分のエフェクト
        if (activeKankiChargeEffect == null || !activeKankiChargeEffect.gameObject.activeInHierarchy)
        {
            activeKankiChargeEffect = null;
            UpdateKankiChargeEffect(true);
        }
        else if (!activeKankiChargeEffect.isPlaying)
        {
            activeKankiChargeEffect.Play();
            EffectDLog($"[{PlayerName}] 漢気ゲージ充填中エフェクトが停止していたため再生し直し");
        }
        //下部分のエフェクト
        if (activeKankiChargeEffect_Ground == null || !activeKankiChargeEffect_Ground.gameObject.activeInHierarchy)
        {
            activeKankiChargeEffect_Ground = null;
            UpdateKankiChargeEffect(true);
        }
        else if (!activeKankiChargeEffect_Ground.isPlaying)
        {
            activeKankiChargeEffect_Ground.Play();
            EffectDLog($"[{PlayerName}] 漢気ゲージ充填中エフェクトが停止していたため再生し直し");
        }
    }

    // ★追加：漢気ゲージ充填中エフェクトの開始／停止を切り替える。
    //   hasBar = 1本以上たまっているか。状態が変わった時だけ生成・停止する（多重生成しない）。
    private void UpdateKankiChargeEffect(bool hasBar)
    {
        if (hasBar && enableKankiChargeEffect)
        {
            if (kankiChargeEffectPrefab == null)
            {
                // ★Prefab未設定だと何も出ないため、原因に気づけるよう警告を出す（常時出力）
                Debug.LogWarning($"[{PlayerName}] kankiChargeEffectPrefabが未設定のため、漢気ゲージ充填中エフェクトを出せません。Inspectorで設定してください。", this);
                return;
            }

            if (activeKankiChargeEffect != null)
            {
                if (!activeKankiChargeEffect.isPlaying) activeKankiChargeEffect.Play();
                return;
            }

            activeKankiChargeEffect = Instantiate(
                kankiChargeEffectPrefab,
                transform.position + kankiChargeEffectOffset,
                Quaternion.Euler(-90f, 0f, 0f),
                transform); // プレイヤーに追従させるため子オブジェクトにする
            activeKankiChargeEffect.transform.localPosition = kankiChargeEffectOffset;

            activeKankiChargeEffect_Ground = Instantiate(
                kankiChargeEffectPrefab_Ground,
                transform.position + kankiChargeEffectOffset,
                Quaternion.Euler(90f, 0f, 0f)); // プレイヤーに追従させるため子オブジェクトにする
            activeKankiChargeEffect_Ground.transform.position = new Vector3(transform.position.x, 0.25f, transform.position.z );

            // ★Prefab側の設定に関係なく、ゲージが1本以上ある間は出し続けるようループを強制する。
            //   （Loop OFF・Stop Action=Destroy/Disableだと1回だけで終わってしまうため）
            //エフェクト上部分
            foreach (var ps in activeKankiChargeEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                if (kankiChargeEffectFollowLocal) main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
            //エフェクト下部分
            foreach (var ps in activeKankiChargeEffect_Ground.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                if (kankiChargeEffectFollowLocal) main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
           
            activeKankiChargeEffect.Play();
            activeKankiChargeEffect_Ground.Play();



            EffectDLog($"[{PlayerName}] 漢気ゲージ充填中エフェクト開始 pos={activeKankiChargeEffect.transform.position} / ゲージ={kankiGauge:F1}");
        }
        else
        {
            if (activeKankiChargeEffect == null) return;

            // 新規発生だけ止め、出ている分は自然にフェードアウトさせる
            activeKankiChargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(activeKankiChargeEffect.gameObject, activeKankiChargeEffect.main.startLifetime.constantMax + 0.5f);
            activeKankiChargeEffect = null;

            activeKankiChargeEffect_Ground.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(activeKankiChargeEffect_Ground.gameObject, activeKankiChargeEffect_Ground.main.startLifetime.constantMax + 0.5f);
            activeKankiChargeEffect = null;

            EffectDLog($"[{PlayerName}] 漢気ゲージ充填中エフェクト終了 / ゲージ={kankiGauge:F1}");
        }
    }

    // ★追加：ガード成功による一時的な攻撃力上昇分をクリアし、
    //   漢気ゲージ補正だけを反映した本来の攻撃力に戻す。
    //   自分の攻撃が相手に命中した直後（被弾側のOnTriggerEnterから）呼び出される想定。
    public void ResetAtkAfterHit()
    {
        UpdateAtkByGauge();
    }

    //UI(Sliderなど)から参照するための、指定した本数目のゲージの充填率(0?1)を返す
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