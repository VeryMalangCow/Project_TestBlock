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
#if UNITY_EDITOR
        items.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Datas/Items" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            ItemData data = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (data != null)
            {
                items.Add(data);
            }
        }
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[ItemDatabase] Updated list with {items.Count} items.");
#endif
    }
}
