package models

// GameEvent ゲーム中に発生するイベントの通知用構造体
type GameEvent struct {
	Type      string `json:"type"`      // イベント種別："FOUND"（発見）/ "ITEM_PICKED"（アイテム取得）
	PlayerID  string `json:"player_id"` // イベントを発生させたプレイヤーID
	TargetID  string `json:"target_id"` // 対象のアイテムIDや敵のID
	Timestamp int64  `json:"timestamp"` // 発生時刻
}
