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

        // Route to the correct stack via marker component — O(1), no string
        // compare, and impossible to silently drop an object (missing tag
        // surfaces as a LogError instead of a quiet pool leak).
        var tag = go.GetComponent<FXPoolTag>();
        if (tag == null)
        {
            // Should never happen — all pooled objects are created via CreateFXObject.
            Debug.LogError($"[FXPool] Return called on '{go.name}' which has no FXPoolTag — object leaked.");
            return;
        }
        pools[tag.type].Push(go);
    }

    private GameObject CreateFXObject(FXType type)
    {
        var go = new GameObject(type.ToString());
        go.transform.SetParent(poolRoot, false);
        go.AddComponent<FXPoolTag>().type = type;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = type == FXType.SuckGhost ? 5 : 15;
        sr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
        return go;
    }
}

/// <summary>
/// Runtime tag attached to pooled FX GameObjects. Lets <see cref="FXPool.Return"/>
/// route objects back to the correct stack in O(1) without string comparison
/// (previously used go.name.StartsWith, which was both slow and silently
/// dropped any object whose name didn't match one of three hard-coded prefixes).
/// </summary>
public class FXPoolTag : MonoBehaviour
{
    public FXPool.FXType type;
}
