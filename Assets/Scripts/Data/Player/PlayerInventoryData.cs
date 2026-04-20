using System;

[Serializable]
public class PlayerInventoryData
{
    public PlayerInventorySlotData[] slots;
    public event Action<int, PlayerInventorySlotData> OnInventoryChanged;

    public PlayerInventoryData(int size = 50)
    {
        slots = new PlayerInventorySlotData[size];
        for (int i = 0; i < size; i++)
        {
            slots[i] = new PlayerInventorySlotData(-1, 0);
        }
    }

    /// <summary>
    /// Adds an item to the inventory while respecting maxStack and stacking rules.
    /// Returns the remaining count that couldn't be added.
    /// </summary>
    public int AddItem(int id, int count)
    {
        ItemData itemData = ItemDataManager.Instance.GetItem(id);
        if (itemData == null) return count;

        int remaining = count;

        // 1. Try to stack with existing items (if stackable)
        if (itemData.maxStack > 1)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].itemID == id && slots[i].stackCount < itemData.maxStack)
                {
                    int addCount = Math.Min(remaining, itemData.maxStack - slots[i].stackCount);
                    
                    // [Fix] 구조체 멤버 직접 수정 후 배열에 명시적 재할당 (안정성 보장)
                    var updatedSlot = slots[i];
                    updatedSlot.stackCount += addCount;
                    slots[i] = updatedSlot;
                    
                    remaining -= addCount;
                    OnInventoryChanged?.Invoke(i, slots[i]);

                    if (remaining <= 0) return 0;
                }
            }
        }

        // 2. Find empty slots for remaining items
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty)
            {
                int addCount = Math.Min(remaining, itemData.maxStack);
                slots[i] = new PlayerInventorySlotData(id, addCount);
                
                remaining -= addCount;
                OnInventoryChanged?.Invoke(i, slots[i]);

                if (remaining <= 0) return 0;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Gets the slot by index. (Returns a copy as it's a struct)
    /// </summary>
    public PlayerInventorySlotData GetSlot(int index)
    {
        if (index >= 0 && index < slots.Length) return slots[index];
        return new PlayerInventorySlotData(-1, 0);
    }

    /// <summary>
    /// Updates a slot by index.
    /// </summary>
    public void SetSlot(int index, PlayerInventorySlotData data)
    {
        if (index >= 0 && index < slots.Length) 
        {
            slots[index] = data;
            OnInventoryChanged?.Invoke(index, data);
        }
    }

    /// <summary>
    /// Updates a slot without triggering OnInventoryChanged event.
    /// Used for network sync to prevent infinite loops.
    /// </summary>
    public void SetSlotWithoutNotify(int index, PlayerInventorySlotData data)
    {
        if (index >= 0 && index < slots.Length)
        {
            slots[index] = data;
        }
    }

    /// <summary>
    /// Clears the specific slot.
    /// </summary>
    public void ClearSlot(int index)
    {
        if (index >= 0 && index < slots.Length) 
        {
            slots[index] = new PlayerInventorySlotData(-1, 0);
            OnInventoryChanged?.Invoke(index, slots[index]);
        }
    }
}
