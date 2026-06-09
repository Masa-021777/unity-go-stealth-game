package handlers

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"stealth_game/Internal/models"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/gorilla/websocket"
)

// 定数
const adminKey = "del123"                // 管理者キー（ランキングリセット用）
const gameDurationSeconds = 180          // ゲームの制限時間（秒）
const rankingFilePath = "./ranking.json" // ランキングデータの保存先

// グローバル変数
var players = make(map[*websocket.Conn]*models.Player)
var rooms = make(map[string]*models.Room)
var readyPlayers = make(map[string]map[string]bool)
var gameTimers = make(map[string]*time.Timer) // ルームID → タイマー
var gameStarted = make(map[string]time.Time)  // ルームID → ゲーム開始時刻
var goalPlayers = make(map[string]map[string]bool)

var mutex = &sync.Mutex{}
var upgrader = websocket.Upgrader{
	CheckOrigin: func(r *http.Request) bool { return true },
}

// ランキングデータの構造体
type RankingEntry struct {
	Name         string  `json:"name"`
	ClearTime    float64 `json:"clear_time"`
	MissionCount int     `json:"mission_count"`
}

type RankingData struct {
	Rankings []RankingEntry `json:"rankings"`
}

var rankingMutex = &sync.Mutex{}

// loadRanking ランキングデータをファイルから読み込む
func loadRanking() RankingData {
	rankingMutex.Lock()
	defer rankingMutex.Unlock()

	data, err := os.ReadFile(rankingFilePath)
	if err != nil {
		return RankingData{Rankings: []RankingEntry{}}
	}
	var rd RankingData
	if err := json.Unmarshal(data, &rd); err != nil {
		return RankingData{Rankings: []RankingEntry{}}
	}
	return rd
}

// saveRanking ランキングデータをファイルに保存する
func saveRanking(rd RankingData) {
	rankingMutex.Lock()
	defer rankingMutex.Unlock()

	data, err := json.MarshalIndent(rd, "", "  ")
	if err != nil {
		fmt.Println("ランキング保存エラー:", err)
		return
	}
	if err := os.WriteFile(rankingFilePath, data, 0644); err != nil {
		fmt.Println("ランキング書き込みエラー:", err)
	}
}

// addRankingEntry スコアを追加してTop3を保持する
// 順位基準: ミッション数が多い → クリアタイムが短い
func addRankingEntry(entry RankingEntry) {
	rd := loadRanking()
	rd.Rankings = append(rd.Rankings, entry)

	// ソート（ミッション数降順、同点はクリアタイム昇順）
	for i := 0; i < len(rd.Rankings); i++ {
		for j := i + 1; j < len(rd.Rankings); j++ {
			a, b := rd.Rankings[i], rd.Rankings[j]
			if b.MissionCount > a.MissionCount ||
				(b.MissionCount == a.MissionCount && b.ClearTime < a.ClearTime) {
				rd.Rankings[i], rd.Rankings[j] = rd.Rankings[j], rd.Rankings[i]
			}
		}
	}

	// Top3だけ保持
	if len(rd.Rankings) > 3 {
		rd.Rankings = rd.Rankings[:3]
	}

	saveRanking(rd)
}

// HandleRanking ランキングの取得・追加を処理する
// GET /ranking  → ランキング一覧を返す
// POST /ranking → スコアを追加する（Unityから呼ぶ）
func HandleRanking(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Access-Control-Allow-Origin", "*")

	switch r.Method {
	case http.MethodGet:
		rd := loadRanking()
		json.NewEncoder(w).Encode(rd)

	case http.MethodPost:
		var entry RankingEntry
		if err := json.NewDecoder(r.Body).Decode(&entry); err != nil {
			http.Error(w, "invalid body", http.StatusBadRequest)
			return
		}
		addRankingEntry(entry)
		fmt.Fprintf(w, `{"status":"ok"}`)
		fmt.Printf("[ランキング] %s を追加 (%.1f秒, ミッション%d)\n",
			entry.Name, entry.ClearTime, entry.MissionCount)

	default:
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
	}
}

// HandleRankingReset ランキングをリセットする
// DELETE /ranking/reset?key=xxx
// 使い方: curl -X DELETE "http://localhost:8080/ranking/reset?key=del123"
func HandleRankingReset(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")

	key := r.URL.Query().Get("key")
	if key != adminKey {
		http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
		fmt.Println("[ランキング] リセット失敗：キーが違います")
		return
	}

	saveRanking(RankingData{Rankings: []RankingEntry{}})
	fmt.Fprintf(w, `{"status":"ok","message":"ランキングをリセットしました"}`)
	fmt.Println("[ランキング] リセット完了")
}

