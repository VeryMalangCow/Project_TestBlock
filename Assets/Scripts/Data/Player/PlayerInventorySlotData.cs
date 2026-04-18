using System;
using Unity.Netcode;

[Serializable]
public struct PlayerInventorySlotData : INetworkSerializable, IEquatable<PlayerInventorySlotData>
{
    public int itemID;
    public int stackCount;

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

    // [Network] 데이터를 직렬화하여 패킷으로 보낼 때 사용
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemID);
        serializer.SerializeValue(ref stackCount);
    }

    // [Equality] NetworkList가 변경 사항을 감지하기 위해 필요
    public bool Equals(PlayerInventorySlotData other)
    {
        return itemID == other.itemID && stackCount == other.stackCount;
    }
}
