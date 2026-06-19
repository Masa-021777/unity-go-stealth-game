using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
#if UNITY_EDITOR
using static UnityEditor.Experimental.GraphView.GraphView;
#endif

public class ElementGenerator : MonoBehaviour
{
	#region 定数

	readonly Color ROOM_COLOR = new Color(0, 0.8f, 1, 0.5f);        // 部屋の色
	readonly Color AISLE_COLOR = new Color(0, 1, 0, 0.5f);          // 通路の色
	readonly Color PLAYER_COLOR = new Color(1, 1, 0, 1);            // プレイヤーの色
	readonly Color ENEMY_COLOR = new Color(1, 0, 0, 0.5f);          // 敵の色
	readonly Color ITEM_COLOR = new Color(1, 0, 1, 1f);             // アイテムの色
	readonly Color SWITCH_COLOR = new Color(1, 0, 1, 1f);           // スイッチの色

	#endregion

	#region リソース格納用

	GameObject goalObjects;                                         // ゴール
	GameObject[] enemiesList = new GameObject[1];                   // 敵リスト
	GameObject[] strongEnemisList = new GameObject[1];              // 強化敵リスト
	GameObject[] itemsList = new GameObject[1];                     // アイテムリスト
	GameObject[] switchesList = new GameObject[1];                  // スイッチリスト
	GameObject[] patrolPointsList = new GameObject[1];              // 巡回ポイントリスト
	GameObject[] mapTilesList = new GameObject[4];                  // マップタイルリスト
	GameObject[] respawnPointsList = new GameObject[1];             // リスポーン地点リスト
	GameObject wallObjects;                                         // マップ作成用キューブ
	Sprite goalIcon;                                                // ゴールアイコン

	#endregion

	#region マップ生成管理

	CsvMapLoader mapGenerate;                                       // ミニマップ生成スクリプト
	string[,] map;                                                  // マップ読み込み
	GameObject objMap2D;                                            // Map2D
	GameObject[,] objMapExist;                                      // 生成したマップチップ

	// CSVデータ数値パラメータ
	public enum MapObjectType
	{
		Enemy = 3,
		StrongEnemy = 4,
		Item = 5,
		Goal = 6,
		Switch = 7,
		Player1 = 8,
		PatrolPoint = 9,
		Player2 = 10,
		Respawn = 11
	}
	// メンバ変数として追加
	public Dictionary<int, List<GameObject>> gimmickWallDic = new Dictionary<int, List<GameObject>>();
	#endregion

	#region プレイヤー位置管理

	int playerX = 0;                                                // プレイヤーX座標
	int playerY = 0;                                                // プレイヤーY座標
	int currentPlayerX;                                             // 現在プレイヤーがいるXマス
	int currentPlayerY;                                             // 現在プレイヤーがいるYマス
	int oldPlayerX;                                                 // 前回プレイヤーがいたXマス
	int oldPlayerY;                                                 // 前回プレイヤーがいたYマス

	#endregion

	#region ミニマップUI管理

	[SerializeField] RectTransform miniMapMaskRect;
	[SerializeField] RectTransform map2DRect;
	int mapX = 45;                                                 // ミニマップ位置座標X
	int mapY = 145;                                                // ミニマップ位置座標Y
	float cellSize = 5f;

	#endregion

	#region 敵視野UI管理

	[Header("敵の視野UI")]
	[SerializeField] GameObject enemyViewPrefab;
	[SerializeField] GameObject enemyStrongViewPrefab;
	[SerializeField] GameObject itemViewPrefab;

	List<GameObject> viewList = new List<GameObject>();
	List<GameObject> viewStrongList = new List<GameObject>();

	#endregion

	#region 澤田作:マルチプレイヤー管理/プレイヤー別ミニマップ管理

	/// <summary>
	/// マルチプレイヤー管理
	/// </summary>
	[Header("Player サーバ関連"), SerializeField]
	Transform player;
	Transform remotePlayer;   // 相手のTransform
	int currentRemoteX, currentRemoteY;
	int oldRemoteX, oldRemoteY;

	/// <summary>
	/// プレイヤー別ミニマップ管理
	/// </summary>
	[Header("プレイヤー1のミニマップ")]
	[SerializeField] RectTransform map2DRect_P1;
	[SerializeField] RectTransform miniMapMaskRect_P1;

