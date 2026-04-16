using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Project_BlockTest/ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();

    public ItemData GetItem(int id)
    {
        return items.Find(item => item.id == id);
    }

    public void RefreshList()
    {
        // 에디터 툴에서 목록을 갱신하기 위한 메서드
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
