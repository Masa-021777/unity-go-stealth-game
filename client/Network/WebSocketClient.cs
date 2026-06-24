using UnityEngine;
using NativeWebSocket;
using System.Text;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

#region 受信メッセージ定義

// 接続初期化メッセージ
[System.Serializable]
public class InitMessage
{
	public string type;
	public string id;
	public string name;
	public int player_number;
	public PositionData position;
	public PositionData rotation;
}

// 既存プレイヤー一覧メッセージ（後から入室した側が受信）
[System.Serializable]
public class ExistingPlayersMessage
{
	public string type;
	public PlayerData[] players;
}

// 新規プレイヤー参加通知メッセージ
[System.Serializable]
public class PlayerJoinedMessage
{
	public string type;
	public string id;
	public string name;
	public int player_number;
	public PositionData position;
	public PositionData rotation;
}

// プレイヤー移動・動作状態の同期メッセージ
[System.Serializable]
public class PlayerMoveMessage
{
	public string type;
	public string id;
	public PositionData position;
	public PositionData rotation;
	public string anim_state;   // "idle" / "run" / "sneak"
	public string anim_trigger; // "PunchEnemy" / "PunchSwitch" など
}

// プレイヤー退室通知メッセージ
[System.Serializable]
public class PlayerLeftMessage
{
	public string type;
	public string id;
	public string name;
}

[System.Serializable]
public class ExistingPlayersWrapper
{
	public PlayerData[] players;
}

// 残り時間更新メッセージ
[System.Serializable]
public class TimerUpdateMessage
{
	public string type;        // "timer_update"
	public int time_remaining; // 残り秒数
}

// ゴール関連メッセージ
[System.Serializable]
public class GoalMessage
{
	public string type;    // "goal"
	public string id;      // ゴールしたプレイヤーID
	public string name;    // プレイヤー名
	public float elapsed;  // クリアタイム（秒）
}

// 敵の位置・状態同期メッセージ
[System.Serializable]
public class EnemyMoveMessage
{
	public string type;         // "enemy_move"
	public int enemy_index;     // シーン内の敵の番号
	public float x;
	public float y;
	public float z;
	public float angle;         // 向き（Y軸回転）
	public float light_r;
	public float light_g;
	public float light_b;
	public float last_sound_x;
	public float last_sound_z;
	public string reaction;     // "" / "!" / "?"
	public string sender_id;
}

// スイッチ操作の同期メッセージ
[System.Serializable]
public class SwitchActivatedMessage
{
	public string type;
	public int switch_id;
}

// リスポーン位置の同期メッセージ
[System.Serializable]
public class RespawnMessage
{
	public string type;
	public string id;
	public PositionData position;
}

// 意思疎通用チャットメッセージ
[System.Serializable]
public class ChatMessage
{
	public string type;
	public string message;
	public string sender_id;
	public string sender_name;
}

#endregion

/// <summary>
/// 送信メッセージを組み立てるクラス。
/// JSON直書きによる対応漏れを防ぐ。
/// </summary>
public static class SendMessageBuilder
{
	public static string Goal()
		=> "{\"type\":\"goal\"}";

	public static string PlayerMove(string id, Vector3 pos, Vector3 rot, string animState, string trigger)
		=> $"{{\"type\":\"player_move\",\"id\":\"{id}\"," +
		   $"\"position\":{{\"x\":{pos.x},\"y\":{pos.y},\"z\":{pos.z}}}," +
		   $"\"rotation\":{{\"x\":{rot.x},\"y\":{rot.y}}}," +
		   $"\"anim_state\":\"{animState}\"," +
		   $"\"anim_trigger\":\"{trigger}\"}}";

	public static string Respawn(string id, Vector3 pos)
		=> $"{{\"type\":\"respawn\",\"id\":\"{id}\"," +
		   $"\"position\":{{\"x\":{pos.x},\"y\":{pos.y},\"z\":{pos.z}}}}}";

	public static string SwitchActivated(int switchId)
		=> $"{{\"type\":\"switch_activated\",\"switch_id\":{switchId}}}";

	public static string ItemPicked()
		=> "{\"type\":\"item_picked\"}";

	public static string EnemyStun(int enemyIndex, string senderId)
		=> $"{{\"type\":\"enemy_stun\",\"enemy_index\":{enemyIndex},\"sender_id\":\"{senderId}\"}}";

	public static string EnemyStunCancel(int enemyIndex, string senderId)
		=> $"{{\"type\":\"enemy_stun_cancel\",\"enemy_index\":{enemyIndex},\"sender_id\":\"{senderId}\"}}";

	public static string RemoteRespawn(string id)
		=> $"{{\"type\":\"remote_respawn\",\"id\":\"{id}\"}}";

	public static string Chat(string message, string senderId)
	=> $"{{\"type\":\"chat\",\"message\":\"{message}\",\"sender_id\":\"{senderId}\"}}";

	public static string EnemyMove(int index, Vector3 pos, float angle, Color light, string reaction, Vector3 lastSound)
		=> $"{{\"type\":\"enemy_move\",\"enemy_index\":{index}," +
		   $"\"x\":{pos.x},\"y\":{pos.y},\"z\":{pos.z},\"angle\":{angle}," +
		   $"\"light_r\":{light.r},\"light_g\":{light.g},\"light_b\":{light.b}," +
		   $"\"reaction\":\"{reaction}\"," +
		   $"\"last_sound_x\":{lastSound.x},\"last_sound_z\":{lastSound.z}}}";
}

