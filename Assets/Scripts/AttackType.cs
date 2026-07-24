/// <summary>
/// 攻撃の種類。PlayerとEnemy(CPU)の両方から参照し、
/// 「今どんな攻撃をしているか」に応じて擬音演出(HitEffectData)を出し分けるために使う。
/// </summary>
public enum AttackType
{
    Punch,
    Kick,
    UpKick,
    DownKick,
    None
}
