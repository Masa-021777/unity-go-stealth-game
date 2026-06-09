using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ゲーム開始直後にチュートリアル画像を表示し、OKで閉じるとゲームが始まるクラス。
///
/// Unityでの設定手順：
/// 1. ヒエラルキーに空のGameObjectを作り "TutorialManager" とリネームしてスクリプトをアタッチ
/// 2. Canvas配下に以下を作る
///    └ TutorialPanel (Panel)
///         ├ TutorialImage (Image)   ← チュートリアル画像を設定
///         ├ PageText (Text)         ← "1 / 3" みたいなページ数表示（任意）
///         └ OKButton (Button)       ← 「次へ」「スタート！」ボタン
/// 3. Inspectorで各フィールドをドラッグ＆ドロップ
/// 4. WebSocketClient.cs の start_game 受信時に ShowTutorial() を呼ぶ
/// 
/// ※現在未使用
/// </summary>
public class TutorialManager : MonoBehaviour
{
	public static TutorialManager Instance;

	#region インスペクター設定

	[Header("UIパーツ")]
	public GameObject tutorialPanel;
	public Image tutorialImage;
	public Text pageText;
	public Button okButton;

	[Header("チュートリアル画像（複数枚可）")]
	public Sprite[] tutorialSprites;

	#endregion

	#region 内部状態

	private int _currentPage;
	private System.Action _onFinished;

	#endregion

	#region Unityイベント

	void Awake()
	{
		if (Instance == null) Instance = this;
		else { Destroy(gameObject); return; }

		if (tutorialPanel != null) tutorialPanel.SetActive(false);
		if (okButton != null) okButton.onClick.AddListener(OnOKClicked);
	}

	#endregion

	#region 公開メソッド

	/// <summary>
	/// ゲーム開始時に呼ぶ。
	/// onFinished にゲーム本体の開始処理を渡す（不要なら null でOK）。
	/// </summary>
	public void ShowTutorial(System.Action onFinished = null)
	{
		_onFinished = onFinished;
		_currentPage = 0;

		if (tutorialSprites == null || tutorialSprites.Length == 0)
		{
			FinishTutorial();
			return;
		}

		ShowPage(0);
		if (tutorialPanel != null) tutorialPanel.SetActive(true);
		Time.timeScale = 0f; 
	}

	#endregion

	#region 内部処理

	/// <summary>指定ページの画像とページ数テキストを更新する</summary>
	private void ShowPage(int page)
	{
		if (tutorialImage != null && page < tutorialSprites.Length)
			tutorialImage.sprite = tutorialSprites[page];

		if (pageText != null)
			pageText.text = $"{page + 1} / {tutorialSprites.Length}";

		// 最終ページならボタンを「スタート！」に変える
		var btnText = okButton != null ? okButton.GetComponentInChildren<Text>() : null;
		if (btnText != null)
			btnText.text = (page >= tutorialSprites.Length - 1) ? "スタート！" : "次へ";
	}

	/// <summary>OKボタンが押されたとき。次ページがあれば進み、なければ終了する</summary>
	private void OnOKClicked()
	{
		_currentPage++;
		if (_currentPage < tutorialSprites.Length)
			ShowPage(_currentPage);
		else
			FinishTutorial();
	}

	/// <summary>チュートリアルを閉じてゲームを再開する</summary>
	private void FinishTutorial()
	{
		if (tutorialPanel != null) tutorialPanel.SetActive(false);
		Time.timeScale = 1f;
		_onFinished?.Invoke();
		Debug.Log("チュートリアル完了 → ゲームスタート");
	}

	#endregion
}