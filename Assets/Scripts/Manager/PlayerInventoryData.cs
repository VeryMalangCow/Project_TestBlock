using System;

[Serializable]
public class PlayerInventoryData
{
    public InventorySlot[] slots;

    public PlayerInventoryData(int size = 50)
    {
        slots = new InventorySlot[size];
        for (int i = 0; i < size; i++)
        {
            slots[i] = new InventorySlot();
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
            foreach (var slot in slots)
            {
                if (slot.itemID == id && slot.stackCount < itemData.maxStack)
                {
                    int addCount = Math.Min(remaining, itemData.maxStack - slot.stackCount);
                    slot.stackCount += addCount;
                    remaining -= addCount;

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
                slots[i].itemID = id;
                slots[i].stackCount = addCount;
                remaining -= addCount;

                if (remaining <= 0) return 0;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Gets the slot by index.
    /// </summary>
    public InventorySlot GetSlot(int index)
    {
        if (index >= 0 && index < slots.Length) return slots[index];
        return null;
    }

    /// <summary>
    /// Clears the specific slot.
    /// </summary>
    public void ClearSlot(int index)
    {
        if (index >= 0 && index < slots.Length) slots[index].Clear();
    }
}
