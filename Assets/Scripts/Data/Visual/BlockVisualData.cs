using UnityEngine;

[CreateAssetMenu(fileName = "BlockVisual_", menuName = "Project_Block/VisualData")]
public class BlockVisualData : ScriptableObject
{
    public int blockID;
    public string blockName;

    [Header("### Visual Settings")]
    public Color mainColor = Color.white; // 파티클과 플래시에 공통 적용될 색상
    public bool useGlow = true;           // 타격 시 번쩍이는 효과(FakeGlow) 사용 여부

    [Header("### Sounds")]
    public AudioClip hitSound;
    public AudioClip breakSound;
}
