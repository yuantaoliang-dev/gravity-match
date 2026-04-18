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
    private SpriteRenderer bhRingSr;

    // Growth tracking
    private int bhAte = 0;
    public float Radius => GameConstants.BHRadiusBase + bhAte * GameConstants.BHGrowthRadius;
    public float EventHorizon => GameConstants.BHEventHorizonBase + bhAte * GameConstants.BHGrowthEH;

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

        // Purple ring: child circle scaled slightly larger behind the dark center
        var ringGo = new GameObject("BHRing");
        ringGo.transform.SetParent(bhTransform, false);
        ringGo.transform.localScale = new Vector3(1.35f, 1.35f, 1f);
        bhRingSr = ringGo.AddComponent<SpriteRenderer>();
        bhRingSr.sprite = bhSr.sprite;
        bhRingSr.color = new Color(0.55f, 0.15f, 0.85f, 0.5f);
        bhRingSr.sortingOrder = -11;
        bhRingSr.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
    }

    /// <summary>Called every frame from GameManager.Update.</summary>
    public void UpdateVisuals()
    {
        // Update BH visual size
        float bhDiam = Radius * 2f;
        bhTransform.localScale = new Vector3(bhDiam, bhDiam, 1f);

        // Ring pulse
        if (bhRingSr)
        {
            float pulse = 0.4f + 0.2f * Mathf.Sin(Time.time * 2.5f);
            bhRingSr.color = new Color(0.55f, 0.15f, 0.85f, pulse);
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
        float absThresh = EventHorizon + GameConstants.BallRadius * 0.5f;
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
