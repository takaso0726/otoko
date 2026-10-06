using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//=====================================================
// 観客（客席）の反応を管理するスクリプト。
//
// ★Player.csは一切変更していません（変更禁止）。
//   このスクリプトが読み取る／購読するのは以下の情報・イベントです。
//     ・Player.Player_status（Player.cs既存のpublicフィールド。変更なし）
//     ・FightingCameraController.OnGuardImpactStart
//       （CameraController.cs側にのみ1行追加したイベント。
//        Player.cs側の「fightingCamera.OnGuardImpact(...)」呼び出し自体は
//        元々あったコードで、これも変更していません）
//     ・GameMNG.OnPlayerHpReduced
//       （GameMNG.cs側にのみ追加したイベント。Player_ReduceHP(hp, PlayerName)は
//        Player.cs側が元々呼んでいた既存メソッドで、そちらは変更していません）
//     ・Player.transform.position / Player.enemyPlayer / Player.enemy
//       （すべてPlayer.cs既存のpublicメンバー。変更なし）
//
// ★「後ろに下がり続けている（Retreat）」の検知について：
//   Player.cs内部には元々、相手から離れる方向へ移動中かどうかを判定するロジック
//   （移動方向ベクトルと「相手への方向」の内積が負なら後退、というもの）がありますが、
//   これはprivateメソッド(ApplyGaugeLossIfRetreating)内に閉じており、外部からは
//   直接呼び出せません。Player.cs非変更の制約上イベント化もできないため、
//   本スクリプトでは同じ考え方を、Player.cs既存のpublicメンバーだけを使って
//   このスクリプト側で毎フレーム独自に再計算しています：
//     1. targetPlayers[i].transform.position の前フレームからの差分で「今の移動方向」を求める
//     2. targetPlayers[i].enemyPlayer（対人戦）または .enemy（対CPU戦）のtransform.positionから
//        「相手への方向」を求める
//     3. 両者の内積が負＝後退中とみなし、retreatDetectionDuration秒以上連続したら
//        AudienceSituation.Retreatを発火する
//
// ★アニメーション制御は PeopleAnimController.controller の実際の構成に合わせています。
//   このコントローラーは Trigger ではなく "Bool" パラメータで動く作りです：
//     - isChangeble : 現在の反応を先(main→over→Idleへ)へ進めてよいかどうかのフラグ
//     - goClap / goCool / goCall : Idleからそれぞれの反応(拍手/クール系/コール)へ入るフラグ
//   Idle → (goX=true) → start → (isChangeble=true) → main → (isChangeble=true) → over → Idle
//   という一方通行の流れになっているため、このスクリプト内部で
//   「goXをtrueにする→実際にstartへ遷移したことを確認する→isChangebleをtrueにする
//    →Idleに戻るまで待つ→両方falseに戻す」という一連の手順をコルーチンで自動的に行っています。
//   Inspector側では、状況(situation)ごとに「Clap/Cool/Callのどれを再生するか」を
//   選ぶだけで済むようにしてあります（複数選ぶとランダムでどれか1つが再生される）。
//
// ★観客キャラクターは複数体登録できます（Audience Animators）。
//   各キャラクターは完全に独立して状態管理されており、同じ状況(situation)が
//   発生しても「今すぐ反応できるか」「どのClap/Cool/Callを再生するか」は
//   キャラごとに別々に抽選されます。そのため、全員が同じタイミング・同じ反応で
//   ピッタリ揃って再生されることはなく、客席らしいバラつきのある見た目になります。
//
// 使い方：
//   1. 観客キャラクター（Animator付き。PeopleAnimControllerをセットしたもの）を
//      Audience Animators に必要な数だけ登録する（1体でも複数体でも可）。
//      このスクリプト自体は、観客キャラクターとは別の管理用オブジェクトに
//      アタッチしてもよい。
//   2-a. GameMNGがシーンにあるなら、Inspectorの Game MNG にドラッグするだけでOK。
//        p1・p2を自動的に監視対象へ登録する（Target Playersは空のままでよい）。
//   2-b. GameMNGを使わない場合は、Target Players に直接Playerを登録する。
//   3. Camera Controller に、シーン上のFightingCameraControllerをセットする
//      （これが「仁王立ちで実際に攻撃を受け止めた瞬間」の反応に必要）。
//   4. Reactions リストで、状況ごとに再生したい反応(Clap/Cool/Call/待機（Idle）)を選ぶ。
//      Waiting（仁王立ち・ダメージ等、他の状況が何も起きていない待機時間）も
//      他の状況と同じ形式で登録できる。実際に発火する間隔は
//      Enable Idle Variations / Idle Variation Interval Range で設定する
//      （こちらもキャラごとに独立した間隔でランダム発火する）。
//   5. Priority Situations で、他の反応中でも取りこぼしたくない状況
//      （仁王立ち成功＝GuardBlock/GuardBlockBig、攻撃ヒット＝AttackHit）を登録する。
//   6. Retreat（後ろに下がり続け）の検知は、対戦相手（enemyPlayer/enemy）が
//      設定されていれば自動的に動作する。発火までの連続後退秒数は
//      Retreat Detection Duration、ノイズ除去用の最低速度はRetreat Min Speedで調整できる。
//
// ★必殺技（Special）の検知について：
//   Player.cs内の必殺技発動(EnterSpecial)は外部へ通知されず、状態(currentState)もprivateなので、
//   Player.cs既存のpublicメンバー（GetKankiGauge / specialRequiredGauge / specialGaugeCost）から
//   「漢気ゲージが必殺技の消費量ぶん1フレームで一括して減った」ことを検知して発火している。
//   （漢気復活もゲージを一括消費するが、ダウン中(Reborn)のため除外している）
//
// 注意：
//   ・仁王立ちの「構えに入った瞬間」自体はPlayer.cs内部のprivateな状態
//     （isGuarding）でしか管理されておらず、Player.cs非変更の制約上は
//     外部から検知できません。そのため本スクリプトでは、実際に攻撃を
//     受け止めた瞬間（ガードインパクト＝CameraController.OnGuardImpactStart）
//     を「仁王立ちの反応どころ」として使っています。
//   ・HP消耗によるダウン／死亡はPlayer.cs側で元々Status.Reborn／Status.Dead
//     がセットされるので、追加変更なしでそのまま検知できます。
//=====================================================

// 観客が反応する「状況」の一覧。今後増やしたい場合はここに追加するだけでよい。
public enum AudienceSituation
{
    AttackHit,        // 攻撃を当てた（ガードされていない通常ヒット・投げ含む）
    GuardBlock,       // 仁王立ちで攻撃を受け止めた（通常）
    GuardBlockBig,    // 仁王立ちで攻撃を受け止めた（連続ガードでヒートアップ）
    KnockDown,        // HPが0になりダウン（根性復活チャレンジ中）
    Dead,             // 復活失敗・決着
    Revive,           // 根性復活に成功
    Waiting,          // 仁王立ち・ダメージ等、他の状況が何も起きていない待機時間（一定間隔で発火）
    Retreat,          // プレイヤーが一定時間、相手から離れる方向へ移動し続けた（消極的な展開への野次）
    Special,          // 必殺技を出した（漢気ゲージを消費して必殺技を発動した瞬間）
}

// 観客がカメラのどこを基準に向くか
public enum AudienceFacingMode
{
    [InspectorName("カメラの位置を向く")]
    CameraPosition,   // 各観客からカメラ位置へ向く（観客ごとに微妙に角度が違う。立体感が出る）
    [InspectorName("カメラの画面方向を向く")]
    CameraViewPlane,  // カメラの向きの逆方向（画面に正対する向き）へ全員が向く。カメラが横移動すると全員が同じ角度だけ回る
}

// PeopleAnimController.controller が実際に持っているアニメーション反応。
// 待機（Idle）＝Noneは「何も再生せず、Idleのまま反応しない」ことを表す。
// SituationReaction.clipReactions に他の反応と一緒に登録しておくと、
// ランダム抽選の中に「あえて反応しない（待機のまま）」確率を混ぜることができる。
// 例：clipReactions = { Call, Call, Idle } → 2/3の確率でCall、1/3は無反応のまま
public enum AudienceClipReaction
{
    [InspectorName("待機（Idle）")]
    None,
    Clap,      // 拍手
    Cool,      // クール系の反応（やれやれ、感心 等）
    Call,      // 声援・コール

