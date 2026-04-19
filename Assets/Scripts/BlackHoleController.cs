using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles black hole growth, visual updates (ring pulse), auto-absorb,
/// and projectile absorption. Extracted from GameManager.
/// </summary>
public class BlackHoleController : MonoBehaviour
{
    private GameManager gm;
    private Transform bhTransform;
    private SpriteRenderer discSr;        // accretion disc sprite

    // Growth tracking
    private int bhAte = 0;
    public float Radius => GameConstants.BHRadiusBase + bhAte * GameConstants.BHGrowthRadius;
    public float EventHorizon => GameConstants.BHEventHorizonBase + bhAte * GameConstants.BHGrowthEH;

    // ===== ACCRETION DISC =====
    // Rendered via a single pre-baked SpriteRenderer with a radial-gradient
    // texture: solid at the inner rim, fading to transparent at the outer rim.
    // The texture's outer is at dNorm = 1.0, so scaling the sprite to 2×EH
    // world diameter places the outer rim exactly at EventHorizon — the
    // gameplay absorption line.
    //
    // Geometry summary:
    //   - BH sphere:      radius = Radius
    //   - Disc inner rim: radius = DiscInnerNormOfOuter × EventHorizon  (α=1)
    //   - Disc outer rim: radius = EventHorizon                          (α=0)
    //   - Pulse zone:     d ∈ (EH + BallRadius, EH + 2×BallRadius]       (invisible)
    //
    // Ball entering pulse zone  → brightness pulse.
    // Ball touching disc outer  → absorbed (d ≤ EH + BallRadius, unchanged).
    private const float DiscInnerNormOfOuter = 0.60f;      // disc inner rim as fraction of outer rim
    private const int   DiscTextureSize      = 256;
    private static readonly Color DiscColor  = new Color(0.72f, 0.28f, 0.98f, 1f);

    // Ball brightness pulse while inside the warning zone — color RGB is
    // multiplied by (1 + Amp × sin(t × Freq)), size stays constant.
    private const float BallPulseAmp  = 0.35f;   // ±35% brightness swing
    private const float BallPulseFreq = 2.5f;    // radians/sec (slow breath)

    // ===== ZERO-ALLOC BUFFERS =====
    // AutoAbsorb runs every frame during Play state — buffers reused across frames.
    private readonly HashSet<int> _absIds       = new HashSet<int>();
    private readonly List<Ball>   _absorbed     = new List<Ball>(32);
    private readonly List<Ball>   _touchScratch = new List<Ball>(64);

    public void Init(GameManager gm, Transform blackHoleTransform)
    {
        this.gm = gm;
        this.bhTransform = blackHoleTransform;
        SetupVisuals();
    }

    /// <summary>Reset BH state for new level.</summary>
    public void ResetForLevel()
    {
        bhAte = 0;
    }

    /// <summary>Called by GameManager.RemoveBalls to track growth.</summary>
    public void OnBallsEaten(int count)
    {
        bhAte += count;
    }

    // ===== VISUALS =====

    void SetupVisuals()
    {
        var bhSr = bhTransform.GetComponent<SpriteRenderer>();
        if (bhSr == null) return;

        bhSr.color = new Color(0.06f, 0.06f, 0.10f, 1f);
        bhSr.sortingOrder = -10;
        bhSr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();

        // Accretion disc — a ring-shaped radial-gradient sprite. Inner rim is
        // solid purple (clear boundary for the player); outer rim fades to
        // fully transparent (atmospheric "falling into" look). The outer rim
        // sits at EventHorizon, so the instant a ball's edge reaches the disc
        // it's absorbed.
        //
        // NOT parented to bhTransform — parent has a dynamic localScale equal
        // to Radius×2, which would distort the disc. We update position and
        // scale manually in UpdateVisuals.
        var discGo = new GameObject("BHAccretionDisc");
        discSr = discGo.AddComponent<SpriteRenderer>();
        discSr.sprite = CreateAccretionDiscSprite();
        discSr.color = DiscColor;
        discSr.sortingOrder = 3;       // above balls (2), below shooter UI (10+)
        discSr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
    }

