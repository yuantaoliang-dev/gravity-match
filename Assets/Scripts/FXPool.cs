using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pool that manages reusable FX GameObjects with SpriteRenderer.
/// Get() activates and returns a pooled object, Return() deactivates it.
/// Pre-warms 8 of each type at Init.
/// </summary>
public class FXPool : MonoBehaviour
{
    public enum FXType { SuckGhost, HighlightRing, BuddyRing }

    private Dictionary<FXType, Stack<GameObject>> pools = new Dictionary<FXType, Stack<GameObject>>();
    private Transform poolRoot;

    /// <summary>Initialize pools and pre-warm 8 of each type.</summary>
    public void Init()
    {
        poolRoot = new GameObject("FXPoolRoot").transform;
        poolRoot.SetParent(transform, false);

        pools[FXType.SuckGhost] = new Stack<GameObject>();
        pools[FXType.HighlightRing] = new Stack<GameObject>();
        pools[FXType.BuddyRing] = new Stack<GameObject>();

        // Pre-warm 8 of each type
        foreach (FXType type in System.Enum.GetValues(typeof(FXType)))
        {
            for (int i = 0; i < 8; i++)
            {
                var go = CreateFXObject(type);
                go.SetActive(false);
                pools[type].Push(go);
            }
        }
    }

    /// <summary>Get an active FX object from the pool. Sets sprite if provided.</summary>
    public GameObject Get(FXType type, Sprite sprite = null)
    {
        GameObject go;
        if (pools[type].Count > 0)
        {
            go = pools[type].Pop();
        }
        else
        {
            go = CreateFXObject(type);
        }

        go.SetActive(true);
        go.transform.SetParent(null, false);

        if (sprite != null)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = sprite;
        }

        return go;
    }

    /// <summary>Return an FX object to the pool, deactivating it.</summary>
    public void Return(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        go.transform.SetParent(poolRoot, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;

        // Determine type from name and push back
        if (go.name.StartsWith("SuckGhost"))
            pools[FXType.SuckGhost].Push(go);
        else if (go.name.StartsWith("HighlightRing"))
            pools[FXType.HighlightRing].Push(go);
        else if (go.name.StartsWith("BuddyRing"))
            pools[FXType.BuddyRing].Push(go);
    }

    private GameObject CreateFXObject(FXType type)
    {
        var go = new GameObject(type.ToString());
        go.transform.SetParent(poolRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = type == FXType.SuckGhost ? 5 : 15;
        sr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
        return go;
    }
}
