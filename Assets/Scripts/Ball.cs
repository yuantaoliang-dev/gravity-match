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

    /// <summary>
    /// Replace the ball's rendered color with an arbitrary value (does NOT
    /// change <see cref="ballColor"/>). Intended for transient overlays like
    /// the shooter's aim-beam highlight. Callers are responsible for
    /// restoring via <see cref="SetBrightnessMultiplier"/>(1) — typically the
    /// next frame's BH / baseline pass will overwrite this anyway.
    /// </summary>
    public void SetColor(Color c)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = c;
    }

    /// <summary>
    /// Modulate rendered brightness while keeping the stored <see cref="ballColor"/>
    /// as the baseline. Multiplier of 1 restores the base color exactly;
    /// &gt;1 brightens (RGB clamped to 1), &lt;1 darkens. Used by
    /// BlackHoleController to pulse balls inside the BH warning zone.
    /// </summary>
    public void SetBrightnessMultiplier(float multiplier)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;
        if (multiplier == 1f)
        {
            sr.color = ballColor;
            return;
        }
        sr.color = new Color(
            Mathf.Clamp01(ballColor.r * multiplier),
            Mathf.Clamp01(ballColor.g * multiplier),
            Mathf.Clamp01(ballColor.b * multiplier),
            ballColor.a);
    }
}
