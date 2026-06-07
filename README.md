# 【開発中】ステルスゲーム

Unity（C#）× Go言語によるリアルタイム2人対戦型マルチプレイヤーゲーム。

## 概要
逃げるプレイヤーと敵AIから逃げながらアイテムを取得しゴールを目指すステルスゲームです。
WebSocketを用いたリアルタイム通信により、2人のプレイヤーが同期してプレイできます。

## 使用技術
- **クライアント**：Unity 2022 / C#
- **サーバー**：Go言語 / gorilla/websocket
- **通信**：WebSocket（NativeWebSocket）
- **その他**：google/uuid

## 担当範囲
### 自分（澤田）
- Goサーバー全般
- WebSocketClient
- MissionManager / ItemManager
- GlobalCamera / NameInputManager / RoomMemberPanel / PlayerData
- EnemyManager・ElementGeneratorの通信連携部分

### メンバー
- EnemyManager（AI・巡回・索敵ロジック）
- ElementGenerator（マップ生成・ミニマップ表示）

## 主な実装内容
- WebSocketによるリアルタイムプレイヤー位置同期
- Goroutineを用いたルーム並列管理
- Mutexによるスレッドセーフ設計
- サーバー側タイマー（1秒ごとにブロードキャスト）
- ランキングシステム（JSONファイル永続化・Top3管理）
- 敵AI同期（ホストが送信、ゲストが受信して補間移動）
- 3つのミッション管理（MissionManager）

## 開発状況
現在開発中です。
