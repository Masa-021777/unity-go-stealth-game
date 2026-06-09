using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// リザルト画面からタイトルへ戻るクラス。
/// コントローラーのAボタン、またはキーボードのAキーで遷移する。
/// </summary>
public class ReturnToTitle : MonoBehaviour
{
	#region Unityイベント

	void Update()
	{
		// AボタンまたはAキーでタイトルへ遷移
		if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.A))
		{
			SceneManager.LoadScene("Title");
		}
	}

	#endregion
}