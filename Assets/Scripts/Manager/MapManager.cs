using System;
using UnityEngine;

#region Map

[Serializable]
public class BlockData
{
    public int id;
    public int kindId;

    public BlockData(int id, int kindId = 0)
    {
        this.id = id;
        this.kindId = kindId;
    }
}

[Serializable]
public class ChunkData
{
    public static readonly Vector2Int ChunkSize = new Vector2Int(16, 16);

    public BlockData[,] blocks;

    public ChunkData()
    {
        blocks = new BlockData[ChunkSize.x, ChunkSize.y];
    }
}

[Serializable]
public class MapData
{
    public static readonly Vector2Int MapSize = new Vector2Int(16, 16);

    public ChunkData[,] chunks;

    public MapData()
    {
        chunks = new ChunkData[MapSize.x, MapSize.y];
        for (int x = 0; x < MapSize.x; x++)
        {
            for (int y = 0; y < MapSize.y; y++)
            {
                chunks[x, y] = new ChunkData();
            }
        }
    }
}

#endregion

public class MapManager : Singleton<MapManager>
{
    #region Variable

    // Inspector
    [Header("# Generator")]
    [SerializeField] public MapGenerator mapGenerator;

    [Header("# Data")]
    [HideInInspector] public MapData mapData;

    #endregion
}
