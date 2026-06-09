package models

import (
	"sync"

	"github.com/gorilla/websocket"
)

// Room 接続中のプレイヤーを管理するルーム
type Room struct {
	Clients map[*websocket.Conn]*Player // 接続中のプレイヤー一覧
	Mutex   sync.Mutex
}

// NewRoom 新しいルームを生成して返す
func NewRoom() *Room {
	return &Room{
		Clients: make(map[*websocket.Conn]*Player),
	}
}

// AddPlayer ルームにプレイヤーを追加する
func (r *Room) AddPlayer(conn *websocket.Conn, player *Player) {
	r.Mutex.Lock()
	defer r.Mutex.Unlock()
	r.Clients[conn] = player
}

// RemovePlayer ルームからプレイヤーを削除する
func (r *Room) RemovePlayer(conn *websocket.Conn) {
	r.Mutex.Lock()
	defer r.Mutex.Unlock()
	delete(r.Clients, conn)
}

// BroadcastMessage ルーム内の全プレイヤーにメッセージを送信する
// 送信に失敗した接続は自動的に削除する
func (r *Room) BroadcastMessage(message map[string]interface{}) {
	r.Mutex.Lock()
	defer r.Mutex.Unlock()

	for conn := range r.Clients {
		err := conn.WriteJSON(message)
		if err != nil {
			conn.Close()
			delete(r.Clients, conn)
		}
	}
}
