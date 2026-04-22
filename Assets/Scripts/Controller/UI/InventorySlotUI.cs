using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [Header("### UI References")]
    [SerializeField] protected Image iconImage;
    [SerializeField] protected TextMeshProUGUI stackText;

    [Header("### Config")]
    [SerializeField] protected ItemType targetType = ItemType.None; // None means any item

    protected InventoryUI ownerUI;
    protected int slotIndex = -1;
    protected int currentItemID = -2;

    public int SlotIndex => slotIndex;
    public ItemType TargetType => targetType;

    public virtual void Init(InventoryUI ui, int index)
    {
        ownerUI = ui;
        slotIndex = index;
        currentItemID = -2;
        ClearSlot();
    }

    public virtual void UpdateSlot(PlayerInventorySlotData slot)
    {
        int nextID = slot.IsEmpty ? -1 : slot.itemID;

        // 1. 수량은 항상 업데이트
        if (!slot.IsEmpty)
        {
            stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            stackText.enabled = true;
        }

        if (nextID == currentItemID) return;

        // 2. ID가 바뀌었으므로 이미지 업데이트
        currentItemID = nextID;

        if (nextID == -1)
        {
            ClearSlotInternal();
            return;
        }

        // [중앙 캐시 호출]
        int sliceIdx = ItemIconCacheManager.Instance.GetSlotIndex(nextID);
        
        // 아이콘 표시용 머티리얼 설정
        if (iconImage.material == null || iconImage.material.shader.name != "UI/ItemIconArray")
        {
            // 전용 셰이더를 사용하는 새로운 머티리얼 인스턴스 생성 (드로우콜 배칭을 위해 나중에 최적화 가능)
            Material sharedMat = Resources.Load<Material>("Materials/M_ItemIconArray"); 
            if (sharedMat != null)
                iconImage.material = new Material(sharedMat);
        }

        if (iconImage.material != null)
        {
            iconImage.material.SetFloat("_SliceIndex", sliceIdx);
            iconImage.enabled = true;
            iconImage.color = Color.white;
        }
    }

    public void ClearSlot()
    {
        currentItemID = -2;
        ClearSlotInternal();
    }

    protected virtual void ClearSlotInternal()
    {
        if (iconImage != null)
        {
            if (iconImage.material != null && iconImage.material.shader.name == "UI/ItemIconArray")
                iconImage.material.SetFloat("_SliceIndex", -1);

            iconImage.enabled = false;
        }
        if (stackText != null)
        {
            stackText.text = "";
            stackText.enabled = false;
        }
    }
}