/// <summary>
/// サーバーとの通信を一元管理するクラス。
/// プレイヤーの移動・動作・敵の位置・ゲームの進行状況をリアルタイムで送受信する。
/// シーンをまたいでも破棄されずに動作し続ける。
/// </summary>
public class WebSocketClient : MonoBehaviour
{
	#region インスペクター設定

	public GameObject playerPrefab;
	public GameObject myPlayer;
	public string serverUrl = "ws://192.168.56.102:8080/ws?room_id=test&name=Player1";

	public Material localPlayerMaterial;  // 自分（Player1）のマテリアル
	public Material remotePlayerMaterial; // 相手（Player2）のマテリアル

	public RoomMemberPanel roomMemberPanel;

	public string ngrokUrl = "https://rice-washer-suitcase.ngrok-free.dev";
	public ServerMode serverMode = ServerMode.VirtualBox;

	// 接続先の切り替え用
	public enum ServerMode
	{
		VirtualBox, // 仮想環境
		LocalHost,  // ローカル実行
		Ngrok,      // 外部公開トンネル
		Render,     // クラウドサービス（Render）
		FlyIO       // クラウドサービス（Fly.io）
	}

	#endregion

	#region フィールド

	private WebSocket websocket;
	public string myId;
	private Dictionary<string, GameObject> playerObjects = new Dictionary<string, GameObject>();
	private bool isGameSceneLoaded;
	private List<string> pendingMessages = new List<string>();

	private float sendInterval = 0.05f; // 位置送信間隔（秒）
	private float timer = 0f;

	private string playerName;

	private Vector3 pendingSpawnPos = Vector3.zero;
	private bool hasSpawnPos = false;

	private Dictionary<string, Vector3> targetPositions = new Dictionary<string, Vector3>();
	private Dictionary<string, Quaternion> targetRotations = new Dictionary<string, Quaternion>();

	private Dictionary<int, Vector3> spawnPositions = new Dictionary<int, Vector3>();
	public int myPlayerNumber = 0; // 1=ホスト（敵を送信）, 2=ゲスト（敵を受信）
	private GameObject[] _enemyObjects; // シーン内の全敵

	private Dictionary<int, Vector3> enemyTargetPositions = new Dictionary<int, Vector3>();
	private Dictionary<int, float> enemyTargetAngles = new Dictionary<int, float>();
	private float enemySendTimer = 0f;
	private float enemySendInterval = 0.05f;

	private Vector3 _lastRemotePosition = Vector3.zero;
	private bool _stunSent = false;
	private int _connectGeneration = 0;

	#endregion

	#region プレイヤー番号判定

	/// <summary>自分がホスト（Player1）かどうか</summary>
	public bool IsHostPlayer() => myPlayerNumber == 1;

	/// <summary>自分がゲスト（Player2）かどうか</summary>
	public bool IsGuestPlayer() => myPlayerNumber == 2;

	#endregion

	#region Unityイベント

	void Awake()
	{
		var existing = FindObjectsOfType<WebSocketClient>();
		if (existing.Length > 1)
		{
			// 自分より先に存在するインスタンスがあれば自分を破棄
			foreach (var other in existing)
			{
				if (other != this)
				{
					Destroy(gameObject);
					return;
				}
			}
		}
		Application.runInBackground = true;
		Application.targetFrameRate = 60;
		DontDestroyOnLoad(this.gameObject);
		SceneManager.sceneLoaded += OnSceneLoaded;
		isGameSceneLoaded = SceneManager.GetActiveScene().name == "MapTest";
	}

	async void Start()
	{
		Application.runInBackground = true;
		playerName = "";
	}

	async void Update()
	{
		// 相手プレイヤーの位置をなめらかに反映する
		foreach (var id in targetPositions.Keys)
		{
			if (id == myId) continue;
			if (!playerObjects.ContainsKey(id)) continue;
			var obj = playerObjects[id];
			if (obj == null) continue;
			obj.transform.position = Vector3.Lerp(obj.transform.position, targetPositions[id], Time.deltaTime * 25f);
			if (targetRotations.ContainsKey(id))
				obj.transform.rotation = Quaternion.Lerp(obj.transform.rotation, targetRotations[id], Time.deltaTime * 25f);
		}

		if (websocket != null && websocket.State == WebSocketState.Open)
		{
#if !UNITY_WEBGL || UNITY_EDITOR
			websocket.DispatchMessageQueue();
#endif
			if (!string.IsNullOrEmpty(myId))
			{
				timer += Time.deltaTime;
				if (timer >= sendInterval) { SendPosition(); timer = 0f; }
				UpdateEnemySync();
			}
		}
	}

	private async void OnApplicationQuit()
	{
		if (websocket != null && websocket.State == WebSocketState.Open) await websocket.Close();
	}

	#endregion

	#region 接続管理

	/// <summary>
	/// 接続先の設定に応じた接続先アドレスを返す
	/// </summary>
	private string GetServerUrl(string roomId)
	{
		switch (serverMode)
		{
			case ServerMode.VirtualBox:
				return $"ws://192.168.56.102:8080/ws?room_id={roomId}&name={playerName}";
			case ServerMode.LocalHost:
				return $"ws://localhost:8080/ws?room_id={roomId}&name={playerName}";
			case ServerMode.Ngrok:
				return $"wss://{ngrokUrl.Replace("https://", "")}/ws?room_id={roomId}&name={playerName}";
			case ServerMode.Render:
				return $"wss://stealth-game-server.onrender.com/ws?room_id={roomId}&name={playerName}";
			case ServerMode.FlyIO:
				return $"wss://stealth-game-server.fly.dev/ws?room_id={roomId}&name={playerName}";
			default:
				return $"ws://192.168.56.102:8080/ws?room_id={roomId}&name={playerName}";
		}
	}

