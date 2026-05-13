using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("### Data")]
    [SerializeField] private BlockVisualDatabase visualDatabase;

    [Header("### Audio")]
    [SerializeField] private AudioSource localAudioSource;

    [Header("### Fake Glow")]
    [SerializeField] private GameObject fakeGlowPrefab;

    private Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (visualDatabase != null) visualDatabase.Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #region Public Interface

    public void PlayHitFX(Vector2 worldPos, int blockID)
    {
        var data = visualDatabase.GetData(blockID);
        if (data == null) return;

        // 1. Dust Particles
        if (data.hitDustPrefab != null)
        {
            SpawnFromPool(data.hitDustPrefab, worldPos, Quaternion.identity);
        }

        // 2. Hit Sound (Local)
        if (data.hitSound != null && localAudioSource != null)
        {
            localAudioSource.PlayOneShot(data.hitSound);
        }

        // 3. Fake Glow
        if (fakeGlowPrefab != null)
        {
            StartCoroutine(FakeGlowSequence(worldPos, data.hitFlashColor, 0.1f, 12f));
        }
    }

    public void PlayBreakFX(Vector2 worldPos, int blockID)
    {
        var data = visualDatabase.GetData(blockID);
        if (data == null) return;

        // 1. Break Particles
        if (data.breakDustPrefab != null)
        {
            SpawnFromPool(data.breakDustPrefab, worldPos, Quaternion.identity);
        }

        // 2. Break Sound (Spatial)
        if (data.breakSound != null)
        {
            AudioSource.PlayClipAtPoint(data.breakSound, new Vector3(worldPos.x, worldPos.y, -2f));
        }

        // 3. Fake Glow (Stronger)
        if (fakeGlowPrefab != null)
        {
            StartCoroutine(FakeGlowSequence(worldPos, data.hitFlashColor, 0.2f, 20f));
        }
    }

    #endregion

    #region Pooling System

    private GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        string key = prefab.name;
        if (!poolDictionary.ContainsKey(key))
        {
            poolDictionary.Add(key, new Queue<GameObject>());
        }

        GameObject objectToSpawn;
        if (poolDictionary[key].Count > 0)
        {
            objectToSpawn = poolDictionary[key].Dequeue();
            objectToSpawn.SetActive(true);
            objectToSpawn.transform.position = position;
            objectToSpawn.transform.rotation = rotation;
        }
        else
        {
            objectToSpawn = Instantiate(prefab, position, rotation);
            objectToSpawn.name = key;
        }

        // Return to pool after finish
        StartCoroutine(ReturnToPoolAfterDelay(objectToSpawn, key));

        return objectToSpawn;
    }

    private IEnumerator ReturnToPoolAfterDelay(GameObject obj, string key)
    {
        // For ParticleSystems, wait for duration
        var ps = obj.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            yield return new WaitForSeconds(ps.main.duration + ps.main.startLifetime.constantMax);
        }
        else
        {
            yield return new WaitForSeconds(1f); // Fallback
        }

        obj.SetActive(false);
        poolDictionary[key].Enqueue(obj);
    }

    #endregion

    #region Visual Sequences

    private IEnumerator FakeGlowSequence(Vector2 pos, Color color, float duration, float scale)
    {
        GameObject glow = SpawnFromPool(fakeGlowPrefab, pos, Quaternion.identity);
        var renderer = glow.GetComponent<SpriteRenderer>();
        if (renderer == null) yield break;

        renderer.color = color;
        glow.transform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            glow.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * scale, t);
            
            Color c = color;
            c.a = 1f - t;
            renderer.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        glow.SetActive(false);
    }

    #endregion
}
