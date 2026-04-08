using UnityEngine;

public class Ball : MonoBehaviour
{
    public int id;
    public Color ballColor;

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

        // Force correct visual scale (prefab scale may not match WorldScale)
        float diam = GameConstants.BallRadius * 2f;
        transform.localScale = new Vector3(diam, diam, 1f);

        // Use unlit shader so balls are visible without 2D lighting
        var mat = GameConstants.CreateUnlitSpriteMaterial();
        if (mat != null) sr.material = mat;
    }

    public float DistTo(Ball other)
    {
        return Vector2.Distance(transform.position, other.transform.position);
    }

    public float DistTo(Vector2 point)
    {
        return Vector2.Distance(transform.position, point);
    }
}
