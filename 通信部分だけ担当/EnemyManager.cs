using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;
using UnityEngine.Experimental.GlobalIllumination;
using static UnityEngine.GraphicsBuffer;


public class EnemyManager : MonoBehaviour
{
    #region 状態管理

    public enum EnemyState
    {
        Patrol,
        FocusPlayer,
        LookSoundPoint
    }
    public enum StrongEnemyState
    {
        sPatrol,
        sLookSoundPoint
    }

    [SerializeField] EnemyState enemyState;
    [SerializeField] StrongEnemyState strongEnemyState;

    #endregion

    #region コンポーネント参照

    Light Sl;
    PlayerController Pc;
    SwitchManager Sm;
    public TextMeshProUGUI reactionText;
    private Transform cameraTransform;

    Animator animWall;
    Animator animEnemy;

    #endregion

    #region 巡回設定

    // 巡回ポイント
    GameObject startPoint;   // 最初のポイント
    GameObject targetPoint;  // 今向かっているポイント

    [Header("巡回ポイント")]
    public GameObject[] movePoints;

    // 移動
    [Header("移動速度")]
    public float speed = 3f;

    [Header("回転速度")]
    public float rotateSpeed = 3f;
    public float stoprotateSpeed = 50f;

    // 停止
    [Header("停止時間 最小")]
    public float stopMoveCooldownMin = 5f;
    [Header("停止時間 最大")]
    public float stopMoveCooldownMax = 10f;

    float stopMoveCooldown;
    float currentTime = 0f;

    #endregion

    #region 強化敵設定

    [Header("強化敵用")]
    [SerializeField] float waitTime = 3f;
    [SerializeField] float rotateAngle = 90f; // 回転する角度

    private float timer = 0f;
    private bool isRotating = false;
    private float currentRotateAmount = 0f;

    #endregion

    #region 視界検知

    Transform targetPlayer; // プレイヤー検知

    [Header("検知する距離")]
    public float viewRadius = 10f;
    [Header("検知する角度")]
    public float viewAngle = 90f;

    [Header("警戒カウント")]
    public float alertCount = 6;
    public float currentAlertCount;

    [Header("見失ってから巡回に戻る時間")]
    public float lostPlayerTime = 2f;
    float lostTimer = 0f;
    private Transform _alertTarget; // 警戒度を上げたプレイヤー

    #endregion

    #region 音検知

    [Header("プレイヤー")]
    public Transform player;
    [Header("敵が足音の聞こえる範囲")]
    public float hearingRange = 15f;
    [Header("音が聞こえなくなってからあきらめるまでの時間")]
    public float alertDuration = 3f;
    private float alertTimer = 0f;

    public Vector3 GetLastSoundPosition() => lastSoundPosition;
    private bool isHearingSound = false;     // 今、音が聞こえているかどうかのフラグ
    private Vector3 lastSoundPosition;       // 最後に音が聞こえた場所
    private float _remoteSoundCooldown = 0f; // Player2音検知用クールダウン

    #endregion

    #region フラグ管理

    bool isAlerted = false;
    bool isFoundPlayer = false;
    bool isReaction = false;
    public bool isStopMove = false; // 敵が止まっているときに動きを完全に止める

    private bool _soundRegistered = false;
    private bool _isRespawning = false;

    [HideInInspector] public bool isRemoteControlled = false; // player2側はtrue


    #endregion

    #region ギミック連携

    [Header("スイッチギミック用ID")]
    public int enemyID;
    public int gimmickCount = 0;
    [Header("いくつのボタンでギミック解除するか"), SerializeField]
    public int switchCount;

    #endregion

    #region デバック

    [Header("Gizmos Debug")]
    [SerializeField] bool showViewDebug = true;   // 視界・レイキャスト
    [SerializeField] bool showSoundDebug = true;  // 音検知
    private Vector3 debugSoundPos;
    private float debugSoundRange;
    private float debugTimer;

    #endregion

    #region 巡回ポイント決定

    /// <summary>
    /// 一番近いポイントを取得
    /// </summary>
    /// <returns></returns>
    GameObject StartPoint()
    {
        GameObject[] objs = GameObject.FindGameObjectsWithTag("Point");

        GameObject nearest = null;
        float minDistance = Mathf.Infinity;

        Vector3 currentPos = transform.position;

        foreach (GameObject obj in objs)
        {
            float distance = Vector3.SqrMagnitude(obj.transform.position - currentPos);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }
        return nearest;
    }