    // ↓PeopleAnimController.controllerに新しく追加されたBoolパラメータに対応
    [InspectorName("がっかり待機（Sad Idle）")]
    SadIdle,   // goSad_Idle
    Rage,      // goRage        怒り・激高
    Nervous,   // goNervous     動揺・不安
    [InspectorName("悪い予感（Bad Sign）")]
    BadSign,   // goBad_Sign
    Angry,     // goAngry       怒り
    Sadness,   // goSadness     悲しみ・落胆
    Cheers,    // goCheers      歓声・大盛り上がり
}

// 1つの状況に対して、再生候補の反応(Clap/Cool/Call/待機)を複数登録できる設定。
// 複数登録した場合は、その中からランダムで1つが再生される。
// 「待機（Idle）」も選択肢に含められる（あえて反応させたくない確率を作りたい場合に使う）。
[Serializable]
public class SituationReaction
{
    public AudienceSituation situation;

    [Tooltip("この状況で再生する反応。複数登録するとランダムで1つ再生される（「待機（Idle）」を混ぜると反応しない確率を作れる）")]
    public AudienceClipReaction[] clipReactions;

    [Tooltip("この状況で鳴らすSE(効果音)。複数登録するとランダムで1つ再生される。" +
             "配列内の要素をNone(未設定/空欄)のままにしておくと、その抽選のときだけ" +
             "SEを鳴らさない、という選択肢を混ぜることもできる。空配列のままならSEは一切鳴らさない。")]
    public AudioClip[] seClips;
}

public class AudienceController : MonoBehaviour
{
    [Header("GameMNGから自動取得（推奨・省略可）")]
    [Tooltip("設定すると、Start時にp1・p2を自動でTarget Playersへ登録する")]
    public GameMNG gameMNG;

    [Header("監視対象のプレイヤー")]
    [Tooltip("GameMNGを使わない場合はここに直接登録する（1人でも複数でも可）")]
    public Player[] targetPlayers;

    [Header("観客アニメーター")]
    [Tooltip("観客側のAnimatorコンポーネント（PeopleAnimControllerをセットしたもの）を必要な数だけ登録する。" +
             "複数登録した場合、各キャラは完全に独立して状態管理され、同じ状況が起きても" +
             "反応できるタイミングも再生される反応の種類もバラバラになる（客席らしい自然な見た目になる）")]
    public Animator[] audienceAnimators;

    [Header("アニメーションしない観客（向きだけカメラに追従）")]
    [Tooltip("Animatorを使わない（反応アニメを再生しない）観客のTransformを登録する。\n" +
             "ここに登録した観客は、Clap/Cool/Call等の反応は一切再生せず、向きのカメラ追従だけを行う。\n" +
             "Animatorを持っていてもここに登録すれば「アニメしない観客」として扱える（Audience Animatorsには入れないこと）")]
    public Transform[] staticAudiences;

    [Tooltip("ONなら、アニメしない観客もカメラの方を向かせる。OFFなら配置したままの向きで固定する")]
    public bool staticAudiencesFaceCamera = true;

    [Header("カメラ連携（仁王立ち反応に必須）")]
    [Tooltip("仁王立ちで実際に攻撃を受け止めた瞬間(ガードインパクト)を検知するために使う")]
    public FightingCameraController cameraController;

    [Tooltip("この連続ガード成功回数(comboCount)以上でGuardBlockBig状況として扱う")]
    public int bigCheerComboThreshold = 3;

    [Header("状況ごとの反応設定")]
    [Tooltip("状況(situation)ごとに、再生したい反応(Clap/Cool/Call/待機（Idle）)を選ぶ。複数選ぶとランダム再生")]
    public List<SituationReaction> reactions = new List<SituationReaction>
    {
        new SituationReaction { situation = AudienceSituation.AttackHit,    clipReactions = new[] { AudienceClipReaction.Call } },
        new SituationReaction { situation = AudienceSituation.GuardBlock,    clipReactions = new[] { AudienceClipReaction.Clap } },
        new SituationReaction { situation = AudienceSituation.GuardBlockBig, clipReactions = new[] { AudienceClipReaction.Call } },
        new SituationReaction { situation = AudienceSituation.KnockDown,     clipReactions = new[] { AudienceClipReaction.Cool } },
        new SituationReaction { situation = AudienceSituation.Dead,         clipReactions = new[] { AudienceClipReaction.Cool } },
        new SituationReaction { situation = AudienceSituation.Revive,       clipReactions = new[] { AudienceClipReaction.Call } },
        new SituationReaction { situation = AudienceSituation.Waiting,     clipReactions = new[] { AudienceClipReaction.Cool, AudienceClipReaction.None } },
        new SituationReaction { situation = AudienceSituation.Special,     clipReactions = new[] { AudienceClipReaction.Cheers } },
        new SituationReaction { situation = AudienceSituation.Retreat,     clipReactions = new[] { AudienceClipReaction.BadSign, AudienceClipReaction.Sadness } },
    };

    [Header("SE設定")]
    [Tooltip("状況ごとのSE(効果音)を再生するためのAudioSource。ここに1つセットする。" +
             "SEの再生中に別のSEが発生した場合は、再生中のSEを徐々に小さくしながら(フェードアウト)、" +
             "新しいSEをすぐに再生する。フェードアウト用に、実行時にこのAudioSourceと同じ設定の" +
             "AudioSourceが同じGameObjectへ1つ自動追加される。" +
             "（BGMは別のAudioSourceで管理すること）。")]
    public AudioSource seAudioSource;

    [Min(0f)]
    [Tooltip("SEの再生中に別のSEが発生したとき、直前のSEが完全に消えるまでの秒数。0なら即座に止める。")]
    public float seFadeOutDuration = 0.5f;

    [Tooltip("ここに登録した状況のSEは、再生中に「ここに登録されていない状況」のSEで中断（フェードアウト）されない。\n" +
             "そのSEの再生が終わるまで、登録されていない状況のSEは鳴らさずスキップする。\n" +
             "登録された状況どうしのSEは、通常どおり新しい方に切り替わる（前の方がフェードアウト）。\n" +
             "例：必殺技(Special)のSEが、直後の攻撃ヒット(AttackHit)のSEにかき消されるのを防ぐ。")]
    public AudienceSituation[] seProtectedSituations = new[] { AudienceSituation.Special };

    [Range(0f, 1f)]
    [Tooltip("SE再生時の音量スケール(0〜1)。AudioSource自体のVolumeとは別に、SEだけをまとめて音量調整したい場合に使用する。")]
    public float seVolume = 1f;

    [Header("優先して再生したい状況")]
    [Tooltip("ここに登録した状況（仁王立ち成功＝GuardBlock/GuardBlockBig、攻撃ヒット＝AttackHit）は、" +
             "そのキャラがWaiting等の反応で再生中で今すぐ反応できない場合でも取りこぼさない。" +
             "そのキャラの再生中の反応がIdleに戻り次第、優先的に（他の状況より先に）再生される。" +
             "※PeopleAnimControllerはIdleからしか次の反応へ入れない一方通行の作りのため、" +
             "再生中のアニメーションを即座に中断して割り込ませることはしない（中断するとAnimator側の状態が崩れるため）。" +
             "あくまで「今すぐは無理でも、Idleに戻り次第、他の状況より先に確実に再生する」という予約制の優先度（キャラごとに独立して管理）。")]
    public AudienceSituation[] prioritySituations = new[]
    {
        AudienceSituation.AttackHit,
        AudienceSituation.GuardBlock,
        AudienceSituation.GuardBlockBig,
        AudienceSituation.Special,
    };

    [Header("PeopleAnimController側のパラメータ名（通常は変更不要）")]
    [Tooltip("main→over→Idleへ進めてよいかを示すBoolパラメータ名")]
    public string isChangebleParamName = "isChangeble";
    public string goClapParamName = "goClap";
    public string goCoolParamName = "goCool";
    public string goCallParamName = "goCall";

    [Tooltip("PeopleAnimController.controllerに新しく追加されたBoolパラメータ名（実際のパラメータ名と完全一致させること）")]
    public string goSadIdleParamName = "goSad_Idle";
    public string goRageParamName = "goRage";
    public string goNervousParamName = "goNervous";
    public string goBadSignParamName = "goBad_Sign";
    public string goAngryParamName = "goAngry";
    public string goSadnessParamName = "goSadness";
    public string goCheersParamName = "goCheers";

    [Tooltip("Idle状態の名前（Animator上のステート名と合わせる）")]
    public string idleStateName = "Idle";

    [Header("後退（下がり続け）検知設定")]
    [Tooltip("この秒数以上、相手から離れる方向へ移動し続けたらRetreat状況を発生させる")]
    public float retreatDetectionDuration = 2.5f;

