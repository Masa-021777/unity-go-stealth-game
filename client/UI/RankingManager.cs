using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// スコアをサーバーに送信し、ランキングを取得して表示するクラス。
/// サーバーの接続先は WebSocketClient の設定に合わせて自動で切り替わる。
/// </summary>
public class RankingManager : MonoBehaviour
{
	public static RankingManager Instance;

	#region インスペクター設定

	[Header("UI")]
	public GameObject rankingPanel;
	public Text rankingText;
	public Button returnButton;

	[Header("サーバー設定（WebSocketClientと同じ接続先にする）")]
	public string serverUrl = "http://192.168.56.102:8080";

	#endregion

	#region Unityイベント

	void Awake()
	{
		if (Instance == null) Instance = this;
		else { Destroy(gameObject); return; }

		if (rankingPanel != null) rankingPanel.SetActive(false);
		if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);
	}

	void Start()
	{
		// WebSocketClientの接続先に合わせてURLを切り替える
		var wsClient = FindObjectOfType<WebSocketClient>();
		if (wsClient != null)
		{
			switch (wsClient.serverMode)
			{
				case WebSocketClient.ServerMode.VirtualBox:
					serverUrl = "http://192.168.56.102:8080"; break;
				case WebSocketClient.ServerMode.LocalHost:
					serverUrl = "http://localhost:8080"; break;
				case WebSocketClient.ServerMode.Ngrok:
					serverUrl = wsClient.ngrokUrl; break;
				case WebSocketClient.ServerMode.Render:
					serverUrl = "https://stealth-game-server.onrender.com"; break;
				case WebSocketClient.ServerMode.FlyIO:
					serverUrl = "https://stealth-game-server.fly.dev"; break;
			}
		}
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// ゴール時にWebSocketClientから呼ぶ。
	/// スコアをサーバーへ送信してからランキングを取得して表示する。
	/// </summary>
	public void SubmitAndShow(string playerName, float clearTime, int missionCount)
	{
		StartCoroutine(SubmitCoroutine(playerName, clearTime, missionCount));
	}

	#endregion

	#region ランキング処理

	/// <summary>スコアを送信し、完了後にランキングを取得する</summary>
	private IEnumerator SubmitCoroutine(string playerName, float clearTime, int missionCount)
	{
		string json = JsonUtility.ToJson(new ScorePayload
		{
			name = playerName,
			clear_time = clearTime,
			mission_count = missionCount
		});

		using var postReq = new UnityEngine.Networking.UnityWebRequest(
			serverUrl + "/ranking",
			UnityEngine.Networking.UnityWebRequest.kHttpVerbPOST
		);
		byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
		postReq.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
		postReq.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
		postReq.SetRequestHeader("Content-Type", "application/json");
		yield return postReq.SendWebRequest();

		if (postReq.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
			Debug.LogWarning("ランキング送信失敗: " + postReq.error);

		yield return StartCoroutine(FetchAndShow());
	}

	/// <summary>サーバーからランキングを取得して表示する</summary>
	private IEnumerator FetchAndShow()
	{
		using var getReq = UnityEngine.Networking.UnityWebRequest.Get(serverUrl + "/ranking");
		yield return getReq.SendWebRequest();

		if (getReq.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
		{
			Debug.LogWarning("ランキング取得失敗: " + getReq.error);
			ShowFallback();
			yield break;
		}

		var wrapper = JsonUtility.FromJson<RankingResponse>(getReq.downloadHandler.text);
		DisplayRanking(wrapper.rankings);
	}

	/// <summary>取得したランキングデータをテキストに整形して表示する</summary>
	private void DisplayRanking(RankingEntry[] entries)
	{
		if (rankingText == null) return;

		string[] medals = { "🥇", "🥈", "🥉" };
		var sb = new System.Text.StringBuilder();
		sb.AppendLine(" ランキング ");

		if (entries == null || entries.Length == 0)
		{
			sb.AppendLine("（まだ記録なし）");
		}
		else
		{
			for (int i = 0; i < entries.Length; i++)
			{
				string medal = i < 3 ? medals[i] : $"{i + 1}位";
				string timeStr = FormatTime(entries[i].clear_time);
				sb.AppendLine($"{medal}  {entries[i].name}");
				sb.AppendLine($"     {timeStr}  ミッション {entries[i].mission_count}/3");
			}
		}

		rankingText.text = sb.ToString();
		if (rankingPanel != null) rankingPanel.SetActive(true);
	}

	/// <summary>ランキング取得失敗時の代替表示</summary>
	private void ShowFallback()
	{
		if (rankingText != null) rankingText.text = "ランキング取得に失敗しました";
		if (rankingPanel != null) rankingPanel.SetActive(true);
	}

	/// <summary>秒数を「1分23秒」形式に変換する</summary>
	private string FormatTime(float seconds)
	{
		int m = Mathf.FloorToInt(seconds / 60f);
		int s = Mathf.FloorToInt(seconds % 60f);
		return m > 0 ? $"{m}分{s:D2}秒" : $"{s}秒";
	}

	/// <summary>戻るボタン押下時にパネルを閉じてタイトルへ遷移する</summary>
	private void OnReturnClicked()
	{
		if (rankingPanel != null) rankingPanel.SetActive(false);
		UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
	}

	#endregion

	#region データ構造

	[System.Serializable]
	private class ScorePayload
	{
		public string name;
		public float clear_time;
		public int mission_count;
	}

	[System.Serializable]
	private class RankingResponse
	{
		public RankingEntry[] rankings;
	}

	#endregion
}

// ランキングの1件分のデータ
[System.Serializable]
public class RankingEntry
{
	public string name;
	public float clear_time;
	public int mission_count;
}