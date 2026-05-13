using UnityEngine;

[CreateAssetMenu(fileName = "BlockVisual_", menuName = "Project_Block/VisualData")]
public class BlockVisualData : ScriptableObject
{
    public int blockID;
    public string blockName;

    [Header("### Hit Effects")]
    public GameObject hitDustPrefab; 
    public AudioClip hitSound;
    public Color hitFlashColor = Color.white; 

    [Header("### Break Effects")]
    public GameObject breakDustPrefab; 
    public AudioClip breakSound;
    public float glowIntensity = 1.0f; 
}
