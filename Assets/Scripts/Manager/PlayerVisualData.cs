using System;
using UnityEngine;

[Serializable]
public class PlayerVisualData
{
    public string skinColorHex = "#FFDBAC";
    public string eyeColorHex = "#634E34";
    public string hairColorHex = "#4B2C20";
    public int hairStyleIndex = 0;

    public PlayerVisualData()
    {
        skinColorHex = "#FFDBAC";
        eyeColorHex = "#634E34";
        hairColorHex = "#4B2C20";
        hairStyleIndex = 0;
    }
}
