using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// プレイヤーを追従するカメラの制御クラス。
/// シーンをまたいでも破棄されず、プレイヤー生成後に追従対象を設定できる。
/// </summary>
public class GlobalCamera : MonoBehaviour
{
	#region フィールド

	public static GlobalCamera Instance;
	public Vector3 offset = new Vector3(0, 15, -5); // カメラとプレイヤーの距離

	private Transform _target; // 追従対象

	#endregion

	#region Unityイベント

	void Awake()
	{
		// シングルトン設定
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject);
		}
		else
		{
			Destroy(gameObject);
		}
	}

	void Start()
	{
		// 見下ろし視点に初期化
		transform.rotation = Quaternion.Euler(75, 0, 0);

		if (_target == null)
		{
			var player = GameObject.FindWithTag("Player1");
			if (player != null)
			{
				SetTarget(player.transform);
			}
		}
	}

	void LateUpdate()
	{
		// 追従対象の位置にオフセットを加算してカメラを移動させる
		if (_target != null)
		{
			transform.position = _target.position + offset;
		}
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// 追従対象を設定する。プレイヤー生成後に呼ぶ。
	/// </summary>
	/// <param name="target">追従させる対象</param>
	public void SetTarget(Transform target)
	{
		_target = target;
	}

	#endregion
}