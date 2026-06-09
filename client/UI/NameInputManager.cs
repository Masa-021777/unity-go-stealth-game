using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タイトル画面での名前入力を管理するクラス。
/// 入力内容を確認してからルーム選択画面へ切り替える。
/// </summary>
public class NameInputManager : MonoBehaviour
{
	#region インスペクター設定

	[Header("UI")]
	public InputField nameInputField;
	public Button confirmButton;
	public GameObject titlePanel;
	public GameObject roomSelectPanel;
	public Text warningText;

	#endregion

	#region 内部状態

	private WebSocketClient _wsClient;
	private Text _placeholder;
	private string _defaultPlaceholderText;
	private Color _defaultPlaceholderColor;

	#endregion

	#region Unityイベント

	void Start()
	{
		_placeholder = nameInputField.placeholder.GetComponent<Text>();
		if (_placeholder != null)
		{
			_defaultPlaceholderText = _placeholder.text;
			_defaultPlaceholderColor = _placeholder.color;
		}

		// サーバー通信クラスを取得
		_wsClient = FindObjectOfType<WebSocketClient>();
		if (_wsClient == null)
		{
			Debug.LogError("WebSocketClientがない");
		}

		// ボタンの紐付け
		if (confirmButton != null)
		{
			confirmButton.onClick.RemoveAllListeners();
			confirmButton.onClick.AddListener(OnConfirmName);
		}
		else
		{
			Debug.LogError("ConfirmButtonない");
		}

		if (warningText != null) warningText.gameObject.SetActive(false);
	}

	#endregion

	#region 内部処理

	/// <summary>
	/// 名前確定ボタンが押されたときの処理。
	/// 空欄なら警告を表示し、入力があればルーム選択画面へ切り替える。
	/// </summary>
	private void OnConfirmName()
	{
		string inputName = nameInputField.text.Trim();

		if (string.IsNullOrEmpty(inputName))
		{
			if (warningText != null)
			{
				warningText.gameObject.SetActive(true);
				warningText.text = "※ユーザーネームを入力してください";
				warningText.color = Color.red;
			}
			return;
		}

		if (warningText != null) warningText.gameObject.SetActive(false);

		if (_wsClient != null)
		{
			_wsClient.SetPlayerName(inputName);
		}

		if (titlePanel != null) titlePanel.SetActive(false);
		var roomSelectManager = FindObjectOfType<RoomSelectManager>();
		if (roomSelectManager != null) roomSelectManager.ShowRoomSelect();
	}

	#endregion
}