// HandleRoomList 現在のルーム一覧と参加人数を返す
// GET /rooms
func HandleRoomList(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Access-Control-Allow-Origin", "*")

	validRooms := []string{"room1", "room2", "room3", "room4"}
	type RoomInfo struct {
		ID      string `json:"id"`
		Name    string `json:"name"`
		Current int    `json:"current"`
		Max     int    `json:"max"`
	}

	result := []RoomInfo{}
	mutex.Lock()
	for _, id := range validRooms {
		current := 0
		if room, exists := rooms[id]; exists {
			room.Mutex.Lock()
			current = len(room.Clients)
			room.Mutex.Unlock()
		}
		result = append(result, RoomInfo{
			ID:      id,
			Name:    map[string]string{"room1": "Room 1", "room2": "Room 2", "room3": "Room 3", "room4": "Room 4"}[id],
			Current: current,
			Max:     2,
		})
	}
	mutex.Unlock()

	json.NewEncoder(w).Encode(result)
}

// IsRoomFull 指定したルームが満員かどうか確認する
func IsRoomFull(roomID string) bool {
	mutex.Lock()
	defer mutex.Unlock()
	room, exists := rooms[roomID]
	if !exists {
		return false
	}
	room.Mutex.Lock()
	defer room.Mutex.Unlock()
	return len(room.Clients) >= 2
}

// HandleConnections 接続を受け付け、プレイヤーをルームに参加させる
func HandleConnections(w http.ResponseWriter, r *http.Request) {
	roomID := r.URL.Query().Get("room_id")
	playerName := r.URL.Query().Get("name")

	if IsRoomFull(roomID) {
		http.Error(w, "満員です", http.StatusForbidden)
		return
	}
	if roomID == "" || playerName == "" {
		http.Error(w, "必須パラメータ不足", http.StatusBadRequest)
		return
	}

	ws, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}
	defer ws.Close()

	playerID := uuid.New().String()

	mutex.Lock()
	player := &models.Player{
		ID: playerID, Name: playerName, RoomID: roomID,
		PositionX: 0, PositionY: 1, PositionZ: 0,
	}
	players[ws] = player
	room, exists := rooms[roomID]
	if !exists {
		room = models.NewRoom()
		rooms[roomID] = room
	}
	mutex.Unlock()

	room.AddPlayer(ws, player)

	room.Mutex.Lock()
	playerNumber := len(room.Clients)
	room.Mutex.Unlock()

	player.PlayerNumber = playerNumber

	// 接続したプレイヤーに初期情報を送信
	ws.WriteJSON(map[string]interface{}{
		"type": "init", "id": playerID, "name": playerName,
		"player_number": playerNumber,
		"position":      map[string]float64{"x": 0, "y": 1, "z": 0},
		"rotation":      map[string]float64{"x": 0, "y": 0},
	})

	// 既存プレイヤーの情報を新規プレイヤーへ通知
	room.Mutex.Lock()
	existingPlayers := []interface{}{}
	for _, p := range room.Clients {
		if p.ID != playerID {
			existingPlayers = append(existingPlayers, map[string]interface{}{
				"id": p.ID, "name": p.Name,
				"player_number": p.PlayerNumber,
				"position":      map[string]float64{"x": p.PositionX, "y": p.PositionY, "z": p.PositionZ},
				"rotation":      map[string]float64{"x": p.RotationX, "y": p.RotationY},
			})
		}
	}
	// 既存プレイヤーに新規参加を通知
	for conn, p := range room.Clients {
		if p.ID != playerID {
			conn.WriteJSON(map[string]interface{}{
				"type": "player_joined", "id": playerID, "name": playerName,
				"player_number": playerNumber,
				"position":      map[string]float64{"x": 0, "y": 1, "z": 0},
				"rotation":      map[string]float64{"x": 0, "y": 0},
			})
		}
	}
	room.Mutex.Unlock()

	ws.WriteJSON(map[string]interface{}{
		"type": "existing_players", "players": existingPlayers,
	})

	fmt.Printf("[ルーム %s] プレイヤー '%s' (ID: %s) が参加。人数: %d\n",
		roomID, playerName, playerID, len(room.Clients))

	handleMessages(ws, player, room)
}

