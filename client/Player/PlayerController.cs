using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの移動・動作・足音を管理するクラス。
/// 自分のプレイヤーのときだけ入力を受け付ける。
/// 相手のプレイヤーはサーバーから受け取った位置情報で動く。
/// </summary>
public class PlayerController : MonoBehaviour
{
	#region インスペクター設定

	[Header("操作設定")]
	public bool isLocalPlayer = false;

	[Header("移動速度設定")]
	public float walkSpeed = 5.0f;
	public float crouchSpeed = 2.5f;

	[Header("足音設定")]
	public float sneakVolume = 5f;
	public float walkVolume = 15f;
	public delegate void SoundEventHandler(Vector3 position, float volume);
	public event SoundEventHandler OnMakeSound;

	[Header("リスポーン演出")]
	public Image catchFadePanel;
	public Text catchText;

	#endregion

	#region フィールド

	private Rigidbody _rb;

	// 入力管理
	private Vector2 _moveInput;
	private PlayerInput playerInput;
	private InputAction moveAction;
	private InputAction punchAction;
	private InputAction moveSneak;
	public System.Action OnPunchInput;

	Animator Am;
	public bool isAnimationStart = false;

	public bool isAction = false;
	public bool isPlayerMoveStop = false; // 移動停止フラグ（スイッチ操作中など）
	public bool isSneaking = false;

	public string lastTrigger = ""; // 最後に発火した動作トリガー（同期用）

	private Transform currentRespawnPoint; // リスポーン地点

	#endregion

	#region Unityイベント

	void Awake()
	{
		_rb = GetComponent<Rigidbody>();
		if (_rb != null)
			_rb.constraints = RigidbodyConstraints.FreezeRotation;
		Am = GetComponent<Animator>();

		// 入力管理の初期化
		playerInput = GetComponent<PlayerInput>();
		moveAction = playerInput.actions["Move"];
		punchAction = playerInput.actions["ActionPunch"];
		moveSneak = playerInput.actions["Sneak"];

		catchFadePanel = GameObject.Find("RespawnFadePanel")?.GetComponent<Image>();
		catchText = GameObject.Find("リスポーン時テキスト")?.GetComponent<Text>();
	}

	private void OnEnable()
	{
		punchAction.performed += OnPunch;
		moveSneak.started += OnSneakStart;
		moveSneak.canceled += OnSneakEnd;
	}

	private void OnDisable()
	{
		punchAction.performed -= OnPunch;
		moveSneak.started -= OnSneakStart;
		moveSneak.canceled -= OnSneakEnd;
	}

	void Update()
	{
		if (!isLocalPlayer) return;
		CaptureInput();
	}

	void FixedUpdate()
	{
		if (!isLocalPlayer) return;
		ApplyMovement();
	}

	#endregion

	#region 入力管理

	/// <summary>攻撃ボタンが押されたとき</summary>
	public void OnPunch(InputAction.CallbackContext context)
	{
		if (!context.performed) return;
		OnPunchInput?.Invoke();
	}

	/// <summary>忍び歩きボタンが押されたとき</summary>
	public void OnSneakStart(InputAction.CallbackContext context)
	{
		isSneaking = true;
	}

	/// <summary>忍び歩きボタンが離されたとき</summary>
	public void OnSneakEnd(InputAction.CallbackContext context)
	{
		isSneaking = false;
	}

	#endregion

	#region 入力処理

	/// <summary>
	/// 入力を取得してアニメーションと足音を制御する
	/// </summary>
	private void CaptureInput()
	{
		// 移動停止中
		if (isPlayerMoveStop)
		{
			_moveInput = Vector2.zero;
			if (_rb != null) _rb.velocity = Vector3.zero;
			Am.SetBool("Run", false);
			Am.SetBool("Sneak", false);
			return;
		}

		_moveInput = moveAction.ReadValue<Vector2>();
		bool isMoving = _moveInput.sqrMagnitude > 0.01f;

		// 忍び歩き
		if (isMoving && isSneaking)
		{
			Am.SetBool("Sneak", true);
			Am.SetBool("Run", false);
		}
		// 走り
		else if (isMoving)
		{
			MakeSound(transform.position, walkVolume);
			Am.SetBool("Run", true);
			Am.SetBool("Sneak", false);
		}
		// 待機
		else
		{
			Am.SetBool("Run", false);
			Am.SetBool("Sneak", false);
		}
	}

	#endregion

	#region 移動処理

	/// <summary>
	/// 実際の移動・向き変更・足音発生を行う
	/// </summary>
	private void ApplyMovement()
	{
		Vector3 moveDir = new Vector3(_moveInput.x, 0, _moveInput.y);
		float speed = isSneaking ? crouchSpeed : walkSpeed;

		if (_rb != null)
		{
			if (moveDir.sqrMagnitude < 0.01f)
			{
				_rb.velocity = new Vector3(0, _rb.velocity.y, 0);
				return;
			}

			// 方向転換をなめらかにする
			Vector3 targetVelocity = new Vector3(moveDir.x * speed, _rb.velocity.y, moveDir.z * speed);
			_rb.velocity = Vector3.Lerp(_rb.velocity, targetVelocity, Time.fixedDeltaTime * 20f);
		}

		if (moveDir != Vector3.zero)
			transform.rotation = Quaternion.LookRotation(moveDir);

		if (isSneaking)
			MakeSound(transform.position, sneakVolume);
		else
			MakeSound(transform.position, walkVolume);
	}