	[Header("プレイヤー2のミニマップ")]
	[SerializeField] RectTransform map2DRect_P2;
	[SerializeField] RectTransform miniMapMaskRect_P2;

	GameObject[,] objMapExist_P1;
	GameObject[,] objMapExist_P2;

	#endregion

	#region シーン内オブジェクト管理

	GameObject[] objEnemys;                                         // 敵
	GameObject[] objEnemyStrongs;                                   // 強化敵
	GameObject[] objItems;                                          // アイテム
	GameObject[] objGoals;                                          // ゴール
	GameObject[] objSwitchs;                                        // スイッチ

	#endregion

	#region ギミック連携管理

	List<EnemyManager> strongEnemyList = new List<EnemyManager>();
	List<SwitchManager> switchList = new List<SwitchManager>();

	public List<SwitchManager> GetSwitchList() => switchList;
	#endregion

	#region 敵のミニマップ更新管理

	List<Vector2Int> oldEnemyPositions = new List<Vector2Int>();    // 敵のマス

	#endregion

	#region デバック設定

	[Header("壁のコライダーを有効化")]
	[SerializeField] bool enableWallCollider = true;                // Debug用

	#endregion

	#region 初期化処理

	void Awake()
	{
		// リソース読み込み
		ReadResources();

		// 壁を生成
		GenerateWall();

		// 敵・アイテム・ゴール・プレイヤー初期配置決め
		GenerateObjectsCSV();

		// WebSocketClientからプレイヤーを取得
		var wsClient = FindObjectOfType<WebSocketClient>();
		if (wsClient != null && wsClient.myPlayer != null)
		{
			player = wsClient.myPlayer.transform;
		}

		GenerateMap2D(map, map2DRect_P1, out objMapExist_P1);  // P1用
		GenerateMap2D(map, map2DRect_P2, out objMapExist_P2);  // P2用
	}

	/// <summary>
	/// リソース読み込み
	/// </summary>
	void ReadResources()
	{
		// マップ作成用キューブ
		wallObjects = (GameObject)Resources.Load("Prefabs/Ryuki/WallPrefab");

		// ゴール
		goalObjects = (GameObject)Resources.Load("Prefabs/Ryuki/Goal");
		goalIcon = (Sprite)Resources.Load("Images/IconGoal");

		// 敵リスト
		enemiesList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/Enemy");

		// 強化敵リスト
		strongEnemisList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/SuperEnemy");

		// アイテムリスト
		itemsList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/Item");

		// スイッチ
		switchesList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/Switch");

		// 巡回ポイント
		patrolPointsList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/PatrolPoint");

		// 2Dのマップチップ読み込み
		mapTilesList[0] = Resources.Load<GameObject>("Prefabs/Ryuki/Map2D/MapUI_0");
		mapTilesList[1] = Resources.Load<GameObject>("Prefabs/Ryuki/Map2D/MapUI_1");
		mapTilesList[2] = Resources.Load<GameObject>("Prefabs/Ryuki/Map2D/MapUI_2");
		mapTilesList[3] = Resources.Load<GameObject>("Prefabs/Ryuki/Map2D/MapUI_3");

		// リスポーン地点
		respawnPointsList[0] = (GameObject)Resources.Load("Prefabs/Ryuki/Respawn");
	}

