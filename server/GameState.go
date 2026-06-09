package models

// GameState ゲーム全体の状態を管理する構造体
type GameState struct {
	RoomID        string `json:"room_id"`        // ルームID
	TimeRemaining int    `json:"time_remaining"` // 残り秒数
	Items         bool   `json:"items"`          // アイテム取得済みフラグ
	IsGameOver    bool   `json:"is_game_over"`   // ゲーム終了フラグ
}
