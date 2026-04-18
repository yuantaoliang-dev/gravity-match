using UnityEngine;

public class Ball : MonoBehaviour
{
    public int id;
    public Color ballColor;

    /// <summary>
    /// Cached 2D position. Kept in sync with transform.position at every write site so
    /// read-only hot paths (projectile sub-stepping, trajectory drawing, match detection)
    /// avoid the C# ↔ C++ boundary cost of transform.position getter.
    /// ALL code that writes transform.position on a Ball MUST also update cachedPos
    /// (use <see cref="SetPos"/> for a single atomic update, or call <see cref="SyncCachedPos"/>
    /// after a manual transform write).
    /// </summary>
    public Vector2 cachedPos;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void Init(int id, Color color)
    {
        this.id = id;
        this.ballColor = color;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.color = color;
        sr.sortingOrder = 2;

        // Force correct visual scale (prefab scale may not match WorldScale)
        float diam = GameConstants.BallRadius * 2f;
        transform.localScale = new Vector3(diam, diam, 1f);

        // Use unlit shader so balls are visible without 2D lighting.
        // sharedMaterial (not material) so every ball references the same
        // Material instance — required for 2D SpriteRenderer batching.
        sr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();

        // Sync cached position (transform.position was set by the spawn code BEFORE Init)
        cachedPos = transform.position;
    }

    /// <summary>Set position and sync cachedPos in one call. Preferred over assigning transform.position directly.</summary>
    public void SetPos(Vector2 p)
    {
        transform.position = p;
        cachedPos = p;
    }

    /// <summary>Pull cachedPos from current transform.position — use when position was already written directly.</summary>
    public void SyncCachedPos()
    {
        cachedPos = transform.position;
    }

    public float DistTo(Ball other)
    {
        return Vector2.Distance(cachedPos, other.cachedPos);
    }

    public float DistTo(Vector2 point)
    {
        return Vector2.Distance(cachedPos, point);
    }

    /// <summary>Squared distance to another ball (avoids sqrt).</summary>
    public float SqrDistTo(Ball other)
    {
        return (cachedPos - other.cachedPos).sqrMagnitude;
    }

    /// <summary>Squared distance to a point (avoids sqrt).</summary>
    public float SqrDistTo(Vector2 point)
    {
        return (cachedPos - point).sqrMagnitude;
    }
}
