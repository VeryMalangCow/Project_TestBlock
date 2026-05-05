using UnityEngine;

[System.Serializable]
public class WeaponProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭
    
    [Header("Stats")]
    public WeaponType weaponType;
    public int damage;
    public float speed; // 초당 공격 횟수
    public float reach;

    public void OnUseClient(UseContext context)
    {
        // 클라이언트: 애니메이션 재생 (PlayerVisuals의 설정값 참조)
        float offset = context.Player.Visuals.SwordSwingOffset;
        context.Player.Visuals.StartItemUseAnimation(90f, GetUseDelay(), offset); 
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 데미지 판정 (현재는 로그만)
        Debug.Log($"[Server] {context.ItemID}번 무기로 공격! 데미지: {damage}");
    }

    public float GetUseDelay() => speed > 0 ? 1f / speed : 0.4f;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => true;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Swing;
}

[System.Serializable]
public class ToolProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭
    
    [Header("Stats")]
    public int minePower;
    public float mineSpeed;

    public void OnUseClient(UseContext context)
    {
        // 마우스 방향에 따른 각도 계산 복구
        Vector2 dir = (context.MouseWorldPos - (Vector2)context.Player.transform.position).normalized;
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (context.Player.IsFlipped) targetAngle = 180f - targetAngle;
        float finalAngle = targetAngle + 90f;

        // 클라이언트: 도구 휘두르기 애니메이션
        float offset = 30f; // 도구는 보통 30도 정도가 적당
        context.Player.Visuals.StartItemUseAnimation(finalAngle, GetUseDelay(), offset);
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 채굴 로직 (현재는 로그만)
        Debug.Log($"[Server] {context.ItemID}번 도구로 채굴! 위력: {minePower}");
    }

    public float GetUseDelay() => mineSpeed;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => false;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Swing;
}

[System.Serializable]
public class BlockProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭 (또는 설정에 따라 우클릭)

    public void OnUseClient(UseContext context)
    {
        // 마우스 방향에 따른 각도 계산 복구
        Vector2 dir = (context.MouseWorldPos - (Vector2)context.Player.transform.position).normalized;
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (context.Player.IsFlipped) targetAngle = 180f - targetAngle;
        float finalAngle = targetAngle + 90f;

        // 클라이언트: 설치 애니메이션
        float offset = context.Player.Visuals.BlockSwingOffset;
        context.Player.Visuals.StartItemUseAnimation(finalAngle, GetUseDelay(), offset);
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 실제 블록 설치 요청
        int wx = Mathf.FloorToInt(context.MouseWorldPos.x);
        int wy = Mathf.FloorToInt(context.MouseWorldPos.y);
        
        // PlayerController의 기존 PlaceBlockRpc 로직이 여기로 들어올 수 있음
        // 지금은 PlayerController에 로직이 있으므로 이를 호출하도록 구조만 유지
    }

    public float GetUseDelay() => 0.2f;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => false;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Place;
}