    [Tooltip("これ未満の移動速度(m/秒)はノイズとして無視し、後退判定に含めない")]
    public float retreatMinSpeed = 0.05f;

    [Tooltip("Retreat状況を再発火させるまでのクールダウン(秒)。0にすると、条件を満たすたびdetectionDuration分の連続後退のみで即座に再発火する")]
    public float retreatRetriggerCooldown = 0f;

    [Header("必殺技（Special）検知設定")]
    [Tooltip("ONなら、プレイヤーが必殺技を出した瞬間にAudienceSituation.Specialを発生させる。\n" +
             "Player.csは変更できないため、漢気ゲージが必殺技の消費量(Special Gauge Cost)ぶん一気に減った瞬間を" +
             "必殺技の発動として検知している（漢気復活によるゲージ消費は除外する）。")]
    public bool enableSpecialDetection = true;

    [Tooltip("必殺技を検知した瞬間に再生するSE。ここに1つ以上登録すると、Reactionsの「Special」行のSe Clipsより優先して使われる。\n" +
             "複数登録した場合はランダムで1つ再生する（空要素=None を混ぜると、その回だけ鳴らさない）。\n" +
             "空のままなら、Reactionsの「Special」行のSe Clipsを使う。")]
    public AudioClip[] specialSeClips;

    [Header("待機中（Waiting）演出の間隔設定")]
    [Tooltip("何も状況が起きていない待機中に、AudienceSituation.Waitingの反応をランダム再生する機能を有効にするか")]
    public bool enableIdleVariations = false;

    [Tooltip("次のWaiting反応を再生するまでの間隔（秒）の範囲。x=最小、y=最大（キャラごとに個別に抽選される）")]
    public Vector2 idleVariationIntervalRange = new Vector2(8f, 20f);

    [Tooltip("Idleに戻らなかった場合の保険（秒）。この時間が経ったら強制的にパラメータをリセットする")]
    public float reactionSafetyTimeout = 10f;

    [Header("カメラ追従（向き）設定")]
    [Tooltip("ONにすると、観客キャラクターの向きをカメラの方向へ追従させる")]
    public bool faceCamera = true;

    [Tooltip("向きの基準にするカメラ。未設定ならCamera Controllerのカメラ→Camera.mainの順で自動取得する")]
    public Transform cameraTarget;

    [Tooltip("カメラのどこを基準に向くか。\n" +
             "カメラの位置を向く：各観客からカメラ位置へ向く（遠いとカメラ移動による角度変化が小さい）\n" +
             "カメラの画面方向を向く：カメラの向きの逆方向へ全員が向く（カメラが動いた分だけ確実に向きが変わる）")]
    public AudienceFacingMode facingMode = AudienceFacingMode.CameraPosition;

    [Tooltip("ONなら水平方向（Y軸回転）のみ追従する。観客が上下に傾かないので通常はONのままでよい")]
    public bool yawOnly = true;

    [Tooltip("向きを変える速さ（度/秒）。0以下にすると即座にカメラの方を向く")]
    public float faceCameraTurnSpeed = 180f;

    [Tooltip("モデルの正面がZ+方向でない場合の補正角（度）。背中を向けてしまう場合は180を入れる")]
    public float faceCameraYawOffset = 0f;

    [Tooltip("全員が同じ向きにならないよう、キャラごとにランダムで加える角度のばらつき（±度）。起動時に1回だけ抽選される")]
    public float faceCameraRandomYaw = 0f;

    [Header("デバッグ")]
    [Tooltip("反応の発火状況をConsoleに出力する（原因調査用）")]
    public bool enableDebugLog = false;

    // 観客キャラクター1体分の再生状態を管理する内部クラス。
    // audienceAnimatorsの各Animatorに対して1つずつ生成し、
    // 「今そのキャラが反応を再生中か」「そのキャラに予約中の優先状況があるか」を
    // キャラごとに完全に独立して管理する。
    private class AudienceUnit
    {
        public Animator animator;
        public Coroutine currentReactionCoroutine;
        public AudienceSituation? pendingPrioritySituation;
        public float facingYawJitter; // カメラ向きのキャラ別ばらつき（度）
    }

    // アニメしない観客1体分の状態（向きのばらつきだけを持つ）
    private class StaticUnit
    {
        public Transform transform;
        public float facingYawJitter;
    }

    private List<StaticUnit> staticUnits = new List<StaticUnit>();

    // audienceAnimators(Inspector設定)から、null除外の上で構築されるキャラごとの状態リスト
    private List<AudienceUnit> audienceUnits = new List<AudienceUnit>();

    // reactionsリストを毎回線形探索しないよう、起動時に辞書化しておく
    private Dictionary<AudienceSituation, AudienceClipReaction[]> reactionMap;

    // reactionsリスト(Inspector設定)のseClipsを、起動時に状況ごとの辞書化しておく
    private Dictionary<AudienceSituation, AudioClip[]> seMap;

    // 各プレイヤーの「直前フレームでのステータス」を覚えておくための配列。
    // これと現在の値を比較することで、「変化した瞬間」だけを検知する。
    private Player.Status[] previousStatus;

    // targetPlayers[i]ごとの「後退（相手から離れる方向への移動）」検知用の内部状態。
    // Player.csは変更していないため、transform.position等の既存public情報から
    // このスクリプト側で毎フレーム独自に追跡している。
    private Vector3[] previousPlayerPositions;
    private float[] retreatTimers;
    private float[] retreatCooldownTimers;

    // targetPlayers[i]ごとの「直前フレームでの漢気ゲージ量」。必殺技発動（ゲージの一括消費）の検知に使う。
    private float[] previousKankiGauges;

    // 監視対象Playerの再チェック用（Playerがゲーム開始後に生成・差し替えされても追従するため）
    private float targetResolveTimer;
    private bool autoFindWarned;

    // 直近でガードインパクト(仁王立ちブロック)が発生したフレーム番号。
    // GameMNG.OnPlayerHpReduced由来のダメージイベントが同じフレームで来た場合、
    // それはガードで減ったHPなので「攻撃ヒット」の反応とは重複させない。
    private int lastGuardImpactFrame = -1;

    // prioritySituations(Inspector設定)を高速判定用にHashSet化したもの
    private HashSet<AudienceSituation> prioritySituationSet;

    // SE再生用AudioSourceに元々設定されているVolume。
    // seVolumeは「このAudioSource自体のVolumeとは別に、SEだけをまとめて音量調整したい場合に使う」
    // スケール値なので、Play()に切り替えた後も同じ意味を保てるよう起動時の値を控えておく。
    private float seAudioSourceBaseVolume = 1f;

    // SEのクロスフェード用。seAudioSource（A）と、実行時に自動追加する2本目（B）を交互に使う。
    private AudioSource seAudioSourceB;
    private AudioSource seCurrentSource;                       // 今鳴っている（直近に再生した）側
    private AudienceSituation seCurrentSituation;              // 今鳴っている（直近に再生した）SEの状況
    private readonly Dictionary<AudioSource, Coroutine> seFadeCoroutines = new Dictionary<AudioSource, Coroutine>();

    void Awake()
    {
        BuildReactionMap();
        BuildSeMap();
        BuildPrioritySituationSet();

        if (seAudioSource != null)
        {
            seAudioSourceBaseVolume = seAudioSource.volume;
        }
    }

