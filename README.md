# SNEAK DUNGEON【開発中】

Unity（C#）× Go言語によるリアルタイム2人協力型ステルスゲーム。

## 概要
2人のプレイヤーが協力して敵AIの目を掻い潜り、アイテムを取得しながらゴールを目指すステルスゲームです。
WebSocketを用いたリアルタイム通信により、2人のプレイヤーの位置・アニメーション・ギミック操作・敵AIの状態が完全同期されます。

## 使用技術
- **クライアント**：Unity 2022 / C#
- **サーバー**：Go言語 / gorilla/websocket
- **通信**：WebSocket（NativeWebSocket）
- **その他**：google/uuid

## 担当範囲

### 自分（澤田）
- **Goサーバー全般**（ルーム管理・ブロードキャスト・ランキング・タイマー）
- **WebSocketClient.cs**（通信の中枢。全メッセージの送受信・振り分け）
- **MissionManager / ItemManager**
- **GlobalCamera / NameInputManager / RoomMemberPanel / PlayerData**
- **EnemyManager・ElementGenerator・SwitchManager の通信連携部分**（後述）

### メンバー
- EnemyManager（敵AI・巡回・索敵ロジック本体）
- ElementGenerator（マップ生成・ミニマップ表示・ギミック壁管理）
- SwitchManager（スイッチ・ギミック操作ロジック本体）

---

## 主な実装内容

### サーバー（Go）
- WebSocketによるリアルタイムルーム管理（最大2人×4ルーム）
- Goroutineを用いたルーム並列処理
- Mutexによるスレッドセーフ設計
- サーバー側タイマー（1秒ごとに残り時間をブロードキャスト）
- ランキングシステム（JSONファイル永続化・ミッション数降順・クリアタイム昇順のTop3管理）
- 満員チェック・必須パラメータバリデーション

### クライアント通信（Unity/C#）
- プレイヤー位置・向き・アニメーション状態のリアルタイム同期
- 敵AI同期（ホストが送信、ゲストが受信して補間移動）
- スイッチ・ギミック操作の同期
- アイテム取得・リスポーン・ゴールの同期
- 入退室・準備完了・ゲームスタートのロビー管理
- チャット・スタン・スタン解除の同期
- シーン遷移時のWebSocket切断・状態リセット処理
- DontDestroyOnLoadによるシーンをまたいだ接続維持
- 3つのミッション管理（MissionManager）

---

## 通信連携担当部分（スクリプト抜粋）

メンバーが担当したスクリプトのうち、以下の箇所は自分（澤田）が通信連携部分を実装・追記しています。

### EnemyManager.cs（通信連携部分）
- `HandleSoundFromRemote()`：ゲスト側でホストのプレイヤー足音をWebSocketClient経由で受信し、敵AIの音検知処理へ渡す
- `GetReactionState()` / `SetReactionState()`：敵のリアクション（！？）をサーバーと同期するための状態取得・設定
- `SetLastSoundPosition()`：サーバーから受信した音検知位置を敵AIに反映
- `AlertColor()`：捕まった判定時にローカルプレイヤーか相手プレイヤーかを区別し、`PlayerController.Respawn()`または`SendRemoteRespawn()`を呼び分ける
- `isRemoteControlled`フラグ：ゲスト側では敵AIの自律動作を停止し、サーバー受信データで動かす

### ElementGenerator.cs（通信連携部分）
- `SetPlayerTransform()` / `SetRemotePlayerTransform()`：WebSocketClientから自分・相手プレイヤーのTransformを受け取り、ミニマップ表示に反映
- `GetSwitchList()`：スイッチ受信処理（HandleSwitchActivatedMessage）が正しいスイッチManagerのみを検索できるよう公開
- `gimmickWallDic`：スイッチ操作と連動したギミック壁の管理辞書（SwitchManagerのOpenGimmickWallから参照）

### SwitchManager.cs（通信連携部分）
- `OnSwitchActivated()`：相手プレイヤーがスイッチを操作した際にサーバーから受信してローカルに反映（壁アニメーション・壁破棄・色変更）
- `OpenGimmickWall()`：ElementGeneratorのgimmickWallDicと連携して透明壁を破棄
- `DoActionSwitch()`内の`SendSwitchActivated()`呼び出し：スイッチ操作をサーバーへ送信
- `DoActionEnemy()`内の`SendEnemyStun()` / `SendEnemyStunCancel()`呼び出し：敵スタン・解除をサーバーへ送信

## 開発状況
現在開発中。