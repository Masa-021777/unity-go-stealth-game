using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// ミッションの進捗管理とUI表示を担当するクラス。
/// タイマー・アイテム取得・ゴール判定・敵発見フラグを管理し、
/// リザルト画面へのフェード遷移も行う。
/// </summary>
public class MissionManager : MonoBehaviour
{
	public static MissionManager Instance;

	#region インスペクター設定

	[Header("UI")]
	public Text missionText;        // ミッション一覧テキスト
	public Text missionClearText;   // 全達成時の演出テキスト
	public Text ClearText;          // ゴール時のCLEAR表示

	[Header("制限時間ミッションの秒数")]
	public int timeLimitSeconds = 180;

	public UnityEngine.UI.Image fadePanel;

	#endregion

	#region 内部状態

	private bool _hasPickedItem = false;
	private bool _mission1Done = false;
	private bool _mission2Done = false;
	private bool _mission3Done = false;
	private bool _mission3Failed = false;
	private bool _isGoalReached = false;
	private bool _isTimeUp = false;

	private bool _timerRunning = false;
	private float _elapsedSeconds = 0f; // ゲーム開始からの経過秒数

	#endregion

	#region Unityイベント

	void Awake()
	{
		if (Instance == null) Instance = this;
		else { Destroy(gameObject); return; }

		if (missionClearText != null) missionClearText.gameObject.SetActive(false);
		if (fadePanel != null) fadePanel.gameObject.SetActive(false);
	}

	void Start()
	{
		RefreshUI();
	}

	void Update()
	{
		if (!_timerRunning) return;

		_elapsedSeconds += Time.deltaTime;

		// 制限時間を超えたらタイムアップ
		if (!_isTimeUp && _elapsedSeconds >= timeLimitSeconds)
		{
			_isTimeUp = true;
			RefreshUI();
		}
		else
		{
			// 経過時間をリアルタイムに更新
			RefreshUI();
		}
	}

	#endregion

	#region 公開メソッド

	/// <summary>ゲーム開始通知を受けてタイマーを開始する</summary>
	public void OnGameStart()
	{
		_elapsedSeconds = 0f;
		_timerRunning = true;
		_isTimeUp = false;
		Debug.Log("タイムスタート");
		RefreshUI();
	}

	/// <summary>アイテム取得通知を受けてフラグを立てる</summary>
	public void OnItemPicked()
	{
		Debug.Log($"OnItemPicked呼ばれた _hasPickedItem: {_hasPickedItem}");
		if (_hasPickedItem) return;
		_hasPickedItem = true;
		RefreshUI();
	}

	/// <summary>自分がゴールしたときに呼ぶ</summary>
	public void OnGoal()
	{
		if (_isGoalReached) return;
		_isGoalReached = true;

		// ミッション1：アイテム取得してゴール
		_mission1Done = _hasPickedItem;

		// ミッション2：制限時間内にゴール
		_mission2Done = !_isTimeUp;

		// ミッション3：敵に見つからずゴール
		_mission3Done = !_mission3Failed;

		RefreshUI();
		CheckAllClear();
		Debug.Log($"ゴール 経過: {_elapsedSeconds:F1}秒 ミッション: アイテム={_mission1Done} 時間={_mission2Done} 敵={_mission3Done}");
	}

	/// <summary>
	/// 敵に発見されたときにEnemyManagerから呼ぶ。
	/// EnemyManagerが完成したらこのメソッドを接続してください。
	/// </summary>
	public void OnEnemyFound()
	{
		_mission3Failed = true;
		RefreshUI();
		Debug.Log("ミッション3失敗");
	}

	/// <summary>達成したミッション数を返す</summary>
	public int GetClearedMissionCount()
	{
		if (!_isGoalReached) return 0;
		int count = 0;
		if (_mission1Done) count++;
		if (_mission2Done) count++;
		if (_mission3Done) count++;
		return count;
	}

	/// <summary>経過秒数を返す（ランキング送信用）</summary>
	public float GetElapsedSeconds() => _elapsedSeconds;

	public bool Mission1Done => _mission1Done;
	public bool Mission2Done => _mission2Done;
	public bool Mission3Done => _mission3Done;

	/// <summary>タイマーを止める</summary>
	public void StopTimer()
	{
		_timerRunning = false;
	}

	/// <summary>CLEAR!テキストを表示する</summary>
	public void ShowClearMessage()
	{
		Debug.Log($"ShowClearMessage呼ばれた ClearText: {ClearText}");
		if (ClearText != null)
		{
			ClearText.gameObject.SetActive(true);
			ClearText.text = "CLEAR！";
		}
	}

	/// <summary>画面中央に任意のメッセージを表示する（相手待ち中など）</summary>
	public void ShowWaitingMessage(string message)
	{
		if (missionClearText != null)
		{
			missionClearText.gameObject.SetActive(true);
			missionClearText.text = message;
		}
	}

	/// <summary>画面を暗転させてリザルト画面へ遷移するコルーチン</summary>
	public IEnumerator FadeToResult()
	{
		yield return new WaitForSeconds(2f);

		if (fadePanel != null)
		{
			fadePanel.gameObject.SetActive(true);
			float elapsed = 0f;
			float duration = 0.5f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				var c = fadePanel.color;
				c.a = Mathf.Lerp(0, 1, elapsed / duration);
				fadePanel.color = c;
				yield return null;
			}
		}

		SceneManager.LoadScene("Result");
	}

	#endregion

	#region UI更新

	private void RefreshUI()
	{
		// ミッション1
		string item1Line;
		if (_hasPickedItem)
			item1Line = "✔ アイテムを取ってクリア";
		else if (_isGoalReached)
			item1Line = "✘ アイテムを取ってクリア";
		else
			item1Line = "　アイテムを取ってクリア";

		// ミッション2
		string mission2Line;
		int elapsed = Mathf.FloorToInt(_elapsedSeconds);
		if (_isGoalReached)
		{
			if (_mission2Done)
				mission2Line = $"✔ {timeLimitSeconds}秒以内にクリア（{elapsed}秒）";
			else
				mission2Line = $"<color=#ffffff88>✘ {timeLimitSeconds}秒以内にクリア（{elapsed}秒）</color>";
		}
		else if (_timerRunning)
		{
			if (_isTimeUp)
				mission2Line = $"<color=#ffffff88>✘ {timeLimitSeconds}秒以内にクリア（{elapsed}秒）</color>";
			else
				mission2Line = $"　{timeLimitSeconds}秒以内にクリア（{elapsed}秒）";
		}
		else
		{
			mission2Line = $"　{timeLimitSeconds}秒以内にクリア";
		}

		// ミッション3
		string item3Line;
		if (_isGoalReached)
			item3Line = $"{(_mission3Done ? "✔" : "✘")} 敵に見つからずクリア";
		else if (_mission3Failed)
			item3Line = "✘ 敵に見つからずクリア";
		else
			item3Line = "　敵に見つからずクリア";

		missionText.text =
			$"・{item1Line}\n" +
			$"・{mission2Line}\n" +
			$"・{item3Line}";
	}

	private void CheckAllClear()
	{
		if (!(_mission1Done && _mission2Done && _mission3Done)) return;
		if (missionClearText != null)
		{
			missionClearText.gameObject.SetActive(true);
			missionClearText.text = "ミッションコンプリート！";
			Invoke(nameof(HideClearText), 3f);
		}
	}

	private void HideClearText()
	{
		if (missionClearText != null) missionClearText.gameObject.SetActive(false);
	}

	#endregion
}