    /// <summary>
    /// Build the accretion-disc sprite once. 256² RGBA; transparent hole for
    /// <c>dNorm &lt; DiscInnerNormOfOuter</c>, linear alpha ramp from 1 at the
    /// inner rim to 0 at the outer rim (dNorm = 1), transparent outside.
    /// Scaling the sprite to 2×EventHorizon world diameter places the outer
    /// rim exactly on the absorption line.
    /// </summary>
    static Sprite CreateAccretionDiscSprite()
    {
        int size = DiscTextureSize;
        int half = size / 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "BHAccretionDisc",
        };
        var pixels = new Color[size * size];
        float innerNorm = DiscInnerNormOfOuter;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - half + 0.5f;
                float dy = y - half + 0.5f;
                float dNorm = Mathf.Sqrt(dx * dx + dy * dy) / half;
                float a;
                if (dNorm < innerNorm || dNorm >= 1f)
                {
                    a = 0f;
                }
                else
                {
                    float t = (dNorm - innerNorm) / (1f - innerNorm); // 0 at inner rim, 1 at outer rim
                    // Quadratic concave falloff: alpha drops quickly after the
                    // inner rim, tail lingers faintly — makes the fade obvious
                    // instead of looking like a uniform-brightness band with a
                    // hard edge. (1 − t)² gives:
                    //   t=0.0 → 1.00   t=0.25 → 0.56   t=0.5 → 0.25
                    //   t=0.75 → 0.06  t=1.0 → 0
                    float inv = 1f - t;
                    a = inv * inv;
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        // pixelsPerUnit = size → sprite is 1 unit in diameter at localScale 1.
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Called every frame from GameManager.Update.</summary>
    public void UpdateVisuals()
    {
        // Update BH center sprite size.
        float bhDiam = Radius * 2f;
        bhTransform.localScale = new Vector3(bhDiam, bhDiam, 1f);

        Vector2 bhPos = bhTransform.position;

        // Accretion disc: position + scale so outer rim lands at EventHorizon.
        // Sprite is 1 unit diameter at localScale 1, so worldDiameter = 2×EH.
        if (discSr != null)
        {
            float worldDiam = EventHorizon * 2f;
            var t = discSr.transform;
            t.position = new Vector3(bhPos.x, bhPos.y, 0f);
            t.localScale = new Vector3(worldDiam, worldDiam, 1f);

            // Gentle alpha breath. Keep overall alpha low so the disc reads
            // as a translucent atmospheric layer rather than a solid plate;
            // the brightest peak is still dimmer than a field ball.
            float sinDisc = Mathf.Sin(Time.time * BallPulseFreq);
            var c = DiscColor;
            c.a = 0.45f + 0.10f * sinDisc; // pulses 0.35 – 0.55
            discSr.color = c;
        }

        // Per-ball brightness pulse when ball is within one BallRadius of the
        // disc's outer rim (i.e. ball edge is 0 to 1 BR outside disc). Absorb
        // happens in AutoAbsorb when ball edge actually touches (d ≤ EH + BR),
        // so the pulse zone is exactly (EH + BR, EH + 2×BR].
        if (gm != null && gm.Balls != null)
        {
            float zoneOuter = EventHorizon + 2f * GameConstants.BallRadius;
            float zoneOuterSq = zoneOuter * zoneOuter;
            float absThresh = EventHorizon + GameConstants.BallRadius;
            float absThreshSq = absThresh * absThresh;
            float pulseSin = Mathf.Sin(Time.time * BallPulseFreq);
            float pulseMul = 1f + BallPulseAmp * pulseSin;

            foreach (var b in gm.Balls)
            {
                float dSq = b.SqrDistTo(bhPos);
                // Inside pulse zone: past absorb threshold in position (but
                // still slightly outside) means ball edge is within 1 BR of
                // the disc outer rim.
                bool inPulseZone = dSq > absThreshSq && dSq <= zoneOuterSq;
                b.SetBrightnessMultiplier(inPulseZone ? pulseMul : 1f);
            }
        }
    }

    // ===== AUTO-ABSORB =====

    /// <summary>
    /// Auto-absorb field balls too close to the growing BH.
    /// Called from GameManager.Update during Play state.
    /// </summary>
    public void AutoAbsorb()
    {
        if (gm.state != GameManager.GameState.Play || gm.shooter.HasProjectile) return;

        Vector2 center = bhTransform.position;
        // Absorb the moment a ball's edge touches the ring (EventHorizon).
        // This matches the visible boundary exactly — the invisible pulse
        // zone outside is only a visual warning (brightness pulse in
        // UpdateVisuals), not an absorb trigger.
        float absThresh = EventHorizon + GameConstants.BallRadius;
        float absThreshSq = absThresh * absThresh;
        _absIds.Clear();
        foreach (var b in gm.Balls)
        {
            if (b.SqrDistTo(center) < absThreshSq)
                _absIds.Add(b.id);
        }
        if (_absIds.Count == 0) return;

        // Expand to touching neighbors (zero-alloc)
        float pullRange = EventHorizon + GameConstants.BallRadius * 2.5f;
        float pullRangeSq = pullRange * pullRange;
        foreach (var b in gm.Balls)
        {
            if (_absIds.Contains(b.id)) continue;
            if (b.SqrDistTo(center) > pullRangeSq) continue;
            gm.GetTouchingNonAlloc(b, -1, _touchScratch);
            for (int i = 0; i < _touchScratch.Count; i++)
            {
                if (_absIds.Contains(_touchScratch[i].id)) { _absIds.Add(b.id); break; }
            }
        }

        // Collect balls to absorb (reused buffer)
        _absorbed.Clear();
        foreach (var b in gm.Balls)
        {
            if (_absIds.Contains(b.id)) _absorbed.Add(b);
        }
        int absorbCount = _absorbed.Count;
        gm.RemoveBalls(_absorbed);
        gm.score += absorbCount * GameConstants.ScoreBHAbsorb;
        gm.ForceValidColors();
        gm.ui.UpdateHUD(gm.ballsLeft, gm.score, gm.Balls.Count);
        gm.CheckEnd();
    }

    // ===== PROJECTILE ABSORBED =====

    /// <summary>Called when a shot ball enters the BH event horizon.</summary>
    public void OnProjectileAbsorbed()
    {
        bhAte++;
        gm.comboCount = 0;
        if (AudioManager.Instance) AudioManager.Instance.PlayBHAbsorb();
        gm.StartRotation();
    }
}
