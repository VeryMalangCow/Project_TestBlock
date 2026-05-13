using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BlockVisualDatabase", menuName = "Project_Block/VisualDatabase")]
public class BlockVisualDatabase : ScriptableObject
{
    [SerializeField] private List<BlockVisualData> visualDataList = new List<BlockVisualData>();
    private Dictionary<int, BlockVisualData> dataCache;

    public void Initialize()
    {
        dataCache = new Dictionary<int, BlockVisualData>();
        foreach (var data in visualDataList)
        {
            if (data != null && !dataCache.ContainsKey(data.blockID))
            {
                dataCache.Add(data.blockID, data);
            }
        }
    }

    public BlockVisualData GetData(int blockID)
    {
        if (dataCache == null) Initialize();
        return dataCache.TryGetValue(blockID, out var data) ? data : null;
    }
}