	/// <summary>
	/// 壁生成
	/// </summary>
	void GenerateWall()
	{
		// CSVマップデータ取得
		mapGenerate = GetComponent<CsvMapLoader>();
		// 2Dマップ読み込み
		map = mapGenerate.Generate();

		// 生成する壁の親となるGameObject
		GameObject objWall = GameObject.Find("Wall");

		// マップサイズ取得
		int width = map.GetLength(0);
		int height = map.GetLength(1);

		// 古いデータが残らないように初期化
		gimmickWallDic.Clear();

		// CSVデータを横一列で見る
		for (int y = 0; y < height; y++)
		{
			int startX = -1; // 開始位置
			string currentWallType = "";
			int currentWallID = -1;

			for (int x = 0; x < width; x++)
			{
				string cell = map[x, y];

				// 空白セルの場合はスキップ
				if (string.IsNullOrEmpty(cell))
				{
					// 壁が途切れたら生成
					if (startX != -1)
					{
						CreateWallBlock(startX, x - 1, y, objWall, currentWallType, currentWallID);
						startX = -1;
						currentWallType = "";
						currentWallID = -1;
					}
					continue;
				}
				// セルのデータを分割
				string[] data = cell.Split('_');
				string type = data[0];
				int id = data.Length > 1 ? int.Parse(data[1]) : -1;

				bool isWall = (type == "0" || type == "12");

				if (isWall)
				{
					if (startX == -1)
					{
						startX = x;
						currentWallType = type;
						currentWallID = id;
					}
					// 壁の種類かIDが変わったら、それまでの壁を生成して区切る
					else if (currentWallType != type || currentWallID != id)
					{
						CreateWallBlock(startX, x - 1, y, objWall, currentWallType, currentWallID);
						startX = x;
						currentWallType = type;
						currentWallID = id;
					}
				}
				else // 壁以外のマス（敵やアイテムなど）
				{
					if (startX != -1)
					{
						CreateWallBlock(startX, x - 1, y, objWall, currentWallType, currentWallID);
						startX = -1;
						currentWallType = "";
						currentWallID = -1;
					}
				}
			}
			if (startX != -1)
			{
				CreateWallBlock(startX, width - 1, y, objWall, currentWallType, currentWallID);
			}
		}
	}

	/// <summary>
	/// 連立するキューブを一つのオブジェクトとして生成させる処理
	/// </summary>
	/// <param name="startX"></param>
	/// <param name="endX"></param>
	/// <param name="y"></param>
	/// <param name="parent"></param>
	void CreateWallBlock(int startX, int endX, int y, GameObject parent, string wallType, int wallID)
	{
		int length = endX - startX + 1;

		GameObject cube = Instantiate(wallObjects);

		cube.transform.parent = parent.transform;
		cube.transform.localScale = new Vector3(length, 4f, 1);

		float centerX = startX + (length / 2f) - 0.5f;
		cube.transform.position = new Vector3(centerX, 2f, y);

		if (wallType == "12")
		{
			MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
			if (renderer != null)
			{
				renderer.enabled = false;
			}
			// 透明壁の通知用スクリプト
			//cube.AddComponent<WallCollision>();

			// IDが設定されている場合のみ辞書に登録
			if (wallID != -1)
			{
				if (!gimmickWallDic.ContainsKey(wallID))
				{
					gimmickWallDic[wallID] = new List<GameObject>();
				}
				gimmickWallDic[wallID].Add(cube);
			}
		}

		// デバック用：コライダーON/OFF切り替え
		var col = cube.GetComponent<Collider>();
		if (col != null)
		{
			col.enabled = enableWallCollider;
		}
	}

