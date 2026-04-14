using System;

[Serializable]
public class PlayerData
{
    // Appearance (Visuals)
    public PlayerVisualData visual = new PlayerVisualData();

    // Equipment
    public PlayerEquipmentData equipment = new PlayerEquipmentData();

    // Inventory (Placeholder)
    public int[] inventorySlots = new int[50]; 

    public PlayerData()
    {
        visual = new PlayerVisualData();
        equipment = new PlayerEquipmentData();
        inventorySlots = new int[50];
    }
}