	#endregion

	#region アクション処理

	/// <summary>
	/// 敵を攻撃するアニメーションを再生する
	/// </summary>
	public void PunchEnemy()
	{
		if (isAction) return;
		isAction = true;
		Am.SetTrigger("PunchEnemy");
		lastTrigger = "PunchEnemy";
	}

	/// <summary>
	/// 敵への攻撃アニメーションの開始フラグを立てる
	/// </summary>
	public void StartAnimationEnemy()
	{
		isAnimationStart = true;
	}

	/// <summary>
	/// スイッチを操作するアニメーションを再生する
	/// </summary>
	public void PunchSwitch()
	{
		//if (isAction) return;
		print("スイッチアニメーション起動");
		Am.SetBool("Run", false);
		Am.SetBool("Sneak", false);
		Am.SetTrigger("PunchSwitch");
		lastTrigger = "PunchSwitch";
	}

	/// <summary>
	/// 現在の動作状態を文字列で返す（同期用）
	/// </summary>
	public string GetAnimState()
	{
		if (Am.GetBool("Run")) return "run";
		if (Am.GetBool("Sneak")) return "sneak";
		return "idle";
	}

	/// <summary>
	/// アクション終了時にアニメーションイベントから呼ばれる
	/// </summary>
	public void EndAction()
	{
		isAction = false;
	}

	/// <summary>
	/// 移動停止フラグを解除する
	/// </summary>
	public void EndMove()
	{
		isPlayerMoveStop = false;
	}

	#endregion

	#region 足音処理

	void MakeSound(Vector3 position, float volume)
	{
		OnMakeSound?.Invoke(position, volume);
	}

	public bool IsSneaking => Input.GetKey(KeyCode.LeftShift);

	#endregion

	#region リスポーン処理

	/// <summary>
	/// リスポーン地点を保存する
	/// </summary>
	public void SetRespawnPoint(Transform point)
	{
		currentRespawnPoint = point;
		Debug.Log("リスポーン地点更新：" + point.position);
	}

	/// <summary>
	/// リスポーン演出を開始する
	/// </summary>
	public void Respawn()
	{
		if (currentRespawnPoint != null)
			StartCoroutine(RespawnWithEffect());
	}

	/// <summary>
	/// 画面を暗転させてリスポーン位置に移動し、フェードで復帰する演出
	/// </summary>
	private IEnumerator RespawnWithEffect()
	{
		// 発見時のテキストを表示
		if (catchText != null)
		{
			var c = catchText.color;
			c.a = 1f;
			catchText.color = c;
		}

		// 画面を暗転させる
		if (catchFadePanel != null)
		{
			float elapsed = 0f;
			while (elapsed < 0.5f)
			{
				elapsed += Time.deltaTime;
				var c = catchFadePanel.color;
				c.a = Mathf.Lerp(0, 1, elapsed / 0.5f);
				catchFadePanel.color = c;
				yield return null;
			}
		}

		yield return new WaitForSeconds(0.5f);

		// リスポーン位置に移動
		_rb.velocity = Vector3.zero;
		_rb.angularVelocity = Vector3.zero;
		transform.position = currentRespawnPoint.position;
		transform.rotation = currentRespawnPoint.rotation;

		var wsClient = FindObjectOfType<WebSocketClient>();
		if (wsClient != null) wsClient.SendRespawn(transform.position);

		// 敵の警戒度をリセット
		var enemies = GameObject.FindGameObjectsWithTag("Enemy");
		foreach (var e in enemies)
		{
			var em = e.GetComponent<EnemyManager>();
			if (em != null)
			{
				em.ResetRespawnFlag();
				em.currentAlertCount = em.alertCount;
			}
		}

		yield return new WaitForSeconds(0.3f);

		if (catchFadePanel != null)
		{
			float elapsed = 0f;
			while (elapsed < 0.5f)
			{
				elapsed += Time.deltaTime;
				float alpha = Mathf.Lerp(1, 0, elapsed / 0.5f);

				var c = catchFadePanel.color;
				c.a = alpha;
				catchFadePanel.color = c;

				if (catchText != null)
				{
					var tc = catchText.color;
					tc.a = alpha;
					catchText.color = tc;
				}

				yield return null;
			}
		}

		if (catchFadePanel != null)
		{
			var c = catchFadePanel.color;
			c.a = 0f;
			catchFadePanel.color = c;
		}
		if (catchText != null)
		{
			var c = catchText.color;
			c.a = 0f;
			catchText.color = c;
		}
	}

	/// <summary>
	/// 相手プレイヤーがつかまったときにサーバーから呼ばれるリスポーン演出
	/// </summary>
	public void RespawnWithEffectPublic()
	{
		StartCoroutine(RespawnWithEffect());
	}

	private IEnumerator CheckPosition()
	{
		yield return new WaitForSeconds(0.1f);
		Debug.Log("0.1秒後：" + transform.position);

		yield return new WaitForSeconds(0.4f);
		Debug.Log("0.5秒後：" + transform.position);
	}

	#endregion
}