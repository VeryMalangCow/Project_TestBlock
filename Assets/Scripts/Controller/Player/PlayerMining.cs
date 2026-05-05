using UnityEngine;

public class PlayerMining : MonoBehaviour
{
    private PlayerController controller;

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    public void PerformMine(ToolProperty stats, UseContext context)
    {
        // 실제 채굴 로직 (서버측 실행)
        Debug.Log($"[Server-Mining] 채굴 실행! 위력: {stats.minePower}");

        // 여기에 나중에 MapManager.Instance를 이용한 블록 파괴 로직이 들어옵니다.
    }
}
