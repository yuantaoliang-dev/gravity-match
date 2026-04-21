using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

public class Shooter : MonoBehaviour
{
    [Header("References")]
    public GameManager gm;
    public LineRenderer aimLine;
    public SpriteRenderer currentBallDisplay;
    public SpriteRenderer nextBallDisplay;

    [Header("State")]
    public bool HasProjectile => projectile != null;

    private GameObject projectile;
    private Vector2 projVelocity;
    private int projBounces;
    private bool aiming;
    private bool uiTouchBlocked; // Tracks if current touch started on UI
    private bool hasValidAim;    // True when aim direction is acceptable to fire
    private Vector2 aimDir;
    private Camera mainCamera; // Cached to avoid per-frame Camera.main lookup
    private int activeTouchId = -1; // Tracks which finger we're following (-1 = none)
    private bool activeMouse; // True while currently tracked left-click is held

    // Short-tap vs long-press gating.
    // Tap: release before threshold → fire immediately, no trajectory, no camera pan
    //      (projectile walls stay at original ±CamHW position).
    // Hold: after threshold → show trajectory + enable camera pan for wide aims.
    [Header("Aim Hold Threshold")]
    [Tooltip("Seconds the player must hold before trajectory/pan become visible. Shorter presses fire immediately as a quick tap. Typical casual taps are 150-250ms; raise this if short taps still show trajectory.")]
    [SerializeField] private float aimHoldThreshold = 0.25f;
    private float aimStartTime;
    private bool aimVisible; // true once aimHoldThreshold elapsed with valid aim

    // Trajectory dot pool (v21 style: small circles with fading alpha)
    private const int MaxTrajDots = 100;
    private List<SpriteRenderer> trajDots = new List<SpriteRenderer>();
    private List<Vector2> trajPoints = new List<Vector2>(400);

    // Landing-preview overlay. Two parallel (overlay, mask) pairs — one per
    // ball that will receive the shadow:
    //   * Index 0: the "firstHit" (primary target) — always used when the
    //              shot will attach.
    //   * Index 1: the "secondHit" (any ball in the trajectory's continued
    //              path within maxTravel = 4/3 BR) — rendered whenever the
    //              ray-circle sweep past firstHit finds another ball, with
    //              NO color / group filter. This is a PATH cue: "your aim
    //              brushes this ball too", independent of whether the
    //              projectile would physically reach it (e.g. different-
    //              color firstHit still gets a second-ball preview).
    //
    // Each pair uses an EXCLUSIVE sortingOrder + matching mask range so
    // mask 0 clips only overlay 0, mask 1 clips only overlay 1. This is
    // why they're at 7 and 6 respectively, with mask ranges [7,7] and [6,6]
    // — overlapping ranges would have each mask affect both overlays and
    // the two shadows would bleed onto each other's balls.
    //
    // Sorting stack for the preview overlays:
    //     balls = 2
    //     BH accretion disc = 3
    //     → overlay 1 (secondHit) = 6
    //     → overlay 0 (firstHit)  = 7
    //     top/bottom edge mask (HUD background) = 8
    //     trajectory dots = 10
    //     shooter visuals = 12+
    // Both overlays stay BELOW the edge mask (8) so target balls that drift
    // into the HUD safe-area zone don't get their hidden half drawn back in
    // by the shadow stripe.
    private const int PreviewCount = 2;
    private const int PreviewOverlayFirstHitOrder  = 7;
    private const int PreviewOverlaySecondHitOrder = 6;
    private readonly SpriteRenderer[] previewOverlays = new SpriteRenderer[PreviewCount];
    private readonly SpriteMask[]     previewMasks    = new SpriteMask[PreviewCount];

    // Shooter area visuals
    private SpriteRenderer breathingGlow;
    private SpriteRenderer colorGlow;
    private TMPro.TextMeshPro remainingCountTMP;

    // Combo display (right side of shooter)
    private GameObject comboDisplay;
    private TMPro.TextMeshPro comboText;
    private SpriteRenderer[] comboDots = new SpriteRenderer[3];

