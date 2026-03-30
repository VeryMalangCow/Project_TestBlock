using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Test : MonoBehaviour
{
    [Header("# Player")]
    [SerializeField] private PlayerController player;
    bool running = false;
    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.fKey.wasPressedThisFrame && !running)
            StartCoroutine(test_GenerateMap());

        if (Keyboard.current.digit1Key.wasPressedThisFrame && !running)
            StartCoroutine(test_SwitchMap(WorldStyle.Standard));
        
        if (Keyboard.current.digit2Key.wasPressedThisFrame && !running)
            StartCoroutine(test_SwitchMap(WorldStyle.GreatCave));
        
        if (Keyboard.current.digit3Key.wasPressedThisFrame && !running)
            StartCoroutine(test_SwitchMap(WorldStyle.Hell));
        
    }

    private IEnumerator test_GenerateMap()
    {
        running = true;
        // Disable only components, not the whole GameObject, 
        // so this coroutine can keep running.
        if (player != null) player.gameObject.SetActive(false);

        Debug.Log("[Test] Map Generation Started...");
        yield return MapManager.Instance.GenerateMapCo();
        Debug.Log("[Test] Map Generation Finished.");

        if (player != null)
        {
            player.gameObject.SetActive(true);
            // Optionally reposition player to surface
            player.transform.position = MapManager.Instance.GetPositionByRatio(50f, 100f);
        }
        running = false;
    }

    private IEnumerator test_SwitchMap(WorldStyle style)
    {
        running = true;
        if (player != null) player.gameObject.SetActive(false);

        Debug.Log("[Test] Map Switch Started...");
        yield return MapManager.Instance.SwitchWorldCo(style);
        Debug.Log("[Test] Map Switch Finished.");

        if (player != null)
        {
            player.gameObject.SetActive(true);
            player.transform.position = MapManager.Instance.GetPositionByRatio(50f, 100f);
        }
        running = false;
    }
}
