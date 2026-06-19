using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// リザルト画面からタイトルへ戻るクラス。
/// コントローラーのAボタン、またはキーボードのAキーで遷移する。
/// </summary>
public class ReturnToTitle : MonoBehaviour
{
	#region Unityイベント

	void Update()
	{
		bool pad = Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
		bool key = Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame;

		if (pad || key)
		{
			ReturnTitle();
		}
	}

	#endregion

	#region タイトル復帰

	/// <summary>
	/// WebSocket切断・各種状態をリセットしてタイトルへ遷移する。
	/// </summary>
	private async void ReturnTitle()
	{
		WebSocketClient wsClient = FindObjectOfType<WebSocketClient>();
		if (wsClient != null)
		{
			await wsClient.DisconnectAndReset();
		}

		SceneManager.LoadScene("Title");
	}

	#endregion
}