	/// <summary>
	/// CSVに敵やアイテムなども調整できるように
	/// </summary>
	void GenerateObjectsCSV()
	{
		for (int x = 0; x < map.GetLength(0); x++)
		{
			for (int y = 0; y < map.GetLength(1); y++)
			{
				Vector3 pos = new Vector3(x, 0, y);

				string cell = map[x, y];

				if (string.IsNullOrEmpty(cell))
				{
					continue;
				}

				string[] data = cell.Split('_');

				//int type = int.Parse(data[0]);
				MapObjectType type = (MapObjectType)int.Parse(data[0]);
				int id = data.Length > 1 ? int.Parse(data[1]) : -1;

				switch (type)
				{
					case MapObjectType.Enemy: // 敵
						GameObject normalEnemyObj = Instantiate(enemiesList[0], pos, Quaternion.identity);
						GameObject view = Instantiate(enemyViewPrefab, map2DRect);
						viewList.Add(view);
						objEnemys = GameObject.FindGameObjectsWithTag("Enemy");
						EnemyManager normalEm = normalEnemyObj.GetComponent<EnemyManager>();
						if (normalEm != null) normalEm.enemyID = id;
						SwitchManager normalSw = normalEnemyObj.GetComponentInChildren<SwitchManager>();
						if (normalSw != null) normalSw.targetEnemyID = id; // 追加
						map[x, y] = "1";
						break;

					case MapObjectType.StrongEnemy: // 強化敵
						GameObject enemyObj = Instantiate(strongEnemisList[0], pos, Quaternion.identity);
						GameObject viewStrong = Instantiate(enemyStrongViewPrefab, map2DRect);
						viewStrongList.Add(viewStrong);
						objEnemyStrongs = GameObject.FindGameObjectsWithTag("StrongEnemy");
						map[x, y] = "1";
						//ギミック用
						EnemyManager em = enemyObj.GetComponent<EnemyManager>();
						em.enemyID = id;
						strongEnemyList.Add(em);
						break;

					case MapObjectType.Item: // アイテム
						Instantiate(itemsList[0], pos, Quaternion.identity);
						objItems = GameObject.FindGameObjectsWithTag("Item");
						map[x, y] = "1";
						break;

					case MapObjectType.Goal: // ゴール
						Instantiate(goalObjects, pos, Quaternion.identity);
						objGoals = GameObject.FindGameObjectsWithTag("Goal");
						map[x, y] = "1";
						break;

					case MapObjectType.Switch: // スイッチ
						GameObject switchObj = Instantiate(switchesList[0], pos, Quaternion.identity);
						objSwitchs = GameObject.FindGameObjectsWithTag("Switch");
						map[x, y] = "1";
						// ギミック用
						SwitchManager sw = switchObj.GetComponentInChildren<SwitchManager>();
						sw.targetEnemyID = id;
						switchList.Add(sw);
						break;

					case MapObjectType.Player1: // プレイヤー1
						var wsClient = FindObjectOfType<WebSocketClient>();
						if (wsClient != null) wsClient.SetSpawnPosition(1, pos);
						map[x, y] = "1";
						break;

					case MapObjectType.PatrolPoint: // 敵の巡回ポイント
						Instantiate(patrolPointsList[0], pos, Quaternion.identity);
						map[x, y] = "1";
						break;

					case MapObjectType.Player2: // プレイヤー2
						var wsClient2 = FindObjectOfType<WebSocketClient>();
						if (wsClient2 != null)
							wsClient2.SetSpawnPosition(2, pos);
						map[x, y] = "1";
						break;

					case MapObjectType.Respawn: // リスポーン
						Instantiate(respawnPointsList[0], pos, Quaternion.identity);
						map[x, y] = "1";
						break;
				}
			}
		}
		IDLinking();
	}

	/// <summary>
	/// スイッチギミック紐づけ処理
	/// </summary>
	void IDLinking()
	{
		foreach (SwitchManager sw in switchList)
		{
			if (sw == null)
			{
				Debug.LogError("SwitchManagerが取得できてない");
				continue;
			}

			EnemyManager targetEnemy = null;

			foreach (EnemyManager em in strongEnemyList)
			{
				if (em == null)
					continue;

				// ID一致チェック
				if (em.enemyID == sw.targetEnemyID)
				{
					targetEnemy = em;
					break;
				}
			}

			if (targetEnemy != null)
			{
				sw.SetTarget(targetEnemy);
				Debug.Log($"スイッチID:{sw.targetEnemyID} → 敵ID:{targetEnemy.enemyID} 接続");
			}
			else
			{
				Debug.LogWarning($"対応する敵が見つかりません ID:{sw.targetEnemyID}");
			}
		}
	}