    // ★Inspectorで保存済みのReactionsリストには、後から追加した状況(例：Special)の行が存在しないため、
    //   そのままではSEや反応を登録できない（登録先の行が無い）。
    //   このメソッドは、AudienceSituationの中でReactionsに行が無いものを末尾へ追加する。
    //   ・自動では実行されない（行を「－」で消しても勝手に復活しないようにするため）。
    //   ・使い方：Inspector上でこのコンポーネントの右上「︙」メニュー →
    //     「Reactionsに足りない状況の行を追加」を実行する。
    //   ・既存の行・設定内容には一切触れない。追加される行の反応は空（何も再生しない）。
    //     ただしSpecialだけは歓声(Cheers)を初期値にする。
    [ContextMenu("Reactionsに足りない状況の行を追加")]
    void AddMissingSituationRows()
    {
        if (reactions == null) reactions = new List<SituationReaction>();

        int added = 0;
        foreach (AudienceSituation sit in Enum.GetValues(typeof(AudienceSituation)))
        {
            bool exists = false;
            foreach (var r in reactions)
            {
                if (r != null && r.situation == sit) { exists = true; break; }
            }
            if (exists) continue;

            reactions.Add(new SituationReaction
            {
                situation = sit,
                clipReactions = sit == AudienceSituation.Special
                    ? new[] { AudienceClipReaction.Cheers }
                    : Array.Empty<AudienceClipReaction>(),
                seClips = Array.Empty<AudioClip>(),
            });
            added++;
        }

        Debug.Log($"[AudienceController] Reactionsに足りない状況の行を{added}件追加しました。({gameObject.name})");
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // reactionsリスト(Inspector設定)からDictionaryを構築する
    void BuildReactionMap()
    {
        reactionMap = new Dictionary<AudienceSituation, AudienceClipReaction[]>();
        foreach (var r in reactions)
        {
            if (r == null) continue;
            reactionMap[r.situation] = r.clipReactions ?? Array.Empty<AudienceClipReaction>();
        }
    }

    // reactionsリスト(Inspector設定)のseClipsからDictionaryを構築する
    void BuildSeMap()
    {
        seMap = new Dictionary<AudienceSituation, AudioClip[]>();
        foreach (var r in reactions)
        {
            if (r == null) continue;
            seMap[r.situation] = r.seClips ?? Array.Empty<AudioClip>();
        }
    }

    // prioritySituationsリスト(Inspector設定)からHashSetを構築する
    void BuildPrioritySituationSet()
    {
        prioritySituationSet = new HashSet<AudienceSituation>(prioritySituations ?? Array.Empty<AudienceSituation>());
    }

    void Start()
    {
        // 監視対象のPlayerを確定する（prefabアセットは除外し、シーン上の実体だけを使う）
        ResolveTargetPlayers(true);

        // audienceAnimators(Inspector設定)から、キャラごとの状態管理オブジェクトを構築する。
        // nullが混ざっていても無視して続行する。
        audienceUnits = new List<AudienceUnit>();
        if (audienceAnimators != null)
        {
            foreach (var animator in audienceAnimators)
            {
                if (animator == null) continue;
                audienceUnits.Add(new AudienceUnit
                {
                    animator = animator,
                    facingYawJitter = UnityEngine.Random.Range(-faceCameraRandomYaw, faceCameraRandomYaw),
                });
            }
        }

        // アニメしない観客（向きのカメラ追従のみ）を構築する。
        // Audience Animatorsと重複して登録されていたら、反応アニメを優先して除外する。
        staticUnits = new List<StaticUnit>();
        if (staticAudiences != null)
        {
            foreach (var tf in staticAudiences)
            {
                if (tf == null) continue;
                if (IsAnimatedAudience(tf))
                {
                    Debug.LogWarning($"[AudienceController] {tf.name} はAudience Animatorsにも登録されているため、Static Audiencesの登録は無視します。({gameObject.name})");
                    continue;
                }
                staticUnits.Add(new StaticUnit
                {
                    transform = tf,
                    facingYawJitter = UnityEngine.Random.Range(-faceCameraRandomYaw, faceCameraRandomYaw),
                });
            }
        }

        // ★起動時に1回だけ、SE・必殺技まわりの設定状況をConsoleへ出す（Enable Debug LogがOFFでも出る）。
        //   「最新のスクリプトが動いているか」「SEの設定が読み込まれているか」の確認用。
        {
            int specialRowSeCount = 0;
            bool hasSpecialRow = false;
            if (reactions != null)
            {
                foreach (var r in reactions)
                {
                    if (r != null && r.situation == AudienceSituation.Special)
                    {
                        hasSpecialRow = true;
                        specialRowSeCount += r.seClips != null ? r.seClips.Length : 0;
                    }
                }
            }
            Debug.Log($"[AudienceController] 起動確認({gameObject.name}): targetPlayers={targetPlayers.Length}体 / " +
                      $"SE AudioSource={(seAudioSource != null ? seAudioSource.name : "未設定")} / " +
                      $"Special検知={(enableSpecialDetection ? "ON" : "OFF")} / Special Se Clips={(specialSeClips != null ? specialSeClips.Length : 0)}個 / " +
                      $"Reactionsのspecial行={(hasSpecialRow ? "あり(Se Clips " + specialRowSeCount + "個)" : "なし")} / " +
                      $"Enable Debug Log={(enableDebugLog ? "ON" : "OFF")}");

            // 監視対象の各Playerについて、必殺技の検知に使う値を出す。
            // ・シーン上のオブジェクトか（Projectビューのprefabそのものを登録していると、動かない別物を見てしまい検知できない）
            // ・Special Required Gauge / Special Gauge Cost（検知の判定に使う値）
            for (int i = 0; i < targetPlayers.Length; i++)
            {
                var tp = targetPlayers[i];
                if (tp == null)
                {
                    Debug.Log($"[AudienceController] 起動確認: targetPlayers[{i}] は未設定(None)です。");
                    continue;
                }
                Debug.Log($"[AudienceController] 起動確認: targetPlayers[{i}] = {tp.name} / " +
                          $"シーン上のオブジェクト={(tp.gameObject.scene.IsValid() ? "はい" : "いいえ(prefab等。これだと検知できません)")} / " +
                          $"Special Required Gauge={tp.specialRequiredGauge:F1} / Special Gauge Cost={tp.specialGaugeCost:F1} / " +
                          $"現在のゲージ={tp.GetKankiGauge():F1}");
            }
        }

        // ★追加：主要な参照が未設定だと「エラーは出ないが何も反応しない」状態になり
        //   原因調査がしづらいので、起動時にConsoleへ警告を出しておく。
        if (audienceUnits.Count == 0)
        {
            Debug.LogWarning($"[AudienceController] Audience Animatorsが1体も設定されていません。({gameObject.name}) 反応アニメーションは一切再生されません。");
        }
        if (gameMNG == null)
        {
            Debug.LogWarning($"[AudienceController] Game MNGが未設定です。({gameObject.name}) AttackHit状況が発火しません。また Target Players の自動登録も行われません。");
        }
        if (cameraController == null)
        {
            Debug.LogWarning($"[AudienceController] Camera Controllerが未設定です。({gameObject.name}) GuardBlock/GuardBlockBig状況が発火しません。");
        }
        if (targetPlayers.Length == 0)
        {
            Debug.LogWarning($"[AudienceController] 監視対象のPlayerが1体も登録されていません。({gameObject.name}) KnockDown/Dead/Revive状況が発火しません。");
        }
        if (seAudioSource == null && HasAnySeClipConfigured())
        {
            Debug.LogWarning($"[AudienceController] SE Audio Sourceが未設定です。({gameObject.name}) reactionsにSEが設定されていますが再生されません。");
        }

        // 待機中（Waiting）演出のループを、キャラごとに独立して開始する
        // （enableIdleVariationsは実行時にトグル可能。間隔もキャラごとに別々に抽選される）
        foreach (var unit in audienceUnits)
        {
            StartCoroutine(IdleVariationLoop(unit));
        }
    }

    void OnEnable()
    {
        if (cameraController != null)
        {
            cameraController.OnGuardImpactStart += HandleGuardImpact;
        }
        if (gameMNG != null)
        {
            gameMNG.OnPlayerHpReduced += HandlePlayerDamaged;
        }
    }

    void OnDisable()
    {
        if (cameraController != null)
        {
            cameraController.OnGuardImpactStart -= HandleGuardImpact;
        }
        if (gameMNG != null)
        {
            gameMNG.OnPlayerHpReduced -= HandlePlayerDamaged;
        }
    }

    //-----------------------------------------------------------------------
    // 監視対象Playerの確定
    //-----------------------------------------------------------------------

    // ProjectビューのprefabアセットをInspectorに登録してしまうと、ゲーム中に動いている実体とは別物なので
    // ゲージ・状態・位置が一切変化せず、必殺技などの検知が働かない。そのため、シーン上の実体だけを使う。
    static bool IsSceneObject(Player p)
    {
        return p != null && p.gameObject.scene.IsValid();
    }

    // 優先順位：
    //  1. targetPlayersに登録された、シーン上のPlayer（prefabアセットやNoneは除外）
    //  2. gameMNG.p1 / p2（シーン上の実体のとき）
    //  3. 上記で1体も無ければ、シーン上のPlayerを自動検出
    // 結果が前回と変わった時だけ、targetPlayersを置き換えて追跡用の配列を作り直す。
    void ResolveTargetPlayers(bool isStart)
    {
        var list = new List<Player>();

        if (targetPlayers != null)
        {
            foreach (var tp in targetPlayers)
            {
                if (tp == null) continue;
                if (!IsSceneObject(tp))
                {
                    if (isStart)
                    {
                        Debug.LogWarning($"[AudienceController] Target Playersの「{tp.name}」はProjectビューのprefabアセットで、ゲーム中に動く実体ではないため除外しました。" +
                                         $"({gameObject.name}) HierarchyにあるPlayerオブジェクトを登録してください。");
                    }
                    continue;
                }
                if (!list.Contains(tp)) list.Add(tp);
            }
        }

        if (gameMNG != null)
        {
            if (IsSceneObject(gameMNG.p1) && !list.Contains(gameMNG.p1)) list.Add(gameMNG.p1);
            if (IsSceneObject(gameMNG.p2) && !list.Contains(gameMNG.p2)) list.Add(gameMNG.p2);
        }

        if (list.Count == 0)
        {
#if UNITY_2022_2_OR_NEWER
            var found = FindObjectsByType<Player>(FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<Player>();
#endif
            foreach (var f in found)
            {
                if (IsSceneObject(f) && !list.Contains(f)) list.Add(f);
            }

            if (list.Count > 0 && !autoFindWarned)
            {
                autoFindWarned = true;
                Debug.LogWarning($"[AudienceController] Target Players/Game MNGに有効なPlayerが無いため、シーン上のPlayerを自動検出しました（{list.Count}体）。" +
                                 $"({gameObject.name}) Inspectorで Target Players にHierarchyのPlayerを設定してください。");
            }
        }

        // 前回と同じ構成なら何もしない
        if (targetPlayers != null && targetPlayers.Length == list.Count)
        {
            bool same = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (targetPlayers[i] != list[i]) { same = false; break; }
            }
            if (same) return;
        }

        targetPlayers = list.ToArray();
        InitTargetTracking();

        if (!isStart)
        {
            var names = new List<string>();
            foreach (var tp in targetPlayers) names.Add(tp != null ? tp.name : "None");
            DLog($"[AudienceController] 監視対象のPlayerを更新しました: {targetPlayers.Length}体 ({string.Join(", ", names)})");
        }
    }

    // targetPlayersの構成に合わせて、状態・位置・ゲージなどの追跡用配列を作り直す
    void InitTargetTracking()
    {
        previousStatus = new Player.Status[targetPlayers.Length];
        previousPlayerPositions = new Vector3[targetPlayers.Length];
        retreatTimers = new float[targetPlayers.Length];
        retreatCooldownTimers = new float[targetPlayers.Length];
        previousKankiGauges = new float[targetPlayers.Length];
        for (int i = 0; i < targetPlayers.Length; i++)
        {
            if (targetPlayers[i] != null)
            {
                previousKankiGauges[i] = targetPlayers[i].GetKankiGauge();
                previousStatus[i] = targetPlayers[i].Player_status;
                previousPlayerPositions[i] = targetPlayers[i].transform.position;
            }
        }
    }

    void Update()
    {
        // 監視対象Playerを0.5秒ごとに再チェックする（変化があった時だけ内部状態を作り直す）
        targetResolveTimer -= Time.unscaledDeltaTime;
        if (targetResolveTimer <= 0f)
        {
            targetResolveTimer = 0.5f;
            ResolveTargetPlayers(false);
        }

        for (int i = 0; i < targetPlayers.Length; i++)
        {
            Player p = targetPlayers[i];
            if (p == null) continue;

            Player.Status current = p.Player_status;
            Player.Status prev = previousStatus[i];

            // 必殺技の発動検知（previousStatusを更新する前に行う）
            UpdateSpecialDetection(i, p, prev, current);

            // ステータスが変化した瞬間だけ反応させる（毎フレーム連打しないようにするため）
            if (current != prev)
            {
                HandleStatusChanged(prev, current);
                previousStatus[i] = current;
            }

            UpdateRetreatDetection(i, p);
        }
    }

    //-----------------------------------------------------------------------
    // 観客の向きをカメラへ追従させる
    //-----------------------------------------------------------------------

    // アニメーション適用後に向きを上書きするため、LateUpdateで処理する
    void LateUpdate()
    {
        if (!faceCamera) return;

        Transform cam = ResolveCameraTransform();
        if (cam == null) return;

        // アニメする観客（Audience Animators）
        if (audienceUnits != null)
        {
            foreach (var unit in audienceUnits)
            {
                if (unit == null || unit.animator == null) continue;
                FaceCameraTransform(unit.animator.transform, unit.facingYawJitter, cam);
            }
        }

        // アニメしない観客（Static Audiences）
        if (staticAudiencesFaceCamera && staticUnits != null)
        {
            foreach (var unit in staticUnits)
            {
                if (unit == null || unit.transform == null) continue;
                FaceCameraTransform(unit.transform, unit.facingYawJitter, cam);
            }
        }
    }

    // 指定のTransformが、Audience Animatorsのいずれかのキャラ（またはその子/親）かどうか
    bool IsAnimatedAudience(Transform tf)
    {
        if (audienceUnits == null) return false;
        foreach (var unit in audienceUnits)
        {
            if (unit != null && unit.animator != null && unit.animator.transform == tf) return true;
        }
        return false;
    }

    // 向きの基準となるカメラのTransformを返す（毎回nullチェックして、シーン切替等にも耐える）
    Transform ResolveCameraTransform()
    {
        if (cameraTarget != null) return cameraTarget;
        if (cameraController != null) return cameraController.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    // 1体の観客を、カメラの方向へ向ける（アニメする／しないの両方で共通）
    void FaceCameraTransform(Transform tf, float yawJitter, Transform cam)
    {
        Vector3 toCamera = facingMode == AudienceFacingMode.CameraViewPlane
            ? -cam.forward                    // カメラの向きの逆＝画面に正対する方向
            : cam.position - tf.position;     // 観客からカメラ位置への方向
        if (yawOnly) toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f) return; // 真上・真下などで向きが定まらない場合は何もしない

        Quaternion target = Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                          * Quaternion.Euler(0f, faceCameraYawOffset + yawJitter, 0f);

        if (faceCameraTurnSpeed <= 0f)
        {
            tf.rotation = target;
        }
        else
        {
            // Time.deltaTimeを使うため、ガード成功時のスロー演出(timeScale低下)中は向き変更もゆっくりになる
            tf.rotation = Quaternion.RotateTowards(tf.rotation, target, faceCameraTurnSpeed * Time.deltaTime);
        }
    }

    //-----------------------------------------------------------------------
    // 必殺技の発動検知 → Special反応
    //-----------------------------------------------------------------------

    // Player.csには必殺技の発動を通知するイベントが無く、変更もできないため、
    // 既存のpublicメンバー(GetKankiGauge / specialRequiredGauge / specialGaugeCost)だけで判定する。
    // 必殺技を出すと、漢気ゲージがspecialGaugeCostぶん1フレームで一括して減る。
    // ・直前フレームのゲージがspecialRequiredGauge以上だった
    // ・今フレームで「消費量ぶん」減った（後退によるじわじわした減少は1フレームでは小さいので該当しない）
    // ・直前/現在がダウン中(Reborn)・死亡ではない（漢気復活もゲージを一括消費するため、それは除外する）
    // を満たしたときだけ AudienceSituation.Special を発火する。
    void UpdateSpecialDetection(int index, Player p, Player.Status prevStatus, Player.Status currentStatus)
    {
        float before = previousKankiGauges[index];
        float now = p.GetKankiGauge();
        previousKankiGauges[index] = now;

        if (!enableSpecialDetection) return;
        if (p.specialGaugeCost <= 0f) return;

        float drop = before - now;
        if (drop <= 0.5f) return;

        // ここから先は「ゲージが1フレームで0.5以上減った」ケース（必殺技・漢気復活など）。
        // 必殺技として扱わなかった場合は、原因調査用に理由をログへ出す。
        if (before < p.specialRequiredGauge - 0.01f)
        {
            DLog($"[AudienceController] {p.name}：ゲージが{drop:F1}減ったが、直前のゲージ({before:F1})がSpecial Required Gauge({p.specialRequiredGauge:F1})未満のため必殺技とみなしません。");
            return;
        }
        if (drop < Mathf.Min(p.specialGaugeCost, before) - 0.5f)
        {
            DLog($"[AudienceController] {p.name}：ゲージが{drop:F1}減ったが、必殺技の消費量(Special Gauge Cost={p.specialGaugeCost:F1})に満たないため必殺技とみなしません。");
            return;
        }

        // 漢気復活（ダウン中のゲージ消費）は必殺技ではない
        if (prevStatus == Player.Status.Reborn || prevStatus == Player.Status.Dead
            || currentStatus == Player.Status.Reborn || currentStatus == Player.Status.Dead)
        {
            DLog($"[AudienceController] {p.name}：ゲージが{drop:F1}減ったが、ダウン/死亡中(Status {prevStatus}→{currentStatus})のため必殺技とみなしません（漢気復活など）。");
            return;
        }

        DLog($"[AudienceController] 必殺技の発動を検知: {p.name}（ゲージ {before:F1} → {now:F1}）");
        PlayReaction(AudienceSituation.Special);
    }

    //-----------------------------------------------------------------------
    // 後退（下がり続け）の検知 → Retreat反応
    //-----------------------------------------------------------------------

    // targetPlayers[i]（プレイヤーp）が、対戦相手から離れる方向へ
    // retreatDetectionDuration秒以上連続で移動し続けたかどうかを毎フレーム判定し、
    // 満たしたらAudienceSituation.Retreatを発火する。
    // Player.csは変更していないため、判定はすべてtransform.position等の
    // 既存public情報からこのスクリプト側で独自に再計算している。
    void UpdateRetreatDetection(int index, Player p)
    {
        Vector3 currentPos = p.transform.position;
        Vector3 delta = currentPos - previousPlayerPositions[index];
        previousPlayerPositions[index] = currentPos;

        // クールダウン中は時間を消化するだけで、判定・発火はスキップする
        if (retreatCooldownTimers[index] > 0f)
        {
            retreatCooldownTimers[index] -= Time.deltaTime;
        }

        // 決着後・ダウン中などは後退判定の対象外（動いていても野次らない）
        if (p.Player_status != Player.Status.Live)
        {
            retreatTimers[index] = 0f;
            return;
        }

        Transform opponentTf = GetOpponentTransform(p);
        if (opponentTf == null)
        {
            retreatTimers[index] = 0f;
            return;
        }

        delta.y = 0f;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = delta.magnitude / dt;

        bool isRetreatingThisFrame = false;
        if (speed >= retreatMinSpeed)
        {
            Vector3 toOpponent = opponentTf.position - currentPos;
            toOpponent.y = 0f;
            if (toOpponent.sqrMagnitude > 0.0001f)
            {
                // 移動方向と「相手への方向」の内積が負＝相手から離れる方向へ動いている
                // （Player.cs内部のApplyGaugeLossIfRetreatingと同じ考え方）
                float dot = Vector3.Dot(delta.normalized, toOpponent.normalized);
                isRetreatingThisFrame = dot < 0f;
            }
        }

        if (!isRetreatingThisFrame)
        {
            // 前進・停止・横移動などに切り替わったら、連続後退時間はリセットする
            retreatTimers[index] = 0f;
            return;
        }

        retreatTimers[index] += Time.deltaTime;
        if (retreatTimers[index] < retreatDetectionDuration) return;
        if (retreatCooldownTimers[index] > 0f) return; // クールダウン中は再発火しない

        DLog($"[AudienceController] [{p.PlayerName}] {retreatDetectionDuration}秒以上、相手から離れる方向へ移動し続けたためRetreat状況を発生させます");
        PlayReaction(AudienceSituation.Retreat);

        // 後退が続いていれば一定間隔ごとに繰り返し発火させたいので、時間だけリセットする
        // （retreatRetriggerCooldownを0より大きくすると、そのぶん再発火の間隔が空く）
        retreatTimers[index] = 0f;
        retreatCooldownTimers[index] = retreatRetriggerCooldown;
    }

    // 対人戦(enemyPlayer)／対CPU戦(enemy)どちらの場合でも、相手のTransformを取得する。
    // Player.cs側にある同名ロジック(private)と同じ考え方だが、
    // enemyPlayer/enemyはどちらもPlayer.cs既存のpublicフィールドなので、
    // Player.csを変更せずこのスクリプト側だけで参照できる。
    Transform GetOpponentTransform(Player p)
    {
        if (p.enemyPlayer != null) return p.enemyPlayer.transform;
        if (p.enemy != null) return p.enemy.transform;
        return null;
    }

    // ステータスの変化内容を、対応する状況(AudienceSituation)に変換して反応させる
    void HandleStatusChanged(Player.Status prev, Player.Status current)
    {
        DLog($"[AudienceController] Player_status変化を検知: {prev} -> {current}");

        switch (current)
        {
            case Player.Status.Reborn:
                // HPが0になり、根性復活チャレンジ（ダウン中）に入った瞬間
                PlayReaction(AudienceSituation.KnockDown);
                break;

            case Player.Status.Dead:
                // 制限時間内に復活できず、決着がついた瞬間
                PlayReaction(AudienceSituation.Dead);
                break;

            case Player.Status.Live:
                // ダウン(Reborn)状態から生存(Live)に戻った＝根性復活成功
                if (prev == Player.Status.Reborn)
                {
                    PlayReaction(AudienceSituation.Revive);
                }
                break;
        }
    }

    //-----------------------------------------------------------------------
    // ダメージ関連イベントの受信 → AttackHit反応
    //-----------------------------------------------------------------------

    // CameraController.OnGuardImpactStartから呼ばれる：
    // 仁王立ちで実際に攻撃を受け止めた瞬間の反応。連続回数が多いほど盛り上げる。
    void HandleGuardImpact(Transform target, int comboCount)
    {
        DLog($"[AudienceController] OnGuardImpactStart受信 comboCount={comboCount}");

        // 同じフレームで発生するPlayer_ReduceHP由来のダメージイベントを、
        // 「攻撃ヒット」ではなく「ガードで減った分」として区別できるよう記録しておく
        lastGuardImpactFrame = Time.frameCount;

        var situation = comboCount >= bigCheerComboThreshold
            ? AudienceSituation.GuardBlockBig
            : AudienceSituation.GuardBlock;

        PlayReaction(situation);
    }

    // GameMNG.OnPlayerHpReducedから呼ばれる：
    // 攻撃が当たってHPが減った瞬間の反応（AttackHit）を再生する。
    void HandlePlayerDamaged(string playerName, int remainingHp)
    {
        DLog($"[AudienceController] OnPlayerHpReduced受信 player={playerName} remainingHp={remainingHp}");

        // 同じフレームでガードインパクトが発生していた場合（＝ガードで減ったHP）は、
        // GuardBlock側の反応と重複させないためAttackHitをスキップする
        if (WasGuardImpactThisFrame())
        {
            DLog("[AudienceController] 同フレームでガードイベントを検知済みのため、AttackHitはスキップします");
            return;
        }

        PlayReaction(AudienceSituation.AttackHit);
    }

    // 直近のガードインパクトが「今フレーム」発生したものかどうかを判定する
    bool WasGuardImpactThisFrame()
    {
        return Time.frameCount == lastGuardImpactFrame;
    }

    // reactionsのどこかに1つでもSEが設定されているかどうか（起動時の警告判定用）
    bool HasAnySeClipConfigured()
    {
        if (reactions == null) return false;
        foreach (var r in reactions)
        {
            if (r == null || r.seClips == null) continue;
            foreach (var clip in r.seClips)
            {
                if (clip != null) return true;
            }
        }
        return false;
    }

    // 指定した状況(situation)に対応するSEを再生する。
    // ・アニメ反応(clipReactions)の抽選/再生中判定とは完全に独立しており、
    //   観客キャラクター全員が反応中で今すぐアニメを再生できない場合でも、
    //   SE自体は状況が発生するたびに毎回再生される（客席の歓声・どよめきに相当するため）。
    // ・候補が複数ある場合はランダムで1つ再生する。候補にNone(未設定)を混ぜておくと、
    //   その抽選の回だけ「あえてSEを鳴らさない」という結果にもできる
    //   （この場合、再生中の前のSEはそのまま鳴り続ける）。
    // ・SEの再生中に別のSEが発生した場合は、再生中のSEをseFadeOutDuration秒かけて徐々に小さくし、
    //   新しいSEは待たずにそのまま再生する（2本のAudioSourceを交互に使ってクロスフェードする）。
    //   （BGMは別のAudioSourceで管理する想定のため、このルールの対象外）。
    void PlaySituationSE(AudienceSituation situation)
    {
        if (seMap == null) BuildSeMap();
        seMap.TryGetValue(situation, out var clips);

        // 必殺技専用のSE欄に登録があれば、Reactionsの「Special」行よりそちらを優先する
        if (situation == AudienceSituation.Special && specialSeClips != null && specialSeClips.Length > 0)
        {
            clips = specialSeClips;
        }

        if (clips == null || clips.Length == 0)
        {
            DLog($"[AudienceController] SE({situation})：Reactionsにこの状況の行が無い、またはSe Clips(必殺技はSpecial Se Clipsも)が空のため鳴らしません。");
            return;
        }

        if (seAudioSource == null)
        {
            Debug.LogWarning($"[AudienceController] SE Audio Sourceが未設定のため、SE({situation})を再生できません。");
            return;
        }

        // 保護対象の状況のSEが再生中で、新しいSEが保護対象外の状況なら、再生中のSEを守るためこのSEはスキップする
        if (seCurrentSource != null && seCurrentSource.isPlaying
            && IsSeProtected(seCurrentSituation) && !IsSeProtected(situation))
        {
            DLog($"[AudienceController] SE({situation})：保護対象のSE({seCurrentSituation})が再生中のためスキップしました。");
            return;
        }

        var chosen = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (chosen == null)
        {
            DLog($"[AudienceController] SE({situation})：抽選の結果「鳴らさない(None)」が選ばれました。");
            return; // 「あえて鳴らさない」が選ばれた場合
        }

        EnsureSeSources();

        var previous = seCurrentSource != null ? seCurrentSource : seAudioSource;
        var prevSituation = seCurrentSituation;
        var next = (previous == seAudioSource) ? seAudioSourceB : seAudioSource;

        // 直前のSEは徐々に小さくする
        if (previous.isPlaying)
        {
            StartSeFadeOut(previous);
        }

        // 次に使う側がまだ前々回のSEをフェード中なら、そのフェードを打ち切って再利用する
        StopSeFade(next);
        next.Stop();

        next.volume = seAudioSourceBaseVolume * seVolume;
        next.clip = chosen;
        next.Play();
        seCurrentSource = next;
        seCurrentSituation = situation;

        DLog($"[AudienceController] SE再生: {situation} -> {chosen.name}" +
             (previous.isPlaying ? $"（直前のSE({prevSituation})はフェードアウト）" : ""));
    }

    // 指定した状況が、seProtectedSituations（他のSEに中断されない状況）に登録されているか
    bool IsSeProtected(AudienceSituation situation)
    {
        return seProtectedSituations != null && Array.IndexOf(seProtectedSituations, situation) >= 0;
    }

    // フェード用の2本目のAudioSourceを（まだ無ければ）seAudioSourceと同じ設定で作る
    void EnsureSeSources()
    {
        if (seAudioSourceB != null) return;

        seAudioSourceB = seAudioSource.gameObject.AddComponent<AudioSource>();
        seAudioSourceB.playOnAwake = false;
        seAudioSourceB.loop = false;
        seAudioSourceB.outputAudioMixerGroup = seAudioSource.outputAudioMixerGroup;
        seAudioSourceB.mute = seAudioSource.mute;
        seAudioSourceB.bypassEffects = seAudioSource.bypassEffects;
        seAudioSourceB.bypassListenerEffects = seAudioSource.bypassListenerEffects;
        seAudioSourceB.bypassReverbZones = seAudioSource.bypassReverbZones;
        seAudioSourceB.ignoreListenerPause = seAudioSource.ignoreListenerPause;
        seAudioSourceB.priority = seAudioSource.priority;
        seAudioSourceB.pitch = seAudioSource.pitch;
        seAudioSourceB.panStereo = seAudioSource.panStereo;
        seAudioSourceB.spatialBlend = seAudioSource.spatialBlend;
        seAudioSourceB.reverbZoneMix = seAudioSource.reverbZoneMix;
        seAudioSourceB.dopplerLevel = seAudioSource.dopplerLevel;
        seAudioSourceB.spread = seAudioSource.spread;
        seAudioSourceB.rolloffMode = seAudioSource.rolloffMode;
        seAudioSourceB.minDistance = seAudioSource.minDistance;
        seAudioSourceB.maxDistance = seAudioSource.maxDistance;
    }

    void StartSeFadeOut(AudioSource source)
    {
        StopSeFade(source);

        if (seFadeOutDuration <= 0f)
        {
            source.Stop();
            return;
        }

        seFadeCoroutines[source] = StartCoroutine(SeFadeOutRoutine(source, seFadeOutDuration));
    }

    void StopSeFade(AudioSource source)
    {
        if (source == null) return;
        if (seFadeCoroutines.TryGetValue(source, out var co))
        {
            if (co != null) StopCoroutine(co);
            seFadeCoroutines.Remove(source);
        }
    }

    // 指定したAudioSourceのVolumeを現在値から0へ徐々に下げ、0になったらStopする。
    // （ポーズ(timeScale=0)中でも止まらないようunscaledDeltaTimeを使う）
    IEnumerator SeFadeOutRoutine(AudioSource source, float duration)
    {
        float startVolume = source.volume;
        float t = 0f;
        while (t < duration && source.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, t / duration);
            yield return null;
        }

        source.Stop();
        seFadeCoroutines.Remove(source);
    }

