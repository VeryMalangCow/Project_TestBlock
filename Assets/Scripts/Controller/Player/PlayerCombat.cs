using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    private PlayerController controller;

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    public void PerformAttack(WeaponProperty stats, UseContext context)
    {
        // 실제 공격 로직 (서버측 실행)
        Debug.Log($"[Server-Combat] {stats.weaponType} 공격 실행! 데미지: {stats.damage}, 사거리: {stats.reach}");
        
        // 여기에 나중에 Physics2D.OverlapCircle 등을 이용한 타격 판정 로직이 들어옵니다.
    }
}
