using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; // 被写界深度(背景ボケ)用のVolume制御に使用

/// <summary>
/// 3D格闘ゲーム用カメラコントローラー
/// 出場中の全キャラクターの中心点（重心）を自動算出し、
/// カメラがその座標へスムーズに移動する。
/// キャラクター同士の距離に応じてズーム（カメラ距離）も自動調整する。
/// </summary>
public class FightingCameraController : MonoBehaviour
{
    [Header("追従対象")]
    [Tooltip("現在出場中のキャラクターのTransformリスト。動的に増減してOK")]
    public List<Transform> targets = new List<Transform>();

    [Header("カメラ追従設定")]
    [Tooltip("中心点への移動速度（大きいほど速く追従）")]
    public float followSmoothTime = 0.25f;

    [Tooltip("中心点から見たカメラの基本オフセット（ローカル方向）")]
    public Vector3 baseOffset = new Vector3(0f, 3f, -8f);

    [Header("ズーム（距離）調整設定")]
    [Tooltip("キャラクター間の最大距離がこの値のときの最小カメラ距離倍率")]
    public float minZoomDistance = 6f;

    [Tooltip("キャラクター間の最大距離がこの値のときの最大カメラ距離倍率")]
    public float maxZoomDistance = 16f;

    [Tooltip("キャラクター間の距離とカメラ距離の対応を調整する係数")]
    public float spreadToDistanceMultiplier = 1.5f;

    [Tooltip("ズーム変化のスムーズさ")]
    public float zoomSmoothTime = 0.3f;

    [Header("注視点設定")]
    [Tooltip("キャラクターの足元ではなく少し上を見るためのYオフセット")]
    public float lookAtHeightOffset = 1.0f;

    [Tooltip("注視点の回転スムーズさ")]
    public float rotationSmoothTime = 0.2f;

    [Header("勝利演出（フォーカス）設定")]
    [Tooltip("勝利したキャラクターにズームする際のカメラ距離")]
    public float focusZoomDistance = 4f;

    [Tooltip("フォーカス演出時、対象を見る高さオフセット")]
    public float focusLookAtHeightOffset = 1.2f;

    [Header("仁王立ち被弾演出（ガードインパクトカメラ）")]
    [Tooltip("被弾した瞬間、対象からどれだけ離れるか（かなり近距離にして威圧感を出す）")]
    public float guardImpactDistance = 2.2f;

    [Tooltip("カメラの高さ（地面に近いほどローアングルになる）")]
    public float guardImpactCamHeight = 0.4f;

    [Tooltip("正面に対してどれだけ横に回り込むか（度）。斜めからの見上げ画角になる")]
    public float guardImpactHorizontalAngle = 35f;

    [Tooltip("ONにするとキャラクターの正面側（顔が見える側）にカメラを配置する。モデルのForwardが逆向きの場合はOFFにして調整する")]
    public bool guardImpactFilmFromFront = true;

    [Tooltip("見上げる注視点の高さ（頭上あたりを見ることで見上げ角が強調される）")]
    public float guardImpactLookAtHeight = 1.9f;

    [Tooltip("突入時のカメラ移動の速さ（小さいほど素早くグッと寄る）")]
    public float guardImpactMoveInSmoothTime = 0.08f;

    [Tooltip("新たな被弾が無い場合、この演出を継続する時間（秒）。連続被弾時はリセットされ続く")]
    public float guardImpactHoldDuration = 0.6f;

    [Header("被弾シェイク設定")]
    [Tooltip("1回目のガード成功時の揺れの強さ")]
    public float shakeBaseAmplitude = 0.05f;

    [Tooltip("連続ガード1回ごとに加算される揺れの強さ")]
    public float shakeAmplitudePerCombo = 0.04f;

    [Tooltip("揺れの強さの上限（これ以上は大きくならない）")]
    public float shakeMaxAmplitude = 0.4f;

    [Tooltip("揺れの細かさ（大きいほど小刻みに震える）")]
    public float shakeFrequency = 25f;

    [Header("背景ボケ（被写界深度）連携")]
    [Tooltip("被写界深度(Depth of Field)を設定したVolumeを割り当てる（URP/HDRP共通）。未設定でも動作する")]
    public Volume guardImpactVolume;

    [Tooltip("演出中に持っていくVolumeのWeight（1でDOFの効果が全開になる）")]
    public float guardImpactVolumeWeight = 1f;

    [Tooltip("Volumeの重みが切り替わる速さ")]
    public float volumeWeightSmoothSpeed = 8f;