    // ★外部からも呼べる公開メソッド。
    //   指定した状況(situation)が発生したことを、登録済みの全観客キャラクターへ通知する。
    //   各キャラクターは完全に独立して「今すぐ反応できるか」「Clap/Cool/Callのどれを再生するか」を
    //   それぞれ別々に抽選するため、全員が同じタイミング・同じ反応で揃うことはない。
    //   他のスクリプトからも audienceController.PlayReaction(AudienceSituation.XXX) で呼び出せる。
    public void PlayReaction(AudienceSituation situation)
    {
        if (reactionMap == null) BuildReactionMap();
        if (seMap == null) BuildSeMap();
        if (prioritySituationSet == null) BuildPrioritySituationSet();

        // SEはアニメ反応(clipReactions)の設定/再生状況とは無関係に、状況が発生するたび毎回再生する
        PlaySituationSE(situation);

        if (!reactionMap.TryGetValue(situation, out var clips) || clips.Length == 0)
        {
            // Inspectorでその状況の設定自体が空の場合は、何も再生しない（エラーにはしない）
            // ※意図的に空にしているケースもあるためログは出さない
            return;
        }

        if (audienceUnits == null || audienceUnits.Count == 0) return;

        foreach (var unit in audienceUnits)
        {
            PlayReactionOnUnit(unit, situation, clips);
        }
    }

