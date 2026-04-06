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