    [Header("根性復活演出（リバースカム：クローズアップ〜引き）")]
    [Tooltip("ダウン直後、顔や拳へ寄る最短距離")]
    public float rebornCloseUpDistance = 0.8f;

    [Tooltip("復活レベルが最大まで上がったときの距離（ここまで徐々に引く）")]
    public float rebornPullBackDistance = 4.0f;

    [Tooltip("クローズアップする高さ（顔・拳あたり）")]
    public float rebornCloseUpHeight = 1.4f;

    [Tooltip("正面から見た角度のズレ（度）。真正面すぎない見せ方にする")]
    public float rebornCloseUpAngle = 15f;

    [Tooltip("復活レベル(0〜rebornMaxLevel)に応じて距離が変化する際のスムーズさ")]
    public float rebornZoomSmoothTime = 0.35f;

    [Tooltip("復活演出の最大レベル（このレベルで最も引いた画になる）")]
    public int rebornMaxLevel = 3;

    [Header("根性復活演出（立ち上がり180度バレットタイム）")]
    [Tooltip("180度回り込むのにかかる時間。短いほど高速でキレのある回り込みになる")]
    public float rebornOrbitDuration = 0.45f;

    [Tooltip("回り込み時にキャラを中心にとる半径")]
    public float rebornOrbitRadius = 3.5f;

    [Tooltip("回り込み時のカメラの高さ")]
    public float rebornOrbitHeight = 1.8f;

    [Tooltip("咆哮するキャラクターのどのあたりを見るか（高さ）")]
    public float rebornRoarLookAtHeight = 1.6f;

    // 内部状態
    private Vector3 _velocityPos;   // SmoothDamp用
    private float _velocityZoom;    // SmoothDamp用（float）
    private float _currentDistance; // 現在のカメラ距離
    private Vector3 _currentLookAtVelocity;
    private Vector3 _smoothedLookAt;

    // フォーカス（勝者ズーム）関連
    private bool _isFocusMode = false;
    private Transform _focusTarget;

    // 仁王立ち被弾（ガードインパクト）関連
    private bool _isGuardImpactMode = false;
    private Transform _guardImpactTarget;
    private int _guardImpactComboCount = 0;
    private float _guardImpactHoldTimer = 0f;
    private Vector3 _guardImpactVelocityPos;
    private float _currentVolumeWeight = 0f;

    // 根性復活演出（リバースカム）関連
    private bool _isRebornCloseUpMode = false;
    private bool _isRebornOrbitMode = false;
    private Transform _rebornTarget;
    private int _rebornLevel = 0;
    private float _rebornCurrentDistance;
    private float _rebornVelocityZoom;
    private Vector3 _rebornVelocityPos;
    private float _rebornOrbitTimer;
    private float _rebornOrbitStartAngle;

    void Start()
    {
        _currentDistance = (maxZoomDistance + minZoomDistance) * 0.5f;
        _smoothedLookAt = CalculateCenterPoint();
    }

    void LateUpdate()
    {
        // Volumeの重みは常にスムーズに追従させる（演出のON/OFFに関わらず処理）
        UpdateGuardImpactVolume();

        // 仁王立ちで攻撃を受け止めた直後は、最優先でローアングルの被弾カメラを処理する
        if (_isGuardImpactMode)
        {
            UpdateGuardImpactCamera();
            return;
        }

        // 根性復活：立ち上がった瞬間の180度バレットタイム回り込み
        if (_isRebornOrbitMode)
        {
            UpdateRebornOrbitCamera();
            return;
        }

        // 根性復活：ダウン中の連打演出（顔・拳クローズアップ〜引き）
        if (_isRebornCloseUpMode)
        {
            UpdateRebornCloseUpCamera();
            return;
        }

        // 勝敗が決まり、勝者へのフォーカス演出中の場合は専用の処理を行う
        if (_isFocusMode)
        {
            UpdateFocusCamera();
            return;
        }

        // 出場キャラクターがいない場合は何もしない
        CleanupNullTargets();
        if (targets.Count == 0) return;

        // 1. 中心点（重心）を算出
        Vector3 centerPoint = CalculateCenterPoint();

        // 2. キャラクター間の広がり（最大距離）を算出してズーム量を決定
        float spread = CalculateMaxSpread(centerPoint);
        float targetDistance = Mathf.Clamp(
            spread * spreadToDistanceMultiplier,
            minZoomDistance,
            maxZoomDistance
        );

        _currentDistance = Mathf.SmoothDamp(
            _currentDistance,
            targetDistance,
            ref _velocityZoom,
            zoomSmoothTime
        );

        // 3. 注視点をスムーズに更新
        Vector3 lookAtTarget = centerPoint + Vector3.up * lookAtHeightOffset;
        _smoothedLookAt = Vector3.SmoothDamp(
            _smoothedLookAt,
            lookAtTarget,
            ref _currentLookAtVelocity,
            rotationSmoothTime
        );

        // 4. オフセット方向を距離に応じてスケーリングしてカメラ目標位置を算出
        Vector3 offsetDirection = baseOffset.normalized;
        Vector3 desiredCameraPos = _smoothedLookAt + offsetDirection * _currentDistance;

        // 5. カメラ位置をスムーズに移動
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredCameraPos,
            ref _velocityPos,
            followSmoothTime
        );