    void Start()
    {
        // All SpriteRenderers share one Material instance (sharedMaterial) so the
        // 2D batcher can merge them into a single draw call. Previously this
        // class allocated ~110 unique Material instances (dots + ring + glows).
        var sharedMat = GameConstants.GetUnlitSpriteMaterial();

        // Hide shooter platform sprite (v21 has no platform indicator)
        var sr = GetComponent<SpriteRenderer>();
        if (sr) sr.enabled = false;

        // Setup shooter area displays matching v21 layout
        float br = GameConstants.BallRadius;
        float ballDiam = br * 2f;
        float curSize = (br + 0.02f * GameConstants.WorldScale) * 2f; // v21: BR+2 radius
        if (currentBallDisplay)
        {
            currentBallDisplay.transform.localPosition = Vector3.zero;
            currentBallDisplay.transform.localScale = new Vector3(curSize, curSize, 1f);
            currentBallDisplay.sortingOrder = 15;
            currentBallDisplay.sharedMaterial = sharedMat;

            // v21: outer white ring (BR+5, alpha 0.25)
            var outlineGo = new GameObject("CurrentOutline");
            outlineGo.transform.SetParent(transform, false);
            outlineGo.transform.localPosition = Vector3.zero;
            float outlineSize = (br + 0.05f * GameConstants.WorldScale) * 2f;
            outlineGo.transform.localScale = new Vector3(outlineSize, outlineSize, 1f);
            var outlineSr = outlineGo.AddComponent<SpriteRenderer>();
            outlineSr.sprite = currentBallDisplay.sprite;
            outlineSr.color = new Color(1f, 1f, 1f, 0.25f);
            outlineSr.sortingOrder = 14;
            outlineSr.sharedMaterial = sharedMat;

            // v21: breathing glow ring (BR+6+pulse*2, animated in UpdateDisplay)
            var glowGo = new GameObject("BreathingGlow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localPosition = Vector3.zero;
            breathingGlow = glowGo.AddComponent<SpriteRenderer>();
            breathingGlow.sprite = currentBallDisplay.sprite;
            breathingGlow.sortingOrder = 13;
            breathingGlow.sharedMaterial = sharedMat;

            // v21: color glow behind ball (BR+10, same color, low alpha)
            var colorGlowGo = new GameObject("ColorGlow");
            colorGlowGo.transform.SetParent(transform, false);
            colorGlowGo.transform.localPosition = Vector3.zero;
            float colorGlowSize = (br + 0.10f * GameConstants.WorldScale) * 2f;
            colorGlowGo.transform.localScale = new Vector3(colorGlowSize, colorGlowSize, 1f);
            colorGlow = colorGlowGo.AddComponent<SpriteRenderer>();
            colorGlow.sprite = currentBallDisplay.sprite;
            colorGlow.sortingOrder = 12;
            colorGlow.sharedMaterial = sharedMat;
        }
        if (nextBallDisplay)
        {
            // v21: nxX = SX - BR*9 (left of shooter)
            float nxOffset = -br * 5f;
            nextBallDisplay.transform.localPosition = new Vector3(nxOffset, 0, 0);
            nextBallDisplay.transform.localScale = new Vector3(ballDiam, ballDiam, 1f);
            nextBallDisplay.sortingOrder = 15;
            nextBallDisplay.sharedMaterial = sharedMat;

            // v21: remaining count text below next ball (nxX, nxY + BR + 12)
            var remGo = new GameObject("RemainingCount");
            remGo.transform.SetParent(transform, false);
            remGo.transform.localPosition = new Vector3(nxOffset, -br - 0.06f, 0);
            remainingCountTMP = remGo.AddComponent<TMPro.TextMeshPro>();
            remainingCountTMP.fontSize = 1.2f;
            remainingCountTMP.fontStyle = TMPro.FontStyles.Bold;
            remainingCountTMP.alignment = TMPro.TextAlignmentOptions.Center;
            remainingCountTMP.sortingOrder = 16;
            var remRT = remGo.GetComponent<RectTransform>();
            remRT.sizeDelta = new Vector2(0.3f, 0.1f);
        }

        // Create trajectory LineRenderer programmatically — all three use sharedMat
        CreateTrajectoryDots();
        CreateAimLine();
        CreateComboDisplay();
        CreatePreviewOverlay();
    }

    void CreatePreviewOverlay()
    {
        Sprite ballSprite = currentBallDisplay ? currentBallDisplay.sprite : null;

        // Sanity check — if the sprite is null or currentBallDisplay isn't wired
        // in the Inspector, the whole preview system is silently broken.
        if (currentBallDisplay == null)
            Debug.LogError("[PreviewOverlay] currentBallDisplay is not assigned in Inspector — preview will not render.");
        else if (ballSprite == null)
            Debug.LogError("[PreviewOverlay] currentBallDisplay.sprite is null — preview mask will have no shape.");
        else
            Dbg.Log($"[PreviewOverlay] Init OK — mask sprite='{ballSprite.name}' bounds={ballSprite.bounds.size}");

        // Bake the shared soft-edged stripe sprite once. All preview overlays
        // (firstHit + any second-ball-in-path) share this sprite; they only
        // differ by transform and tint.
        //
        // Gradient: 32×32 alpha-falloff texture. Each axis uses smoothstep
        // from an inner plateau out to 0 at the edge, then the two are
        // multiplied to give a rounded-rectangle / "soft stadium" alpha
        // field. Bilinear filtering + clamped wrap means the gradient
        // stretches smoothly when the stripe is scaled up, so the rendered
        // edges inside the target ball are gently feathered, NOT a hard
        // rectangle side.
        //
        // Plateau sizes tuned to give a defined shadow, not a fuzzy blob:
        //   * Width plateau 0.65 — solid 65% of the width, 35% falloff on
        //     the two long edges. Earlier we used 0.35 which left most of
        //     the stripe in the falloff, reading as "blurry smudge" rather
        //     than "shadow band".
        //   * Length plateau 0.80 — only 20% falloff at the ends (which
        //     the circular mask clips anyway, so this barely matters; the
        //     tiny remaining feather hides any rasterization seam at the
        //     mask boundary).
        const int size = 32;
        var stripeTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "PreviewStripeTex",
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (float)(size - 1)) * 2f - 1f; // -1 .. 1
                float ny = (y / (float)(size - 1)) * 2f - 1f;
                float aX = Mathf.SmoothStep(1f, 0.65f, Mathf.Abs(nx));
                float aY = Mathf.SmoothStep(1f, 0.80f, Mathf.Abs(ny));
                pixels[y * size + x] = new Color(1f, 1f, 1f, aX * aY);
            }
        }
        stripeTex.SetPixels(pixels);
        stripeTex.Apply();
        // pixelsPerUnit = size → sprite is 1×1 world unit at localScale 1,
        // preserving the (1×1) scale contract so UpdatePreviewOverlay's
        // localScale math stays unchanged.
        var stripeSprite = Sprite.Create(stripeTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);

        // Build the two parallel (mask, overlay) pairs with NON-OVERLAPPING
        // sortingOrder ranges, so mask 0 clips only overlay 0 and mask 1
        // clips only overlay 1 (otherwise both shadows would be clipped to
        // the union of both balls' circles, bleeding across).
        int[] overlayOrders = { PreviewOverlayFirstHitOrder, PreviewOverlaySecondHitOrder };
        for (int i = 0; i < PreviewCount; i++)
        {
            int order = overlayOrders[i];
            string tag = i == 0 ? "FirstHit" : "SecondHit";

            // Mask: placed at its ball each frame. Range is a degenerate
            // single-value [order, order] so only the matching overlay at
            // that sortingOrder is inside this mask's affect range.
            var maskGo = new GameObject($"PreviewMask_{tag}");
            maskGo.transform.SetParent(transform, false);
            var mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = ballSprite;
            mask.alphaCutoff = 0.5f; // ball sprite is solid white — 0.5 is safe
            mask.isCustomRangeActive = true;
            mask.backSortingOrder  = order;
            mask.frontSortingOrder = order;
            mask.enabled = false;
            previewMasks[i] = mask;

            // Overlay: ball-specific stripe tinted with that ball's shadow.
            // HARDCODED VisibleInsideMask — the older Inspector toggle to
            // bypass masking turned into a footgun (flipped on once, the
            // stripe then bleeds over everything unmasked), so masking is
            // no longer optional.
            var overlayGo = new GameObject($"PreviewOverlay_{tag}");
            overlayGo.transform.SetParent(transform, false);
            var overlay = overlayGo.AddComponent<SpriteRenderer>();
            overlay.sprite = stripeSprite;
            overlay.sortingOrder = order;
            overlay.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            overlay.sharedMaterial = GameConstants.GetUnlitSpriteMaterial();
            overlay.enabled = false;
            previewOverlays[i] = overlay;
        }
    }

    void CreateComboDisplay()
    {
        // v21: combo counter at SX + BR*7, SY (right of shooter)
        float br = GameConstants.BallRadius;
        comboDisplay = new GameObject("ComboDisplay");
        comboDisplay.transform.SetParent(transform, false);
        comboDisplay.transform.localPosition = new Vector3(br * 7f, 0, 0);

        // "COMBO" text
        var textGo = new GameObject("ComboText");
        textGo.transform.SetParent(comboDisplay.transform, false);
        textGo.transform.localPosition = new Vector3(0, 0.02f, 0);
        comboText = textGo.AddComponent<TMPro.TextMeshPro>();
        comboText.text = "COMBO";
        comboText.fontSize = 0.8f;
        comboText.fontStyle = TMPro.FontStyles.Bold;
        comboText.alignment = TMPro.TextAlignmentOptions.Center;
        comboText.sortingOrder = 16;
        var textRT = textGo.GetComponent<RectTransform>();
        textRT.sizeDelta = new Vector2(0.4f, 0.1f);

        // 3 progress dots — share one Material
        Sprite dotSprite = currentBallDisplay ? currentBallDisplay.sprite : null;
        float dotSize = 0.04f * GameConstants.WorldScale;
        float dotSpacing = 0.06f * GameConstants.WorldScale;
        var sharedMat = GameConstants.GetUnlitSpriteMaterial();
        for (int i = 0; i < 3; i++)
        {
            var dotGo = new GameObject($"ComboDot{i}");
            dotGo.transform.SetParent(comboDisplay.transform, false);
            dotGo.transform.localPosition = new Vector3(
                (i - 1) * dotSpacing, -0.04f, 0);
            dotGo.transform.localScale = new Vector3(dotSize, dotSize, 1f);
            var sr = dotGo.AddComponent<SpriteRenderer>();
            sr.sprite = dotSprite;
            sr.sortingOrder = 16;
            sr.sharedMaterial = sharedMat;
            comboDots[i] = sr;
        }

        comboDisplay.SetActive(false);
    }

    void UpdateComboDisplay()
    {
        int count = gm.comboCount;
        if (count <= 0)
        {
            if (comboDisplay) comboDisplay.SetActive(false);
            return;
        }

        comboDisplay.SetActive(true);
        // v21: comboCount >= 2 → amber, else half-transparent white
        Color activeCol = count >= 2
            ? new Color(0.937f, 0.624f, 0.153f, 1f) // #EF9F27
            : new Color(1f, 1f, 1f, 0.5f);
        Color inactiveCol = new Color(1f, 1f, 1f, 0.1f);

        comboText.color = activeCol;
        for (int i = 0; i < 3; i++)
        {
            comboDots[i].color = i < count ? activeCol : inactiveCol;
        }
    }

    void CreateTrajectoryDots()
    {
        // v21 style: pool of small dot sprites, positioned along trajectory.
        // Biggest batching win in the project — 100 dots now share ONE Material.
        Sprite dotSprite = currentBallDisplay ? currentBallDisplay.sprite : null;
        float dotSize = 0.025f * GameConstants.WorldScale; // v21: radius 1.2px
        var parent = new GameObject("TrajectoryDots");
        parent.transform.SetParent(transform, false);

        var sharedMat = GameConstants.GetUnlitSpriteMaterial();
        for (int i = 0; i < MaxTrajDots; i++)
        {
            var go = new GameObject($"Dot{i}");
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = new Vector3(dotSize, dotSize, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dotSprite;
            sr.sortingOrder = 10;
            sr.sharedMaterial = sharedMat;
            go.SetActive(false);
            trajDots.Add(sr);
        }
    }

    void CreateAimLine()
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        aimLine = go.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.startWidth = 0.02f * GameConstants.WorldScale;
        aimLine.endWidth = 0.02f * GameConstants.WorldScale;
        aimLine.startColor = new Color(1f, 1f, 1f, 0.6f);
        aimLine.endColor = new Color(1f, 1f, 1f, 0.3f);
        aimLine.sortingOrder = 10;
        aimLine.positionCount = 0;
        aimLine.sharedMaterial = GameConstants.GetUnlitLineMaterial();
    }

    void Update()
    {
        // Always update ball color display and combo counter
        UpdateDisplay();
        UpdateComboDisplay();

        if (projectile != null)
        {
            UpdateProjectile();
            return;
        }

        // Unified input pipeline for Play and Rotating states.
        // Rotating differs only in that a valid release snaps the rotation to
        // completion before firing (see ReleaseAim).
        bool play = gm.state == GameManager.GameState.Play;
        bool rotating = gm.state == GameManager.GameState.Rotating;
        if (!play && !rotating) return;

        ReadPointer(out bool justDown, out bool justUp, out bool isHeld, out Vector2 screenPos);

        if (justDown && !aiming)
        {
            if (!TryBeginAim()) return;
        }

        if (aiming && isHeld)
        {
            UpdateAiming(screenPos);
        }
        else if (!aiming)
        {
            ClearAimVisuals();
        }

        if (justUp && aiming)
        {
            ReleaseAim(duringRotation: rotating);
        }
    }

    // ===== INPUT PIPELINE =====
    // Shared by Play and Rotating states. The only behavioural difference is in
    // ReleaseAim: during Rotating, we also snap the rotation to completion
    // before firing, so the shot matches the current (not final-animated) field.

    /// <summary>
    /// Unified pointer read. Produces edge events (justDown/justUp) and held state
    /// for touch (with finger-ID tracking) and mouse (editor). Replaces the
    /// hand-rolled versions previously duplicated in HandleInput /
    /// PollInputDuringRotation.
    /// </summary>
    void ReadPointer(out bool justDown, out bool justUp, out bool isHeld, out Vector2 screenPos)
    {
        justDown = false;
        justUp = false;
        isHeld = false;
        screenPos = Vector2.zero;

        if (Input.touchCount > 0)
        {
            // Still tracking a specific finger?
            Touch? tracked = null;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.fingerId == activeTouchId) { tracked = t; break; }
            }

            if (tracked.HasValue)
            {
                screenPos = tracked.Value.position;
                var phase = tracked.Value.phase;
                if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
                {
                    justUp = true;
                    activeTouchId = -1;
                }
                else
                {
                    isHeld = true;
                }
            }
            else
            {
                // Look for a fresh Began to start tracking — prevents lingering
                // finger (e.g. still down from a UI button tap) being picked up.
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began)
                    {
                        activeTouchId = t.fingerId;
                        screenPos = t.position;
                        justDown = true;
                        isHeld = true;
                        break;
                    }
                }
            }
        }
        else
        {
            // Tracked touch disappeared (system gesture canceled, app lost focus)
            if (activeTouchId >= 0)
            {
                activeTouchId = -1;
                if (aiming) justUp = true;
            }

            // Mouse (editor)
            if (Input.GetMouseButtonDown(0))
            {
                activeMouse = true;
                justDown = true;
            }
            if (activeMouse)
            {
                screenPos = Input.mousePosition;
                isHeld = Input.GetMouseButton(0);
                if (Input.GetMouseButtonUp(0))
                {
                    justUp = true;
                    activeMouse = false;
                }
            }
        }
    }

    /// <summary>
    /// Begin aiming if the player just pressed and isn't already aiming.
    /// Returns false (and sets uiTouchBlocked) if the press landed on UI.
    /// </summary>
    bool TryBeginAim()
    {
        bool uiBlock = IsPointerOverUI();
        uiTouchBlocked = uiBlock;
        Dbg.Log($"[Shooter] Aim begin: uiBlocked={uiBlock}");
        if (uiBlock) return false;
        aiming = true;
        aimStartTime = Time.time;
        aimVisible = false; // hidden until player holds past aimHoldThreshold
        return true;
    }

    /// <summary>
    /// Update aim direction, trajectory, and camera pan while the pointer is held.
    /// Validates the aim direction is clearly up (not a side-tap).
    /// </summary>
    void UpdateAiming(Vector2 screenPos)
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        Vector2 inputWorld = mainCamera.ScreenToWorldPoint(screenPos);
        Vector2 shooterPos = transform.position;
        Vector2 offset = inputWorld - shooterPos;

        // Require touch to be clearly above shooter (at least 1 ball diameter up
        // AND dominant y-component) to avoid firing on accidental side touches
        float minUp = GameConstants.BallRadius * 2f;
        if (offset.y >= minUp && offset.y > Mathf.Abs(offset.x) * 0.3f)
        {
            aimDir = offset.normalized;
            hasValidAim = true;

            // Reveal trajectory / enable camera pan only after the hold threshold.
            // Short tap releases before this fires, keeping walls at original ±CamHW.
            if (!aimVisible && Time.time - aimStartTime >= aimHoldThreshold)
                aimVisible = true;

            if (aimVisible)
            {
                Ball firstHit = ShowTrajectory(shooterPos, aimDir * GameConstants.BallSpeed);
                UpdatePreviewOverlay(firstHit, gm.currentColor);
                ShowAimLine(shooterPos, aimDir);
                UpdateCameraPan(aimDir);
            }
        }
        else
        {
            ClearAimVisuals();
        }
    }

    /// <summary>
    /// Player released. Fire if aim was valid, otherwise clean up silently.
    /// If <paramref name="duringRotation"/>, snap the in-progress rotation to
    /// completion first so the fired ball uses the final field layout.
    /// </summary>
    void ReleaseAim(bool duringRotation)
    {
        bool validAim = hasValidAim;
        Vector2 dir = aimDir;
        aiming = false;
        hasValidAim = false;
        aimVisible = false;
        uiTouchBlocked = false;
        HideTrajectory();
        HideAimLine();

        // Pan reset strategy preserves prior per-state behaviour:
        //   - Play + valid fire: KEEP pan so projectile walls match the aim.
        //     UpdateProjectile resets pan to 0 when the ball lands / flies off.
        //   - Play + invalid:    reset pan (nothing was fired).
        //   - Rotating (always): reset pan — SnapRotationAndFire recomputes the
        //     field atomically, so the pre-snap pan target is no longer meaningful.
        if (!validAim || duringRotation)
        {
            gm.SetCameraPanTarget(0);
        }

        if (validAim)
        {
            if (duringRotation) gm.SnapRotationAndFire();
            Fire(dir);
        }
    }

    /// <summary>Hide trajectory / aim-line and reset pan. Used when aim goes invalid mid-drag.</summary>
    void ClearAimVisuals()
    {
        hasValidAim = false;
        HideTrajectory();
        HideAimLine();
        gm.SetCameraPanTarget(0);
    }

    /// <summary>
    /// Pan camera horizontally to reveal balls that would otherwise be
    /// cut off at screen edges on the aim side. Pan amount = max overflow
    /// of any ball on the aim side (with a small buffer for smoothness).
    /// </summary>
    void UpdateCameraPan(Vector2 dir)
    {
        // Only pan when aiming toward a side (not straight up)
        float aimX = dir.x;
        if (Mathf.Abs(aimX) < 0.15f) { gm.SetCameraPanTarget(0); return; }

        float halfW = gm.CamHW;
        float r = GameConstants.BallRadius;
        float maxOverflow = 0f;

        foreach (var b in gm.Balls)
        {
            float bx = b.cachedPos.x;
            if (aimX > 0)
            {
                // Aiming right: check balls beyond right edge
                float over = (bx + r) - halfW;
                if (over > maxOverflow) maxOverflow = over;
            }
            else
            {
                // Aiming left: check balls beyond left edge
                float over = -halfW - (bx - r);
                if (over > maxOverflow) maxOverflow = over;
            }
        }

        // No overflow on this side = no pan
        if (maxOverflow <= 0f) { gm.SetCameraPanTarget(0); return; }

        // Pan all the way to show the outermost ball + small extra margin
        float extraMargin = GameConstants.BallRadius * 2f;
        float pan = Mathf.Sign(aimX) * (maxOverflow + extraMargin);
        gm.SetCameraPanTarget(pan);
    }

    // Raycast helper reused to avoid allocation
    private static readonly List<UnityEngine.EventSystems.RaycastResult> raycastResults = new();
    private static UnityEngine.EventSystems.PointerEventData pointerEventData;

    /// <summary>
    /// Check if pointer is over a UI element using manual GraphicRaycaster.
    /// Avoids the first-frame bug of EventSystem.IsPointerOverGameObject(fingerId).
    /// </summary>
    bool IsPointerOverUI()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        Vector2 screenPos = Input.touchCount > 0
            ? (Vector2)Input.GetTouch(0).position
            : (Vector2)Input.mousePosition;

        if (pointerEventData == null) pointerEventData = new UnityEngine.EventSystems.PointerEventData(es);
        pointerEventData.position = screenPos;

        raycastResults.Clear();
        es.RaycastAll(pointerEventData, raycastResults);
        return raycastResults.Count > 0;
    }

    void Fire(Vector2 dir)
    {
        if (gm.ballsLeft <= 0) return;

        gm.ForceValidColors();

        // Create projectile from pool
        projectile = gm.SpawnBall(transform.position, gm.currentColor).gameObject;
        // Remove from field ball list (projectile is in-flight, not a field ball)
        var ball = projectile.GetComponent<Ball>();
        gm.Balls.Remove(ball);
        ball.Init(-1, gm.currentColor);
        projVelocity = dir * GameConstants.BallSpeed;
        projBounces = 0;
        if (AudioManager.Instance) AudioManager.Instance.PlayShoot();

        gm.OnBallFired();
    }

    void UpdateProjectile()
    {
        if (projectile == null) return;

        // 3 full-speed steps per frame
        float dt = Time.deltaTime * 60f; // normalize to 60fps
        for (int fStep = 0; fStep < 3; fStep++)
        {
            if (projectile == null) return;

            Vector2 pos = projectile.transform.position;
            float minDistSq = float.MaxValue;
            foreach (var b in gm.Balls)
            {
                float dSq = (b.cachedPos - pos).sqrMagnitude;
                if (dSq < minDistSq) minDistSq = dSq;
            }

            // Sub-stepping near balls (compare with squared thresholds)
            float thresh3 = GameConstants.BallRadius * 3f;
            float thresh6 = GameConstants.BallRadius * 6f;
            int sub = minDistSq < thresh3 * thresh3 ? 6 :
                      minDistSq < thresh6 * thresh6 ? 3 : 1;
            Vector2 subVel = projVelocity / sub * dt / 60f;

            for (int step = 0; step < sub; step++)
            {
                pos += subVel;

                // Wall bounces — only horizontal (left/right).
                // Top is open: ball can fly past screen top and just disappears
                // if it doesn't hit anything.
                float hw = gm.CamHW;
                float camX = mainCamera != null ? mainCamera.transform.position.x : 0f;
                float leftWall = camX - hw;
                float rightWall = camX + hw;
                float r = GameConstants.BallRadius;
                int prevBounces = projBounces;
                if (pos.x - r <= leftWall) { pos.x = leftWall + r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (pos.x + r >= rightWall) { pos.x = rightWall - r; projVelocity.x *= -1; subVel.x *= -1; projBounces++; }
                if (projBounces > prevBounces && AudioManager.Instance)
                    AudioManager.Instance.PlayBounce();

                // Out of bounds: too many bounces, flew below shooter, or flew off top
                float topFlyLimit = gm.CamHH + GameConstants.BallRadius * 6f;
                if (projBounces > GameConstants.MaxBounces ||
                    pos.y < transform.position.y - 0.2f ||
                    pos.y > topFlyLimit)
                {
                    gm.ReturnBallToPool(projectile);
                    projectile = null;
                    gm.SetCameraPanTarget(0);
                    gm.comboCount = 0;
                    gm.StartRotation();
                    return;
                }

                // Black hole absorb
                Vector2 bhPos = gm.blackHole.position;
                float bhEH = gm.BHEventHorizon;
                if (((Vector2)pos - bhPos).sqrMagnitude < bhEH * bhEH)
                {
                    gm.ReturnBallToPool(projectile);
                    projectile = null;
                    gm.SetCameraPanTarget(0);
                    gm.OnProjectileAbsorbedByBH();
                    return;
                }

                // Hit detection (squared distance comparison, cachedPos avoids transform boundary)
                Ball hitBall = null;
                float hitDistSq = float.MaxValue;
                float hdSq = GameConstants.HitDetectDist * GameConstants.HitDetectDist;
                foreach (var b in gm.Balls)
                {
                    float dSq = (b.cachedPos - pos).sqrMagnitude;
                    if (dSq < hdSq && dSq < hitDistSq)
                    {
                        hitDistSq = dSq;
                        hitBall = b;
                    }
                }

                if (hitBall != null)
                {
                    Color projColor = projectile.GetComponent<Ball>().ballColor;
                    Vector2 vel = projVelocity.normalized;
                    gm.ReturnBallToPool(projectile);
                    projectile = null;
                    gm.SetCameraPanTarget(0);

                    Vector2 hitCenter = hitBall.cachedPos;
                    float od = GameConstants.OverlapDistance;
                    float hd = GameConstants.HitDetectDist;

                    // Look-ahead: continue flying 2/3 ball diameter past
                    // the first hit point to check for a second ball.
                    float maxTravel = GameConstants.BallRadius * 4f / 3f;
                    Ball secondHit = null;
                    float secondHitT = float.MaxValue;

                    foreach (var b in gm.Balls)
                    {
                        if (b.id == hitBall.id) continue;
                        Vector2 bc = b.cachedPos;
                        Vector2 db = pos - bc;
                        // Ray-circle intersection: |pos + t*vel - bc| = HitDetectDist
                        float bCoeff = 2f * Vector2.Dot(db, vel);
                        float cCoeff = db.sqrMagnitude - hd * hd;
                        float disc = bCoeff * bCoeff - 4f * cCoeff;
                        if (disc < 0) continue;

                        float sqrtDisc = Mathf.Sqrt(disc);
                        float t = (-bCoeff - sqrtDisc) / 2f; // entry point
                        if (t < 0) t = 0f; // already inside range
                        if (t > maxTravel) continue;

                        if (t < secondHitT)
                        {
                            secondHitT = t;
                            secondHit = b;
                        }
                    }

                    // Placement distance depends on color match:
                    // Same color → OverlapDistance (visual overlap)
                    // Different color → just touching, no visual overlap
                    float diffColorDist = GameConstants.BallRadius * 2f;
                    bool sameColorHit = (hitBall.ballColor == projColor);
                    float placeDist = sameColorHit ? od : diffColorDist;
                    Vector2 approachDir = (pos - hitCenter).normalized;
                    Vector2 newPos = hitCenter + approachDir * placeDist;

                    // Slide-through: only when BOTH hitBall and secondHit are
                    // same color as projectile, and they're in different groups.
                    // If hitBall is different color, always stop at first contact.
                    if (sameColorHit && secondHit != null && secondHit.ballColor == projColor)
                    {
                        var hitGroup = gm.FindGroup(hitBall, GameConstants.MatchTouchDist);
                        // Manual membership check (replaces LINQ .Any)
                        bool alreadyConnected = false;
                        for (int gi = 0; gi < hitGroup.Count; gi++)
                        {
                            if (hitGroup[gi].id == secondHit.id) { alreadyConnected = true; break; }
                        }
                        if (!alreadyConnected)
                        {
                            Vector2 contactPos = pos + vel * secondHitT;
                            Vector2 secondPos = secondHit.cachedPos;
                            Vector2 dirFromSecond = (contactPos - secondPos).normalized;
                            newPos = secondPos + dirFromSecond * od;
                            Dbg.Log($"[GravityMatch] Slide-through: bridging separate groups");
                        }
                    }

                    // Push away from other balls (up to 30 iterations × N balls — use cachedPos)
                    // Same color: use OverlapDistance (allow overlap)
                    // Different color: use MinVisDist (no visual overlap)
                    float pushNudge = 0.002f * GameConstants.WorldScale;
                    for (int it = 0; it < 30; it++)
                    {
                        bool pushed = false;
                        foreach (var b2 in gm.Balls)
                        {
                            if (b2.id == hitBall.id) continue;
                            if (secondHit != null && b2.id == secondHit.id) continue;
                            Vector2 bp = b2.cachedPos;
                            float d2 = Vector2.Distance(newPos, bp);
                            bool sameColor = b2.ballColor == projColor;
                            float pushDist = sameColor ? (od - 0.005f * GameConstants.WorldScale) : diffColorDist;
                            if (d2 < pushDist)
                            {
                                Vector2 pDir = (newPos - bp).normalized;
                                newPos += pDir * (pushDist - d2 + pushNudge);
                                pushed = true;
                            }
                        }
                        if (!pushed) break;
                    }

                    var newBall = gm.SpawnBall(newPos, projColor);
                    if (AudioManager.Instance) AudioManager.Instance.PlayAttach();
                    gm.SetCameraPanTarget(0);
                    ProcessMatch(newBall);
                    return;
                }

                projectile.transform.position = pos;
            }
        }
    }

    void ProcessMatch(Ball newBall)
    {
        // Standard v21 match detection: BFS with MatchTouchDist
        var grp = gm.FindGroup(newBall, GameConstants.MatchTouchDist);

        Dbg.Log($"[GravityMatch] ProcessMatch: group size={grp.Count}, color={GameConstants.ColorToHex(newBall.ballColor)}");
        if (grp.Count >= 3)
        {
            var allTargets = new List<Ball>(grp);

            // Cone detection for 4+/5+ match
            if (grp.Count >= 4)
            {
                Vector2 center = Vector2.zero;
                foreach (var g in grp) center += g.cachedPos;
                center /= grp.Count;

                Vector2 bhPos = gm.blackHole.position;
                float baseAngle = Mathf.Atan2(center.y - bhPos.y, center.x - bhPos.x);
                float coneAngle = (grp.Count >= 5 ? GameConstants.ConeAngle5 : GameConstants.ConeAngle4) * Mathf.Deg2Rad;
                bool colorOnly = grp.Count < 5;
                var matchIds = new HashSet<int>(grp.Select(b => b.id));

                // Find cone targets
                var hitIds = new HashSet<int>();
                foreach (var b in gm.Balls)
                {
                    if (matchIds.Contains(b.id)) continue;
                    if (colorOnly && b.ballColor != newBall.ballColor) continue;
                    Vector2 bp = b.cachedPos;
                    float a = Mathf.Atan2(bp.y - bhPos.y, bp.x - bhPos.x);
                    float da = Mathf.DeltaAngle(baseAngle * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float bd = b.DistTo(bhPos);
                    if (bd < GameConstants.BallRadius) continue;
                    float halfW = Mathf.Asin(Mathf.Min(1f, GameConstants.BallRadius / bd));
                    if (Mathf.Abs(da) - halfW <= coneAngle) hitIds.Add(b.id);
                }

                // Spread to neighbors
                bool spread = true;
                while (spread)
                {
                    spread = false;
                    foreach (var b in gm.Balls)
                    {
                        if (hitIds.Contains(b.id) || matchIds.Contains(b.id)) continue;
                        foreach (var nb in gm.GetTouching(b))
                        {
                            if (hitIds.Contains(nb.id)) { hitIds.Add(b.id); spread = true; break; }
                        }
                    }
                }

                var coneExtra = gm.Balls.Where(b => hitIds.Contains(b.id)).ToList();
                allTargets.AddRange(coneExtra);
                Dbg.Log($"[GravityMatch] Cone sweep: {grp.Count}-match, colorOnly={colorOnly}, coneExtra={coneExtra.Count}, totalTargets={allTargets.Count}");

                // Pass cone info for visual FX
                gm.StartMatchSequence(grp.Count, allTargets, grp, baseAngle, coneAngle);
                return;
            }

            // 3-match (no cone)
            gm.StartMatchSequence(grp.Count, allTargets, grp, 0, 0);
        }
        else
        {
            gm.comboCount = 0;
            gm.StartRotation();
        }
    }

    // ===== TRAJECTORY PREVIEW (v21 style: dotted path) =====
    /// <summary>
    /// Simulate the trajectory, render it as dots, and return the first Ball
    /// the path would hit (or null if it flies off-screen / into BH / times
    /// out). Caller uses the return value to colour-code the aim beam.
    /// </summary>
    Ball ShowTrajectory(Vector2 start, Vector2 vel)
    {
        // Simulate trajectory matching v21: half-speed steps, stop at ball/BH
        trajPoints.Clear();
        Vector2 pos = start;
        Vector2 dir = vel.normalized;
        // v21 step: BSPD*0.5 pixels = 2.25px → world units
        float stepDist = 0.0225f * GameConstants.WorldScale;
        Vector2 v = dir * stepDist;
        int bounces = 0;
        float hw = gm.CamHW;
        // Walls follow current camera X — matches physics during panned aim
        float camX = mainCamera != null ? mainCamera.transform.position.x : 0f;
        float leftWall = camX - hw;
        float rightWall = camX + hw;
        float r = GameConstants.BallRadius;
        float hd = GameConstants.HitDetectDist;
        Vector2 bhPos = gm.blackHole.position;
        float shooterY = transform.position.y;
        float trajBhEH = gm.BHEventHorizon;
        float trajBhEHSq = trajBhEH * trajBhEH;
        float hdSqTraj = hd * hd;

        Ball firstHit = null;

        for (int i = 0; i < 400; i++) // v21 max 400 steps
        {
            pos += v;

            // Wall bounces — only horizontal; top is open
            if (pos.x - r <= leftWall) { pos.x = leftWall + r; v.x *= -1; bounces++; }
            if (pos.x + r >= rightWall) { pos.x = rightWall - r; v.x *= -1; bounces++; }

            // Stop conditions
            if (bounces > GameConstants.MaxBounces) break;
            if (pos.y < shooterY - 0.1f) break;
            // Trajectory visual stops at mask edge (ball physics continues past it)
            if (pos.y > gm.PlayTopY) break;
            if ((pos - bhPos).sqrMagnitude < trajBhEHSq) break;

            // Stop at ball hit — capture the Ball reference (not just a bool)
            // so the caller can highlight it as the previewed target.
            Ball hitBall = null;
            foreach (var b in gm.Balls)
            {
                if ((b.cachedPos - pos).sqrMagnitude < hdSqTraj) { hitBall = b; break; }
            }
            if (hitBall != null)
            {
                firstHit = hitBall;
                break;
            }

            trajPoints.Add(pos);
        }

        // Render as dots with increasing spacing: dense near shooter, sparse far away
        int dotIdx = 0;
        int show = Mathf.Min(trajPoints.Count, GameConstants.TrajectoryMaxDots);
        int i2 = 8; // small skip to avoid overlap with current ball visuals
        while (i2 < show && dotIdx < MaxTrajDots)
        {
            var dot = trajDots[dotIdx];
            dot.gameObject.SetActive(true);
            dot.transform.position = (Vector3)trajPoints[i2];
            float alpha = 0.35f; // uniform brightness along the whole trajectory
            dot.color = new Color(1f, 1f, 1f, alpha);
            dotIdx++;

            // Spacing grows gradually: denser near shooter, sparser further out
            int step = 5 + i2 / 25;
            i2 += step;
        }
        // Hide unused dots
        for (int i = dotIdx; i < MaxTrajDots; i++)
            trajDots[i].gameObject.SetActive(false);

        return firstHit;
    }

    void HideTrajectory()
    {
        for (int i = 0; i < trajDots.Count; i++)
            trajDots[i].gameObject.SetActive(false);
        for (int i = 0; i < PreviewCount; i++)
        {
            if (previewOverlays[i] != null) previewOverlays[i].enabled = false;
            if (previewMasks[i]    != null) previewMasks[i].enabled    = false;
        }
    }

    /// <summary>
    /// Tint the target ball(s) in the region the projectile's PATH would
    /// sweep through. Not a ghost-ball at the landing spot — the visual is a
    /// shot-direction stripe clipped to each affected ball's circle.
    ///
    /// Geometry:
    ///   * The projectile (circle, radius BR) sweeps a capsule of half-width
    ///     BR along its trajectory line.
    ///   * For each affected ball, we find pClosest — the perpendicular foot
    ///     from that ball's center onto the trajectory line. A dead-center
    ///     shot puts pClosest at the ball's center; grazing shots offset it.
    ///   * A rectangle stripe of width 2×BR × length 4×BR is drawn at
    ///     pClosest, rotated so its length aligns with the shot direction.
    ///   * Each ball's SpriteMask clips its own stripe to its own circle.
    ///
    /// Second-ball case: any ball the trajectory would reach within
    /// maxTravel = 4/3 BR past firstHit ALSO gets a shadow, with no
    /// color / group filter. This is wider than the real slide-through
    /// physics branch (see <see cref="FindSecondBallInPath"/>) — the
    /// shadow is a PATH cue, communicating "your aim sweeps across
    /// these balls", independent of whether the projectile would
    /// actually come to rest touching both.
    ///
    /// Result per ball:
    ///   * Dead-center hit → full ball tinted (stripe covers whole width).
    ///   * Grazing hit     → thin tinted slice on the hit side.
    ///   * No first-hit    → both overlays hidden.
    ///   * First hit only  → secondary overlay hidden.
    ///
    /// Requires GameManager's Update (and thus BH.UpdateVisuals) to run
    /// before Shooter's Update so per-frame pulse colors are applied before
    /// the overlay renders. Enforced via <c>[DefaultExecutionOrder(-10)]</c>
    /// on GameManager.
    /// </summary>
    void UpdatePreviewOverlay(Ball firstHit, Color shotColor)
    {
        // Disable both pairs up front — any early return below must leave
        // both overlays hidden so stale shadows don't persist after the aim
        // direction changes.
        for (int i = 0; i < PreviewCount; i++)
        {
            if (previewOverlays[i] != null) previewOverlays[i].enabled = false;
            if (previewMasks[i]    != null) previewMasks[i].enabled    = false;
        }

        int count = trajPoints.Count;
        // Need ≥ 2 trajectory points to derive shot direction from the last
        // segment (bounces can change it mid-flight, so (hitCenter - trajEnd)
        // isn't always correct).
        if (firstHit == null || count < 2) return;
        if (previewOverlays[0] == null || previewMasks[0] == null) return;

        Vector2 trajEnd = trajPoints[count - 1];
        Vector2 shotDir = trajPoints[count - 1] - trajPoints[count - 2];
        float shotMag = shotDir.magnitude;
        if (shotMag < 1e-4f) return;
        shotDir /= shotMag;

        // Render shadow on firstHit (always, when we got this far).
        RenderPreviewStripe(0, firstHit, trajEnd, shotDir);

        // Second-ball preview: any ball the trajectory would reach within
        // maxTravel past firstHit gets a shadow — regardless of color or
        // group. This is a PATH cue (here's where your shot sweeps), not
        // a slide-through prediction; the projectile may physically stop
        // on firstHit and never touch this second ball, but the aim beam
        // would still pass through its circle if firstHit weren't in the
        // way, and that's what the shadow communicates.
        Ball secondHit = FindSecondBallInPath(firstHit, trajEnd, shotDir);
        if (secondHit != null)
        {
            RenderPreviewStripe(1, secondHit, trajEnd, shotDir);
            Dbg.Log($"[PreviewOverlay] two-ball preview: first={firstHit.id} second={secondHit.id}");
        }
        // shotColor parameter is no longer read here (the color-gated
        // slide-through branch was removed), but the signature is kept
        // for call-site compatibility with Shooter.UpdateAiming.
        _ = shotColor;
    }

    /// <summary>
    /// Position, scale, rotate, tint and enable the (mask, overlay) pair at
    /// <paramref name="idx"/> to draw a path-swept shadow inside
    /// <paramref name="ball"/>. See UpdatePreviewOverlay for the full
    /// geometry rationale.
    /// </summary>
    void RenderPreviewStripe(int idx, Ball ball, Vector2 trajEnd, Vector2 shotDir)
    {
        var overlay = previewOverlays[idx];
        var mask    = previewMasks[idx];
        if (overlay == null || mask == null) return;

        Vector2 hitCenter = ball.cachedPos;

        // pClosest = perpendicular projection of the ball's center onto the
        // trajectory line. See UpdatePreviewOverlay's summary for derivation.
        Vector2 q = hitCenter - trajEnd;
        float tClosest = Vector2.Dot(q, shotDir);
        Vector2 pClosest = trajEnd + shotDir * tClosest;

        float diam = GameConstants.BallRadius * 2f;

        // Mask = this ball's full circle at its center.
        mask.transform.position   = (Vector3)hitCenter;
        mask.transform.localScale = new Vector3(diam, diam, 1f);
        mask.enabled = true;

        // Rotation: rotate local +Y (Unity SpriteRenderer's default "up")
        // to align with shotDir. After Z-rotation θ: local +Y = (−sin θ, cos θ).
        // Setting equal to shotDir ⇒ θ = atan2(shotDir.y, shotDir.x) − 90°.
        float angle = Mathf.Atan2(shotDir.y, shotDir.x) * Mathf.Rad2Deg - 90f;

        // localScale:
        //   x (perpendicular to shot) = diam      full sweep width
        //   y (along shot)            = diam × 2  overshoots both ends of the
        //                                         mask circle so the feathered
        //                                         gradient ends up entirely
        //                                         outside the ball.
        overlay.transform.position   = (Vector3)pClosest;
        overlay.transform.rotation   = Quaternion.Euler(0, 0, angle);
        overlay.transform.localScale = new Vector3(diam, diam * 2f, 1f);

        // Shadow = ball's own color pulled 28% toward black (= ballColor ×
        // 0.72). See UpdatePreviewOverlay for why darken-instead-of-mix.
        Color shadow = Color.Lerp(ball.ballColor, Color.black, 0.28f);

        // Alpha depth ramp: darker near the shooter, lighter near the HUD.
        // Rationale — balls farther along the flight path feel "farther
        // away" and the preview should convey reach: a strong shadow on
        // a near ball reads as "you're going to hit this for sure", a
        // faint shadow on a far ball reads as "this is a long shot".
        //
        // Using world-y distance (ball.y − shooter.y) ÷ play-area height
        // as the parameter — NOT the trajectory arc length. Reasoning:
        //   * Almost every shot is mostly vertical, so straight-y
        //     distance tracks arc length closely in practice.
        //   * Arc length would need trajPoints arithmetic to get the
        //     prefix length at each hit ball — possible but extra
        //     bookkeeping for little perceptual gain.
        //   * For wall-bounce shots the straight-y approximation
        //     slightly UNDERSHOOTS the true arc length (a bounced far
        //     ball still gets a meaningful-but-faded shadow, which is
        //     the right read).
        // Range 0.9 → 0.3:
        //   * 0.9 near shooter  — heavy shadow, unmistakable
        //   * 0.3 at HUD edge   — still visible tint, but clearly lighter
        //   * Linear Lerp (not smoothstep) because linear depth feels
        //     more "physical"; the eye expects brightness to scale
        //     monotonically with distance.
        float shooterY   = transform.position.y;
        float topY       = gm.PlayTopY;
        float playHeight = Mathf.Max(topY - shooterY, 1e-3f); // guard /0 on degenerate setups
        float depthT     = Mathf.Clamp01((ball.cachedPos.y - shooterY) / playHeight);
        shadow.a         = Mathf.Lerp(0.9f, 0.3f, depthT);

        overlay.color   = shadow;
        overlay.enabled = true;
    }

    /// <summary>
    /// Purely geometric search for a SECOND ball the trajectory would
    /// reach within <c>maxTravel = 4/3·BR</c> past <paramref name="firstHit"/>,
    /// ignoring color and group membership.
    ///
    /// Originally this enforced same-color + different-group gates so the
    /// preview matched the real slide-through physics branch. The preview
    /// intent has since widened — any ball that the aimed trajectory
    /// <b>could plausibly brush</b> should receive a shadow, even if the
    /// projectile would actually come to rest on firstHit alone (e.g.
    /// different-color firstHit, or same-color same-group firstHit). The
    /// shadow is a PATH cue, not a prediction of final ball-placement
    /// geometry.
    ///
    /// Ray–circle maths identical to the secondHit sweep inside
    /// <c>UpdateProjectile</c>: solve |trajEnd + t·shotDir − bc| = hd for
    /// t, take the entry root, keep the nearest candidate whose t lies
    /// in [0, maxTravel]. Non-alloc — the foreach over <c>gm.Balls</c>
    /// is O(n) and all scratch state fits in locals.
    /// </summary>
    /// <returns>The nearest second ball in path, or null if none.</returns>
    Ball FindSecondBallInPath(Ball firstHit, Vector2 trajEnd, Vector2 shotDir)
    {
        float hd = GameConstants.HitDetectDist;
        float hdSq = hd * hd;
        float maxTravel = GameConstants.BallRadius * 4f / 3f;

        Ball  candidate = null;
        float bestT = float.MaxValue;

        foreach (var b in gm.Balls)
        {
            if (b.id == firstHit.id) continue;

            Vector2 bc = b.cachedPos;
            Vector2 db = trajEnd - bc;
            // Ray-circle intersection: |trajEnd + t·shotDir − bc| = hd
            float bCoeff = 2f * Vector2.Dot(db, shotDir);
            float cCoeff = db.sqrMagnitude - hdSq;
            float disc   = bCoeff * bCoeff - 4f * cCoeff;
            if (disc < 0) continue;

            float t = (-bCoeff - Mathf.Sqrt(disc)) * 0.5f; // entry root
            if (t < 0) t = 0f;
            if (t > maxTravel) continue;

            if (t < bestT)
            {
                bestT = t;
                candidate = b;
            }
        }

        return candidate;
    }

    void ShowAimLine(Vector2 start, Vector2 dir)
    {
        // Aim line disabled — trajectory dots convey aim direction clearly enough.
        // The short white line overlapped with the shooter visuals.
        if (aimLine == null) return;
        aimLine.positionCount = 0;
    }

    void HideAimLine()
    {
        if (aimLine) aimLine.positionCount = 0;
    }

    void UpdateDisplay()
    {
        bool showCurrent = gm.ballsLeft > 0 || projectile != null;
        float br = GameConstants.BallRadius;

        // Current ball + breathing glow
        if (currentBallDisplay)
        {
            currentBallDisplay.gameObject.SetActive(showCurrent);
            if (showCurrent) currentBallDisplay.color = gm.currentColor;
        }
        if (breathingGlow)
        {
            breathingGlow.gameObject.SetActive(showCurrent);
            if (showCurrent)
            {
                // v21: shipPulse = 0.5 + 0.5 * sin(Date.now() * 0.004)
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
                float glowSize = (br + (0.06f + pulse * 0.02f) * GameConstants.WorldScale) * 2f;
                breathingGlow.transform.localScale = new Vector3(glowSize, glowSize, 1f);
                breathingGlow.color = new Color(1f, 1f, 1f, 0.12f + pulse * 0.12f);
            }
        }
        if (colorGlow)
        {
            colorGlow.gameObject.SetActive(showCurrent);
            if (showCurrent)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
                Color gc = gm.currentColor;
                gc.a = 0.15f + pulse * 0.1f;
                colorGlow.color = gc;
            }
        }

        // Next ball (half-transparent)
        if (nextBallDisplay)
        {
            bool showNext = gm.ballsLeft > 1;
            nextBallDisplay.gameObject.SetActive(showNext);
            if (showNext)
            {
                Color nc = gm.nextColor;
                nc.a = 0.5f;
                nextBallDisplay.color = nc;
            }
        }

        // Remaining ball count below next ball
        if (remainingCountTMP)
        {
            remainingCountTMP.gameObject.SetActive(showCurrent);
            if (showCurrent)
            {
                remainingCountTMP.text = gm.ballsLeft.ToString();
                // v21: ≤5 red flash, ≤10 orange, ≤15 yellow, else dim white
                if (gm.ballsLeft <= 5)
                {
                    float flash = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                    remainingCountTMP.color = new Color(0.973f, 0.318f, 0.286f, flash);
                }
                else if (gm.ballsLeft <= 10)
                    remainingCountTMP.color = new Color(0.937f, 0.624f, 0.153f);
                else if (gm.ballsLeft <= 15)
                    remainingCountTMP.color = new Color(1f, 0.902f, 0.427f);
                else
                    remainingCountTMP.color = new Color(1f, 1f, 1f, 0.45f);
            }
        }
    }
}