    /// <summary>
    /// ランダムで次の近くのポイントを探す
    /// </summary>
    /// <param name="currentPoint"></param>
    /// <returns></returns>
    GameObject GetNearPoint(GameObject currentPoint)
    {
        GameObject[] objs = GameObject.FindGameObjectsWithTag("Point");

        List<GameObject> nearPoints = new List<GameObject>();

        foreach (GameObject obj in objs)
        {
            if (obj == currentPoint) continue;

            float distance = Vector3.Distance(currentPoint.transform.position, obj.transform.position);

            // 一定距離以内だけ候補にする
            if (distance <= 10f)
            {
                nearPoints.Add(obj);
            }
        }

        // 候補が無い場合
        if (nearPoints.Count == 0)
        {
            return currentPoint;
        }

        // 候補からランダム選択
        return nearPoints[Random.Range(0, nearPoints.Count)];
    }

    #endregion

    #region 巡回ポイント移動

    /// <summary>
    /// ポイントに向かって移動
    /// </summary>
    void MoveEnemy()
    {
        if (targetPoint == null) return;

        if (!isStopMove)
        {
            MoveToPoint(targetPoint); // 実際に移動させる処理
        }

        // 近づいたら切り替え
        if (Vector3.Distance(transform.position, targetPoint.transform.position) < 0.1f)
        {
            isStopMove = true;
            currentTime += Time.deltaTime; // 目的の位置に着いたら少し止める
            SearchPlayer();                // 止まっている間視点を動かす

            if (currentTime >= stopMoveCooldown)
            {
                GameObject oldPoint = targetPoint;

                // 次のポイントをランダム取得
                targetPoint = GetNearPoint(oldPoint);

                // 停止時間を再抽選
                stopMoveCooldown = Random.Range(stopMoveCooldownMin, stopMoveCooldownMax);

                currentTime = 0;
                isStopMove = false;
            }
        }
    }

    /// <summary>
    /// 取得したポイントに向かって移動
    /// </summary>
    /// <param name="point"></param>
    void MoveToPoint(GameObject point)
    {
        // 目的地方向
        Vector3 direction = point.transform.position - transform.position;

        direction.y = 0;

        // 向きたい回転
        Quaternion lookRotation = Quaternion.LookRotation(direction);

        // なめらか回転
        transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, rotateSpeed * Time.deltaTime);

        // 角度差を取得
        float angle = Quaternion.Angle(transform.rotation, lookRotation);

