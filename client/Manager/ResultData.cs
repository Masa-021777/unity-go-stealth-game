/// <summary>
/// シーンをまたいでデータを受け渡すための静的クラス。
/// リザルト画面で表示するプレイヤー名・クリアタイム・ミッション数を保持する。
/// </summary>
public static class ResultData
{
	public static string playerName = "";
	public static string remotePlayerName = "";
	public static float elapsedTime = 0f;
	public static int missionCount = 0;
}