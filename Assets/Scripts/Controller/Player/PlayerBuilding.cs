using UnityEngine;

/// <summary>
/// 블록 설치 로직을 전담하는 컴포넌트입니다.
/// </summary>
public class PlayerBuilding : MonoBehaviour
{
    private PlayerController controller;

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    /// <summary>
    /// 서버 측 블록 설치 시도 (검증 포함)
    /// </summary>
    public void TryPlaceBlock(BlockProperty property, UseContext context)
    {
        var playerCtrl = context.Player;
        if (playerCtrl == null || playerCtrl.Data == null || playerCtrl.Data.inventory == null) return;

        int itemID = context.ItemID;
        int hotbarIndex = context.HotbarIndex;
        Vector2 mouseWorldPos = context.MouseWorldPos;

        int wx = Mathf.FloorToInt(mouseWorldPos.x);
        int wy = Mathf.FloorToInt(mouseWorldPos.y);

        // 1. 거리 체크 (기존 로직 유지)
        Vector2 playerPos = playerCtrl.transform.position;
        if (Mathf.Abs(wx + 0.5f - playerPos.x) > 8.5f || Mathf.Abs(wy + 0.5f - playerPos.y) > 6.5f) return;

        // 2. 이미 블록이 있는지 체크
        if (MapManager.Instance.IsBlockActive(wx, wy)) return;

        // 3. 인접 블록 체크 (공중에 설치 방지)
        bool hasNeighbor = MapManager.Instance.IsBlockActive(wx + 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx - 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx, wy + 1) ||
                           MapManager.Instance.IsBlockActive(wx, wy - 1);

        if (!hasNeighbor) return;

        // 4. 플레이어 겹침 체크
        Vector2 checkPos = new Vector2(wx + 0.5f, wy + 0.5f);
        if (Physics2D.OverlapBox(checkPos, Vector2.one * 0.95f, 0f, LayerMask.GetMask("Player")) != null) return;

        // 5. 아이템 소모 및 맵 수정
        if (playerCtrl.Data.inventory.RemoveItemFromSlot(hotbarIndex, 1))
        {
            MapManager.Instance.SetBlock(wx, wy, itemID);
            playerCtrl.SyncInventoryToNetwork();
            Debug.Log($"[Server-Building] 블록 설치 성공: {wx}, {wy} (ID: {itemID})");
        }
    }
}