        // ある程度向けたら移動
        if (angle < 3f)
        {
            transform.position += transform.forward * speed * Time.deltaTime;
        }
    }

    #endregion

    #region プレイヤーを探す処理

    /// <summary>
    /// 敵があたりを見渡す処理
    /// </summary>
    void SearchPlayer()
    {
        if (!isStopMove)
        {
            return;
        }
        if (currentTime >= 3)
        {
            return;
        }
        // 見回り動作
        transform.Rotate(0, stoprotateSpeed * Time.deltaTime, 0); // 右回転
    }

    /// <summary>
    /// 強化敵があたりを見渡す処理
    /// </summary>
    void StrongSearchPlayer()
    {
        // 停止中
        if (!isRotating)
        {
            timer += Time.deltaTime;

            if (timer >= waitTime)
            {
                timer = 0f;

                isRotating = true;
                currentRotateAmount = 0f;
            }
        }
        // 回転中
        else
        {
            float rotateThisFrame = rotateSpeed * Time.deltaTime;

            transform.Rotate(0f, rotateThisFrame, 0f);

            currentRotateAmount += rotateThisFrame;

            if (currentRotateAmount >= rotateAngle)
            {
                isRotating = false;
            }
        }
    }

    #endregion

    #region プレイヤー発見処理

    /// <summary>
    /// プレイヤーを発見するさせる時の処理
    /// Player1とPlayer2両方を検知するように変更
    /// </summary>
    void PlayerFound()
    {
        // 両方のプレイヤーを確認して近い方をターゲットに
        GameObject p1 = GameObject.FindWithTag("Player1");
        GameObject p2 = GameObject.FindWithTag("Player2");
        Transform closest = null;
        float minDist = Mathf.Infinity;
        foreach (var p in new[] { p1, p2 })
        {
            if (p == null) continue;
            float dist = Vector3.Distance(transform.position, p.transform.position);
            if (dist < minDist) { minDist = dist; closest = p.transform; }
        }
        if (closest != null) targetPlayer = closest;

        if (targetPlayer == null) return;

        isFoundPlayer = false;
        // ターゲット方向計算
        Vector3 dirToTarget = (targetPlayer.position - transform.position).normalized;
        // 距離計算
        float dstToTarget = Vector3.Distance(transform.position, targetPlayer.position);

        // 距離が範囲内かチェック
        if (dstToTarget < viewRadius)
        {
            // 正面方向となす角度を計算
            float angleToTarget = Vector3.Angle(transform.forward, dirToTarget);

            // 角度が扇型の範囲内かチェック
            if (angleToTarget < viewAngle / 2f)
            {
                // 間に障害物がないかレイキャストで確認
                RaycastHit hit;
                if (Physics.Raycast(transform.position, dirToTarget, out hit, dstToTarget))
                {
                    // 障害物に当たった場合
                    if (hit.transform == targetPlayer || hit.transform.IsChildOf(targetPlayer))
                    {
                        //Debug.Log("プレイヤー発見");
                        isFoundPlayer = true;
                        _alertTarget = targetPlayer;

                        if (gameObject.tag == "StrongEnemy") return;

                        TowardThePlayer();
                        enemyState = EnemyState.FocusPlayer;
                    }
                }
            }
        }
    }

    #endregion

    #region 警戒状態処理

    /// <summary>
    /// 警戒状態処理
    /// </summary>
    void AlertFunction()
    {
        // 警戒時間
        if (isFoundPlayer)
        {
            currentAlertCount -= Time.deltaTime;
        }
        else
        {
            currentAlertCount += Time.deltaTime;
        }

        currentAlertCount = Mathf.Clamp(currentAlertCount, 0, alertCount);

        // 見失った時の処理
        if (enemyState == EnemyState.FocusPlayer && !isAlerted)
        {
            if (isFoundPlayer)
            {
                // 見えている間はリセット
                lostTimer = lostPlayerTime;
            }
            else
            {
                // 見失ったら減算
                lostTimer -= Time.deltaTime;

                // 一定時間見失ったら巡回へ
                if (lostTimer <= 0f)
                {
                    ResetPatrolState();
                    enemyState = EnemyState.Patrol;
                }
            }
        }

        AlertColor(); // スポットライト色変更

        // 警戒状態のときのみタイマーを減算
        if (isAlerted)
        {
            alertTimer -= Time.deltaTime;

            // 音が聞こえなくなって一定時間経った
            if (alertTimer <= 0)
            {
                CancelAlert();
            }
        }
    }

    /// <summary>
    /// スポットライトの色変更
    /// </summary>
    void AlertColor()
    {
        if (Sl == null) return;
        if (currentAlertCount <= 0 && !_isRespawning)
        {
            _isRespawning = true;
            Debug.Log($"捕まった: _alertTarget={_alertTarget?.name} isLocalPlayer={_alertTarget?.GetComponent<PlayerController>()?.isLocalPlayer}");
            if (_alertTarget != null)
            {
                var pc = _alertTarget.GetComponent<PlayerController>();
                if (pc != null && pc.isLocalPlayer)
                {
                    pc.Respawn();
                }
                else
                {
                    var wsClient = FindObjectOfType<WebSocketClient>();
                    if (wsClient != null) wsClient.SendRemoteRespawn();
                    currentAlertCount = alertCount; // 警戒度リセット
                                                    _isRespawning = false; // ここでリセット
                }
            }
        }
        if (currentAlertCount <= 1)
            Sl.color = Color.red;
        else if (currentAlertCount < 3)
            Sl.color = new Color(1f, 0.5f, 0f);
        else
            Sl.color = new Color(0.827f, 0.851f, 0.439f);
    }

    /// <summary>
    /// 音が聞こえなくなった時の処理
    /// </summary>
    void CancelAlert()
    {
        isAlerted = false;
        isStopMove = false;

        if (gameObject.tag == "StrongEnemy")
        {
            strongEnemyState = StrongEnemyState.sLookSoundPoint;
            return;
        }

        // 最後に聞こえた音の地点に視点を向ける
        enemyState = EnemyState.LookSoundPoint;
    }

    public void ResetRespawnFlag()
    {
        _isRespawning = false;
    }

    #endregion

    #region デバック用ギズモ

    // 範囲デバック用
    private void OnDrawGizmos()
    {
        // 視界・レイキャスト
        if (showViewDebug)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewRadius);

            Vector3 leftBoundary = Quaternion.Euler(0, -viewAngle / 2, 0) * transform.forward;

            Vector3 rightBoundary = Quaternion.Euler(0, viewAngle / 2, 0) * transform.forward;

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, transform.position + leftBoundary * viewRadius);

            Gizmos.DrawLine(transform.position, transform.position + rightBoundary * viewRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * viewRadius);

            if (targetPlayer != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, targetPlayer.position);
            }
        }

        // 音検知
        if (showSoundDebug)
        {
            // 音検知範囲
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, hearingRange);

            // 最後に聞いた音
            if (lastSoundPosition != Vector3.zero)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastSoundPosition, 0.5f);

                Gizmos.DrawLine(transform.position, lastSoundPosition);
            }

            // 検知した音の有効範囲
            if (debugTimer > 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(debugSoundPos, debugSoundRange);

                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(debugSoundPos, 0.3f);

                Gizmos.DrawLine(transform.position, debugSoundPos);
            }
        }
    }

    #endregion

    #region 音検知関数

    /// <summary>
    /// プレイヤーの方向に向かせる処理
    /// </summary>
    void FocusPlayer()
    {
        if (targetPlayer == null) return;
        Vector3 direction = targetPlayer.position - transform.position;

        // 高さ無視
        direction.y = 0;

        // 向きたい回転
        Quaternion lookRotation = Quaternion.LookRotation(direction);

        // なめらか回転
        transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, rotateSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 視野の範囲内ならプレイヤーの方向を見ながら移動
    /// </summary>
    void TowardThePlayer()
    {
        // 音が聞こえている間だけ、その場所に向かって移動する
        if (isHearingSound)
        {
            // 音の位置
            Vector3 targetPos = new Vector3(lastSoundPosition.x, transform.position.y, lastSoundPosition.z);

            // 向きたい方向
            Vector3 direction = targetPos - transform.position;

            // 向きたい回転
            Quaternion lookRotation = Quaternion.LookRotation(direction);

            // なめらか回転
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, rotateSpeed * Time.deltaTime);

            // 回転しながら移動
            float towardSpeed = speed / 2;

            transform.position = Vector3.MoveTowards(transform.position, targetPos, towardSpeed * Time.deltaTime);
        }
        isHearingSound = false;
    }

    /// <summary>
    ///  プレイヤーの音検知
    /// </summary>
    /// <param name="soundPosition"></param>
    /// <param name="volume"></param>
    void HandleSound(Vector3 soundPosition, float volume)
    {
        if (Sm != null && Sm.isEnemyMoveStop) return; // スタン中は音検知しない

        // 音が聞こえる範囲内かどうかを計算
        float distanceToSound = Vector3.Distance(transform.position, soundPosition);

        // 音量と距離から最終的な検知距離を計算
        if (distanceToSound <= hearingRange * volume)
        {
            // 音を検知したら、警戒状態にしてタイマーを最大値にリセット
            isAlerted = true;
            alertTimer = alertDuration;

            isHearingSound = true;           // 音が聞こえている状態にする
            lastSoundPosition = soundPosition; // 音の位置を記憶

            // デバック用
            debugSoundPos = soundPosition;
            debugSoundRange = hearingRange * volume;
            debugTimer = 2f; // 2秒表示

            // 音を出したプレイヤーを特定してalertTargetに設定
            var p1 = GameObject.FindWithTag("Player1");
            var p2 = GameObject.FindWithTag("Player2");
            foreach (var p in new[] { p1, p2 })
            {
                if (p == null) continue;
                if (Vector3.Distance(p.transform.position, soundPosition) < 1f)
                {
                    _alertTarget = p.transform;
                    break;
                }
            }

            if (reactionText != null)
            {
                reactionText.text = "!";
                reactionText.gameObject.SetActive(true); // ビックリマーク表示
                isReaction = true;
            }

            enemyState = EnemyState.FocusPlayer;
        }
    }

    #region 澤田作：サーバ側音検知

    /// <summary>
    /// Player2の音検知（WebSocketClient経由で呼ばれる）
    /// クールダウン付きでHandleSoundを呼ぶ
    /// </summary>
    public void HandleSoundFromRemote(Vector3 position, float volume)
    {
        if (_remoteSoundCooldown > 0) return; // クールダウン中は無視
        HandleSound(position, volume);
        _remoteSoundCooldown = 0.1f;
    }

    #endregion

    /// <summary>
    /// 最後に聞こえた音の方向を見る
    /// </summary>
    void LookSoundPoint()
    {
        Vector3 direction = lastSoundPosition - transform.position;

        direction.y = 0;

        Quaternion lookRotation = Quaternion.LookRotation(direction);

        // なめらか回転
        transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, rotateSpeed * Time.deltaTime);

        // 向き終わったか判定
        float angle = Quaternion.Angle(transform.rotation, lookRotation);

        if (angle < 3f)
        {
            // 少し待ってから巡回へ戻す
            currentTime += Time.deltaTime;
            reactionText.text = "?";

            if (currentTime >= 1.5f)
            {
                ResetPatrolState();
                currentTime = 0;
                reactionText.gameObject.SetActive(false); // ビックリマーク非表示
                enemyState = EnemyState.Patrol;
            }
        }
    }

    /// <summary>
    /// 強化適用音検知
    /// </summary>
    void StrongLookSoundPoint()
    {
        Vector3 direction = lastSoundPosition - transform.position;

        direction.y = 0;

        Quaternion lookRotation = Quaternion.LookRotation(direction);

        // なめらか回転
        transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, rotateSpeed * Time.deltaTime);

        // 向き終わったか判定
        float angle = Quaternion.Angle(transform.rotation, lookRotation);

        if (angle < 3f)
        {
            // 少し待ってから巡回へ戻す
            currentTime += Time.deltaTime;
            reactionText.text = "?";

            if (currentTime >= 1.5f)
            {
                currentTime = 0;
                reactionText.gameObject.SetActive(false); // ビックリマーク非表示
                strongEnemyState = StrongEnemyState.sPatrol;
            }
        }
    }

    #endregion

    #region 澤田作：State(サーバー用)

    public string GetReactionState()
    {
        if (reactionText == null || !reactionText.gameObject.activeSelf) return "";
        return reactionText.text;
    }

    public void SetReactionState(string state)
    {
        if (reactionText == null) return;
        if (Sm != null && Sm.isEnemyMoveStop) return; // スタン中は無視
        if (string.IsNullOrEmpty(state))
        {
            reactionText.gameObject.SetActive(false);
            isReaction = false;
        }
        else
        {
            reactionText.text = state;
            reactionText.gameObject.SetActive(true);
            isReaction = true;
        }
    }

    public void SetLastSoundPosition(Vector3 pos)
    {
        lastSoundPosition = pos;
    }

    #endregion

    #region アニメーション

    /// <summary>
    /// 強化敵の壁をだす処理
    /// </summary>
    public void PlayAnimationWall()
    {
        animWall.SetTrigger("wallUp");
        Sm.isEnemyMoveStop = true;
    }

    /// <summary>
    /// 敵が攻撃されてスタン状態にする処理
    /// </summary>
    public void PlayAnimationEnemy()
    {
        animEnemy.SetTrigger("Stun");
        Sm.isEnemyMoveStop = true;

        // 攻撃されたらリアクションテキストを消す
        if (reactionText != null)
        {
            reactionText.gameObject.SetActive(false);
            isReaction = false;
        }
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }
    }

    public void StunCancel()
    {
        animEnemy.SetTrigger("StunCancel");
        if (Sm != null) Sm.isEnemyMoveStop = false; // 追加
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }

    #endregion

    #region ギミック

    public void SwitchCountValue(int value)
    {
        gimmickCount += value;
    }

    #endregion

    #region 初期化処理

    /// <summary>
    /// 巡回状態を初期化
    /// </summary>
    public void ResetPatrolState()
    {
        currentTime = 0f;

        isStopMove = false;

        // 次のポイントを再設定
        targetPoint = GetNearPoint(StartPoint());

        // 停止時間を再抽選
        stopMoveCooldown = Random.Range(stopMoveCooldownMin, stopMoveCooldownMax);

        // テキスト非表示
        reactionText.gameObject.SetActive(false);
    }

    /// <summary>
    /// テキスト初期化
    /// </summary>
    void InitText()
    {
        Transform child = transform.Find("Model/ActionCanvas/ReactionText");
        if (child != null)
        {
            reactionText = child.GetComponent<TextMeshProUGUI>();
        }

        if (reactionText != null)
        {
            reactionText.gameObject.SetActive(false); // 最初は非表示
        }
    }

    /// <summary>
    /// コンポーネント初期化
    /// </summary>
    void InitComponent()
    {
        Sl = GetComponentInChildren<Light>();
        Sm = GetComponent<SwitchManager>();
        animEnemy = GetComponentInChildren<Animator>();
        animWall = GetComponentInChildren<Animator>();
    }

    void InitializeEnemyState()
    {
        enemyState = EnemyState.Patrol;
        currentAlertCount = alertCount;
        gimmickCount = 0;
    }

    /// <summary>
    /// 最初の移動ポイント決定
    /// </summary>
    void InitPoint()
    {
        // 一番近いポイント（スタート地点）
        startPoint = StartPoint();

        // 最初の目的地をランダムに決定
        targetPoint = GetNearPoint(startPoint);

        // 最初の停止時間をランダム化
        stopMoveCooldown = Random.Range(stopMoveCooldownMin, stopMoveCooldownMax);
    }

    /// <summary>
    /// 澤田作：プレイヤー初期化
    /// </summary>
    void InitPlayer()
    {
        Pc = (GameObject.FindWithTag("Player1") ?? GameObject.FindWithTag("Player2"))?.GetComponent<PlayerController>();
        if (Pc != null)
            Pc.OnMakeSound += HandleSound;

        targetPlayer = (GameObject.FindWithTag("Player1") ?? GameObject.FindWithTag("Player2"))?.transform;
    }

    void InitCamera()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    #endregion

    #region 澤田作：サーバ関連Update処理

    void TextUpdate()
    {
        // テキストが表示されている間、常にカメラの方を向かせる
        if (isReaction && reactionText != null && cameraTransform != null)
        {
            Vector3 dir = reactionText.transform.position - cameraTransform.position;
            dir.y = 0; // Y軸回転だけにする
            reactionText.transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    #endregion

    #region ステート管理処理

    void StateMachine()
    {
        if (gameObject.tag == "StrongEnemy")
        {
            switch (strongEnemyState)
            {
                case StrongEnemyState.sPatrol:
                    StrongSearchPlayer(); // 一定時間に視点を回転させる
                    break;
                case StrongEnemyState.sLookSoundPoint:
                    StrongLookSoundPoint();
                    break;
            }
            return;
        }

        // ステートマシーン
        switch (enemyState)
        {
            case EnemyState.Patrol:
                MoveEnemy();   // 敵が移動する処理
                break;
            case EnemyState.FocusPlayer:
                FocusPlayer(); // プレイヤーに視点のほうに視点を向ける処理
                break;
            case EnemyState.LookSoundPoint:
                LookSoundPoint(); // 最後に聞こえた音の地点に視点を向ける処理
                break;
        }
    }

    #endregion

    #region Update処理

    void UpdateDebugTimer()
    {
        if (debugTimer > 0)
        {
            debugTimer -= Time.deltaTime;
        }
    }

    #endregion

    #region Unityイベント

    // Startをプレイヤー生成待ちのために遅延実行に変更
    void Start()
    {
        Invoke("DelayedStart", 0.5f);
    }

    void DelayedStart()
    {
        InitText();                                 // テキスト初期化
        InitComponent();                            // コンポーネント初期化
        InitializeEnemyState();                     // 敵関連初期化
        InitPlayer();                               // プレイヤー初期化(澤田作)
        InitCamera();                               // カメラ初期化
        InitPoint();                                // 開始ポイント決定
    }

    // Update is called once per frame
    void Update()
    {
        UpdateDebugTimer();                         // 音検知デバック
        TextUpdate();                               // テキストを画面の方向に向ける処理(サーバ連携してないので今後実装予定)

        // 澤田作：サーバ関連Update
        if (isRemoteControlled) return;

        if (Sm == null || Sm.isEnemyMoveStop) return;

        if (_remoteSoundCooldown > 0)
            _remoteSoundCooldown -= Time.deltaTime;

        if (Sm.isEnemyMoveStop) return;

        if (!_soundRegistered)
        {
            var p = GameObject.FindWithTag("Player1") ?? GameObject.FindWithTag("Player2");
            if (p != null)
            {
                Pc = p.GetComponent<PlayerController>();
                if (Pc != null)
                {
                    Pc.OnMakeSound += HandleSound;
                    _soundRegistered = true;
                }
            }
        }

        if (isRemoteControlled) return;             // ゲスト側はAIを動かさない（WebSocketClientが位置を受信して動かす）

        PlayerFound();                              // プレイヤー発見処理
        AlertFunction();                            // 警戒状態処理

        StateMachine();                             // 敵のステート管理関数
    }

    #endregion

}