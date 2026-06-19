using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ゲーム中のログを一元管理するクラス。
/// どこからでも AddLog() を呼ぶだけでログパネルに表示できる。
/// 最大4件まで表示し、古いものから消える。
/// </summary>
public class LogManager : MonoBehaviour
{
	public static LogManager Instance;

	#region インスペクター設定

	[Header("UI")]
	public Text logText;

	[Header("1件あたりの表示時間（秒）")]
	public float displayTime = 4f;

	#endregion

	#region 内部状態

	private Queue<string> _logs = new Queue<string>();
	private const int MaxLogs = 4;

	#endregion

	#region Unityイベント

	void Awake()
	{
		if (Instance == null) Instance = this;
		else { Destroy(gameObject); return; }
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// ログを追加して表示する。
	/// color は UnityのRichText形式（例: #ffffff, red, #ff6666）
	/// </summary>
	public void AddLog(string message, string color = "#c0a0ff")
	{
		string line = $"<color={color}>{message}</color>";

		_logs.Enqueue(line);

		if (_logs.Count > MaxLogs)
			_logs.Dequeue();

		RefreshUI();
	}

	#endregion

	#region UI更新

	private void RefreshUI()
	{
		if (logText == null) return;
		var lines = new List<string>(_logs);
		if (!string.IsNullOrEmpty(_waitingLog))
			lines.Add(_waitingLog);
		logText.text = string.Join("\n", lines);
	}

	/// <summary>
	/// 「...」が点滅し続けるログを表示する
	/// </summary>
	public void AddWaitingLog(string message, string color = "#aadd44")
	{
		StopAllCoroutines();
		StartCoroutine(WaitingLogCoroutine(message, color));
	}

	private string _waitingLog = "";

	private IEnumerator WaitingLogCoroutine(string message, string color)
	{
		string[] dots = { ".", "..", "..." };
		int i = 0;
		while (true)
		{
			_waitingLog = $"<color={color}>{message}{dots[i % 3]}</color>";
			RefreshUI();
			i++;
			yield return new WaitForSeconds(0.5f);
		}
	}

	public void StopWaitingLog()
	{
		StopAllCoroutines();
		_waitingLog = "";
		RefreshUI();
	}
	#endregion
}