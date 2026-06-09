using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// ルーム待機画面のメンバー一覧UIを管理するクラス。
/// プレイヤーの追加・削除・準備状態の変化をリアルタイムに反映する。
/// </summary>
public class RoomMemberPanel : MonoBehaviour
{
	#region インスペクター設定

	[Header("メンバー表示")]
	public Transform memberListParent; 
	public GameObject memberRowPrefab;

	#endregion

	#region 内部状態

	// key: プレイヤーID、value: 生成済みの行オブジェクト
	private Dictionary<string, GameObject> _memberRows = new Dictionary<string, GameObject>();

	// key: プレイヤーID、value: （名前, 準備完了かどうか）
	private Dictionary<string, (string name, bool isReady)> _members
		= new Dictionary<string, (string, bool)>();

	#endregion

	#region 公開メソッド

	/// <summary>メンバーを追加、または情報を更新する</summary>
	public void AddOrUpdateMember(string playerId, string playerName, bool isReady = false)
	{
		_members[playerId] = (playerName, isReady);
		RefreshUI();
	}

	/// <summary>指定プレイヤーの準備完了状態を更新する</summary>
	public void SetReady(string playerId, bool isReady)
	{
		if (_members.ContainsKey(playerId))
		{
			var m = _members[playerId];
			_members[playerId] = (m.name, isReady);
			RefreshUI();
		}
	}

	/// <summary>指定プレイヤーをリストから削除する</summary>
	public void RemoveMember(string playerId)
	{
		_members.Remove(playerId);
		RefreshUI();
	}

	/// <summary>全メンバーをクリアする（ルーム退出時など）</summary>
	public void ClearAll()
	{
		_members.Clear();
		RefreshUI();
	}

	#endregion

	#region UI更新

	/// <summary>既存の行を全削除して最新の状態で再生成する</summary>
	private void RefreshUI()
	{
		foreach (var row in _memberRows.Values)
			Destroy(row);
		_memberRows.Clear();

		foreach (var kv in _members)
		{
			GameObject row = Instantiate(memberRowPrefab, memberListParent);
			Text[] texts = row.GetComponentsInChildren<Text>();
			// texts[0] = 名前、texts[1] = 状態
			if (texts.Length >= 2)
			{
				texts[0].text = kv.Value.name;
				texts[1].text = kv.Value.isReady ? "✔ 準備完了" : "…待機中";
				texts[1].color = kv.Value.isReady ? Color.green : Color.gray;
			}
			_memberRows[kv.Key] = row;
		}
	}

	#endregion
}