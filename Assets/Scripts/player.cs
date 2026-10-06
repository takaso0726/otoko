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
        Stunned,    // ★追加：やられ状態（被弾後、攻撃ごとに設定した秒数だけ行動不能）
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
    [Header("アクション時間設定（秒）")]
    // ※パンチ／キック／上キック／下キックの拘束時間は、下の「攻撃ごとの設定」（各技のヘッダー）へ移動した。
    [SerializeField] float guardDuration = 0.5f;    // 仁王立ちの拘束時間
    [SerializeField] float throwDuration = 1.5f;    // 投げの拘束時間

    //=====================================================
    // ★攻撃の種類ごとの攻撃力（Inspectorで個別に調整可能）
    //   ここに新しい技を追加する場合は、対応するフィールドを増やし、
    //   GetAttackPower() の switch にもケースを追加すること。
    //=====================================================
    // ※パンチ／キック／上キック／下キック／必殺技の攻撃力は、下の「攻撃ごとの設定」（各技のヘッダー）へ移動した。
    [Header("投げの攻撃力")]
    [SerializeField] int throwAtk = 5;     // 投げ（つかみ）成立時の固定ダメージ
    [Tooltip("投げ成立で飛ばされた相手が、着地した後に【やられ状態（行動不能）】になる時間（秒）。" +
             "着地した瞬間から数え始める。0だとやられ状態にならない。\n" +
             "※投げ不成立（掴みタイムアウト）で互いに吹き飛んだ場合は対象外。")]
    [SerializeField] float throwStunDuration = 0.5f;

    //=====================================================
    // ★追加：投げ（掴み）仕様変更に伴う詳細設定
    //   掴み成立→(掴み状態中に脱出できなければ)→投げ飛ばし、という2段階の流れになる。
    //   throwDurationは「掴んでから実際に投げ飛ばすまでの拘束時間」として引き続き使用する
    //   （Animatorの"Throw-start"モーションの長さと合わせること）。
    //=====================================================
    [Header("投げ（掴み）詳細設定")]
    [Tooltip("HPが0の時に脱出（掴みを振りほどく）までに必要な時間。値が大きいほど脱出しにくい＝投げられやすい。")]
    [SerializeField] float escapeTimeAtZeroHp = 1.5f;
    [Tooltip("HPが満タンの時に脱出（掴みを振りほどく）までに必要な時間。値が小さいほど脱出しやすい＝投げられにくい。")]
    [SerializeField] float escapeTimeAtFullHp = 0.4f;
    [Tooltip("掴まれている間、ボタンやスティックを入力する度に、脱出に必要な残り時間からさらに追加で短縮する秒数。")]
    [SerializeField] float escapeReductionPerInput = 0.15f;
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
    [SerializeField] float throwStartPlayTime = 0.2f;
    [Tooltip("★追加：ONにすると、手が相手に接触してから上のthrowStartPlayTime秒だけ掴みモーションを再生し続け、その後に止める。" +
             "OFF（既定）の時は、手が相手に接触した瞬間にアニメーションを即座に止める。")]
    [SerializeField] bool enableThrowStartPlay = false;

    //=====================================================
    // ★ガード（仁王立ち）成功時の攻撃力上昇設定
    //=====================================================
    [Header("ガード成功時の攻撃力上昇設定")]
    [Tooltip("ガード成功時、「相手の攻撃力 × この倍率」を自分の攻撃力に上乗せする。" +
             "1なら相手の攻撃力をそのまま加算、0.5なら半分だけ加算、0なら上昇なし。")]
    [SerializeField] float guardAtkBonusMultiplier = 1.0f;

    //=====================================================
    // ★ガード（仁王立ち）成功時の、相手へのスタン設定
    //=====================================================
    [Header("ガード成功時の相手スタン設定")]
    [Tooltip("仁王立ちでガードに成功した時、攻撃してきた相手を行動不能（スタン）にする時間（秒）。" +
             "この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。0だとスタンさせない。\n" +
             "※相手が必殺技・投げ（掴み中）・ダウン中などの場合はスタンさせない。")]
    [SerializeField] float guardSuccessStunDuration = 0.5f;

    //=====================================================
    // ★根性復活（ダウン後の復活チャレンジ）設定
    //=====================================================
    [Header("復活（根性）設定")]
    [SerializeField] float rebornTimeLimit = 5.0f;  // 復活チャレンジの制限時間
    [Range(0f, 1f)]
    [SerializeField] float rebornHpRatio = 0.3f;    // 復活成功時に回復するHPの割合（maxHPに対する割合。0.3なら最大HPの3割まで回復）
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
    [Tooltip("CPU(Enemy)の攻撃を受けた時など、攻撃側に個別設定が無い場合に使う、通常被弾時のヒットストップ時間（秒）。\n" +
             "対人戦でPlayerの攻撃を受けた時は、攻撃者側の「攻撃ごとの設定」のヒットストップ時間が使われる。")]
    [SerializeField] float hitStopDuration = 0.08f;         // 通常被弾時（CPU等の攻撃側設定が無い場合）に少しだけ動けなくなる時間（秒）
    [SerializeField] float guardHitStopDuration = 0.05f;    // 仁王立ちガード成功時に少しだけ動けなくなる時間（秒）
    [SerializeField] bool freezeAnimatorDuringHitStop = true; // ヒットストップ中はアニメーションも一瞬止めるか

    private float hitStopTimer = 0f;   // 残りヒットストップ時間。0より大きい間は入力処理をすべてスキップする
    private bool pendingHeadHitAnimation = false; // ヒットストップ終了時にHeadHitアニメーションを再生するか（ガード成功時はfalse）
    private bool hasPendingKnockback = false;    // ヒットストップ終了時にノックバックを与えるか
    private Vector3 pendingKnockbackVelocity;    // 与えるノックバックの初速（水平＋上方向）

    //=====================================================
    // ★攻撃ごとの設定（攻撃の種類ごと）
    //   各技のヘッダーの下に、次の順で並べている：
    //     ① 攻撃力 … この技が当たった時に相手へ与えるダメージ（漢気ゲージ補正の前の素の値）
    //     ② 攻撃後のクールダウン（拘束時間）… 攻撃を出した後、次の行動ができるまでの時間
