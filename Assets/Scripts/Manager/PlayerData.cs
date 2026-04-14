using System;

[Serializable]
public class PlayerData
{
    // Appearance
    public string skinColorHex = "#FFFFFF";
    public string eyeColorHex = "#FFFFFF";
    public string hairColorHex = "#FFFFFF";
    public int hairStyleIndex = 0;

    // Inventory (Placeholder for now)
    public int[] inventorySlots = new int[50]; 

    public PlayerData()
    {
        // Default values
        skinColorHex = "#FFDBAC"; // Light skin tone
        eyeColorHex = "#634E34";  // Brown eyes
        hairColorHex = "#4B2C20"; // Dark hair
        hairStyleIndex = 0;
    }
}