    // situationの発生を、指定した1体の観客キャラクター(unit)にだけ反映する内部処理。
    // ・そのキャラが既に別の反応を再生中の場合：
    //     優先状況(prioritySituations)なら「そのキャラの予約」として覚えておき、
    //     優先状況でなければそのキャラはこの機会をスキップする（他のキャラは影響を受けない）。
    // ・そのキャラがIdle中の場合：
    //     clipsの中からこのキャラ用に独立してランダム抽選して再生する。
    void PlayReactionOnUnit(AudienceUnit unit, AudienceSituation situation, AudienceClipReaction[] clips)
    {
        if (unit == null || unit.animator == null) return;

        if (unit.currentReactionCoroutine != null)
        {
            if (prioritySituationSet.Contains(situation))
            {
                DLog($"[AudienceController] [{unit.animator.name}] 優先状況({situation})を検知しましたが再生中のため、この反応が終わり次第優先的に再生されるよう予約します。");
                unit.pendingPrioritySituation = situation;
            }
            return;
        }

        var chosen = clips[UnityEngine.Random.Range(0, clips.Length)];
        PlayClipReactionOnUnit(unit, chosen);
    }

    // 予約されていた優先状況の反応があれば、そのキャラに対して再生する。
    // PlayGoParamRoutineが終わり、そのキャラのcurrentReactionCoroutineがnullに戻った直後に呼ばれる。
    void TryPlayPendingPriorityReaction(AudienceUnit unit)
    {
        if (unit == null || !unit.pendingPrioritySituation.HasValue) return;

        var situation = unit.pendingPrioritySituation.Value;
        unit.pendingPrioritySituation = null;

        if (reactionMap == null) BuildReactionMap();
        if (!reactionMap.TryGetValue(situation, out var clips) || clips.Length == 0) return;

        DLog($"[AudienceController] [{unit.animator.name}] 予約されていた優先状況({situation})の反応を再生します。");
        var chosen = clips[UnityEngine.Random.Range(0, clips.Length)];
        PlayClipReactionOnUnit(unit, chosen);
    }

