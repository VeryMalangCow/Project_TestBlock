using System;

[Serializable]
public class PlayerInventorySlotData
{
    public int itemID = -1;    // -1: Empty
    public int stackCount = 0;

    public PlayerInventorySlotData()
    {
        itemID = -1;
        stackCount = 0;
    }

    public PlayerInventorySlotData(int id, int count)
    {
        itemID = id;
        stackCount = count;
    }

    public bool IsEmpty => itemID == -1 || stackCount <= 0;

    public void Clear()
    {
        itemID = -1;
        stackCount = 0;
    }
}
