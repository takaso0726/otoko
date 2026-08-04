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
    Clap,   // 拍手
    Cool,   // クール系の反応（やれやれ、感心 等）
    Call,   // 声援・コール
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
    };

    [Header("SE設定")]
    [Tooltip("状況ごとのSE(効果音)を再生するためのAudioSource。ここに1つセットする。" +
             "常に1本だけ再生する仕様で、新しいSEが発生すると再生中の前のSEを打ち切って差し替える" +
             "（どんな状況でもSEが重ねて鳴ることはない。BGMは別のAudioSourceで管理すること）。")]
    public AudioSource seAudioSource;

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
    };

    [Header("PeopleAnimController側のパラメータ名（通常は変更不要）")]
    [Tooltip("main→over→Idleへ進めてよいかを示すBoolパラメータ名")]
    public string isChangebleParamName = "isChangeble";
    public string goClapParamName = "goClap";
    public string goCoolParamName = "goCool";
    public string goCallParamName = "goCall";

    [Tooltip("Idle状態の名前（Animator上のステート名と合わせる）")]
    public string idleStateName = "Idle";

    [Header("待機中（Waiting）演出の間隔設定")]
    [Tooltip("何も状況が起きていない待機中に、AudienceSituation.Waitingの反応をランダム再生する機能を有効にするか")]
    public bool enableIdleVariations = false;

    [Tooltip("次のWaiting反応を再生するまでの間隔（秒）の範囲。x=最小、y=最大（キャラごとに個別に抽選される）")]
    public Vector2 idleVariationIntervalRange = new Vector2(8f, 20f);

    [Tooltip("Idleに戻らなかった場合の保険（秒）。この時間が経ったら強制的にパラメータをリセットする")]
    public float reactionSafetyTimeout = 10f;

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
    }

    // audienceAnimators(Inspector設定)から、null除外の上で構築されるキャラごとの状態リスト
    private List<AudienceUnit> audienceUnits = new List<AudienceUnit>();

    // reactionsリストを毎回線形探索しないよう、起動時に辞書化しておく
    private Dictionary<AudienceSituation, AudienceClipReaction[]> reactionMap;

    // reactionsリスト(Inspector設定)のseClipsを、起動時に状況ごとの辞書化しておく
    private Dictionary<AudienceSituation, AudioClip[]> seMap;

    // 各プレイヤーの「直前フレームでのステータス」を覚えておくための配列。
    // これと現在の値を比較することで、「変化した瞬間」だけを検知する。
    private Player.Status[] previousStatus;

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
        // GameMNGが設定されていれば、p1・p2を自動的に監視対象へ加える
        // （targetPlayersに直接手動登録したものがあればそちらも維持しつつ重複は避ける）
        if (gameMNG != null)
        {
            var list = new List<Player>(targetPlayers ?? new Player[0]);
            if (gameMNG.p1 != null && !list.Contains(gameMNG.p1)) list.Add(gameMNG.p1);
            if (gameMNG.p2 != null && !list.Contains(gameMNG.p2)) list.Add(gameMNG.p2);
            targetPlayers = list.ToArray();
        }

        previousStatus = new Player.Status[targetPlayers.Length];
        for (int i = 0; i < targetPlayers.Length; i++)
        {
            if (targetPlayers[i] != null)
            {
                previousStatus[i] = targetPlayers[i].Player_status;
            }
        }

        // audienceAnimators(Inspector設定)から、キャラごとの状態管理オブジェクトを構築する。
        // nullが混ざっていても無視して続行する。
        audienceUnits = new List<AudienceUnit>();
        if (audienceAnimators != null)
        {
            foreach (var animator in audienceAnimators)
            {
                if (animator == null) continue;
                audienceUnits.Add(new AudienceUnit { animator = animator });
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

    void Update()
    {
        for (int i = 0; i < targetPlayers.Length; i++)
        {
            Player p = targetPlayers[i];
            if (p == null) continue;

            Player.Status current = p.Player_status;
            Player.Status prev = previousStatus[i];

            // ステータスが変化した瞬間だけ反応させる（毎フレーム連打しないようにするため）
            if (current != prev)
            {
                HandleStatusChanged(prev, current);
                previousStatus[i] = current;
            }
        }
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
    //   その抽選の回だけ「あえてSEを鳴らさない」という結果にもできる。
    // ・SEはどんな状況でも重ねて再生しない：seAudioSourceは常に1本のクリップしか鳴らさず、
    //   新しいSEが発生した場合は再生中の前のSEを打ち切って即座に差し替える
    //   （BGMは別のAudioSourceで管理する想定のため、このルールの対象外）。
    void PlaySituationSE(AudienceSituation situation)
    {
        if (seMap == null) BuildSeMap();
        if (!seMap.TryGetValue(situation, out var clips) || clips == null || clips.Length == 0) return;

        var chosen = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (chosen == null) return; // 「あえて鳴らさない」が選ばれた場合

        if (seAudioSource == null)
        {
            Debug.LogWarning($"[AudienceController] SE Audio Sourceが未設定のため、SE({situation})を再生できません。");
            return;
        }

        // PlayOneShotだと複数のSEが同時に重なって再生されてしまうため、
        // 単一クリップの再生に切り替えて重複を防ぐ。
        // 既にSEが再生中でも一旦止めて、新しい方をすぐ再生する（後勝ち・割り込み方式）。
        seAudioSource.Stop();
        seAudioSource.volume = seAudioSourceBaseVolume * seVolume;
        seAudioSource.clip = chosen;
        seAudioSource.Play();

        DLog($"[AudienceController] SE再生: {situation} -> {chosen.name}");
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
            default: return null;
        }
    }
}