	/// <summary>
	/// ミニマップ生成
	/// </summary>
	/// <param name="map"></param>
	void GenerateMap2D(string[,] map, RectTransform targetRect, out GameObject[,] mapExist)
	{
		mapExist = new GameObject[map.GetLength(0), map.GetLength(1)];

		objMap2D = GameObject.Find("Map2D").gameObject;
		objMapExist = new GameObject[map.GetLength(0), map.GetLength(1)];

		// 位置検索
		for (int x = 0; x < map.GetLength(0); x++)
		{
			for (int y = 0; y < map.GetLength(1); y++)
			{
				if (map[x, y] == "10") // プレイヤー位置検索
				{
					playerX = x;
					playerY = y;
				}
			}
		}

		// map[x,y]のパラメタ：0:壁、1:部屋、2:通路、10:プレイヤーが居る部屋
		for (int i = 0; i < map.GetLength(0); i++)
		{
			for (int j = 0; j < map.GetLength(1); j++)
			{
				int index = 0;

				if (map[i, j] == "0") index = 0;      // 壁
				else if (map[i, j] == "1") index = 1; // 部屋
                else if (map[i, j] == "2" || map[i, j] == "12") index = 2; // 通路


                objMapExist[i, j] = Instantiate(mapTilesList[index]);

				// Map2D直下に階層を移動
				objMapExist[i, j].transform.SetParent(objMap2D.transform, false);
				objMapExist[i, j].transform.localScale = new Vector3(1, 1, 1);

				Vector2 vector2 = new Vector2(cellSize * i - mapX, cellSize * j - mapY);
				objMapExist[i, j].GetComponent<RectTransform>().anchoredPosition = vector2;

				// ミニマップ色変更
				if (map[i, j] == "1") // 部屋
				{
					objMapExist[i, j].GetComponent<Image>().color = ROOM_COLOR;
				}
				else if (map[i, j] == "2" || map[i, j] == "12") // 通路
				{
					objMapExist[i, j].GetComponent<Image>().color = AISLE_COLOR;
				}
				else if (map[i, j] == "3") // 敵
				{
					objMapExist[i, j].GetComponent<Image>().color = ENEMY_COLOR;
				}
			}
		}
		CenterMiniMap(playerX, playerY);

		currentPlayerX = playerX;
		currentPlayerY = playerY;

		oldPlayerX = playerX;
		oldPlayerY = playerY;

		for (int i = 0; i < map.GetLength(0); i++)
			for (int j = 0; j < map.GetLength(1); j++)
				mapExist[i, j] = objMapExist[i, j];
	}

	#endregion

	#region ミニマップ管理関数

	/// <summary>
	/// プレイヤーのがミニマップの中央に来るようにマップをずらす処理
	/// </summary>
	/// <param name="mapX"></param>
	/// <param name="mapY"></param>
	void CenterMiniMap(int mapX, int mapY)
	{
		// Mask中央
		float centerX = miniMapMaskRect.rect.width * 0.5f;
		float centerY = miniMapMaskRect.rect.height * 0.5f;

		// プレイヤーマスの中心座標
		float playerCellX = (mapX * cellSize) + (cellSize * 0.5f);
		float playerCellY = (mapY * cellSize) + (cellSize * 0.5f);

		// プレイヤーマス中心をMask中央へ
		float posX = centerX - playerCellX;
		float posY = centerY - playerCellY;

		map2DRect.anchoredPosition = new Vector2(posX, posY);
	}

	void UpdatePlayerOnMap(
	Transform target,
	ref int curX, ref int curY,
	ref int oldX, ref int oldY,
	GameObject[,] mapExist,
	RectTransform targetRect,
	RectTransform maskRect)
	{
		if (mapExist == null || target == null) return;

		curX = Mathf.RoundToInt(target.position.x);
		curY = Mathf.RoundToInt(target.position.z);

		if (oldX != curX || oldY != curY)
		{
			// 前のマスを地形色に戻す
			if (IsInsideMap(oldX, oldY, mapExist))
			{
				Image oldImg = mapExist[oldX, oldY].GetComponent<Image>();
				if (map[oldX, oldY] == "1") oldImg.color = ROOM_COLOR;
				else if (map[oldX, oldY] == "2") oldImg.color = AISLE_COLOR;
				else oldImg.color = ROOM_COLOR;
			}

			// 現在のマスをプレイヤー色に
			if (IsInsideMap(curX, curY, mapExist))
			{
				mapExist[curX, curY].GetComponent<Image>().color = PLAYER_COLOR;
			}

			oldX = curX;
			oldY = curY;
		}

		// ミニマップを中心に移動
		float centerX = maskRect.rect.width * 0.5f;
		float centerY = maskRect.rect.height * 0.5f;
		float playerCellX = (curX * cellSize) + (cellSize * 0.5f);
		float playerCellY = (curY * cellSize) + (cellSize * 0.5f);
		targetRect.anchoredPosition = new Vector2(centerX - playerCellX, centerY - playerCellY);
	}

	#endregion

	#region 澤田作：サーバ関連処理

