using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine.EventSystems;

/// <summary>
/// ルーム選択画面を管理するクラス。
/// サーバーからルーム一覧を定期取得してボタンに反映し、
/// 選択したルームへ接続する。
/// </summary>
public class RoomSelectManager : MonoBehaviour
{
	#region データ構造

	[System.Serializable]
	public class RoomInfo
	{
		public string id;
		public string name;
		public int current;
		public int max;
	}

	[System.Serializable]
	public class RoomListWrapper
	{
		public RoomInfo[] rooms;
	}

	#endregion

	#region インスペクター設定

	public string serverBaseUrl = "https://stealth-game-server.onrender.com";

	public Button[] roomButtons;
	public Text[] roomLabels;          // 各ボタンのテキスト

	public GameObject roomSelectPanel; // ルーム選択パネル
	public GameObject readyPanel;      // 準備待ちパネル
	public GameObject titlePanel;

	public RoomMemberPanel roomMemberPanel;

	[SerializeField] private GameObject roomButton;       // ルーム選択時の初期フォーカス対象
	[SerializeField] private GameObject readyPanelButton; // 準備パネルの初期フォーカス対象

	#endregion

	#region 内部状態

	private WebSocketClient wsClient;
	private string selectedRoomId;

	#endregion

	#region Unityイベント

	void Start()
	{
		wsClient = FindObjectOfType<WebSocketClient>();

		// 接続先の設定に応じてURLを切り替える
		switch (wsClient.serverMode)
		{
			case WebSocketClient.ServerMode.VirtualBox:
				serverBaseUrl = "http://192.168.56.102:8080"; break;
			case WebSocketClient.ServerMode.LocalHost:
				serverBaseUrl = "http://localhost:8080"; break;
			case WebSocketClient.ServerMode.Ngrok:
				serverBaseUrl = wsClient.ngrokUrl; break;
			case WebSocketClient.ServerMode.Render:
				serverBaseUrl = "https://stealth-game-server.onrender.com"; break;
			case WebSocketClient.ServerMode.FlyIO:
				serverBaseUrl = "https://stealth-game-server.fly.dev"; break;
		}

		// 3秒ごとにルーム一覧を自動更新する
		InvokeRepeating("FetchRoomList", 0f, 3f);
	}

	void OnDestroy()
	{
		CancelInvoke("FetchRoomList");
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// 名前入力後にこのパネルを表示する。
	/// 名前が未入力の場合は表示しない。
	/// </summary>
	public void ShowRoomSelect()
	{
		if (string.IsNullOrEmpty(wsClient.GetPlayerName()))
		{
			Debug.LogWarning("名前が入力されていません");
			return;
		}

		StartCoroutine(SelectRoomButton());
		roomSelectPanel.SetActive(true);
		FetchRoomList();
	}

	/// <summary>退出ボタン押下時に接続を切断して選択画面に戻る</summary>
	public void OnQuitButtonClicked()
	{
		wsClient.OnQuitButtonClicked();

		readyPanel.SetActive(false);
		roomSelectPanel.SetActive(true);
		StartCoroutine(SelectRoomButton());

		FetchRoomList();
	}

	#endregion

	#region 内部処理

	/// <summary>ルーム選択ボタンにフォーカスを当てる</summary>
	private IEnumerator SelectRoomButton()
	{
		yield return new WaitForEndOfFrame();
		EventSystem.current.SetSelectedGameObject(roomButton);
	}

	/// <summary>準備パネルのボタンにフォーカスを当てる</summary>
	private IEnumerator ReadyPanel()
	{
		yield return new WaitForEndOfFrame();
		Debug.Log("移行");
		EventSystem.current.SetSelectedGameObject(readyPanelButton);
	}

	private void FetchRoomList()
	{
		if (!gameObject.activeInHierarchy) return;
		StartCoroutine(FetchRoomListCoroutine());
	}

	/// <summary>サーバーからルーム一覧を取得してボタンに反映する</summary>
	private IEnumerator FetchRoomListCoroutine()
	{
		using (UnityWebRequest req = UnityWebRequest.Get($"{serverBaseUrl}/rooms"))
		{
			req.SetRequestHeader("ngrok-skip-browser-warning", "true");
			yield return req.SendWebRequest();

			if (req.result == UnityWebRequest.Result.Success)
			{
				string json = "{\"rooms\":" + req.downloadHandler.text + "}";
				RoomListWrapper wrapper = JsonUtility.FromJson<RoomListWrapper>(json);

				for (int i = 0; i < roomButtons.Length && i < wrapper.rooms.Length; i++)
				{
					RoomInfo room = wrapper.rooms[i];
					bool isFull = room.current >= room.max;

					roomLabels[i].text = $"{room.name}: {room.current}/{room.max}人";
					roomButtons[i].interactable = !isFull;

					// クロージャ対策でローカル変数に退避
					string roomId = room.id;
					roomButtons[i].onClick.RemoveAllListeners();
					roomButtons[i].onClick.AddListener(() => OnRoomButtonClicked(roomId));
				}
			}
			else
			{
				Debug.LogWarning("ルーム一覧取得失敗: " + req.error);
			}
		}
	}

	/// <summary>
	/// ルームボタンが押されたとき。
	/// 接続してメンバーパネルを初期化し、準備待ち画面へ切り替える。
	/// </summary>
	private void OnRoomButtonClicked(string roomId)
	{
		if (string.IsNullOrEmpty(wsClient.GetPlayerName()))
		{
			Debug.LogWarning("名前が入力されていません");
			return;
		}

		selectedRoomId = roomId;
		wsClient.ConnectToRoom(roomId);

		if (roomMemberPanel != null)
		{
			roomMemberPanel.ClearAll();
			string myName = wsClient.GetPlayerName();
			roomMemberPanel.AddOrUpdateMember("self", myName, false);
		}
		/*string myName = FindObjectOfType<WebSocketClient>().GetPlayerName();
        if (roomMemberPanel != null)
            roomMemberPanel.AddOrUpdateMember("self", myName, false);*/

		roomSelectPanel.SetActive(false);
		readyPanel.SetActive(true);
		StartCoroutine(ReadyPanel());
	}

	#endregion
}