        // 6. 常に中心点を見るように回転
        transform.LookAt(_smoothedLookAt);
    }

    /// <summary>
    /// 出場中の全キャラクターの中心点（重心）を算出する
    /// </summary>
    private Vector3 CalculateCenterPoint()
    {
        if (targets.Count == 0) return transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var t in targets)
        {
            if (t == null) continue;
            sum += t.position;
            count++;
        }

        return count > 0 ? sum / count : transform.position;
    }

    /// <summary>
    /// 中心点から最も離れているキャラクターまでの距離（広がり具合）を算出する
    /// キャラクター同士が離れているほどカメラを引く（ズームアウト）ために使用
    /// </summary>
    private float CalculateMaxSpread(Vector3 center)
    {
        float maxDist = 0f;
        foreach (var t in targets)
        {
            if (t == null) continue;
            float dist = Vector3.Distance(center, t.position);
            if (dist > maxDist) maxDist = dist;
        }
        return maxDist;
    }

    /// <summary>
    /// リストからnull（撃破・非表示等で消えたキャラクター）を除去する
    /// </summary>
    private void CleanupNullTargets()
    {
        targets.RemoveAll(t => t == null);
    }

    /// <summary>
    /// フォーカスモード中のカメラ更新処理
    /// 指定した勝者キャラクターにズームして注視する
    /// </summary>
    private void UpdateFocusCamera()
    {
        // フォーカス対象が消えていたら何もしない
        if (_focusTarget == null) return;

        // 注視点（勝者の少し上）をスムーズに更新
        Vector3 lookAtTarget = _focusTarget.position + Vector3.up * focusLookAtHeightOffset;
        _smoothedLookAt = Vector3.SmoothDamp(
            _smoothedLookAt,
            lookAtTarget,
            ref _currentLookAtVelocity,
            rotationSmoothTime
        );

        // カメラ距離をフォーカス用の距離までスムーズに詰める
        _currentDistance = Mathf.SmoothDamp(
            _currentDistance,
            focusZoomDistance,
            ref _velocityZoom,
            zoomSmoothTime
        );

        // オフセット方向を距離に応じてスケーリングしてカメラ目標位置を算出
        Vector3 offsetDirection = baseOffset.normalized;
        Vector3 desiredCameraPos = _smoothedLookAt + offsetDirection * _currentDistance;

        // カメラ位置をスムーズに移動
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredCameraPos,
            ref _velocityPos,
            followSmoothTime
        );

        // 常に勝者を見るように回転
        transform.LookAt(_smoothedLookAt);
    }

    /// <summary>
    /// 仁王立ち中に攻撃を受け止めた瞬間、GameMNGやプレイヤースクリプトから呼び出す。
    /// 対象キャラの斜め下からのローアングルにグッとズームし、
    /// 連続で耐えるほどシェイクが強くなる（comboCountを都度加算して渡すこと）。
    /// </summary>
    /// <param name="target">受け止めたキャラクターのTransform</param>
    /// <param name="comboCount">現在の連続ガード成功回数（1回目なら1）</param>
    public void OnGuardImpact(Transform target, int comboCount)
    {
        if (target == null) return;

        _isGuardImpactMode = true;
        _guardImpactTarget = target;
        _guardImpactComboCount = Mathf.Max(1, comboCount);
        _guardImpactHoldTimer = guardImpactHoldDuration;
    }

    /// <summary>
    /// ガードインパクト演出を強制的に終了し、通常の追従モードへ戻す
    /// </summary>
    public void ClearGuardImpact()
    {
        _isGuardImpactMode = false;
        _guardImpactTarget = null;
        _guardImpactComboCount = 0;
    }

    /// <summary>
    /// ガードインパクトカメラの毎フレーム更新処理
    /// </summary>
    private void UpdateGuardImpactCamera()
    {
        // 対象が消えていたら演出を打ち切って通常モードへ戻す
        if (_guardImpactTarget == null)
        {
            ClearGuardImpact();
            return;
        }

        // 新たな被弾が無いまま時間が経過したら通常モードへ戻す
        _guardImpactHoldTimer -= Time.deltaTime;
        if (_guardImpactHoldTimer <= 0f)
        {
            ClearGuardImpact();
            return;
        }

        // 1. 見上げる注視点（対象の頭上あたり）を設定
        Vector3 lookAtTarget = _guardImpactTarget.position + Vector3.up * guardImpactLookAtHeight;

        // 2. 対象の向きを基準に、斜め横・低い位置へ回り込んだカメラ位置を算出
        //    guardImpactFilmFromFrontがtrueなら「正面（顔が見える側）」、falseなら背後側を基準にする
        Vector3 baseDir = guardImpactFilmFromFront ? _guardImpactTarget.forward : -_guardImpactTarget.forward;
        Vector3 diagonalDir = Quaternion.AngleAxis(guardImpactHorizontalAngle, Vector3.up) * baseDir;
        Vector3 desiredCameraPos = _guardImpactTarget.position + diagonalDir.normalized * guardImpactDistance;
        desiredCameraPos.y = _guardImpactTarget.position.y + guardImpactCamHeight;

        // 3. グッと素早く寄るように、通常より短いスムーズタイムで移動
        Vector3 smoothedPos = Vector3.SmoothDamp(
            transform.position,
            desiredCameraPos,
            ref _guardImpactVelocityPos,
            guardImpactMoveInSmoothTime
        );

        // 4. 連続ガード回数に応じてシェイクの強さを決定（上限あり）
        float amplitude = Mathf.Min(
            shakeBaseAmplitude + shakeAmplitudePerCombo * (_guardImpactComboCount - 1),
            shakeMaxAmplitude
        );
        float t = Time.time * shakeFrequency;
        Vector3 shakeOffset = new Vector3(
            (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f,
            (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f,
            (Mathf.PerlinNoise(t, t) - 0.5f) * 2f
        ) * amplitude;

        // 5. シェイクを乗せた最終位置を反映し、見上げる注視点を向く
        transform.position = smoothedPos + shakeOffset;
        transform.LookAt(lookAtTarget);

        // 通常モードに戻ったときに違和感が出ないよう、注視点の内部状態も合わせておく
        _smoothedLookAt = lookAtTarget;
    }

    /// <summary>
    /// 背景ボケ用VolumeのWeightを、演出中は1、通常時は0へスムーズに追従させる
    /// </summary>
    private void UpdateGuardImpactVolume()
    {
        if (guardImpactVolume == null) return;

        float targetWeight = _isGuardImpactMode ? guardImpactVolumeWeight : 0f;
        _currentVolumeWeight = Mathf.MoveTowards(
            _currentVolumeWeight,
            targetWeight,
            volumeWeightSmoothSpeed * Time.deltaTime
        );
        guardImpactVolume.weight = _currentVolumeWeight;
    }

    /// <summary>
    /// HPが0になり「根性復活システム」が発動した瞬間に呼び出す。
    /// ダウンしたキャラの顔・拳への超クローズアップを開始する。
    /// </summary>
    public void StartRebornCloseUp(Transform target)
    {
        if (target == null) return;

        _isRebornCloseUpMode = true;
        _isRebornOrbitMode = false;
        _rebornTarget = target;
        _rebornLevel = 0;
        _rebornCurrentDistance = rebornCloseUpDistance;
    }

    /// <summary>
    /// 連打の進捗（復活レベル）を更新する。0〜rebornMaxLevelの範囲で渡すこと。
    /// レベルが上がるほどカメラが自動的に引いていく。
    /// </summary>
    public void SetRebornLevel(int level)
    {
        _rebornLevel = Mathf.Clamp(level, 0, rebornMaxLevel);
    }

    /// <summary>
    /// 根性復活が成功し、キャラが立ち上がった瞬間に呼び出す。
    /// 咆哮するキャラクターを中心に、現在のカメラ位置から180度高速で回り込む。
    /// </summary>
    public void TriggerRebornStandUpOrbit(Transform target)
    {
        if (target == null) return;

        _isRebornCloseUpMode = false;
        _isRebornOrbitMode = true;
        _rebornTarget = target;
        _rebornOrbitTimer = 0f;

        // 現在のカメラ位置を基準にした角度からスタートすることで、クローズアップから違和感なく繋がる
        Vector3 toCam = transform.position - target.position;
        toCam.y = 0f;
        _rebornOrbitStartAngle = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 根性復活演出を強制終了し、通常の追従モードへ戻す
    /// （復活失敗で死亡した場合や、シーン遷移前のリセット等に使用）
    /// </summary>
    public void ClearReborn()
    {
        _isRebornCloseUpMode = false;
        _isRebornOrbitMode = false;
        _rebornTarget = null;
        _rebornLevel = 0;
    }

    /// <summary>
    /// 連打中のクローズアップ〜引き画の毎フレーム更新処理
    /// </summary>
    private void UpdateRebornCloseUpCamera()
    {
        if (_rebornTarget == null)
        {
            ClearReborn();
            return;
        }

        // レベルに応じて、クローズアップ〜引きの距離を補間
        float t = rebornMaxLevel > 0 ? (float)_rebornLevel / rebornMaxLevel : 0f;
        float targetDistance = Mathf.Lerp(rebornCloseUpDistance, rebornPullBackDistance, t);

        _rebornCurrentDistance = Mathf.SmoothDamp(
            _rebornCurrentDistance,
            targetDistance,
            ref _rebornVelocityZoom,
            rebornZoomSmoothTime
        );

        // 真正面すぎない角度から、顔・拳あたりへ寄る
        Vector3 dir = Quaternion.AngleAxis(rebornCloseUpAngle, Vector3.up) * _rebornTarget.forward;
        Vector3 desiredPos = _rebornTarget.position + dir.normalized * _rebornCurrentDistance;
        desiredPos.y = _rebornTarget.position.y + rebornCloseUpHeight;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPos,
            ref _rebornVelocityPos,
            rebornZoomSmoothTime
        );

        Vector3 lookAtTarget = _rebornTarget.position + Vector3.up * rebornCloseUpHeight;
        transform.LookAt(lookAtTarget);
        _smoothedLookAt = lookAtTarget;
    }

    /// <summary>
    /// 立ち上がった瞬間の180度バレットタイム回り込みの毎フレーム更新処理
    /// </summary>
    private void UpdateRebornOrbitCamera()
    {
        if (_rebornTarget == null)
        {
            ClearReborn();
            return;
        }

        _rebornOrbitTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_rebornOrbitTimer / rebornOrbitDuration);

        // イーズアウトで勢いよく回り込み、終盤でスッと止まる（バレットタイム感）
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float angle = _rebornOrbitStartAngle + 180f * eased;
        float rad = angle * Mathf.Deg2Rad;

        Vector3 orbitOffset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * rebornOrbitRadius;
        Vector3 pos = _rebornTarget.position + orbitOffset;
        pos.y = _rebornTarget.position.y + rebornOrbitHeight;

        transform.position = pos;

        Vector3 lookAtTarget = _rebornTarget.position + Vector3.up * rebornRoarLookAtHeight;
        transform.LookAt(lookAtTarget);
        _smoothedLookAt = lookAtTarget;

        if (t >= 1f)
        {
            ClearReborn();
        }
    }

    /// <summary>
    /// 勝敗決定時に呼び出す。指定したキャラクターにカメラをズームさせる
    /// （GameMNG等、勝敗を管理するスクリプトから呼び出す想定）
    /// </summary>
    public void FocusOnTarget(Transform target)
    {
        if (target == null) return;

        _isFocusMode = true;
        _focusTarget = target;
    }

    /// <summary>
    /// フォーカス演出を終了し、通常の追従モードへ戻す
    /// （リマッチやシーン遷移前のリセット等で使用）
    /// </summary>
    public void ClearFocus()
    {
        _isFocusMode = false;
        _focusTarget = null;
    }

    /// <summary>
    /// キャラクター出現時に呼び出して追従対象に追加する
    /// </summary>
    public void RegisterTarget(Transform t)
    {
        if (t != null && !targets.Contains(t))
        {
            targets.Add(t);
        }
    }

    /// <summary>
    /// キャラクター退場（撃破・リタイア等）時に呼び出して追従対象から除外する
    /// </summary>
    public void UnregisterTarget(Transform t)
    {
        if (targets.Contains(t))
        {
            targets.Remove(t);
        }
    }

    // デバッグ用：シーンビューに中心点と広がり範囲を可視化
    private void OnDrawGizmosSelected()
    {
        if (targets == null || targets.Count == 0) return;

        Vector3 center = CalculateCenterPoint();

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(center, 0.3f);

        Gizmos.color = Color.yellow;
        foreach (var t in targets)
        {
            if (t == null) continue;
            Gizmos.DrawLine(center, t.position);
        }
    }
}