	/// <summary>
	/// ただしAwake時点ではmyPlayerがまだ生成されていない（init受信後に生成される）ので、コールバックで後から渡す方式にします。
	/// </summary>
	/// <param name="t"></param>
	public void SetPlayerTransform(Transform t)
	{
		player = t;
		// ミニマップの初期中心も更新
		currentPlayerX = Mathf.RoundToInt(t.position.x);
		currentPlayerY = Mathf.RoundToInt(t.position.z);
		oldPlayerX = currentPlayerX;
		oldPlayerY = currentPlayerY;
		CenterMiniMap(currentPlayerX, currentPlayerY);
	}

	public void SetRemotePlayerTransform(Transform t)
	{
		remotePlayer = t;
	}

	#endregion

	#region Update処理

	void Update()
	{
		UpdateMiniMap(); // ミニマップに反映
	}

	/// <summary>
	/// プレイヤーと敵の現在位置をミニマップに反映
	/// </summary>
	void UpdateMiniMap()
	{
		if (player == null) return; // プレイヤー生成前はスキップ

		UpdatePlayerMiniMap();                                                   // プレイヤー
		UpdateItemMiniMap();                                                     // アイテム
		UpdateGoalMiniMap();                                                     // ゴール
		UpdateSwitchMiniMap();                                                   // スイッチ

		// 敵は独自で移動するため
		ResetEnemyMiniMap();
		UpdateEnemyMap(objEnemys);
		UpdateEnemyMap(objEnemyStrongs);
		UpdateEnemyView(objEnemys, viewList, 20f, false);
		UpdateEnemyView(objEnemyStrongs, viewStrongList, 45f, true);

	}

	#endregion

	#region ミニマップアイコン

	void UpdatePlayerMiniMap()
	{
		Vector3 playerPos = player.position;

		// ワールド座標 → マップ座標変換
		currentPlayerX = Mathf.RoundToInt(playerPos.x);
		currentPlayerY = Mathf.RoundToInt(playerPos.z);

		// 前回位置の色を戻す
		if (oldPlayerX != currentPlayerX || oldPlayerY != currentPlayerY)
		{
			if (IsInsideMap(oldPlayerX, oldPlayerY, objMapExist))
			{
				// 元の地形色に戻す
				ResetTileColor(oldPlayerX, oldPlayerY);
			}

			// 現在位置をプレイヤー色に
			if (IsInsideMap(currentPlayerX, currentPlayerY, objMapExist))
			{
				print("プレイヤー色変える");

				Image currentImage = objMapExist[currentPlayerX, currentPlayerY].GetComponent<Image>();

				// プレイヤー位置
				currentImage.color = PLAYER_COLOR;
			}
			oldPlayerX = currentPlayerX;
			oldPlayerY = currentPlayerY;

		}

		// マップを逆方向へ動かす
		CenterMiniMap(currentPlayerX, currentPlayerY);

		// P1（自分）
		if (player != null)
			UpdatePlayerOnMap(player, ref currentPlayerX, ref currentPlayerY,
							  ref oldPlayerX, ref oldPlayerY,
							  objMapExist_P1, map2DRect_P1, miniMapMaskRect_P1);

		// P2（相手）
		if (remotePlayer != null)
			UpdatePlayerOnMap(remotePlayer, ref currentRemoteX, ref currentRemoteY,
							  ref oldRemoteX, ref oldRemoteY,
							  objMapExist_P2, map2DRect_P2, miniMapMaskRect_P2);
	}

	void UpdateItemMiniMap()
	{
		// 全アイテム更新
		foreach (GameObject itemObj in objItems)
		{
			Vector3 itemPos = itemObj.transform.position;

			int itemX = Mathf.RoundToInt(itemPos.x);

			int itemY = Mathf.RoundToInt(itemPos.z);

			if (IsInsideMap(itemX, itemY, objMapExist))
			{
				Image img = objMapExist[itemX, itemY].GetComponent<Image>();

				// アイテムの色
				img.color = ITEM_COLOR;
			}
		}
	}

	void UpdateGoalMiniMap()
	{
		// ゴール更新
		foreach (GameObject goalObj in objGoals)
		{
			Vector3 goalPos = goalObj.transform.position;

			int goalX = Mathf.RoundToInt(goalPos.x);

			int goalY = Mathf.RoundToInt(goalPos.z);

			if (IsInsideMap(goalX, goalY, objMapExist))
			{
				Image img = objMapExist[goalX, goalY].GetComponent<Image>();

				img.sprite = goalIcon;
			}
		}
	}

