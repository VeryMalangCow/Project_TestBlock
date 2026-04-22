using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [Header("### UI References")]
    [SerializeField] protected ItemIconImage iconImage;
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
        
        // 이벤트 구독: 아이콘 로드가 완료되면 슬롯 갱신
        ItemIconCacheManager.Instance.OnIconLoaded += HandleIconLoaded;
        
        ClearSlot();
    }

    private void OnDestroy()
    {
        if (ItemIconCacheManager.Instance != null)
            ItemIconCacheManager.Instance.OnIconLoaded -= HandleIconLoaded;
    }

    private void HandleIconLoaded(int loadedItemID)
    {
        // 현재 이 슬롯이 표시하려는 아이템이 로드된 경우에만 갱신
        if (currentItemID == loadedItemID)
        {
            if (ownerUI != null && ownerUI.InventoryData != null)
            {
                if (targetType == ItemType.None)
                {
                    UpdateSlot(ownerUI.InventoryData.GetSlot(slotIndex), true);
                }
                else
                {
                    var equipment = PlayerController.Local.Data.equipment;
                    int typeID = equipment.GetEquipment(targetType);
                    int itemID = ItemDataManager.Instance.FindItemIDByType(targetType, typeID);
                    UpdateSlot(new PlayerInventorySlotData(itemID, itemID >= 0 ? 1 : 0), true);
                }
            }
        }
    }

    public virtual void UpdateSlot(PlayerInventorySlotData slot, bool force = false)
    {
        int nextID = slot.IsEmpty ? -1 : slot.itemID;

        // 1. 수량은 항상 업데이트
        if (!slot.IsEmpty)
        {
            stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            stackText.enabled = true;
        }

        // [Fix] force가 true면 ID가 같더라도 업데이트 진행 (아이콘 로드 대응)
        if (!force && nextID == currentItemID) return;

        // 2. ID 업데이트
        currentItemID = nextID;

        if (nextID == -1)
        {
            ClearSlotInternal();
            return;
        }

        // [중앙 캐시 호출]
        int sliceIdx = ItemIconCacheManager.Instance.GetSlotIndex(nextID);
        
        // [Batch] 공유 머티리얼 할당 (new 하지 않음!)
        if (iconImage.material == null || iconImage.material.shader.name != "UI/ItemIconArrayBatch")
        {
            Material sharedMat = Resources.Load<Material>("Materials/M_ItemIconArrayBatch"); 
            if (sharedMat != null) iconImage.material = sharedMat;
        }

        // [Batch] 인덱스를 버텍스에 주입
        iconImage.SetSliceIndex(sliceIdx);
        iconImage.enabled = true;
        iconImage.color = Color.white;
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
            // [Batch] 인덱스를 -1로 설정
            iconImage.SetSliceIndex(-1);
            iconImage.enabled = false;
        }
        if (stackText != null)
        {
            stackText.text = "";
            stackText.enabled = false;
        }
    }
}
