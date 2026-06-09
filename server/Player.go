package models

// Player ゲーム中のプレイヤー状態を管理する構造体
type Player struct {
	ID           string `json:"id"`            // 一意のプレイヤーID
	Name         string `json:"name"`          // プレイヤー名
	RoomID       string `json:"room_id"`       // 所属するルームID
	PlayerNumber int    `json:"player_number"` // 入室順のプレイヤー番号（1 or 2）

	// 座標
	PositionX float64 `json:"position_x"` // X座標
	PositionY float64 `json:"position_y"` // Y座標
	PositionZ float64 `json:"position_z"` // Z座標
	RotationY float64 `json:"rotation_y"` // 左右の向き（Y軸回転）
	RotationX float64 `json:"rotation_x"` // 上下の向き（X軸回転）
	VelocityX float64 `json:"velocity_x"` // 横方向の移動速度
	VelocityZ float64 `json:"velocity_z"` // 縦方向の移動速度

	// フラグ
	IsCrouching bool `json:"is_crouching"` // しゃがみ状態
	IsHidden    bool `json:"is_hidden"`    // 隠れ場所に入っているか
	IsCaught    bool `json:"is_caught"`    // 捕まったフラグ
}