// handleMessages プレイヤーからのメッセージを受信し、対応した処理を行う
func handleMessages(ws *websocket.Conn, player *models.Player, room *models.Room) {
	// 切断時の後処理
	defer func() {
		room.RemovePlayer(ws)
		mutex.Lock()
		delete(players, ws)
		if readyPlayers[player.RoomID] != nil {
			delete(readyPlayers[player.RoomID], player.ID)
		}
		if goalPlayers[player.RoomID] != nil {
			delete(goalPlayers[player.RoomID], player.ID)
		}
		mutex.Unlock()

		room.BroadcastMessage(map[string]interface{}{
			"type": "player_left", "id": player.ID, "name": player.Name,
		})
		fmt.Printf("[ルーム %s] プレイヤー '%s' が退出。現在の人数: %d\n",
			player.RoomID, player.Name, len(room.Clients))
	}()

	for {
		var msg map[string]interface{}
		if err := ws.ReadJSON(&msg); err != nil {
			break
		}

		msgType, _ := msg["type"].(string)

		switch msgType {

		// 準備完了：全員揃ったらゲームスタート
		case "ready":
			fmt.Printf("プレイヤー %s が準備完了\n", player.Name)

			if pos, ok := msg["position"].(map[string]interface{}); ok {
				player.PositionX, _ = pos["x"].(float64)
				player.PositionY, _ = pos["y"].(float64)
				player.PositionZ, _ = pos["z"].(float64)
			}

			room.BroadcastMessage(map[string]interface{}{
				"type": "player_ready", "id": player.ID, "name": player.Name,
			})

			mutex.Lock()
			if readyPlayers[player.RoomID] == nil {
				readyPlayers[player.RoomID] = make(map[string]bool)
			}
			readyPlayers[player.RoomID][player.ID] = true

			if len(readyPlayers[player.RoomID]) >= 2 {
				room.BroadcastMessage(map[string]interface{}{"type": "start_game"})
				delete(readyPlayers, player.RoomID)

				// ゲーム開始時刻を記録してタイマーを起動
				startTime := time.Now()
				gameStarted[player.RoomID] = startTime
				go startGameTimer(room, player.RoomID)
			}
			mutex.Unlock()

		// プレイヤー移動：座標・動作状態を全員に配信
		case "player_move":
			if pos, ok := msg["position"].(map[string]interface{}); ok {
				player.PositionX, _ = pos["x"].(float64)
				player.PositionY, _ = pos["y"].(float64)
				player.PositionZ, _ = pos["z"].(float64)
			}
			if rot, ok := msg["rotation"].(map[string]interface{}); ok {
				player.RotationX, _ = rot["x"].(float64)
				player.RotationY, _ = rot["y"].(float64)
			}
			animState, _ := msg["anim_state"].(string)
			animTrigger, _ := msg["anim_trigger"].(string)
			room.BroadcastMessage(map[string]interface{}{
				"type": "player_move", "id": player.ID,
				"position":     map[string]float64{"x": player.PositionX, "y": player.PositionY, "z": player.PositionZ},
				"rotation":     map[string]float64{"x": player.RotationX, "y": player.RotationY},
				"anim_state":   animState,
				"anim_trigger": animTrigger,
			})

		// ゴール：1人目はplayer_goal、2人目でall_goalを配信
		case "goal":
			elapsed := 0.0
			mutex.Lock()
			if startTime, ok := gameStarted[player.RoomID]; ok {
				elapsed = time.Since(startTime).Seconds()
			}
			if goalPlayers[player.RoomID] == nil {
				goalPlayers[player.RoomID] = make(map[string]bool)
			}
			goalPlayers[player.RoomID][player.ID] = true
			count := len(goalPlayers[player.RoomID])
			mutex.Unlock()

			if count >= 2 {
				room.BroadcastMessage(map[string]interface{}{
					"type": "all_goal", "elapsed": elapsed,
				})
				mutex.Lock()
				delete(goalPlayers, player.RoomID)
				mutex.Unlock()
			} else {
				room.BroadcastMessage(map[string]interface{}{
					"type": "player_goal", "id": player.ID, "elapsed": elapsed,
				})
			}

		// スイッチ操作：そのまま全員に配信
		case "switch_activated":
			room.BroadcastMessage(msg)

		// アイテム取得：取得プレイヤーIDと対象IDを配信
		case "item_picked":
			targetID, _ := msg["target_id"].(string)
			room.BroadcastMessage(map[string]interface{}{
				"type": "item_picked", "id": player.ID, "target_id": targetID,
			})

		// 敵移動：そのまま全員に配信（ホストのみ送信）
		case "enemy_move":
			room.BroadcastMessage(msg)

		// リスポーン：位置情報をそのまま全員に配信
		case "respawn":
			room.BroadcastMessage(msg)

		default:
			msg["sender_id"] = player.ID
			msg["sender_name"] = player.Name
			room.BroadcastMessage(msg)
		}
	}
}

// startGameTimer ゲーム開始後に1秒ごとに残り時間を配信する
func startGameTimer(room *models.Room, roomID string) {
	ticker := time.NewTicker(1 * time.Second)
	defer ticker.Stop()

	remaining := gameDurationSeconds

	for {
		<-ticker.C
		remaining--

		// 残り時間を配信
		room.BroadcastMessage(map[string]interface{}{
			"type":           "timer_update",
			"time_remaining": remaining,
		})

		if remaining <= 0 {
			// タイムアップ通知
			room.BroadcastMessage(map[string]interface{}{
				"type": "time_up",
			})
			fmt.Printf("[ルーム %s] タイムアップ\n", roomID)

			mutex.Lock()
			delete(gameStarted, roomID)
			mutex.Unlock()
			return
		}
	}
}
