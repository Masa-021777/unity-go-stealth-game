using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// リザルト画面の表示とランキング送受信を管理するクラス。
/// ResultDataから取得したデータを画面に表示し、サーバーへランキングを送信する。
/// </summary>
public class ResultManager : MonoBehaviour
{
	#region インスペクター設定

	[Header("プレイヤー名")]
	public Text playerNameText;

	[Header("クリア結果")]
	public Text clearTimeText;
	public Text missionCountText;
	public Text gradeText;

	[Header("ランキング")]
	public Text rank1Text;
	public Text rank2Text;
	public Text rank3Text;

	public string serverBaseUrl = "http://localhost:8080";

	#endregion

	#region Unityイベント

	void Start()
	{
		// ResultDataの内容を画面に反映
		playerNameText.text = $"{ResultData.playerName} & {ResultData.remotePlayerName}";
		clearTimeText.text = $"{(int)ResultData.elapsedTime}秒";
		missionCountText.text = $"{ResultData.missionCount} / 3";

		string grade = CalcGrade(ResultData.missionCount, ResultData.elapsedTime);
		gradeText.text = grade;

		StartCoroutine(PostAndFetchRanking());
	}

	#endregion

	#region ランキング処理

	/// <summary>
	/// ミッション数とクリアタイムから評価を計算する。
	/// S: 3ミッション達成かつ120秒以内 / A: 3ミッション達成 / B: 2ミッション達成 / C: それ以下
	/// </summary>
	string CalcGrade(int mission, float time)
	{
		if (mission >= 3 && time <= 120f) return "S";
		if (mission >= 3) return "A";
		if (mission >= 2) return "B";
		return "C";
	}

	/// <summary>
	/// スコアをサーバーに送信し、ランキングを取得してUIに反映する
	/// </summary>
	IEnumerator PostAndFetchRanking()
	{
		// ランキングに送信
		string json = $"{{\"name\":\"{ResultData.playerName}\",\"clear_time\":{ResultData.elapsedTime},\"mission_count\":{ResultData.missionCount}}}";

		using (UnityWebRequest req = new UnityWebRequest($"{serverBaseUrl}/ranking", "POST"))
		{
			req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("Content-Type", "application/json");
			req.SetRequestHeader("ngrok-skip-browser-warning", "true");
			yield return req.SendWebRequest();
		}

		// ランキング取得
		using (UnityWebRequest req = UnityWebRequest.Get($"{serverBaseUrl}/ranking"))
		{
			req.SetRequestHeader("ngrok-skip-browser-warning", "true");
			yield return req.SendWebRequest();

			if (req.result == UnityWebRequest.Result.Success)
			{
				var data = JsonUtility.FromJson<RankingResponse>(req.downloadHandler.text);
				if (data != null && data.rankings != null)
					UpdateRankingUI(data.rankings);
			}
		}
	}

	/// <summary>
	/// 取得したランキングデータをUIに反映する
	/// </summary>
	void UpdateRankingUI(RankingItem[] rankings)
	{
		Text[] texts = { rank1Text, rank2Text, rank3Text };
		for (int i = 0; i < texts.Length; i++)
		{
			if (i < rankings.Length)
			{
				var r = rankings[i];
				string g = CalcGrade(r.mission_count, (float)r.clear_time);
				texts[i].text = $"{r.name}　{r.mission_count}ミッション　{(int)r.clear_time}秒　{g}";
			}
			else
			{
				texts[i].text = "---";
			}
		}
	}

	#endregion

	#region ボタン処理

	/// <summary>
	/// もう一度プレイボタン押下時にゲームシーンへ遷移する
	/// </summary>
	public void OnRetryButton()
	{
		SceneManager.LoadScene("MapTest");
	}

	/// <summary>
	/// タイトルボタン押下時にタイトルシーンへ遷移する
	/// </summary>
	public void OnTitleButton()
	{
		SceneManager.LoadScene("Title");
	}

	#endregion
}

// ランキングの1件分のデータ
[System.Serializable]
public class RankingItem
{
	public string name;
	public double clear_time;
	public int mission_count;
}

// ランキング取得時のデータ形式
[System.Serializable]
public class RankingResponse
{
	public RankingItem[] rankings;
}