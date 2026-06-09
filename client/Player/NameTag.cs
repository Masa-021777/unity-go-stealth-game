using UnityEngine;

/// <summary>
/// プレイヤーの頭上に名前を表示するクラス。
/// 常にカメラの方向を向くビルボード表示を行う。
/// </summary>
public class NameTag : MonoBehaviour
{
	#region フィールド

	public Vector3 offset = new Vector3(0, 2.2f, 0); // プレイヤーからの表示位置

	private TextMesh _textMesh;
	private Transform _cameraTransform;

	#endregion

	#region Unityイベント

	void Awake()
	{
		// TextMeshを動的に追加して初期設定
		_textMesh = gameObject.AddComponent<TextMesh>();
		_textMesh.alignment = TextAlignment.Center;
		_textMesh.anchor = TextAnchor.LowerCenter;
		_textMesh.fontSize = 40;
		_textMesh.characterSize = 0.08f;
		_textMesh.color = Color.white;

		// 壁などに隠れないよう描画順を上げる
		GetComponent<MeshRenderer>().sortingOrder = 10;
	}

	void Start()
	{
		_cameraTransform = Camera.main?.transform;
	}

	void LateUpdate()
	{
		// 親プレイヤーの動きに追従
		transform.position = transform.parent.position + offset;

		// カメラに向けて回転（ビルボード）
		if (_cameraTransform != null)
			transform.forward = _cameraTransform.forward;
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// 表示する名前を設定する
	/// </summary>
	public void SetName(string playerName)
	{
		_textMesh.text = playerName;
	}

	/// <summary>
	/// 名前タグの表示・非表示を切り替える
	/// </summary>
	public void SetVisible(bool visible)
	{
		_textMesh.gameObject.SetActive(visible);
	}

	/// <summary>
	/// 現在表示中の名前を取得する
	/// </summary>
	public string GetName()
	{
		return _textMesh.text;
	}

	#endregion
}