    // ★こちらも外部から直接呼べる：状況を経由せず、反応そのもの(Clap/Cool/Call)を直接指定して再生したい場合用。
    //   登録済みの全観客キャラクターのうち、今Idle中のキャラ全員に同じ反応を再生させる。
    public void PlayClipReaction(AudienceClipReaction clip)
    {
        if (clip == AudienceClipReaction.None) return;
        if (audienceUnits == null) return;

        foreach (var unit in audienceUnits)
        {
            if (unit == null || unit.animator == null) continue;
            if (unit.currentReactionCoroutine != null) continue; // 再生中のキャラはスキップ
            PlayClipReactionOnUnit(unit, clip);
        }
    }

    // 指定した1体の観客キャラクター(unit)に対して、反応そのもの(Clap/Cool/Call)を再生する内部処理
    void PlayClipReactionOnUnit(AudienceUnit unit, AudienceClipReaction clip)
    {
        if (clip == AudienceClipReaction.None) return;

        string goParam = GetGoParamName(clip);
        TryPlayGoParam(unit, goParam, clip.ToString());
    }

    //-----------------------------------------------------------------------
    // 待機中（Waiting）の演出ループ
    //-----------------------------------------------------------------------

    // 仁王立ち・ダメージ等、他の状況(situation)が何も起きていない待機中に、
    // ランダムな間隔でAudienceSituation.Waitingの反応を再生し続けるループ。
    // キャラ(unit)ごとに個別のコルーチンとして起動され、間隔も再生する反応も
    // それぞれ独立してランダムに抽選される。
    // ・enableIdleVariationsがfalseの間は何もしない（Inspectorで実行時にON/OFF可）
    // ・そのキャラが現在Idle状態でない、または他の反応(Clap/Cool/Call)再生中の場合はスキップする
    //   （＝GuardBlockやAttackHit等、他の状況の反応中とは自動的に重複しない）
    // ・実際に何を再生するかは、Reactionsリストの Waiting 状況に登録した
    //   反応(Clap/Cool/Call/待機（Idle）)からランダムで選ばれる（他の状況と同じ仕組み）
    IEnumerator IdleVariationLoop(AudienceUnit unit)
    {
        while (true)
        {
            float minInterval = Mathf.Min(idleVariationIntervalRange.x, idleVariationIntervalRange.y);
            float maxInterval = Mathf.Max(idleVariationIntervalRange.x, idleVariationIntervalRange.y);
            float wait = UnityEngine.Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));
            yield return new WaitForSeconds(wait);

