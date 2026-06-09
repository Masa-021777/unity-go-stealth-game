using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// ゴール判定を管理するクラス。
/// 自分のプレイヤーがゴールに触れたらサーバーにゴール通知を送信する。
/// 2人がゴールしたかどうかはサーバー側で管理する。
/// </summary>
public class GoalScript : MonoBehaviour
{
	#region フィールド

	private HashSet<string> playersInGoal = new HashSet<string>();
	private bool isGoalTriggered = false;

	#endregion

	#region Unityイベント

	private void OnTriggerEnter(Collider other)
	{
		if (isGoalTriggered) return;
		if (!other.CompareTag("Player1") && !other.CompareTag("Player2")) return;

		playersInGoal.Add(other.tag);

		// 自分のプレイヤーがゴールに入ったとき
		var wsClient = FindObjectOfType<WebSocketClient>();
		if (wsClient != null && other.gameObject == wsClient.myPlayer)
		{
			// ミッション管理にゴールを通知
			MissionManager.Instance?.OnGoal();

			// サーバーにゴール通知を送信
			wsClient.SendGoal();
		}

		/*// 2人揃ったらリザルトへ
        if (playersInGoal.Count >= 2)
        {
            isGoalTriggered = true;
            //GoToResult();
        }*/
	}

	private void OnTriggerExit(Collider other)
	{
		if (other.CompareTag("Player1") || other.CompareTag("Player2"))
			playersInGoal.Remove(other.tag);
	}

	/*private void GoToResult() リザルトに2人同時に行くためにサーバーで管理
    {
        // データを静的クラスに保存
        if (MissionManager.Instance != null)
        {
            ResultData.elapsedTime = MissionManager.Instance.GetElapsedSeconds();
            ResultData.missionCount = MissionManager.Instance.GetClearedMissionCount();
        }

        var wsClient = FindObjectOfType<WebSocketClient>();
        if (wsClient != null)
            ResultData.playerName = wsClient.GetPlayerName();

        SceneManager.LoadScene("Result");
    }*/

	#endregion
}