//        ★空振り時(〇〇MissDuration)と命中時(〇〇HitDuration)を別々に設定できる。
//        どちらも「攻撃を出した瞬間からの合計時間」。命中した瞬間に、残り時間が命中時の値へ切り替わる。
    //     ③ ヒットストップ … この技を当てられた相手が止まる時間
    //     ④ ノックバック … この技を当てられた相手が飛ぶ量（攻撃者から離れる方向＋上方向）
    //     ⑤ やられ状態の時間 … この技を当てられた相手が行動不能になる時間（秒）。0ならやられ状態にならない。
    //   ③④⑤は「攻撃する側」のInspectorで調整する。
    //   ※仁王立ちガードで防がれた時は、ヒットストップはguardHitStopDuration、ノックバックは無し。
    //   ※新しい技を追加する場合は、対応するフィールドを増やし、GetAttackPower() /
    //     GetCurrentHitStopDuration() / GetCurrentKnockbackSetting() / GetMultiHitSetting() の
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

    [Header("パンチ（空中攻撃含む）")]
    [Tooltip("① パンチの攻撃力（ダメージ量）")]
    [SerializeField] int punchAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束時間・秒）。パンチを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [FormerlySerializedAs("punchDuration")]
    [SerializeField] float punchMissDuration = 0.5f;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束時間・秒）。パンチを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] float punchHitDuration = 0.5f;
    [Tooltip("⑤ パンチを当てた時の、相手の【やられ状態（行動不能）】の時間（秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] float punchStunDuration = 0.3f;
    [Tooltip("③ パンチを当てた時の、相手のヒットストップ時間（秒）。0だと止まらない。")]
    [SerializeField] float punchHitStopDuration = 0.08f;
    [Tooltip("④ パンチを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting punchKnockback = new KnockbackSetting { horizontalSpeed = 2.5f, upSpeed = 0f };


    [Header("通常キック")]
    [Tooltip("① 通常キックの攻撃力（ダメージ量）")]
    [SerializeField] int kickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束時間・秒）。キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [FormerlySerializedAs("kickDuration")]
    [SerializeField] float kickMissDuration = 0.6f;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束時間・秒）。キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] float kickHitDuration = 0.6f;
    [Tooltip("⑤ 通常キックを当てた時の、相手の【やられ状態（行動不能）】の時間（秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] float kickStunDuration = 0.4f;
    [Tooltip("③ 通常キックを当てた時の、相手のヒットストップ時間（秒）。0だと止まらない。")]
    [SerializeField] float kickHitStopDuration = 0.08f;
    [Tooltip("④ 通常キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting kickKnockback = new KnockbackSetting { horizontalSpeed = 3.5f, upSpeed = 0f };

    [Header("上キック")]
    [Tooltip("① 上キックの攻撃力（ダメージ量）")]
    [SerializeField] int upKickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束時間・秒）。上キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [FormerlySerializedAs("upKickDuration")]
    [SerializeField] float upKickMissDuration = 0.7f;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束時間・秒）。上キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] float upKickHitDuration = 0.7f;
    [Tooltip("⑤ 上キックを当てた時の、相手の【やられ状態（行動不能）】の時間（秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] float upKickStunDuration = 0.5f;
    [Tooltip("③ 上キックを当てた時の、相手のヒットストップ時間（秒）。0だと止まらない。")]
    [SerializeField] float upKickHitStopDuration = 0.08f;
    [Tooltip("④ 上キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting upKickKnockback = new KnockbackSetting { horizontalSpeed = 2f, upSpeed = 5f };

    [Header("下キック")]
    [Tooltip("① 下キックの攻撃力（ダメージ量）")]
    [SerializeField] int downKickAtk = 10;
    [Tooltip("② 攻撃後のクールダウン【空振り時】（拘束時間・秒）。下キックを出してから、相手に当たらなかった場合に次の行動ができるまでの時間。")]
    [FormerlySerializedAs("downKickDuration")]
    [SerializeField] float downKickMissDuration = 0.5f;
    [Tooltip("② 攻撃後のクールダウン【命中時】（拘束時間・秒）。下キックを出してから、相手に当たった場合に次の行動ができるまでの時間（攻撃開始からの合計）。命中した時点で既に経過している時間がこれを超えていれば、即座に行動可能になる。")]
    [SerializeField] float downKickHitDuration = 0.5f;
    [Tooltip("⑤ 下キックを当てた時の、相手の【やられ状態（行動不能）】の時間（秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] float downKickStunDuration = 0.4f;
    [Tooltip("③ 下キックを当てた時の、相手のヒットストップ時間（秒）。0だと止まらない。")]
    [SerializeField] float downKickHitStopDuration = 0.08f;
    [Tooltip("④ 下キックを当てた時の、相手のノックバック量")]
    [SerializeField] KnockbackSetting downKickKnockback = new KnockbackSetting { horizontalSpeed = 3f, upSpeed = 0f };


    [Header("必殺技（waza）")]
    [Tooltip("① 必殺技（左足の攻撃判定）が相手に命中した時のダメージ量。パンチ・キック等と同じ仕組みで、" +
             "漢気ゲージによる攻撃力補正（atkPowerPerBar）もこの値を基準に上乗せされる。")]
    [SerializeField] int specialAtk = 20;
    [Tooltip("③ 必殺技を当てた時の、相手のヒットストップ時間（秒）。多段ヒットごとに毎回かかる点に注意。0だと止まらない。\n" +
             "※② 必殺技のクールダウン（拘束時間）は、下の「必殺技設定」のspecialDurationで調整する（waza アニメーションの長さと合わせる必要があるため）。")]
    [SerializeField] float specialHitStopDuration = 0.08f;
    [Tooltip("④ 必殺技を当てた時の、相手のノックバック量。多段ヒットするため、既定は0（飛ばさない）。")]
    [SerializeField] KnockbackSetting specialKnockback = new KnockbackSetting { horizontalSpeed = 0f, upSpeed = 0f };
    [Tooltip("⑤ 必殺技を当てた時の、相手の【やられ状態（行動不能）】の時間（秒）。この間、相手は移動・攻撃・ガード・投げ・ジャンプ等が一切できない。やられ中にさらに攻撃が当たった場合は、残り時間とこの値の長い方に延長される。0だとやられ状態にならない。")]
    [SerializeField] float specialStunDuration = 0.5f;

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
    [Tooltip("相手に攻撃を当てた（ダメージを与えた）時に増える量")]
    public float gaugeGainOnHit = 15f;
    [Tooltip("仁王立ちでガードに成功した時に増える量")]
    public float gaugeGainOnGuard = 10f;
    [Tooltip("後退（相手から離れる方向へ移動）している間、1秒あたりに減る量")]
    public float gaugeLossOnRetreat = 5f;
    [Tooltip("ゲージ1本(満タン)につき上昇する基礎攻撃力倍率。0.25なら1本で+25%（実際のダメージ＝基礎攻撃力倍率×技の攻撃力）")]
    public float atkPowerPerBar = 0.25f;

    // 現在の合計ゲージ量(0 〜 kankiGaugePerBar * kankiGaugeBarCount)
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
    [SerializeField] ParticleSystem kankiChargeEffectPrefab;
    [Tooltip("プレイヤー基準の生成位置。キャラクターの体の中心あたりになるよう調整してください。")]
    [SerializeField] Vector3 kankiChargeEffectOffset = new Vector3(0f, 0.0f, 0f);

    [Tooltip("ONにすると、パーティクルの発生位置がプレイヤーと一緒に動く（Local）。OFFだと発生済みの粒は移動時にその場へ置き去りになる（World）。")]
    [SerializeField] bool kankiChargeEffectFollowLocal = true;

    private ParticleSystem activeKankiChargeEffect; // 生成中のエフェクト参照（多重生成防止・停止処理用）

    //=====================================================
    // ★追加：漢気復活（ダウン中に漢気ゲージを消費して、連打せずに即座に復活する）設定
    //   ダウン中（復活チャレンジの制限時間内）にL1ボタンを押すと、漢気ゲージを消費してその場で復活する。
    //   復活後は一定時間（reviveAtkBuffDuration）、攻撃力が上昇し、その間は専用エフェクトを出し続ける。
    //=====================================================
    [Header("漢気復活設定（ゲージ消費の即時復活）")]
    [Tooltip("ONにすると、ダウン中にL1ボタンで漢気ゲージを消費して即座に復活できる。")]
    [SerializeField] bool enableKankiRevive = true;
    [Tooltip("漢気復活で消費する漢気ゲージの本数。1ならゲージ1本分(kankiGaugePerBar)を消費する。消費分のゲージがたまっていない時は復活できない。")]
    [SerializeField] int kankiReviveBarCost = 1;
    [Tooltip("復活後の攻撃力の倍率。1.5なら攻撃力が1.5倍（+50%）、2なら2倍。1以下にすると上昇しない。漢気ゲージ本数による補正と掛け合わせて計算される。")]
    [SerializeField] float reviveAtkMultiplier = 1.5f;
    [Tooltip("復活後に攻撃力上昇が続く時間（秒）。この間は攻撃力上昇エフェクトも出続ける。")]
    [SerializeField] float reviveAtkBuffDuration = 10f;

    [Header("漢気復活エフェクト設定")]
    [Tooltip("復活した瞬間に1回だけ出すパーティクル（未設定なら出さない）")]
    [SerializeField] ParticleSystem kankiReviveEffectPrefab;
    [Tooltip("プレイヤー基準の生成位置")]
    [SerializeField] Vector3 kankiReviveEffectOffset = new Vector3(0f, 1.0f, 0f);
    [Tooltip("生成した復活エフェクトを破棄するまでの時間（秒）")]
    [SerializeField] float kankiReviveEffectLifetime = 2.0f;
    [Tooltip("攻撃力上昇中に出し続けるパーティクル（未設定なら出さない）。Loopの設定に関わらず上昇中は自動でループさせ、上昇が終わると止まる。")]
    [SerializeField] ParticleSystem reviveBuffEffectPrefab;
    [Tooltip("プレイヤー基準の生成位置")]
    [SerializeField] Vector3 reviveBuffEffectOffset = new Vector3(0f, 1.0f, 0f);

    private bool wantKankiRevive = false;           // ダウン中にL1が押された（漢気復活の要求）
    private bool isReviveBuffed = false;            // 漢気復活による攻撃力上昇中かどうか
    private float reviveBuffTimer = 0f;             // 攻撃力上昇の残り時間（秒）
    private ParticleSystem activeReviveBuffEffect;  // 生成中の攻撃力上昇エフェクトの参照（多重生成防止・停止処理用）

    //=====================================================
    // ---- 必殺技（漢気ゲージ消費） ----
    //=====================================================
    [Header("必殺技設定")]
    [Tooltip("必殺技を発動するために必要な漢気ゲージ量。デフォルトはゲージ満タン(2本分)。")]
    public float specialRequiredGauge = 200f;

    [Tooltip("必殺技発動で消費する漢気ゲージ量。")]
    public float specialGaugeCost = 200f;

    // ※必殺技の攻撃力(specialAtk)は、「攻撃ごとの設定」の「必殺技（waza）」ヘッダーへ移動した。

    [Tooltip("必殺技発動中の拘束時間(秒)。既定値は3秒。\n" +
             "この間は攻撃・ガード・投げ・ジャンプ・必殺技の再発動は一切できず、移動のみ可能。\n" +
             "また、この間は相手からの攻撃を一切受け付けない（無敵）。\n" +
             "「waza」トリガーで再生される必殺技アニメーションの長さと同じ秒数に合わせること。")]
    [SerializeField] float specialDuration = 3.0f;

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
    private float stateTimer;                               // 現在の行動が終わるまでの残り時間（秒）
    private float currentAttackMissDuration;                // ★追加：今出している通常攻撃の「空振り時」拘束時間（命中時に経過時間を求めるために保持）

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
    // ★追加：F6キーでON/OFFする、常時仁王立ちデバッグモードの現在の状態
    private bool debugAlwaysGuard = false;
    bool rebornCamStarted;           // 根性復活のクローズアップカメラを開始済みか
    bool canThrow = true;            // 投げの多重発生を防ぐフラグ
    bool throwReaching;              // ★追加：投げ動作中で、まだ掴みが成立しておらず手の当たり判定の接触を待っている間（接触した瞬間にfalseになる）

    // ★追加：投げ（掴み）仕様変更用の相互参照・タイマー
    Player grabbedTarget;            // 自分が今掴んでいる相手（掴む側の時だけ使用。掴んでいなければnull）
    Player grabbingPlayer;           // 自分を今掴んでいる相手（掴まれる側の時だけ使用。掴まれていなければnull）
    float grabHoldTimer;             // 掴み成立後、掴みモーションを再生し続ける残り時間（0になったら一時停止）
    bool grabHoldFrozen;             // 掴みモーションを一時停止中か（trueの間animator.speed=0）
    bool grabStickWasActive;         // 掴まれ中、前フレームでスティックが倒れていたか（押しっぱなしを連打扱いにしないための判定用）
    float escapeTimer;               // 掴まれている間の、脱出に必要な残り時間（0になったら脱出成功）

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
    [Tooltip("必殺技（waza／左足攻撃）の多段ヒット設定。既定でON（3秒間で最大5回まで命中可能）。")]
    [SerializeField] MultiHitSetting specialMultiHit = new MultiHitSetting { allowMultiHit = true, maxHitCount = 5 };
    [Tooltip("必殺技が命中してから、次のヒットが有効になるまでのクールタイム（秒）。多段ヒットの連続ヒット間隔を制御する。")]
    [SerializeField] float specialHitCooldown = 0.1f;

    // 現在の攻撃で、既に何回ヒットが確定したか。EnterPunch/EnterKick等、新しい攻撃の開始時に0へリセットする。
    private int hitCountThisAttack = 0;

    // 必殺技が最後にヒットを確定した時刻（Time.time）。specialHitCooldownの間隔判定に使用する。
    // 新しい攻撃の開始時（ResetMultiHitCount）に十分過去の値へリセットし、前回の攻撃のクールタイムを引きずらないようにする。
    private float lastSpecialHitTime = -999f;

    // 攻撃の開始時（EnterPunch/EnterKick等）に呼び出し、多段ヒットのカウントをリセットする。
    void ResetMultiHitCount()
    {
        hitCountThisAttack = 0;
        lastSpecialHitTime = -999f;
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

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今出している技のヒットストップ時間（秒）」を問い合わせるための公開メソッド。
    //   必殺技中はspecialHitStopDuration、それ以外はCurrentAttackTypeに応じた値を返す。
    //   攻撃中でない場合（想定外）は、呼び出し側のフォールバックで扱えるよう負の値(-1)を返す。
    //   新しい技を追加した場合は、対応するフィールドを増やし、ここにもケースを追加すること。
    public float GetCurrentHitStopDuration()
    {
        if (currentState == PlayerState.Special) return specialHitStopDuration;

        switch (CurrentAttackType)
        {
            case AttackType.Punch: return punchHitStopDuration;
            case AttackType.Kick: return kickHitStopDuration;
            case AttackType.UpKick: return upKickHitStopDuration;
            case AttackType.DownKick: return downKickHitStopDuration;
            default: return -1f;
        }
    }

    // ★追加：被弾した相手側（OnTriggerEnter）から、「今出している技のやられ状態の時間（秒）」を問い合わせるための公開メソッド。
    //   必殺技中はspecialStunDuration、それ以外はCurrentAttackTypeに応じた値を返す。攻撃中でない場合は0（やられ状態にしない）。
    //   新しい技を追加した場合は、対応するフィールドを増やし、ここにもケースを追加すること。
    public float GetCurrentStunDuration()
    {
        if (currentState == PlayerState.Special) return specialStunDuration;

        switch (CurrentAttackType)
        {
            case AttackType.Punch: return punchStunDuration;
            case AttackType.Kick: return kickStunDuration;
            case AttackType.UpKick: return upKickStunDuration;
            case AttackType.DownKick: return downKickStunDuration;
            default: return 0f;
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

        // ★追加：必殺技は、命中してから次のヒットが有効になるまでspecialHitCooldown秒のクールタイムを設ける。
        //   多段ヒット許可中でも、同一フレーム／連続フレームでの連続ヒットを防ぐための時間的な間引き。
        if (isSpecial && Time.time - lastSpecialHitTime < specialHitCooldown)
        {
            GaugeDLog($"[{PlayerName}] 必殺技クールタイム中により今回の接触は無効（前回ヒットから{Time.time - lastSpecialHitTime:F3}秒 / クールタイム{specialHitCooldown:F2}秒）");
            return false;
        }

        hitCountThisAttack++;
        if (isSpecial) lastSpecialHitTime = Time.time;
        return true;
    }

    // ★追加：攻撃種別ごとの「命中時」クールダウン（攻撃開始からの合計秒数）を返す。
    //   新しい技を追加したときはここにもケースを追加すること。
    float GetHitDuration(AttackType type)
    {
        switch (type)
        {
            case AttackType.Punch: return punchHitDuration;
            case AttackType.Kick: return kickHitDuration;
            case AttackType.UpKick: return upKickHitDuration;
            case AttackType.DownKick: return downKickHitDuration;
            default: return 0f;
        }
    }

    // ★追加：被弾した相手側から「あなたの攻撃、当たりましたよ」と通知してもらうための公開メソッド。
    public void NotifyAttackLanded()
    {
        // ★追加：この攻撃で最初の命中だった場合、クールダウンを「空振り時」から「命中時」の長さへ切り替える。
        //   （必殺技や投げなど、CurrentAttackTypeがNoneの状態では対象外）
        if (!attackLandedThisAttack && CurrentAttackType != AttackType.None)
        {
            float elapsed = currentAttackMissDuration - stateTimer; // 攻撃開始からの経過時間
            float newTimer = Mathf.Max(GetHitDuration(CurrentAttackType) - elapsed, 0f);
            DLog($"[{PlayerName}] 命中によりクールダウン切替：残り{stateTimer:F2}秒 → {newTimer:F2}秒（命中時={GetHitDuration(CurrentAttackType):F2}秒 / 経過={elapsed:F2}秒）");
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
            HitEffectSpawner.Instance.SpawnAtDirection(mashHitEffectData, transform.position, transform.forward);
            EffectDLog($"[{PlayerName}] 連打擬音エフェクト発生 pos={transform.position}");
        }
    }

    // ジャンプボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする（どのボタンでも同じ）
    public void OnJump(InputValue value)
    {
        if (!value.isPressed) return;

        if (HP <= 0)
        {
            RegisterMash();
            return;
        }

        wantJump = true;
    }

    // パンチボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打判定としてこのボタンを使う
    public void OnPunch(InputValue value)
    {
        if (!value.isPressed) return;

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

        if (HP <= 0)
        {
            RegisterMash();
            return;
        }

        wantKick = true;
    }

    // 仁王立ちボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする
    public void OnStand(InputValue value)
    {
        if (!value.isPressed) return;

        if (HP <= 0)
        {
            RegisterMash();
            return;
        }

        wantGuard = true;
    }

    // 投げボタン押下時のコールバック
    // ※HP<=0のダウン中は「根性復活」の連打としてカウントする
    public void OnThrow(InputValue value)
    {
        if (!value.isPressed) return;

        if (HP <= 0)
        {
            RegisterMash();
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

        // ★追加：ダウン中（HP<=0）のL1は必殺技ではなく「漢気復活」の要求として扱う
        //   （OnPunchがダウン中は連打カウントになるのと同じ仕組み）
        if (HP <= 0)
        {
            wantKankiRevive = true;
            RegisterMash(); // ★追加：L1も他のボタンと同じく連打1回分として数える（ゲージが足りなければ連打で復活を目指せる）
            return;
        }

        wantSpecial = true;
    }

    // Update is called once per frame
    // 毎フレームの更新処理。HPが尽きていれば復活チャレンジへ、
    // そうでなければ現在の状態に応じて「拘束中のタイマー消化」か「新しい行動の受付」を行う。
    void Update()
    {
        // ★追加：掴みポーズで止めている最中に、被弾やダウン等でThrow状態から外れた場合の保険として必ず再生を再開する
        if (grabHoldFrozen && currentState != PlayerState.Throw) ResumeGrabHoldAnimation();

        MaintainKankiChargeEffect(); // ★追加：漢気ゲージ1本以上の間、追従エフェクトを出し続ける
        TickReviveBuff();            // ★追加：漢気復活後の攻撃力上昇の残り時間を消化する
        TickMashButtonVisual();      // ★追加：連打ボタン画像の押した／押していない切り替え
        ResolveOpponentIfMissing();  // ★追加：プレハブ化・再配置等で相手(enemyPlayer/enemy)の参照が切れていたら自動で探し直す
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
                    stateTimer = 0f;
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

    // LateUpdate: Update内の移動処理やRigidbodyによる物理移動（ジャンプ等）がすべて反映された後に、
    //   ①P1・P2同士が重なっていたら押しのけ合い（ResolvePlayerOverlap）、
    //   ②最終的なワールド座標をminPosition〜maxPositionの範囲内へ強制的に収める（ClampPositionWithinBounds）。
    //   Updateの最後ではなくLateUpdateで行うことで、物理演算(FixedUpdate)による移動分も確実に制限できる。
    void LateUpdate()
    {
        ResolvePlayerOverlap();
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

    // P1・P2の胴体コライダー(Player_Collider)が重なっている時に、水平方向(XZ平面)へお互いを押し出す。
    // 相手の真上に乗ってしまった場合でも、この水平方向の押し出しにより支え切れずに横へずれ落ち、
    // 重力で自然に滑り落ちる（＝上に乗ったまま静止できなくなる）。
    void ResolvePlayerOverlap()
    {
        if (!enablePlayerPush) return;
        if (enemyPlayer == null) return; // 対人戦(P1/P2)専用。CPU戦(enemy)は対象外
        if (Player_Collider == null || enemyPlayer.Player_Collider == null) return;

        // ダウン中・死亡中のプレイヤーは押しのけ合いの対象から外す
        if (Player_status == Status.Dead || enemyPlayer.Player_status == Status.Dead) return;

        CapsuleCollider myCol = Player_Collider;
        CapsuleCollider otherCol = enemyPlayer.Player_Collider;

        // 半径はワールドスケールを考慮する（通常は1のはずだが、念のためlossyScaleを反映）
        float myRadius = myCol.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float otherRadius = otherCol.radius * Mathf.Max(enemyPlayer.transform.lossyScale.x, enemyPlayer.transform.lossyScale.z);
        float minDistance = myRadius + otherRadius + pushSkinWidth;

        // --- 垂直方向(Y)の重なりチェック ---
        // お互いの高さ範囲が重なっていない（例：ジャンプで相手の頭上を飛び越えている最中）場合は、
        // 実際には接触していないので押しのけを行わない。
        float myBottom = transform.position.y + myCol.center.y - myCol.height * 0.5f;
        float myTop = transform.position.y + myCol.center.y + myCol.height * 0.5f;
        float otherBottom = enemyPlayer.transform.position.y + otherCol.center.y - otherCol.height * 0.5f;
        float otherTop = enemyPlayer.transform.position.y + otherCol.center.y + otherCol.height * 0.5f;

        bool verticalOverlap = myBottom < otherTop && otherBottom < myTop;
        if (!verticalOverlap) return;

        // --- 水平方向(Z軸。P1/P2の間合いはZ軸のみで変化する仕様のため)の重なりチェック ---
        float deltaZ = transform.position.z - enemyPlayer.transform.position.z;
        float distance = Mathf.Abs(deltaZ);

        if (distance >= minDistance) return; // 十分離れているので何もしない

        float overlap = minDistance - distance;
        // 自分の分だけ半分押し出す（相手も同じ処理を自分自身に対して行うため、結果的に均等に離れる）
        float halfPush = overlap * 0.5f;
        float pushThisFrame = Mathf.Min(halfPush, maxPushSpeed * Time.deltaTime);

        float pushDir;
        if (distance > 0.0001f)
        {
            pushDir = Mathf.Sign(deltaZ);
        }
        else
        {
            // 完全に同じZ座標で重なっている（例：真上に乗った瞬間）等、符号が決まらない場合は、
            // InstanceIDで押す方向を固定し、お互い矛盾なく逆方向へ分かれるようにする
            pushDir = GetInstanceID() < enemyPlayer.GetInstanceID() ? -1f : 1f;
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
    [Tooltip("「押した画像」を出す時間（秒）。1回出したら、連打がどれだけ速くてもこの時間で必ず終わらせる。")]
    [SerializeField] float mashButtonPressedDuration = 0.06f;
    [Tooltip("「押していない画像」を最低限出す時間（秒）。押した画像の後に必ずこの時間だけ挟むので、連打中でも押しっぱなしに見えない。")]
    [SerializeField] float mashButtonReleaseMinDuration = 0.05f;
    [Tooltip("画像の大きさ（画面の高さに対する割合）。0.22なら画面高さの22%の正方形で表示する。")]
    [SerializeField] float mashButtonSizeRatio = 0.22f;
    [Tooltip("画像を表示するX位置（画面幅に対する割合。0.5で中央）")]
    [SerializeField] float mashButtonXRatio = 0.5f;
    [Tooltip("画像を表示するY位置（画面高さに対する割合。数字より下に出すため0.72付近）")]
    [SerializeField] float mashButtonYRatio = 0.72f;

    bool mashButtonVisualPressed;   // 今「押した画像」を出しているか
    float mashButtonStateEndTime;   // 今の見た目（押した／押していない）を最低限維持する終了時刻（Time.unscaledTime）
    bool mashButtonPendingPress;    // 押された要求が溜まっている（押していない画像を挟んでから、次の押した画像にする）

    // ★追加：連打ボタン画像の「押した／押していない」を毎フレーム切り替える。
    //   押した画像は mashButtonPressedDuration で必ず終わらせ、
    //   その後に mashButtonReleaseMinDuration だけ「押していない画像」を必ず挟む。
    //   連打が速くて押下要求が溜まっていても、その間は押していない画像を見せるので、押しっぱなしに見えない。
    void TickMashButtonVisual()
    {
        // ダウン中以外は状態をリセットしておく（次のダウン時に前回の押下が残らないようにする）
        if (currentState != PlayerState.KnockedDown)
        {
            mashButtonVisualPressed = false;
            mashButtonPendingPress = false;
            return;
        }

        float now = Time.unscaledTime;

        if (mashButtonVisualPressed)
        {
            // 押した画像は決められた時間で必ず終わらせ、押していない画像へ戻す
            if (now >= mashButtonStateEndTime)
            {
                mashButtonVisualPressed = false;
                mashButtonStateEndTime = now + mashButtonReleaseMinDuration;
            }
        }
        else
        {
            // 押していない画像を最低限の時間出し終えていて、押された要求があれば、次の「押した画像」にする
            if (mashButtonPendingPress && now >= mashButtonStateEndTime)
            {
                mashButtonPendingPress = false;
                mashButtonVisualPressed = true;
                mashButtonStateEndTime = now + mashButtonPressedDuration;
            }
        }
    }

    void OnGUI()
    {
        DrawStunComboDebug(); // ★追加：F10のやられ連続ヒット数表示

        if (!showMashPrompt && !showMashRemainingCount && !showMashButtonImage) return;
        if (currentState != PlayerState.KnockedDown) return;

        // 明滅させて視認性・緊張感を出す
        float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 10f);

        if (showMashPrompt)
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
                (Screen.width - width) / 2f,
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
                Rect rect = new Rect(
                    Screen.width * mashButtonXRatio - size / 2f,
                    Screen.height * mashButtonYRatio - size / 2f,
                    size, size);

                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            }
        }
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
            // ★追加：掴みポーズで止めている最中は、ヒットストップが終わっても止めたままにする
            animator.speed = grabHoldFrozen ? 0f : 1f;

            // ヒットストップで一瞬止めていたぶん、止まった直後に被弾（HeadHit）アニメーションを再生する。
            // ガード成功時のヒットストップ（pendingHeadHitAnimation=false）ではここは実行されない。
            if (pendingHeadHitAnimation)
            {
                animator.Play("HeadHit", 0, 0.0f);
            }
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

    void StunDLog(string message)
    {
        if (showStunComboDebug) Debug.Log($"[STUN] {message}");
    }

    // 被弾時に呼ぶ。duration=やられ状態の秒数、willBeKnockedDown=この一撃でHPが0になるか
    void ApplyStun(float duration, bool willBeKnockedDown)
    {
        // 掴み・投げ・ダウン・死亡・必殺技（無敵）中は、やられ状態に置き換えない
        if (currentState == PlayerState.Grabbed || currentState == PlayerState.Thrown
            || currentState == PlayerState.KnockedDown || currentState == PlayerState.Dead
            || currentState == PlayerState.Special) return;
        if (currentState == PlayerState.Throw && grabbedTarget != null) return; // 相手を掴んでいる最中

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
            stateTimer = Mathf.Max(stateTimer, duration);
        }
        else
        {
            if (duration <= 0f) return; // 0秒設定の技はやられ状態にならない
            stunComboCount = 1;
            EnterStunned(duration);
        }

        StunDLog($"[{PlayerName}] 連続ヒット={stunComboCount} / やられ残り={stateTimer:F2}秒");
    }

    // ★追加：自分の攻撃が相手の仁王立ち（ガード）に防がれた時に、相手側から呼ばれる。
    //   ガードされた側（攻撃者＝自分）を、指定秒数だけ行動不能（Stunned）にする。
    //   通常の被弾（ApplyStun）と違い「連続ヒット数」は増やさない（被弾コンボではないため）。
    //   すでにやられ状態の場合は、残り時間と指定秒数の長い方に延長する。
    public void ApplyGuardedStun(float duration)
    {
        if (duration <= 0f) return; // 0秒設定ならスタンさせない

        // 掴み・投げ・ダウン・死亡・必殺技（無敵）中は、やられ状態に置き換えない（ApplyStunと同じ除外条件）
        if (currentState == PlayerState.Grabbed || currentState == PlayerState.Thrown
            || currentState == PlayerState.KnockedDown || currentState == PlayerState.Dead
            || currentState == PlayerState.Special) return;
        if (currentState == PlayerState.Throw && grabbedTarget != null) return; // 相手を掴んでいる最中

        if (currentState == PlayerState.Stunned)
        {
            stateTimer = Mathf.Max(stateTimer, duration);
        }
        else
        {
            EnterStunned(duration);
        }

        DLog($"[{PlayerName}] 相手の仁王立ちガードに防がれ、{duration:F2}秒スタン（残り{stateTimer:F2}秒）");
    }

    // 実行中の行動を中断してやられ状態に入る
    void EnterStunned(float duration)
    {
        StopMoveAnimation();
        ResetAttackTriggers();
        DisableAllHitboxes();   // 攻撃中だった場合、その当たり判定を消す
        isGuarding = false;
        canThrow = true;
        throwReaching = false;  // 掴み待ち（まだ掴めていない投げ動作）だった場合は中断

        currentState = PlayerState.Stunned;
        stateTimer = duration;

        DLog($"[{PlayerName}] やられ状態に入りました（{duration:F2}秒）");
    }

    // やられ状態の連続ヒット数を確定する（やられ状態が終わった時・ダウンした時に呼ぶ）
    void FinishStunCombo()
    {
        if (stunComboCount <= 0) return;
        lastStunComboCount = stunComboCount;
        StunDLog($"[{PlayerName}] やられ状態終了。連続ヒット数={lastStunComboCount}");
        stunComboCount = 0;
    }

    // F10デバッグ表示：やられ状態中に連続で当たった回数を画面端に表示する
    void DrawStunComboDebug()
    {
        if (!showStunComboDebug) return;

        int index = playerInput != null ? playerInput.playerIndex : 0;
        float width = 420f;
        float height = 150f;
        float x = index == 0 ? 20f : Screen.width - width - 20f;
        Rect rect = new Rect(x, Screen.height * 0.3f, width, height);

        string stateText = currentState == PlayerState.Stunned ? $"やられ中（残り{stateTimer:F2}秒）" : "通常";
        string text = $"[{PlayerName}] やられ状態\n状態: {stateText}\n連続ヒット数: {stunComboCount}\n直前のコンボ: {lastStunComboCount} ヒット";

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
        };
        GUIStyle outline = new GUIStyle(style) { normal = { textColor = Color.black } };
        style.normal.textColor = Color.yellow;

        Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
        foreach (var o in offsets)
        {
            GUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), text, outline);
        }
        GUI.Label(rect, text, style);
    }

    //-----------------------------------------------------
    // 被弾時のノックバック
    //-----------------------------------------------------
    // ノックバックを要求する。ヒットストップが有効な場合はその終了時（EndHitStop）に、
    // 無効な場合はその場ですぐにApplyKnockbackを実行する。
    // ※StartHitStopの早期return条件（enableHitStopがfalse、またはヒットストップ時間が0以下）と揃えてある。
    //   appliedHitStopDuration: 今回の被弾で実際にStartHitStopへ渡したヒットストップ時間。
    void RequestKnockback(Vector3 velocity, float appliedHitStopDuration)
    {
        // ★追加：攻撃が当たった瞬間に、相手(自分)の今の速度をXYZすべて0にする。
        //   前の被弾の吹き飛び中・落下中・移動中の勢いが残ったまま、ノックバックが上乗せされるのを防ぐ。
        //   （ヒットストップ中に前の勢いのまま滑ってしまうのも防ぐ。実際にノックバックを与えるのはその後）
        ZeroRigidbodyVelocity();

        if (enableHitStop && appliedHitStopDuration > 0f)
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

        // ★追加：スティックの倒し具合（0〜1）に応じて、移動アニメーションの再生速度を変化させる
        //   moveInput.xの絶対値をそのまま「移動量」として扱い、minMoveAnimSpeed〜maxMoveAnimSpeedの範囲へ変換する
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
        FaceDirection(worldDirection);

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
    Transform GetOpponentTransform()
    {
        if (enemyPlayer != null) return enemyPlayer.transform;
        if (enemy != null) return enemy.transform;
        return null;
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
        rb.AddForce(force);
        Jumpflag = false; // 空中に出たので再度ジャンプできないようにする
    }

    //-----------------------------------------------------
    // 拘束のある状態（攻撃・ガード・投げ）への遷移
    //-----------------------------------------------------

    // パンチ（弱攻撃）処理。地上か空中かでアニメーションと当たり判定を切り替える
    void EnterPunch()
    {
        // ★修正：移動中にパンチへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   パンチよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        ResetAttackTriggers();
        currentState = PlayerState.Punch;
        stateTimer = punchMissDuration;
        currentAttackMissDuration = punchMissDuration;
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        // パンチの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映して攻撃力を確定する
        baseAtk = GetAttackPower(AttackType.Punch);
        UpdateAtkByGauge();

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
        // ★修正：移動中にキックへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   キックよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        ResetAttackTriggers();
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        if (isCrouchInput)
        {
            currentState = PlayerState.DownKick;
            stateTimer = downKickMissDuration;
            currentAttackMissDuration = downKickMissDuration;
            animator.SetTrigger("DownKick");
            RightFoot.enabled = true;
            RightLeg.enabled = true;

            // 下キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.DownKick);
            UpdateAtkByGauge();
        }
        else if (moveInput.y > upKickInputThreshold)
        {
            currentState = PlayerState.UpKick;
            stateTimer = upKickMissDuration;
            currentAttackMissDuration = upKickMissDuration;
            animator.SetTrigger("UpKick");
            DLog($"[{PlayerName}] UpKick発動：Triggerを立てました（moveInput.y={moveInput.y:F2}）");
            DebugUpKickAnimator(); // ★デバッグ：Animatorが実際にどのステートへ遷移したかを確認する
            RightFoot.enabled = true;
            RightLeg.enabled = true;

            // 上キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.UpKick);
            UpdateAtkByGauge();
        }
        else
        {
            currentState = PlayerState.Kick;
            stateTimer = kickMissDuration;
            currentAttackMissDuration = kickMissDuration;
            animator.SetTrigger("Kick");
            RightFoot.enabled = true;
            RightUpLeg.enabled = true;
            RightLeg.enabled = true;

            // 通常キックの攻撃力（Inspector設定値）を基準に、漢気ゲージ補正を反映する
            baseAtk = GetAttackPower(AttackType.Kick);
            UpdateAtkByGauge();
        }
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

    // 仁王立ち（ガード）処理。ガードフラグを立て、演出用パーティクルを再生する
    void EnterGuard()
    {
        // ★修正：移動中にガードへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   ガードよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Guard;
        stateTimer = guardDuration;
        isGuarding = true;

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
    //   実際に相手を投げ飛ばす（放物線状に飛ばす）のは、この掴み拘束(throwDuration)が終わる瞬間
    //   （TickBusyState→ResolveThrowLaunch）であり、掴んでいる間の移動スティック入力で方向が決まる。
    void EnterThrow()
    {
        // ★修正：移動中に投げへ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   投げよりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Throw;
        stateTimer = throwDuration;
        animator.SetTrigger("Throw-start"); // 掴み（ホールド）モーション。飛ばす瞬間は別トリガー"Throw-release"を使う
        grabbedTarget = null;

        // ★変更：掴みの成立を「間合い(Z距離)の判定」から「手の当たり判定の接触」へ変更した。
        //   投げ動作の間、左手(P-LeftHand)・左前腕(P-LeftForeArm)の当たり判定をONにしておき、
        //   相手の体に触れた瞬間に掴みが成立する（相手側のOnTriggerEnter → OnGrabHitboxContact）。
        //   触れないまま投げ動作(throwDuration)が終われば空振りで、掴めなかった時と同じく何も起きずにIdleへ戻る。
        throwReaching = true;
        grabHoldFrozen = false;
        if (LeftHand != null) LeftHand.enabled = true;
        if (LeftForeArm != null) LeftForeArm.enabled = true;
        DLog($"[{PlayerName}] 投げ開始：左手・左前腕の当たり判定をON（接触した相手を掴む）");
    }

    // ★追加：今が投げ動作中で、colliderが「掴み用の手の当たり判定（左手／左前腕）」かどうかを返す。
    //   相手側のOnTriggerEnterから、「これは通常の攻撃ヒットではなく掴みの接触だ」と区別するために使う。
    //   ※掴み成立後（throwReachingがfalse）でも、同じ物理フレームに残りの手のイベントが届くことがあるため、
    //     throwReachingは見ずに、投げ状態(Throw)であることだけで判定する（ダメージ処理へ流れないようにするため）。
    public bool IsGrabHitbox(Collider c)
    {
        return currentState == PlayerState.Throw && (c == LeftHand || c == LeftForeArm);
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
        throwReaching = false;
        canThrow = false;                      // 一度成立したら再度多重に発動しないようにする
        grabbedTarget = target;

        // 手の当たり判定は役目を終えたのでOFFにする（以降の接触でダメージ判定へ流れないようにする）
        DisableAllHitboxes();

        // 接触した瞬間から数えてthrowDuration後に投げ飛ばす（脱出判定の猶予もこの間）
        stateTimer = throwDuration;

        target.transform.Translate(0f, 0f, -0.0025f); // 敵を少し引き寄せる（既存の演出を踏襲）
        target.EnterGrabbed(this);                    // 相手を掴まれ状態へ移行させる（脱出タイマーは相手のHPから決まる）

        // ★アニメーションの止め方はInspectorのenableThrowStartPlayで切り替える。
        //   止めたアニメーションは、投げ成立／不成立の瞬間にResumeGrabHoldAnimation()で再開される。
        if (enableThrowStartPlay && throwStartPlayTime > 0f)
        {
            // ON：接触後throwStartPlayTime秒だけ再生を続け、その後に止める（TickBusyStateで残り時間を監視）
            grabHoldTimer = throwStartPlayTime;
            grabHoldFrozen = false;
        }
        else
        {
            // OFF：接触した瞬間にアニメーションを止める
            grabHoldTimer = 0f;
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
    float CalculateEscapeTime()
    {
        float hpRatio = maxHP > 0 ? Mathf.Clamp01((float)HP / maxHP) : 0f;
        return Mathf.Lerp(escapeTimeAtZeroHp, escapeTimeAtFullHp, hpRatio);
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
        escapeTimer = CalculateEscapeTime();
        // 掴まれた瞬間にスティックを倒していても、それを「抵抗入力」とは数えない
        grabStickWasActive = Mathf.Abs(moveInput.x) >= moveInputThreshold
                          || Mathf.Abs(moveInput.y) >= moveInputThreshold;

        animator.SetTrigger("Grabbed"); // ★要Animator追加：掴まれ中（もがき）専用のアニメーション。"Thrown"（飛んでいる間）とは別にする

        DLog($"[{PlayerName}] 掴まれた！脱出に必要な時間={escapeTimer:F2}秒（HP={HP}/{maxHP}）");
    }

    // ★追加：掴まれている間、毎フレーム呼ばれる。時間経過に加え、
    //   ボタン（既存の意図フラグ）かスティックの入力があるたびに追加で脱出時間を短縮する。
    void TickGrabbed()
    {
        escapeTimer -= Time.deltaTime;

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
            escapeTimer -= escapeReductionPerInput;
        }

        if (escapeTimer <= 0f)
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

    // ★追加：掴み拘束(throwDuration)が時間切れになった＝相手が脱出できなかった時に、
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

        ResumeGrabHoldAnimation();            // ★追加：止めていた掴みモーションを再開してからトリガーを上げる
        animator.SetTrigger("Throw-release"); // 投げ成立時だけ上げる「投げ飛ばす」モーション用トリガー

        grabbedTarget.damege(throwAtk); // ダメージは掴み成立時ではなく、実際に投げが決まった瞬間に与える

        Vector3 launchDir = GetThrowDirectionFromStick();

        // ★追加：後ろ投げ（自分の向きと逆方向へ投げ飛ばした場合）は、お互いの向きを反転させる。
        //   相手が自分の背後へ飛んでいくため、そのままだと背中を向け合ったままになってしまう。
        //   向きの判定は投げる前の自分の向き(transform.forward)で行う。
        bool isBackThrow = Vector3.Dot(launchDir, transform.forward) < 0f;

        grabbedTarget.LaunchByThrow(launchDir, throwHorizontalSpeed, throwUpSpeed, "Thrown", throwStunDuration);

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

    private float pendingThrowStunDuration = 0f; // ★追加：投げで飛ばされた後、着地時に入るやられ状態の時間（秒）

    // ★追加：投げ技によって、放物線状に吹き飛ばされる処理。掴んでいた相手から呼び出される。
    //   Rigidbodyに初速を与えるだけで、あとは重力(既存のJump同様の物理設定)に任せて放物線を描かせる。
    //   ★変更：投げ成立時（相手を飛ばす）だけでなく、投げ不成立時にお互いが後方へ吹き飛ぶ演出
    //   （PushBackFromGrabFailure）でも共用できるよう、再生するアニメーショントリガーを引数化した。
    //   省略時は従来通り"Thrown"（通常の投げ成立で飛ばされる側のモーション）を使う。
    public void LaunchByThrow(Vector3 horizontalDirection, float horizontalSpeed, float upSpeed, string animTrigger = "Thrown", float stunAfterLanding = 0f)
    {
        // ★追加：着地後にやられ状態へ移行する時間を記録する（0なら着地後は通常復帰）。
        //   投げ不成立の吹き飛び(PushBackFromGrabFailure)は引数省略＝0なので、やられ状態にはならない。
        pendingThrowStunDuration = stunAfterLanding;

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
    }

    // ★追加：掴みポーズで止めていたアニメーションを再開する。
    //   animator.speedが0のままだとトリガーを上げても遷移しないため、Throw-release／Throw-whiffを上げる直前に必ず呼ぶ。
    void ResumeGrabHoldAnimation()
    {
        grabHoldFrozen = false;
        if (animator != null && hitStopTimer <= 0f) animator.speed = 1f; // ヒットストップ中ならEndHitStopが戻す
    }

    // ★追加：投げで吹き飛ばされた後、地面に着地した瞬間にOnCollisionEnterから呼ばれる。
    //   Thrown状態を終えてIdleへ戻す。
    void LandFromThrow()
    {
        DLog($"[{PlayerName}] 投げから着地して復帰");
        currentState = PlayerState.Idle;

        // ★追加：投げ成立で飛ばされていた場合、着地した瞬間からやられ状態（行動不能）に入る。
        //   ダメージでHPが0になっている場合は、ダウン処理（HandleKnockedDown）に任せるため入らない。
        float landStun = pendingThrowStunDuration;
        pendingThrowStunDuration = 0f;
        if (landStun > 0f && HP > 0)
        {
            stunComboCount = 1; // 投げを1ヒットとして数える（やられ中にさらに被弾すると加算される）
            EnterStunned(landStun);
            StunDLog($"[{PlayerName}] 投げ着地→やられ状態 {landStun:F2}秒");
        }

        animator.SetTrigger("Thrown-land"); // ★要Animator追加（任意）：着地モーション用トリガー。未設定でも動作に支障はない
    }

    // 現在の漢気ゲージで必殺技を発動できるかどうか
    bool CanUseSpecial()
    {
        return kankiGauge >= specialRequiredGauge;
    }

    // 必殺技発動処理。左足（LeftFoot/LeftUpLeg/LeftLeg）の当たり判定をONにして、
    // その状態で3秒間（specialDuration）だけ攻撃を行う。
    // この間は移動のみ可能（攻撃・ガード・投げ・ジャンプ・必殺技の再発動は不可）で、
    // かつ相手からの攻撃を一切受けない（無敵。OnTriggerEnter側で判定）。
    // 相手に左足の判定が当たった場合のみ、specialAtk（Inspector設定）のダメージを与える。
    void EnterSpecial()
    {
        // ★修正：移動中に必殺技へ遷移した際、Moveアニメーション(bool)がONのまま残り、
        //   必殺技よりも移動が優先されているように見えてしまう不具合を解消する。
        StopMoveAnimation();

        currentState = PlayerState.Special;
        stateTimer = specialDuration;
        attackLandedThisAttack = false; // 空振り判定用にリセット
        ResetMultiHitCount();           // 多段ヒットカウントを新しい攻撃用にリセット

        // ★Animator Controller側に"waza"という名前のTriggerパラメータを追加しておくこと。
        //   currentState=Specialの間、移動以外の入力(攻撃・ガード・投げ・ジャンプ・必殺技)は
        //   TickBusyState()側で受け付けないため、stateTimer(=specialDuration)が0になって
        //   PlayerState.Idleへ戻るまで次の技は出せない（＝アニメーションが終わるまで次の技を出せない）。
        //   specialDurationは"waza"アニメーションの実際の長さと一致させること。
        animator.SetTrigger("waza");

        // 左足（3箇所）の攻撃用当たり判定をON。オフに戻す処理はTickBusyState()の
        // 拘束時間終了処理（DisableAllHitboxes）で他の技と共通して行われる。
        LeftFoot.enabled = true;
        LeftUpLeg.enabled = true;
        LeftLeg.enabled = true;

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

        DLog($"[{PlayerName}] 必殺技発動！（消費ゲージ={specialGaugeCost} / 攻撃力={atk} / 拘束時間={specialDuration}秒 / 無敵）");
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
        FaceDirection(worldDirection);
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
            stateTimer -= Time.deltaTime;
            if (stateTimer > 0f) return;

            FinishStunCombo();
            currentState = PlayerState.Idle;
            DLog($"[{PlayerName}] やられ状態が終わりました");
            return;
        }

        if (currentState == PlayerState.Special)
        {
            MoveDuringSpecial();
        }

        // ★追加：掴み成立後、throwStartPlayTime秒だけ掴みモーションを再生したら、そのポーズで止める
        if (currentState == PlayerState.Throw && grabbedTarget != null && !grabHoldFrozen)
        {
            grabHoldTimer -= Time.deltaTime;
            if (grabHoldTimer <= 0f)
            {
                grabHoldFrozen = true;
                if (animator != null) animator.speed = 0f;
            }
        }

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
                EffectDLog($"[{PlayerName}] 空振り擬音エフェクト発生 pos={transkunn}");
            }
        }

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

    // HPが0になった際に毎フレーム呼ばれる、根性復活（ボタン連打による復活）の処理
    void HandleKnockedDown()
    {
        FinishStunCombo(); // ★追加：やられ中にダウンした場合、連続ヒット数を確定する
        currentState = PlayerState.KnockedDown;

        //gameMNG.PlayerUI(rebornTimer, mashCount);

        rebornTimer += Time.deltaTime;
        Player_status = Status.Reborn;
        if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Reborn);

        //ダウンした瞬間、一度だけ顔・拳へのクローズアップカメラを開始する
        if (!rebornCamStarted && fightingCamera != null)
        {
            fightingCamera.StartRebornCloseUp(transform);
            rebornCamStarted = true;
        }

        // ★追加：漢気復活。制限時間内にL1が押され、漢気ゲージが足りていれば、連打せずに即座に復活する
        if (wantKankiRevive && rebornTimer < rebornTimeLimit && TryKankiRevive())
        {
            return;
        }

        // 復活に必要な連打回数のしきい値（復活回数が増えるほど厳しくなる）
        int mashThreshold = CurrentMashThreshold();

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
                //復活成功（最大HPのrebornHpRatio割合まで回復）
                HP = Mathf.RoundToInt(maxHP * rebornHpRatio);
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
            if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Dead);

            if (fightingCamera != null)
            {
                fightingCamera.ClearReborn();
            }
            rebornCamStarted = false;
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
        rebornTimer = 0f;
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
            fightingCamera.TriggerRebornStandUpOrbit(transform);
        }
        rebornCamStarted = false; // 次回のダウンに備えてリセット

        SpawnKankiReviveEffect(); // 復活した瞬間のエフェクト
        StartReviveBuff();        // 攻撃力上昇（＋上昇中エフェクト）を開始

        DLog($"[{PlayerName}] 漢気復活！（消費ゲージ={cost:F1} / 復活後HP={HP} / 攻撃力={atk} / 上昇時間={reviveAtkBuffDuration}秒）");
        return true;
    }

    // 復活した瞬間に1回だけ出すエフェクト。プレイヤーの子オブジェクトとして生成し、一定時間後に破棄する。
    void SpawnKankiReviveEffect()
    {
        if (kankiReviveEffectPrefab == null)
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

        EffectDLog($"[{PlayerName}] 漢気復活エフェクト発生 pos={fx.transform.position}");
    }

    // 攻撃力上昇を開始する（すでに上昇中なら残り時間を延ばし直す）
    void StartReviveBuff()
    {
        isReviveBuffed = true;
        reviveBuffTimer = reviveAtkBuffDuration;
        UpdateAtkByGauge(); // 上昇倍率をatkへ反映
        StartReviveBuffEffect();
    }

    // 攻撃力上昇を終了する（時間切れ・ダウン時に呼ばれる）
    void EndReviveBuff()
    {
        if (!isReviveBuffed) return;

        isReviveBuffed = false;
        reviveBuffTimer = 0f;
        UpdateAtkByGauge(); // 上昇倍率を外したatkへ戻す
        StopReviveBuffEffect();

        DLog($"[{PlayerName}] 漢気復活による攻撃力上昇が終了（攻撃力={atk}）");
    }

    // 毎フレームUpdateから呼ばれ、攻撃力上昇の残り時間を消化する
    void TickReviveBuff()
    {
        if (!isReviveBuffed) return;

        reviveBuffTimer -= Time.deltaTime;
        if (reviveBuffTimer <= 0f) EndReviveBuff();
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
        rebornTimer = 0f;
        currentState = PlayerState.Idle;
        Player_status = Status.Live;

        //UIにHPを反映させるように指示
        if (gameMNG != null) gameMNG.Player_ReduceHP(HP, PlayerName);
        if (gameMNG != null) gameMNG.SettestStatus(PlayerName, Status.Live);

        if (fightingCamera != null)
        {
            fightingCamera.SetRebornLevel(fightingCamera.rebornMaxLevel);
            fightingCamera.TriggerRebornStandUpOrbit(transform);
        }
        rebornCamStarted = false; //次回のダウンに備えてリセット

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

        // ★追加：相手が投げ動作中に、その左手・左前腕の当たり判定が自分の体に触れた場合は、
        //   ダメージを与える通常の被弾ではなく「掴みの接触」として相手側に通知する（ダメージ・ヒットストップ等は発生させない）。
        //   掴めない状態（ジャンプ中等）だった場合も、通知先で無視されるだけで、ここでは被弾扱いにしない。
        if (isEnemyPlayerAttack && enemyPlayer.IsGrabHitbox(collision))
        {
            enemyPlayer.OnGrabHitboxContact(this);
            return;
        }

        // ★追加：多段ヒット防止／許可の判定。
        //   同じ攻撃（1回のパンチ・キック等）に対して、今回の接触を有効なヒットとして扱ってよいかを
        //   攻撃側（相手）のInspector設定（多段ヒット設定）に基づいて問い合わせる。
        //   falseが返った場合は、既に規定回数ヒット済みなのでこの接触は完全に無視する（ダメージ・演出も一切発生させない）。
        if (isEnemyPlayerAttack)
        {
            if (!enemyPlayer.TryRegisterHit()) return;
        }
        else if (isEnemyAttack)
        {
            // ※CPU(Enemy)側は現状、多段ヒット制御に未対応です。
            //   Enemy.csにも同様のTryRegisterHit()を実装すればCPU戦にも適用できます。
        }

        //今回被弾させてきた相手の攻撃力を取得（対人戦かCPU戦かで参照先を切り替える）
        int attackerAtk = isEnemyPlayerAttack ? enemyPlayer.atk : enemy.atk;

        //ここまで来たら「敵の攻撃用ヒットボックスが自分の体に当たった」＝正真正銘の被弾
        if (isGuarding)
        {
            // 仁王立ち（ガード）中に被弾した場合の処理
            // ガード成功で「相手の攻撃力 × guardAtkBonusMultiplier」を自分の攻撃力に上乗せする
            // （次の自分の攻撃が当たるまで持続。倍率はInspectorの「ガード成功時の攻撃力上昇設定」で調整可能）
            int guardAtkBonus = Mathf.RoundToInt(attackerAtk * guardAtkBonusMultiplier);
            atk += guardAtkBonus;
            DLog($"[{PlayerName}] ガード成功による攻撃力上昇 +{guardAtkBonus}（相手攻撃力{attackerAtk} × 倍率{guardAtkBonusMultiplier}）");
            DLog("漢!!");
            se.PlayOneShot(MenBlock_se);       // ガード成功の効果音を再生
            HP -= attackerAtk / 2;                // ガード中はダメージを半減させる

            // ガードに成功したので、自分の漢気ゲージを増やす
            AddKankiGauge(gaugeGainOnGuard);

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
            if (isEnemyPlayerAttack) enemyPlayer.NotifyAttackLanded();
            else if (enemy != null) enemy.NotifyAttackLanded();

            // ★追加：ガード成功で、攻撃してきた相手を guardSuccessStunDuration 秒だけスタン（行動不能）にする。
            //   ※NotifyAttackLanded()の後に呼ぶこと（命中によるクールダウン切替の後で、やられ状態へ上書きするため）。
            //   ※CPU(Enemy)が攻撃者の場合は未対応（Enemy.cs側にApplyGuardedStun()相当が必要）。
            if (isEnemyPlayerAttack) enemyPlayer.ApplyGuardedStun(guardSuccessStunDuration);
        }
        else
        {
            // ガードしていない状態で被弾した場合の処理
            guardComboCount = 0;
            animator.SetTrigger("Hit");

            // ★追加：攻撃者が自分の前方にいたか後方にいたかで、被弾アニメーション用のフラグを分けて立てる。
            //   "Hit-front" = 前から攻撃された（相手が自分の正面にいる）／"Hit-back" = 後ろから攻撃された（相手が背後にいる）。
            //   既存の"Hit"は従来どおり立てたまま、それに加えてどちらか一方だけを立てる。
            Transform hitAttackerTf = isEnemyPlayerAttack ? enemyPlayer.transform : (enemy != null ? enemy.transform : null);
            if (hitAttackerTf != null)
            {
                bool attackerInFront = IsOpponentInFront(hitAttackerTf);
                // 反対側の未消費フラグが残っていると、次の被弾で誤ったアニメーションに遷移しうるので先に消す
                animator.ResetTrigger(attackerInFront ? "Hit-back" : "Hit-front");
                animator.SetTrigger(attackerInFront ? "Hit-front" : "Hit-back");
                DLog($"[{PlayerName}] 被弾方向：攻撃者は{(attackerInFront ? "前方" : "後方")}（{(attackerInFront ? "Hit-front" : "Hit-back")}フラグを立てました）");
            }

            // 通常被弾時のヒットストップ（少しだけ動けなくする）。
            // ★変更：対人戦では、攻撃者の「攻撃ごとの設定」のヒットストップ時間を使う。
            //   CPU(Enemy)の攻撃、または攻撃種別が取れない場合は、従来のhitStopDurationにフォールバックする。
            float appliedHitStop = hitStopDuration;
            if (isEnemyPlayerAttack)
            {
                float perAttackHitStop = enemyPlayer.GetCurrentHitStopDuration();
                if (perAttackHitStop >= 0f) appliedHitStop = perAttackHitStop;
            }
            StartHitStop(appliedHitStop);

            // ★追加：ノックバック。攻撃者（相手Player）の技ごとの設定に従って、
            //   攻撃者から離れる方向（Z軸）＋上方向へ飛ばす。
            //   ※CPU(Enemy)の攻撃は未対応（Enemy.cs側にも同様のGetCurrentKnockbackSetting()が必要）。
            //   ※この一撃でHPが0になる場合は、ダウン処理と干渉しないようノックバックしない。
            if (isEnemyPlayerAttack && HP - attackerAtk > 0)
            {
                KnockbackSetting knockback = enemyPlayer.GetCurrentKnockbackSetting();
                if (knockback != null && (knockback.horizontalSpeed != 0f || knockback.upSpeed != 0f))
                {
                    Vector3 awayDirection = GetAwayDirectionFrom(enemyPlayer);
                    RequestKnockback(awayDirection * knockback.horizontalSpeed + Vector3.up * knockback.upSpeed, appliedHitStop);
                }
            }

            // ★追加：やられ状態（行動不能）。攻撃者の技ごとの「やられ状態の時間」だけ動けなくする。
            //   ※CPU(Enemy)の攻撃は未対応（Enemy.csにGetCurrentStunDuration()相当が必要）。
            if (isEnemyPlayerAttack)
            {
                ApplyStun(enemyPlayer.GetCurrentStunDuration(), HP - attackerAtk <= 0);
            }

            Vector3 hitPoint = collision.ClosestPoint(collision.transform.position);
            ParticleSystem hitParticle = Instantiate(Hit_particle, hitPoint, Quaternion.Euler(-90f, 0f, 0f));
            hitParticle.Play();
            Destroy(hitParticle.gameObject, 1.0f);
            EffectDLog($"[{PlayerName}] 被弾パーティクル発生 pos={hitPoint}");

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
                EffectDLog($"[{PlayerName}] 被弾擬音エフェクト発生 pos={hitPoint}");
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
        // ★修正：以前は "= 10" で固定値に戻しており、攻撃の種類ごとの攻撃力や
        //   漢気ゲージによる補正まで消えてしまっていた。ここではガード上昇分だけを
        //   取り消し、ゲージ補正込みの本来の攻撃力に戻す。
        if (isEnemyPlayerAttack) enemyPlayer.ResetAtkAfterHit();
        else if (enemy != null) enemy.atk = 10; // ※Enemy側は未対応。同様の仕組みが必要なら要相談。

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
    // ★増減のタイミングまとめ：
    //   増加：NotifyAttackLanded()（攻撃命中時） / OnTriggerEnterのガード成功時
    //   減少：ApplyGaugeLossIfRetreating()（相手から離れる方向へ移動中、1秒あたり） / 必殺技発動時

    //漢気ゲージを増やす(上限は1本分の合計値でクランプ)
    public void AddKankiGauge(float amount)
    {
        float max = kankiGaugePerBar * kankiGaugeBarCount;
        float before = kankiGauge;
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
        atk = Mathf.RoundToInt(baseAtk * (1f + atkPowerPerBar * filledBars) * reviveMultiplier);

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
                Quaternion.Euler(90f, 0f, 0f),
                transform); // プレイヤーに追従させるため子オブジェクトにする
            activeKankiChargeEffect.transform.localPosition = kankiChargeEffectOffset;

            // ★Prefab側の設定に関係なく、ゲージが1本以上ある間は出し続けるようループを強制する。
            //   （Loop OFF・Stop Action=Destroy/Disableだと1回だけで終わってしまうため）
            foreach (var ps in activeKankiChargeEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                if (kankiChargeEffectFollowLocal) main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }

            activeKankiChargeEffect.Play();

            EffectDLog($"[{PlayerName}] 漢気ゲージ充填中エフェクト開始 pos={activeKankiChargeEffect.transform.position} / ゲージ={kankiGauge:F1}");
        }
        else
        {
            if (activeKankiChargeEffect == null) return;

            // 新規発生だけ止め、出ている分は自然にフェードアウトさせる
            activeKankiChargeEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(activeKankiChargeEffect.gameObject, activeKankiChargeEffect.main.startLifetime.constantMax + 0.5f);
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