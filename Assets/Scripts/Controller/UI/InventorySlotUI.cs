using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class InventorySlotUI : MonoBehaviour
{
    [Header("### UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI stackText;

    private InventoryUI ownerUI;
    private int slotIndex = -1;
    private AsyncOperationHandle<Sprite> iconHandle;
    private int currentItemID = -2; // -1이 공백이므로 -2로 초기화

    public int SlotIndex => slotIndex;

    public void Init(InventoryUI ui, int index)
    {
        ownerUI = ui;
        slotIndex = index;
        currentItemID = -2; // 초기화 시 캐시 리셋
        ClearSlot();
    }

    private void OnDestroy()
    {
        ReleaseIcon();
    }

    public void UpdateSlot(PlayerInventorySlotData slot)
    {
        int nextID = (slot == null || slot.IsEmpty) ? -1 : slot.itemID;

        // 1. 아이템 ID가 이전과 같다면 아무것도 하지 않음 (이미지 로드 중복 방지)
        if (nextID == currentItemID)
        {
            // 수량만 업데이트
            if (slot != null && !slot.IsEmpty)
            {
                stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            }
            return;
        }

        // 2. ID가 바뀌었으므로 상태 업데이트 및 로직 실행
        currentItemID = nextID;
        ReleaseIcon();

        if (slot == null || slot.IsEmpty)
        {
            ClearSlotInternal(); // 내부용 클리어 (캐시 리셋 안 함)
            return;
        }

        ItemData data = ItemDataManager.Instance.GetItem(slot.itemID);
        if (data != null)
        {
            // [Fix] AssetReference 대신 전역 주소 문자열을 사용하여 공유 에셋 중복 로드 충돌 방지
            string address = $"ItemIcon_{data.id:D5}";
            iconHandle = Addressables.LoadAssetAsync<Sprite>(address);
            
            // 안전한 람다 캡처를 위해 현재 핸들을 로컬 변수에 저장
            var currentHandle = iconHandle;
            iconHandle.Completed += (handle) =>
            {
                // [안전 장치] 핸들이 여전히 유효하고, 로드에 성공했을 때만 실행
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (iconImage != null)
                    {
                        iconImage.sprite = handle.Result;
                        iconImage.enabled = true;
                    }
                }
                else
                {
                    if (iconImage != null) iconImage.enabled = false;
                }
            };
            
            stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            stackText.enabled = true;
        }
        else
        {
            ClearSlotInternal();
        }
    }

    public void ClearSlot()
    {
        currentItemID = -2; // 외부에서 명시적으로 지울 때는 캐시도 초기화
        ClearSlotInternal();
    }

    private void ClearSlotInternal()
    {
        ReleaseIcon();
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
        if (stackText != null)
        {
            stackText.text = "";
            stackText.enabled = false;
        }
    }

    private void ReleaseIcon()
    {
        if (iconHandle.IsValid())
        {
            Addressables.Release(iconHandle);
        }
    }
}