            if (!enableIdleVariations) continue;
            if (unit.animator == null) continue;
            if (unit.currentReactionCoroutine != null) continue; // 他の反応(Clap/Cool/Call)再生中は割り込ませない
            if (!IsInIdleState(unit.animator)) continue; // Idle中のみ発火

            if (reactionMap == null) BuildReactionMap();
            if (!reactionMap.TryGetValue(AudienceSituation.Waiting, out var clips) || clips.Length == 0) continue;

            DLog($"[AudienceController] [{unit.animator.name}] Waiting状況の反応を抽選します");
            PlaySituationSE(AudienceSituation.Waiting);
            PlayReactionOnUnit(unit, AudienceSituation.Waiting, clips);
        }
    }

    // goParam（Boolパラメータ名）を指定して、指定した1体の観客キャラクター(unit)に対して
    // 反応コルーチンを開始する共通処理。Clap/Cool/CallとIdleバリエーションの両方から利用する。
    void TryPlayGoParam(AudienceUnit unit, string goParam, string debugLabel)
    {
        if (string.IsNullOrEmpty(goParam)) return;
        if (unit == null) return;

        if (unit.animator == null)
        {
            Debug.LogWarning($"[AudienceController] Audience Animatorが未設定のため、反応({debugLabel})を再生できません。");
            return;
        }

        // そのキャラが既に別の反応を再生中の場合は割り込ませない（Bool制御なので同時発火させると崩れるため）
        if (unit.currentReactionCoroutine != null)
        {
            DLog($"[AudienceController] [{unit.animator.name}] 反応({debugLabel})は前の反応がまだ再生中のためスキップしました。");
            return;
        }

        unit.currentReactionCoroutine = StartCoroutine(PlayGoParamRoutine(unit, goParam, debugLabel));
    }

    // デバッグログ出力（enableDebugLogがtrueの時だけConsoleに出す）
    void DLog(string message)
    {
        if (enableDebugLog) Debug.Log(message);
    }

    // PeopleAnimController の Bool パラメータを、
    // 「goX を true → 実際にstartへ遷移したことを確認 → isChangeble を true
    //  → Idleに戻るまで待つ → 両方 false に戻す」の手順で操作するコルーチン。
    // （goXとisChangebleを同時にtrueにすると、start→main側の遷移条件が
    //   同フレームで即成立し、一瞬で反応が終わってしまうため順序を分けている）
    // Clap/Cool/Call・Idleバリエーションのどちらもこのコルーチンで共通に処理する。
    // unitで指定された1体のAnimatorだけを操作するため、他の観客キャラクターには影響しない。
    IEnumerator PlayGoParamRoutine(AudienceUnit unit, string goParam, string debugLabel)
    {
        var animator = unit.animator;

        DLog($"[AudienceController] [{animator.name}] 反応再生開始: {debugLabel} (Bool: {goParam} -> true)");

        // Idle → start（該当の反応へ入る）
        animator.SetBool(goParam, true);

        // ★修正：goParamとisChangebleを同時にtrueにすると、Idle→startの遷移が
        //   成立した瞬間にはisChangebleも既にtrueになっており、start→main側の
        //   遷移条件（isChangeble==true）が同フレーム内で即座に成立してしまう。
        //   Animator Controller側のstart→mainの遷移にExit Time等の
        //   時間的な歯止めが無いと、start〜over〜Idleまで一瞬で駆け抜けてしまい
        //   「アニメーションがすぐ終了する」原因になる。
        //   そのため、実際にIdleを抜けてstartステートへ遷移したことを
        //   確認してから、isChangebleをtrueにする。
        float enterElapsed = 0f;
        while (IsInIdleState(animator) && enterElapsed < reactionSafetyTimeout)
        {
            enterElapsed += Time.deltaTime;
            yield return null;
        }

        if (enterElapsed >= reactionSafetyTimeout)
        {
            // Idleから抜け出せていない＝goParamに対応するAnimator側の遷移が
            // 想定通りに組まれていない可能性が高い
            Debug.LogWarning($"[AudienceController] [{animator.name}] 反応({debugLabel})を開始しましたが、{reactionSafetyTimeout}秒経ってもIdle State Name(\"{idleStateName}\")から遷移しませんでした。Animator ControllerのIdle→{goParam}側の遷移条件を確認してください。");
            animator.SetBool(goParam, false);
            unit.currentReactionCoroutine = null;
            TryPlayPendingPriorityReaction(unit);
            yield break;
        }

        DLog($"[AudienceController] [{animator.name}] {debugLabel}: startステートへ遷移を確認。isChangeble -> true");

        // start → main → over → Idle と、ループの節目ごとに自動で進めていく
        animator.SetBool(isChangebleParamName, true);

        // パラメータ変更がAnimatorに反映されるのを1フレーム待つ
        yield return null;

        float elapsed = 0f;
        while (!IsInIdleState(animator) && elapsed < reactionSafetyTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= reactionSafetyTimeout)
        {
            // ここに来る場合、Idle State Nameが実際のステート名と一致していないか、
            // Animator Controller側の遷移条件（isChangeble等）が想定と違っている可能性が高い
            Debug.LogWarning($"[AudienceController] [{animator.name}] 反応({debugLabel})後、{reactionSafetyTimeout}秒経ってもIdle State Name(\"{idleStateName}\")に戻ったことを検知できませんでした。ステート名の綴りや、Animator Controller側の遷移条件を確認してください。");
        }

        // 次回のためにパラメータを元に戻しておく
        animator.SetBool(goParam, false);
        animator.SetBool(isChangebleParamName, false);

        DLog($"[AudienceController] [{animator.name}] 反応再生終了: {debugLabel}");

        unit.currentReactionCoroutine = null;
        TryPlayPendingPriorityReaction(unit);
    }

    // 指定したAnimatorが（ベースレイヤーの）Idle状態にいるかどうか
    bool IsInIdleState(Animator animator)
    {
        var info = animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(idleStateName);
    }

    // AudienceClipReaction と、PeopleAnimController側のBoolパラメータ名を対応付ける
    string GetGoParamName(AudienceClipReaction clip)
    {
        switch (clip)
        {
            case AudienceClipReaction.Clap: return goClapParamName;
            case AudienceClipReaction.Cool: return goCoolParamName;
            case AudienceClipReaction.Call: return goCallParamName;
            case AudienceClipReaction.SadIdle: return goSadIdleParamName;
            case AudienceClipReaction.Rage: return goRageParamName;
            case AudienceClipReaction.Nervous: return goNervousParamName;
            case AudienceClipReaction.BadSign: return goBadSignParamName;
            case AudienceClipReaction.Angry: return goAngryParamName;
            case AudienceClipReaction.Sadness: return goSadnessParamName;
            case AudienceClipReaction.Cheers: return goCheersParamName;
            default: return null;
        }
    }
}