	/// <summary>
	/// シーンが切り替わったときの処理。ゲームシーン以外ではプレイヤーを削除する。
	/// </summary>
	void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		isGameSceneLoaded = scene.name == "MapTest";

		if (scene.name == "Title")
		{
			// Titleシーン再ロード時に参照を取り直す
			roomMemberPanel = FindObjectOfType<RoomMemberPanel>();
		}

		if (scene.name != "MapTest")
		{
			ClearRemotePlayers();
		}
		else
		{
			Invoke("ProcessPendingMessages", 0.5f);
			Invoke("DelayedGameStart", 0.6f);
		}
	}

	/// <summary>
	/// 指定したルームに接続する。すでに接続中の場合は切断してから再接続する。
	/// </summary>
	public async void ConnectToRoom(string roomId)
	{
		var ws = new WebSocket(GetServerUrl(roomId));
		ws.OnOpen += OnWebSocketOpened;
		ws.OnMessage += OnMessageReceived;
		//ws.OnError += (e) => Debug.Log($"接続エラー: {e}");
		websocket = ws;
		await ws.Connect();
	}

	/// <summary>プレイヤー名を設定する</summary>
	public void SetPlayerName(string name) { playerName = name; }

	/// <summary>プレイヤー名を返す</summary>
	public string GetPlayerName() => playerName;

	/// <summary>
	/// 退室ボタン押下時に接続を切断してプレイヤーを削除する
	/// </summary>
	public async void OnQuitButtonClicked()
	{

		if (websocket != null)
		{
			websocket.OnMessage -= OnMessageReceived;
			websocket.OnOpen -= OnWebSocketOpened;
			if (websocket.State == WebSocketState.Open) await websocket.Close();
			websocket = null;
		}

		if (myPlayer != null) { Destroy(myPlayer); myPlayer = null; }
		ClearRemotePlayers();
		myId = null;
		spawnPositions.Clear();
		pendingMessages.Clear();
	}

	private void OnWebSocketOpened() { Debug.Log("接続成功"); }

	/// <summary>
	/// 準備完了ボタン押下時にサーバーへ準備完了を通知する
	/// </summary>
	public async void OnReadyButtonClicked()
	{
		//Debug.Log($"準備完了押した websocket={websocket?.State}");
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		string json = "{\"type\":\"ready\",\"position\":{\"x\":0,\"y\":1,\"z\":0}}";
		await websocket.SendText(json);
	}

	/// <summary>
	/// 接続を切断してゲームセッションの状態を初期化する。
	/// タイトル復帰時に呼ぶ。
	/// </summary>
	public async System.Threading.Tasks.Task DisconnectAndReset()
	{
		//_connectGeneration++;
		if (websocket != null)
		{
			websocket.OnMessage -= OnMessageReceived;
			websocket.OnOpen -= OnWebSocketOpened;
			if (websocket.State == WebSocketState.Open)
			{
				try { await websocket.Close(); } catch { }
			}
			websocket = null;
		}

		if (myPlayer != null) { Destroy(myPlayer); myPlayer = null; }
		ClearRemotePlayers();

		myId = null;
		//playerName = "";
		myPlayerNumber = 0;
		spawnPositions.Clear();
		pendingMessages.Clear();
		hasSpawnPos = false;
		pendingSpawnPos = Vector3.zero;
		_enemyObjects = null;
		enemyTargetPositions.Clear();
		enemyTargetAngles.Clear();
	}
	#endregion

	#region メッセージ受信

	/// <summary>
	/// サーバーからメッセージを受信したときの処理。
	/// ゲームシーン読み込み前はキューに保存しておく。
	/// </summary>
	private void OnMessageReceived(byte[] bytes)
	{
		string json = Encoding.UTF8.GetString(bytes);
		//if (json.Contains("enemy_stun"))
		//Debug.Log($"OnMessageReceived: isGameSceneLoaded={isGameSceneLoaded} json={json}");

		if (!isGameSceneLoaded)
		{
			if (json.Contains("\"type\":\"start_game\""))
			{
				SceneManager.LoadScene("MapTest");
				return;
			}
			if (json.Contains("\"type\":\"init\"")) HandleInitForLobby(json);
			else if (json.Contains("\"type\":\"player_joined\"")) HandlePlayerJoinedForLobby(json);
			else if (json.Contains("\"type\":\"player_left\"")) HandlePlayerLeftForLobby(json);
			else if (json.Contains("\"type\":\"player_ready\"")) HandlePlayerReadyForLobby(json);
			else if (json.Contains("\"type\":\"existing_players\"")) HandleExistingPlayersForLobby(json);
			pendingMessages.Add(json);
			return;
		}
		ProcessMessage(json);
	}

	/// <summary>
	/// 保存しておいたメッセージをまとめて処理する
	/// </summary>
	private void ProcessPendingMessages()
	{
		foreach (var json in pendingMessages) ProcessMessage(json);
		pendingMessages.Clear();
	}

	/// <summary>
	/// メッセージの種類に応じて対応する処理に振り分ける
	/// </summary>
	private void ProcessMessage(string json)
	{
		if (json.Contains("\"type\":\"init\"")) HandleInitMessage(json);
		else if (json.Contains("\"type\":\"existing_players\"")) HandleExistingPlayersMessage(json);
		else if (json.Contains("\"type\":\"player_joined\"")) HandlePlayerJoinedMessage(json);
		else if (json.Contains("\"type\":\"player_move\"")) HandlePlayerMoveMessage(json);
		else if (json.Contains("\"type\":\"player_left\"")) HandlePlayerLeftMessage(json);
		else if (json.Contains("\"type\":\"item_picked\"")) HandleItemPickedMessage(json);
		else if (json.Contains("\"type\":\"timer_update\"")) HandleTimerUpdate(json);
		else if (json.Contains("\"type\":\"enemy_move\"")) HandleEnemyMoveMessage(json);
		else if (json.Contains("\"type\":\"remote_respawn\"")) HandleRemoteRespawnMessage(json);
		else if (json.Contains("\"type\":\"player_goal\"")) HandlePlayerGoalMessage(json);
		else if (json.Contains("\"type\":\"all_goal\"")) HandleAllGoalMessage(json);
		else if (json.Contains("\"type\":\"switch_activated\"")) HandleSwitchActivatedMessage(json);
		else if (json.Contains("\"type\":\"enemy_stun_cancel\"")) HandleEnemyStunCancelMessage(json);
		else if (json.Contains("\"type\":\"enemy_stun\"")) HandleEnemyStunMessage(json);
		else if (json.Contains("\"type\":\"respawn\"")) HandleRespawnMessage(json);
		else if (json.Contains("\"type\":\"chat\"")) HandleChatMessage(json);
		else if (json.Contains("\"type\":\"start_game\""))
		{
			if (SceneManager.GetActiveScene().name == "MapTest")
			{
				ProcessPendingMessages();
				MissionManager.Instance?.OnGameStart();
			}
			else
			{
				SceneManager.LoadScene("MapTest");
			}
		}
		else if (json.Contains("\"type\":\"player_ready\""))
		{
			var msg = JsonUtility.FromJson<PlayerReadyMessage>(json);
			if (roomMemberPanel != null) roomMemberPanel.SetReady(msg.id, true);
		}
	}

	#endregion

	#region メッセージ処理（ゲームシーン用）

	/// <summary>
	/// 接続初期化：自分のプレイヤーを生成してカメラを設定する
	/// </summary>
	private void HandleInitMessage(string json)
	{
		if (SceneManager.GetActiveScene().name != "MapTest") return;
		if (myPlayer != null) return;
		InitMessage init = JsonUtility.FromJson<InitMessage>(json);
		myId = init.id;

		if (roomMemberPanel != null)
		{
			roomMemberPanel.RemoveMember("self");
			roomMemberPanel.AddOrUpdateMember(myId, playerName, false);
		}
		if (playerPrefab == null) return;

		myPlayer = Instantiate(playerPrefab);
		AttachNameTag(myPlayer, playerName, false);
		myPlayer.tag = "Player" + init.player_number;
		myPlayerNumber = init.player_number;

		var eg = FindObjectOfType<ElementGenerator>();

		// ゲスト（Player2）は敵のAIを止め、サーバーからの位置情報で動かす
		if (IsGuestPlayer())
		{
			var enemies = GameObject.FindGameObjectsWithTag("Enemy");
			foreach (var e in enemies)
			{
				var em = e.GetComponent<EnemyManager>();
				if (em != null) em.isRemoteControlled = true;
			}
		}

		if (IsHostPlayer())
			myPlayer.GetComponentInChildren<Renderer>().material = localPlayerMaterial;
		else
			myPlayer.GetComponentInChildren<Renderer>().material = remotePlayerMaterial;

		var elementGenerator = FindObjectOfType<ElementGenerator>();
		if (elementGenerator != null) elementGenerator.SetRemotePlayerTransform(myPlayer.transform);

		var controller = myPlayer.GetComponent<PlayerController>();
		if (controller != null) controller.isLocalPlayer = true;
		myPlayer.GetComponent<PlayerController>().enabled = true;
		DontDestroyOnLoad(myPlayer);
		playerObjects[myId] = myPlayer;

		if (spawnPositions.ContainsKey(init.player_number))
			myPlayer.transform.position = spawnPositions[init.player_number];
		else if (init.position != null)
			myPlayer.transform.position = new Vector3(init.position.x, init.position.y, init.position.z);

		if (GlobalCamera.Instance != null)
			GlobalCamera.Instance.SetTarget(myPlayer.transform);
		//else
		//Debug.LogWarning("GlobalCamera.Instanceがnull");

		if (elementGenerator != null) elementGenerator.SetRemotePlayerTransform(myPlayer.transform);

		// ゲーム開始時にチュートリアルを表示する
		if (TutorialManager.Instance != null)
		{
			TutorialManager.Instance.ShowTutorial(() =>
			{
				//Debug.Log("ゲームスタート（チュートリアル後）");
			});
		}

		// 敵に自分のプレイヤーを渡す（ホストのみ）
		if (IsHostPlayer())
		{
			var enemyList = GameObject.FindGameObjectsWithTag("Enemy");
			foreach (var e in enemyList)
			{
				var em = e.GetComponent<EnemyManager>();
				//if (em != null) em.SetTargetPlayer(myPlayer.transform);
			}
		}
	}

	/// <summary>
	/// 既存プレイヤー一覧：後から入室した場合に先に入っていたプレイヤーを生成する
	/// </summary>
	private void HandleExistingPlayersMessage(string json)
	{
		if (SceneManager.GetActiveScene().name != "MapTest") return;
		ExistingPlayersMessage msg = JsonUtility.FromJson<ExistingPlayersMessage>(json);
		if (msg != null && msg.players != null)
		{
			foreach (var player in msg.players)
			{
				if (player.id != myId)
				{
					SpawnRemotePlayer(player);
					if (roomMemberPanel != null) roomMemberPanel.AddOrUpdateMember(player.id, player.name, false);
				}
			}
		}
	}

	/// <summary>
	/// 新規プレイヤー参加：後から入室したプレイヤーを生成する
	/// </summary>
	private void HandlePlayerJoinedMessage(string json)
	{
		if (SceneManager.GetActiveScene().name != "MapTest") return;
		var msg = JsonUtility.FromJson<PlayerJoinedMessage>(json);
		if (string.IsNullOrEmpty(myId) || msg.id == myId) return;
		if (playerObjects.ContainsKey(msg.id)) return;

		var player = new PlayerData
		{
			id = msg.id,
			name = msg.name,
			player_number = msg.player_number,
			position = new PositionData { x = msg.position.x, y = msg.position.y, z = msg.position.z }
		};
		SpawnRemotePlayer(player);
		if (roomMemberPanel != null) roomMemberPanel.AddOrUpdateMember(msg.id, msg.name, false);
	}

	/// <summary>
	/// プレイヤー移動：位置・向き・動作状態を反映する。
	/// ホスト側は相手の足音を敵に通知する。
	/// </summary>
	private void HandlePlayerMoveMessage(string json)
	{
		var msg = JsonUtility.FromJson<PlayerMoveMessage>(json);
		if (msg == null || msg.position == null) return;
		if (!playerObjects.ContainsKey(msg.id)) return;

		GameObject targetPlayer = playerObjects[msg.id];
		if (targetPlayer == null) { playerObjects.Remove(msg.id); return; }

		targetPositions[msg.id] = new Vector3(msg.position.x, msg.position.y, msg.position.z);
		if (msg.rotation != null)
			targetRotations[msg.id] = Quaternion.Euler(msg.rotation.x, msg.rotation.y, 0);

		// ホスト側のみ：相手の足音を敵に通知する
		if (IsHostPlayer() && msg.id != myId)
		{
			Vector3 newPos = new Vector3(msg.position.x, msg.position.y, msg.position.z);
			if (msg.anim_state == "run" || msg.anim_trigger == "PunchSwitch")
			{
				var enemies = GameObject.FindGameObjectsWithTag("Enemy");
				foreach (var e in enemies)
				{
					var em = e.GetComponent<EnemyManager>();
					if (em != null)
						em.HandleSoundFromRemote(newPos, 1f);
				}
			}
		}

		// 動作状態をアニメーションに反映する
		var anim = targetPlayer.GetComponentInChildren<Animator>();
		if (anim != null && !string.IsNullOrEmpty(msg.anim_state))
		{
			anim.SetBool("Run", msg.anim_state == "run");
			anim.SetBool("Sneak", msg.anim_state == "sneak");
		}

		if (!string.IsNullOrEmpty(msg.anim_trigger))
			anim.SetTrigger(msg.anim_trigger);
	}

	/// <summary>
	/// プレイヤー退室：退室したプレイヤーのオブジェクトを削除する
	/// </summary>
	private void HandlePlayerLeftMessage(string json)
	{
		var msg = JsonUtility.FromJson<PlayerLeftMessage>(json);
		if (playerObjects.ContainsKey(msg.id))
		{
			Destroy(playerObjects[msg.id]);
			playerObjects.Remove(msg.id);
		}
		if (roomMemberPanel != null) roomMemberPanel.RemoveMember(msg.id);
	}

	/// <summary>
	/// アイテム取得：ミッション管理に取得を通知してログに表示する
	/// </summary>
	private void HandleItemPickedMessage(string json)
	{
		/*var msg = JsonUtility.FromJson<GoalMessage>(json);
        if (msg.id == myId)*/
		MissionManager.Instance?.OnItemPicked();
		LogManager.Instance?.AddLog("アイテムを取得した", "#aadd44");
	}

	/// <summary>
	/// 片方がゴール：自分かどうかでログメッセージを切り替える
	/// </summary>
	private void HandlePlayerGoalMessage(string json)
	{
		var msg = JsonUtility.FromJson<GoalMessage>(json);

		if (msg.id == myId)
		{
			MissionManager.Instance?.OnGoal();
			LogManager.Instance?.AddWaitingLog("ゴールした！相手を待っています", "#aadd44");
		}
		else
		{
			LogManager.Instance?.AddLog("味方がゴールした！早くゴールへ向かおう！", "#aadd44");
		}
	}

	/// <summary>
	/// 全員ゴール：クリア演出を行いリザルト画面へ遷移する
	/// </summary>
	private void HandleAllGoalMessage(string json)
	{
		MissionManager.Instance?.StopTimer();
		MissionManager.Instance?.ShowClearMessage();
		LogManager.Instance?.StopWaitingLog();

		if (MissionManager.Instance != null)
		{
			ResultData.elapsedTime = MissionManager.Instance.GetElapsedSeconds();
			ResultData.missionCount = MissionManager.Instance.GetClearedMissionCount();
		}
		ResultData.playerName = playerName;
		foreach (var obj in playerObjects.Values)
		{
			var nameTag = obj.GetComponentInChildren<NameTag>();
			if (nameTag != null)
				ResultData.remotePlayerName = nameTag.GetName();
		}

		MissionManager.Instance?.StartCoroutine(
			MissionManager.Instance.FadeToResult()
		);
	}

	/// <summary>
	/// リザルト画面への遷移処理（FadeToResult内から呼ばれる）
	/// </summary>
	private void LoadResultScene()
	{
		var msg_dummy = new GoalMessage();
		if (MissionManager.Instance != null)
		{
			ResultData.elapsedTime = MissionManager.Instance.GetElapsedSeconds();
			ResultData.missionCount = MissionManager.Instance.GetClearedMissionCount();
		}
		ResultData.playerName = playerName;
		foreach (var obj in playerObjects.Values)
		{
			var nameTag = obj.GetComponentInChildren<NameTag>();
			if (nameTag != null)
				ResultData.remotePlayerName = nameTag.GetName();
		}
		SceneManager.LoadScene("Result");
	}

	/// <summary>
	/// 残り時間更新：現時点では未使用
	/// </summary>
	private void HandleTimerUpdate(string json)
	{
		var msg = JsonUtility.FromJson<TimerUpdateMessage>(json);
	}

	/// <summary>
	/// 敵の移動同期（ゲスト側のみ処理）：位置・ライト・反応・音検知位置を反映する
	/// </summary>
	private void HandleEnemyMoveMessage(string json)
	{
		if (IsHostPlayer()) return;
		var msg = JsonUtility.FromJson<EnemyMoveMessage>(json);
		enemyTargetPositions[msg.enemy_index] = new Vector3(msg.x, msg.y, msg.z);
		enemyTargetAngles[msg.enemy_index] = msg.angle;

		if (_enemyObjects == null || _enemyObjects.Length == 0)
			_enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");

		if (msg.enemy_index < _enemyObjects.Length)
		{
			var light = _enemyObjects[msg.enemy_index].GetComponentInChildren<Light>();
			if (light != null)
				light.color = new Color(msg.light_r, msg.light_g, msg.light_b);

			var em = _enemyObjects[msg.enemy_index].GetComponent<EnemyManager>();
			if (em != null)
			{
				em.SetReactionState(msg.reaction);
				em.SetLastSoundPosition(new Vector3(msg.last_sound_x, 0, msg.last_sound_z));
			}
		}
	}

	/// <summary>
	/// チャットメッセージの受信：ログに表示する
	/// </summary>
	private void HandleChatMessage(string json)
	{
		var msg = JsonUtility.FromJson<ChatMessage>(json);
		if (msg.sender_id == myId) return;
		QuickChatManager.Instance?.OnChatReceived(msg.message, msg.sender_name);
	}

	#endregion

	#region メッセージ処理（ロビー用）

	/// <summary>接続初期化（ロビー用）：自分のIDを設定してメンバー表示を更新する</summary>
	private void HandleInitForLobby(string json)
	{
		InitMessage init = JsonUtility.FromJson<InitMessage>(json);
		myId = init.id;
		if (roomMemberPanel != null)
		{
			roomMemberPanel.RemoveMember("self");
			roomMemberPanel.AddOrUpdateMember(myId, playerName, false);
		}
	}

	/// <summary>新規プレイヤー参加（ロビー用）：メンバー表示に追加する</summary>
	private void HandlePlayerJoinedForLobby(string json)
	{
		var msg = JsonUtility.FromJson<PlayerJoinedMessage>(json);
		if (msg == null || msg.id == myId) return;
		if (roomMemberPanel != null) roomMemberPanel.AddOrUpdateMember(msg.id, msg.name, false);
	}

	/// <summary>既存プレイヤー一覧（ロビー用）：メンバー表示に追加する</summary>
	private void HandleExistingPlayersForLobby(string json)
	{
		ExistingPlayersMessage msg = JsonUtility.FromJson<ExistingPlayersMessage>(json);
		if (msg == null || msg.players == null) return;
		foreach (var player in msg.players)
			if (player.id != myId && roomMemberPanel != null)
				roomMemberPanel.AddOrUpdateMember(player.id, player.name, player.is_ready);
	}

	/// <summary>プレイヤー退室（ロビー用）：メンバー表示から削除する</summary>
	private void HandlePlayerLeftForLobby(string json)
	{
		var msg = JsonUtility.FromJson<PlayerLeftMessage>(json);
		if (roomMemberPanel != null) roomMemberPanel.RemoveMember(msg.id);
	}

	/// <summary>準備完了通知（ロビー用）：メンバーの準備状態を更新する</summary>
	private void HandlePlayerReadyForLobby(string json)
	{
		var msg = JsonUtility.FromJson<PlayerReadyMessage>(json);
		if (roomMemberPanel != null) roomMemberPanel.SetReady(msg.id, true);
	}

	/// <summary>
	/// スイッチ操作の受信：対応するスイッチの処理を呼んでログに表示する
	/// </summary>
	private void HandleSwitchActivatedMessage(string json)
	{
		var msg = JsonUtility.FromJson<SwitchActivatedMessage>(json);
		var eg = FindObjectOfType<ElementGenerator>();
		if (eg == null) return;

		foreach (var sw in eg.GetSwitchList())
		{
			if (sw.targetEnemyID == msg.switch_id)
			{
				sw.OnSwitchActivated();
				break;
			}
		}
		LogManager.Instance?.AddLog("どこかのギミックが作動した", "#ffcc44");
	}

	/// <summary>
	/// 敵スタンの受信：自分が送ったものは無視し、該当の敵にスタンを適用する
	/// </summary>
	private void HandleEnemyStunMessage(string json)
	{
		var msg = JsonUtility.FromJson<EnemyMoveMessage>(json);
		if (msg.sender_id == myId) return;

		if (_enemyObjects == null || _enemyObjects.Length == 0)
			_enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");

		foreach (var e in _enemyObjects)
		{
			var em = e.GetComponent<EnemyManager>();
			if (em != null && em.enemyID == msg.enemy_index)
			{
				em.PlayAnimationEnemy();
				break;
			}
		}
	}

	/// <summary>
	/// 敵スタン解除の受信：自分が送ったものは無視し、該当の敵のスタンを解除する
	/// </summary>
	private void HandleEnemyStunCancelMessage(string json)
	{
		var msg = JsonUtility.FromJson<EnemyMoveMessage>(json);
		if (msg.sender_id == myId) return;

		if (_enemyObjects == null || _enemyObjects.Length == 0)
			_enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");

		foreach (var e in _enemyObjects)
		{
			var em = e.GetComponent<EnemyManager>();
			if (em != null && em.enemyID == msg.enemy_index)
			{
				em.StunCancel();
				break;
			}
		}
	}

	#endregion

	#region プレイヤー生成・削除

	/// <summary>
	/// 相手プレイヤーを生成する。番号に応じてマテリアルを設定する。
	/// </summary>
	private void SpawnRemotePlayer(PlayerData player)
	{
		//Debug.Log($"SpawnRemotePlayer: id={player.id}, player_number={player.player_number}");
		if (playerObjects.ContainsKey(player.id)) return;

		GameObject newPlayer = Instantiate(playerPrefab);
		newPlayer.tag = "Player" + player.player_number;
		if (player.player_number == 1)
			newPlayer.GetComponentInChildren<Renderer>().material = localPlayerMaterial;
		else
			newPlayer.GetComponentInChildren<Renderer>().material = remotePlayerMaterial;

		AudioListener remoteListener = newPlayer.GetComponent<AudioListener>();
		if (remoteListener != null) Destroy(remoteListener);

		var controller = newPlayer.GetComponent<PlayerController>();
		if (controller != null) controller.isLocalPlayer = false;

		var col = newPlayer.GetComponent<Collider>();
		if (col != null) col.enabled = true;
		var rb = newPlayer.GetComponent<Rigidbody>();
		if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

		newPlayer.transform.position = new Vector3(player.position.x, player.position.y, player.position.z);
		playerObjects[player.id] = newPlayer;
		AttachNameTag(newPlayer, player.name, true);

		var eg = FindObjectOfType<ElementGenerator>();
		if (eg != null) eg.SetPlayerTransform(newPlayer.transform);
	}

	/// <summary>
	/// 相手プレイヤーのオブジェクトをすべて削除する
	/// </summary>
	private void ClearRemotePlayers()
	{
		foreach (var obj in playerObjects.Values)
			if (obj != null) Destroy(obj);
		playerObjects.Clear();
	}

	/// <summary>
	/// リスポーン位置の受信：自分のリスポーンは補間キャッシュをクリア、相手のリスポーンは位置を更新する
	/// </summary>
	private void HandleRespawnMessage(string json)
	{
		var msg = JsonUtility.FromJson<RespawnMessage>(json);
		if (msg.id == myId)
		{
			targetPositions.Remove(myId);
			LogManager.Instance?.AddLog("リスポーンした", "#ff6666");
			return;
		}
		if (!playerObjects.ContainsKey(msg.id)) return;
		var obj = playerObjects[msg.id];
		if (obj == null) return;
		obj.transform.position = new Vector3(msg.position.x, msg.position.y, msg.position.z);
		targetPositions[msg.id] = obj.transform.position;
		LogManager.Instance?.AddLog("味方がリスポーンした", "#ff6666");
	}

	/// <summary>
	/// 相手がつかまったときの受信処理：ゲスト（Player2）のみリスポーン演出を実行する
	/// </summary>
	private void HandleRemoteRespawnMessage(string json)
	{
		if (IsGuestPlayer())
		{
			// 自分（ゲスト）が捕まった本人
			if (myPlayer == null) return;
			var pc = myPlayer.GetComponent<PlayerController>();
			if (pc == null) return;
			pc.RespawnWithEffectPublic();
			LogManager.Instance?.AddLog("リスポーンした", "#ff6666");
		}
		else if (IsHostPlayer())
		{
			// ホストは見ているだけ
			LogManager.Instance?.AddLog("味方がリスポーンした", "#ff6666");
		}
		/*if (!IsGuestPlayer()) return;

		if (myPlayer == null) return;
		var pc = myPlayer.GetComponent<PlayerController>();
		if (pc == null) return;

		pc.RespawnWithEffectPublic();
		LogManager.Instance?.AddLog("味方がリスポーンした", "#ff6666");*/

		/*var spawnPos = GetSpawnPosition();
        myPlayer.transform.position = spawnPos != Vector3.zero ? spawnPos : myPlayer.transform.position;

        var rb = myPlayer.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var e in enemies)
        {
            var em = e.GetComponent<EnemyManager>();
            if (em != null)
            {
                em.ResetRespawnFlag();
                em.currentAlertCount = em.alertCount;
            }
        }*/
	}

	/// <summary>
	/// 相手がつかまったことをサーバーに通知する
	/// </summary>
	public async void SendRemoteRespawn()
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.RemoteRespawn(myId));
	}

	#endregion

	#region 送信処理

	/// <summary>
	/// 自分の位置・向き・動作状態をサーバーに送信する
	/// </summary>
	public async void SendPosition()
	{
		if (myPlayer == null) return;
		var pc = myPlayer.GetComponent<PlayerController>();
		string animState = pc != null ? pc.GetAnimState() : "idle";
		string trigger = pc?.lastTrigger ?? "";
		if (!string.IsNullOrEmpty(trigger) && pc != null) pc.lastTrigger = "";

		await websocket.SendText(SendMessageBuilder.PlayerMove(
			myId,
			myPlayer.transform.position,
			myPlayer.transform.rotation.eulerAngles,
			animState,
			trigger
		));
	}

	/// <summary>
	/// リスポーン位置をサーバーに送信する
	/// </summary>
	public async void SendRespawn(Vector3 pos)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.Respawn(myId, pos));
	}

	/// <summary>
	/// ゴールをサーバーに通知する
	/// </summary>
	public async void SendGoal()
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.Goal());
	}

	/// <summary>
	/// スイッチ操作をサーバーに通知する
	/// </summary>
	public async void SendSwitchActivated(int switchIndex)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.SwitchActivated(switchIndex));
	}

	/// <summary>
	/// アイテム取得をサーバーに通知する
	/// </summary>
	public async void SendItemPicked()
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.ItemPicked());
	}

	/// <summary>
	/// 敵をスタンさせたことをサーバーに通知する
	/// </summary>
	public async void SendEnemyStun(int enemyIndex)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		//Debug.Log($"SendEnemyStun送信: enemyIndex={enemyIndex}");
		await websocket.SendText(SendMessageBuilder.EnemyStun(enemyIndex, myId));
	}

	/// <summary>
	/// 敵のスタンを解除したことをサーバーに通知する
	/// </summary>
	public async void SendEnemyStunCancel(int enemyIndex)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.EnemyStunCancel(enemyIndex, myId));
	}

	/// <summary>
	/// 定型文チャットをサーバーに送信する
	/// </summary>
	public async void SendChatMessage(string message)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;
		await websocket.SendText(SendMessageBuilder.Chat(message, myId));
	}

	#endregion

	#region 敵の位置同期

	/// <summary>
	/// ホストは敵の位置を送信し、ゲストは受信した位置へなめらかに移動させる
	/// </summary>
	private void UpdateEnemySync()
	{
		if (IsHostPlayer())
		{
			if (_enemyObjects == null || _enemyObjects.Length == 0)
				_enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");

			enemySendTimer += Time.deltaTime;
			if (enemySendTimer >= enemySendInterval)
			{
				enemySendTimer = 0f;
				for (int i = 0; i < _enemyObjects.Length; i++)
				{
					if (_enemyObjects[i] == null) continue;
					var pos = _enemyObjects[i].transform.position;
					var angle = _enemyObjects[i].transform.eulerAngles.y;
					SendEnemyMove(i, pos, angle);
				}
			}
		}
		else if (IsGuestPlayer())
		{
			if (_enemyObjects == null || _enemyObjects.Length == 0)
				_enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");

			foreach (var kv in enemyTargetPositions)
			{
				int idx = kv.Key;
				if (idx >= _enemyObjects.Length || _enemyObjects[idx] == null) continue;
				_enemyObjects[idx].transform.position = Vector3.Lerp(
					_enemyObjects[idx].transform.position, kv.Value, Time.deltaTime * 25f);
				if (enemyTargetAngles.ContainsKey(idx))
					_enemyObjects[idx].transform.rotation = Quaternion.Lerp(
						_enemyObjects[idx].transform.rotation,
						Quaternion.Euler(0, enemyTargetAngles[idx], 0),
						Time.deltaTime * 25f);
			}
		}
	}

	/// <summary>
	/// 敵の位置・ライト色・反応・音検知位置をサーバーに送信する
	/// </summary>
	private async void SendEnemyMove(int index, Vector3 pos, float angle)
	{
		if (websocket == null || websocket.State != WebSocketState.Open) return;

		Color lightColor = Color.white;
		if (_enemyObjects != null && index < _enemyObjects.Length && _enemyObjects[index] != null)
		{
			var light = _enemyObjects[index].GetComponentInChildren<Light>();
			if (light != null) lightColor = light.color;
		}

		string reaction = "";
		var em = _enemyObjects[index].GetComponent<EnemyManager>();
		if (em != null) reaction = em.GetReactionState();

		Vector3 lastSound = Vector3.zero;
		if (em != null) lastSound = em.GetLastSoundPosition();

		await websocket.SendText(SendMessageBuilder.EnemyMove(index, pos, angle, lightColor, reaction, lastSound));
	}

	#endregion

	#region ユーティリティ

	/// <summary>
	/// ゲーム開始をシーン読み込み直後の遅延対策として少し遅らせて通知する
	/// </summary>
	private void DelayedGameStart()
	{
		MissionManager.Instance?.OnGameStart();
	}

	/// <summary>
	/// プレイヤーの頭上に名前タグを生成してアタッチする
	/// </summary>
	private void AttachNameTag(GameObject playerObj, string name, bool visible)
	{
		GameObject tagObj = new GameObject("NameTag");
		tagObj.transform.SetParent(playerObj.transform, false);
		NameTag tag = tagObj.AddComponent<NameTag>();
		tag.SetName(name);
		tag.SetVisible(visible);
	}

	// プレイヤーのリスポーンと連携用。削除しないこと。
	public void SetSpawnPosition(int playerNum, Vector3 pos) { spawnPositions[playerNum] = pos; }


	/// <summary>
	/// 自分の開始位置を返す（リスポーン時などに使用）
	/// </summary>
	public Vector3 GetSpawnPosition()
	{
		if (spawnPositions.ContainsKey(myPlayerNumber))
			return spawnPositions[myPlayerNumber];
		return Vector3.zero;
	}

	#endregion
}