	void UpdateSwitchMiniMap()
	{
		// 全スイッチ更新
		foreach (GameObject SwitchObj in objSwitchs)
		{
			Vector3 SwitchPos = SwitchObj.transform.position;

			int SwitchX = Mathf.RoundToInt(SwitchPos.x);

			int SwitchY = Mathf.RoundToInt(SwitchPos.z);

			if (IsInsideMap(SwitchX, SwitchY, objMapExist))
			{
				Image img = objMapExist[SwitchX, SwitchY].GetComponent<Image>();

				// スイッチの色
				img.color = SWITCH_COLOR;
			}
		}
	}

	void ResetEnemyMiniMap()
	{
		// 前回の敵位置を元に戻す
		foreach (Vector2Int pos in oldEnemyPositions)
		{
			if (IsInsideMap(pos.x, pos.y, objMapExist))
			{
				Image img = objMapExist[pos.x, pos.y].GetComponent<Image>();

				if (map[pos.x, pos.y] == "1")
				{
					img.color = ROOM_COLOR;
				}
				else if (map[pos.x, pos.y] == "2" || map[pos.x, pos.y] == "12")
				{
					img.color = AISLE_COLOR;
				}
			}
		}
		// 今回の敵位置保存用
		oldEnemyPositions.Clear();
	}

	void UpdateEnemyMap(GameObject[] enemies)
	{
		foreach (GameObject enemyObj in enemies)
		{
			Vector3 enemyPos = enemyObj.transform.position;

			int enemyX = Mathf.RoundToInt(enemyPos.x);
			int enemyY = Mathf.RoundToInt(enemyPos.z);

			if (IsInsideMap(enemyX, enemyY, objMapExist))
			{
				Image img = objMapExist[enemyX, enemyY].GetComponent<Image>();

				img.color = ENEMY_COLOR;

				oldEnemyPositions.Add(new Vector2Int(enemyX, enemyY));
			}
		}
	}

	void UpdateEnemyView(
	GameObject[] enemies,
	List<GameObject> views,
	float offset,
	bool useLightColor)
	{
		for (int i = 0; i < enemies.Length; i++)
		{
			GameObject enemyObj = enemies[i];

			Vector3 pos = enemyObj.transform.position;

			int x = Mathf.RoundToInt(pos.x);
			int y = Mathf.RoundToInt(pos.z);

			RectTransform rt = views[i].GetComponent<RectTransform>();

			rt.anchoredPosition =
				new Vector2(x * cellSize - mapX,
							y * cellSize - mapY);

			float angle = enemyObj.transform.eulerAngles.y;

			rt.rotation =
				Quaternion.Euler(0, 0, -angle + offset);

			Image img = rt.GetComponent<Image>();

			if (img == null) continue;

			if (useLightColor)
			{
				Light light = enemyObj.GetComponentInChildren<Light>();

				if (light != null)
				{
					img.color = new Color(
						light.color.r,
						light.color.g,
						light.color.b,
						0.8f);
				}
			}
			else
			{
				EnemyManager em =
					enemyObj.GetComponent<EnemyManager>();

				if (em == null) continue;

				if (em.currentAlertCount <= 1)
					img.color = Color.red;
				else if (em.currentAlertCount < 3)
					img.color = new Color(1f, 0.5f, 0f);
				else
					img.color = new Color(0.827f, 0.851f, 0.439f);
			}
		}
	}

	#endregion

	#region ミニマップ共通処理

	/// <summary>
	/// 元の色に戻す
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	void ResetTileColor(int x, int y)
	{
		Image img = objMapExist[x, y].GetComponent<Image>();

		switch (map[x, y])
		{
			case "1":
				img.color = ROOM_COLOR;
				break;

			case "2":
				img.color = AISLE_COLOR;
				break;

			case "3":
				img.color = PLAYER_COLOR;
				break;

            case "12":
                img.color = AISLE_COLOR;
                break;
        }
	}

	/// <summary>
	/// 座標がマップ範囲内かどうか確認
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	/// <returns></returns>
	bool IsInsideMap(int x, int y, GameObject[,] mapExist)
	{
		return x >= 0 &&
			   y >= 0 &&
			   x < mapExist.GetLength(0) &&
			   y < mapExist.GetLength(1);
	}

	#endregion

}