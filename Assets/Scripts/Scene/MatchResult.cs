/// <summary>
/// シーンをまたいで対戦結果を受け渡すための入れ物。
///
/// static変数はGameObjectに紐づかないため、DontDestroyOnLoadを使わなくても
/// シーン遷移をまたいで値が保持される（アプリを完全終了するまで残る）。
///
/// 使い方の想定:
///   1. GameMNG.cs 側で勝敗が確定したタイミングで
///      MatchResult.LastWinner に結果をセットする
///      （例: player1StatusがWinならMatchResult.LastWinner = Winner.Player1;）
///   2. その後 SceneManager.LoadScene("InGame1V1Result") のように結果画面シーンへ遷移する
///   3. 結果画面側（InGame1V1ResultController.cs）が
///      MatchResult.LastWinner を読み取って表示を出し分ける
///
/// 注意: static変数は次の対戦を始める前に必ずリセットすること
///       （リセットし忘れると、前回の結果が残ったまま次の結果画面に表示されてしまう）。
/// </summary>
public static class MatchResult
{
    public enum Winner
    {
        None,    // 未確定（初期値）
        Player1, // 1Pの勝ち
        Player2, // 2Pの勝ち
        Draw     // 引き分け（現状GameMNG側では未使用だが将来のために用意）
    }

    public static Winner LastWinner = Winner.None;

    /// <summary>
    /// 次の対戦を始める前に呼び出して、結果をNoneに戻す。
    /// InGame1V1シーンのロード直後（GameMNG.Start()など）で呼ぶことを推奨。
    /// </summary>
    public static void Reset()
    {
        LastWinner = Winner.None;
    }
}
