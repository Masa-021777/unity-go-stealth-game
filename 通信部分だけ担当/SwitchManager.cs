using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class SwitchManager : MonoBehaviour
{
    #region コンポーネント参照

    Renderer rd;
    EnemyManager em;
    EnemyManager enemy;
    PlayerController Pc;
    ElementGenerator Eg;

    private TextMeshProUGUI actionText;
    private Transform cameraTransform;

    #endregion

    #region プレイヤー接触管理

    private bool isPlayerInRange = false;

    #endregion

    #region アクション状態管理フラグ

    bool isActionEnemy= false;
    bool isActionSwitch = false;
    bool isEndAction = false;

    public bool isEnemyMoveStop = false;
	private bool _stunSent = false;
    
    #endregion

    #region スタン設定

    [Header("スタン時間"), SerializeField]
    float stanTime = 3f;
    float currentStanTime;

    #endregion

    #region ギミック連携

    public int targetEnemyID;

    #endregion
    
    #region 敵アクション処理

	/// <summary>
	/// 敵を殴った時の処理
	/// </summary>    
	void DoActionEnemy()
	{
		currentStanTime += Time.deltaTime;
		if (stanTime <= currentStanTime)
		{
			isEnemyMoveStop = false;
			currentStanTime = 0f;
			isActionEnemy = false;
			isEndAction = false;
			_stunSent = false; // リセット
			enemy.StunCancel();

			var wsClient = FindObjectOfType<WebSocketClient>();
			if (wsClient != null) wsClient.SendEnemyStunCancel(targetEnemyID);
			return;
		}

		if (!Pc.isAnimationStart) return;
		if (_stunSent) return; // 二重送信防止

		enemy.PlayAnimationEnemy();
		enemy.ResetPatrolState();
		enemy.reactionText.text = "×";
		Pc.isAnimationStart = false;
		isPlayerInRange = false;
		isEndAction = true;
        if (actionText != null) actionText.gameObject.SetActive(false);
        _stunSent = true; // フラグを立てる
        currentStanTime = 0f;

        var wsClient2 = FindObjectOfType<WebSocketClient>();
		if (wsClient2 != null) wsClient2.SendEnemyStun(targetEnemyID);
	}

    #endregion

    #region スイッチアクション処理

    /// <summary>
    /// スイッチを押したら対応した強化敵の遮る壁を出す
    /// </summary>
    void DoActionSwitch()
    {
        if (Pc.isAction) return;
        isEndAction = true;
        isPlayerInRange = false;
        rd.material.color = Color.red;

        isActionSwitch = false;

        var wsClient = FindObjectOfType<WebSocketClient>();
        if (wsClient != null) wsClient.SendSwitchActivated(targetEnemyID);
    }

    #endregion

    #region プレイヤー入力された時の処理

    void TryAction()
    {
        if (!isPlayerInRange) return;

        if (CompareTag("Enemy"))
        {
            isActionEnemy = true;
            Pc.isPlayerMoveStop = true;
            Pc.PunchEnemy();
        }

        if (CompareTag("Switch"))
        {
            Pc.isAction = true;
            Pc.isPlayerMoveStop = true;
            // ここで1回だけ実行
            if (em != null)
            {
                em.SwitchCountValue(1);
                em.PlayAnimationWall();
                OpenGimmickWall(targetEnemyID);
            }
            isActionSwitch = true;
            Pc.PunchSwitch();
        }
    }

    #endregion

    #region ギミック連携用関数

    public void SetTarget(EnemyManager enemy)
    {
        em = enemy;
    }

    /// <summary>
    /// スイッチ起動時に呼ばれる処理。対応するIDの透明壁を消去する
    /// </summary>
    public void OpenGimmickWall(int targetID)
    {
        // 指定されたIDの壁が存在するかチェック
        if (Eg.gimmickWallDic.ContainsKey(targetID))
        {
            foreach (GameObject wall in Eg.gimmickWallDic[targetID])
            {
                // オブジェクトごと破壊する場合
                Destroy(wall);
            }
            Eg.gimmickWallDic.Remove(targetID);
        }
    }

	#endregion

	#region 澤田作：サーバ関連

	/// <summary>
	/// 受信用
	/// </summary>
	public void OnSwitchActivated()
	{
		isEndAction = true;
		isPlayerInRange = false;
		if (em != null) em.PlayAnimationWall();
		OpenGimmickWall(targetEnemyID);
		rd.material.color = Color.red;
	}

	#endregion

	#region Unityイベント

	void Start()
    {
		Invoke("DelayedStart", 0.5f);
	}

	void DelayedStart()
	{
		var p = GameObject.FindWithTag("Player1") ?? GameObject.FindWithTag("Player2");

		Pc = p.GetComponent<PlayerController>();
		rd = GetComponent<Renderer>();
        enemy = GetComponent<EnemyManager>();
        Eg = GameObject.Find("StageMake").GetComponent<ElementGenerator>();
        Pc.OnPunchInput += TryAction;

        currentStanTime = 0f;
		// メインカメラの向きを取得用
		if (Camera.main != null)
		{
			cameraTransform = Camera.main.transform;
		}

		Transform child = transform.Find("Model/ActionCanvas/ActionText");
		if (child != null)
		{
			actionText = child.GetComponent<TextMeshProUGUI>();
		}

		if (actionText != null)
		{
			actionText.gameObject.SetActive(false); // 最初は非表示
		}
	}

	void Update()
    {
        if(isActionEnemy)
        {
            DoActionEnemy();  // 敵を殴った時
        }
        if (isActionSwitch)
        {
            DoActionSwitch(); // スイッチを殴った時
        }

        // テキストが表示されている間、常にカメラの方を向かせる
        if (isPlayerInRange && actionText != null && cameraTransform != null)
        {
            // テキストの親（Canvasなど）をカメラに向ける
            actionText.transform.rotation = Quaternion.LookRotation(actionText.transform.position - cameraTransform.position);
        }
    }

    #endregion

    #region 当たり判定処理

    private void OnTriggerEnter(Collider other)
    {
        if (isEndAction) return;
        if (other.CompareTag("Player1") || other.CompareTag("Player2"))
        {
            isPlayerInRange = true;
            var pc = other.GetComponent<PlayerController>();
            if (pc != null && pc.isLocalPlayer)
                Pc = pc;
            isPlayerInRange = true;
            if (actionText != null)
            {
                actionText.gameObject.SetActive(true);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player1") || other.CompareTag("Player2"))
        {
            isPlayerInRange = false;
            if (actionText != null)
            {
                actionText.gameObject.SetActive(false);
            }
        }
    }

    #endregion
}
