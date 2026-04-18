using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Singleton that manages all game sound effects.
/// Generates simple synth tones at runtime (can be replaced with AudioClips later).
/// Includes haptic feedback for mobile.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Range(0f, 1f)]
    public float volume = 0.7f;

    private AudioSource audioSource;

#if UNITY_ANDROID && !UNITY_EDITOR
    // Cached Android Vibrator to avoid per-call JNI lookup (expensive + GC)
    private AndroidJavaObject vibrator;
    private AndroidJavaClass vibrationEffectClass;
    private int apiLevel;
#endif

    // Cached synth clips (generated once at startup)
    private AudioClip clipShoot;
    private AudioClip clipAttach;
    private AudioClip clipMatch3;
    private AudioClip clipMatch45;
    private AudioClip clipCombo;
    private AudioClip clipBHAbsorb;
    private AudioClip clipBounce;
    private AudioClip clipWin;
    private AudioClip clipLose;

    async void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Vibrator cache is cheap (a few JNI calls) — keep synchronous
        CacheVibrator();

        // Procedural audio generation used to block the main thread for ~50-100ms
        // on slower devices (nine loops of Mathf.Sin over ~90k samples combined).
        // Run the pure-CPU sample math on a background thread; Play() methods
        // safely no-op while clips are null, so any early call during startup
        // (behind the splash screen) is simply silent rather than crashing.
        try
        {
            ClipSpec[] specs = await Task.Run(BuildAllClipData);

            // Back on main thread (Unity's UnitySynchronizationContext). Creating
            // the AudioClip and uploading its samples MUST happen here because
            // AudioClip.Create / SetData are Unity API and not thread-safe.
            if (this == null) return; // GameObject destroyed during await
            clipShoot    = BuildClip(specs[0]);
            clipAttach   = BuildClip(specs[1]);
            clipMatch3   = BuildClip(specs[2]);
            clipMatch45  = BuildClip(specs[3]);
            clipCombo    = BuildClip(specs[4]);
            clipBHAbsorb = BuildClip(specs[5]);
            clipBounce   = BuildClip(specs[6]);
            clipWin      = BuildClip(specs[7]);
            clipLose     = BuildClip(specs[8]);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AudioManager] Clip generation failed: {e}");
        }
    }

    void CacheVibrator()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                apiLevel = buildVersion.GetStatic<int>("SDK_INT");
            }
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            // API 26+ uses VibrationEffect; cache the class reference
            if (apiLevel >= 26)
            {
                vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
            }
            Dbg.Log($"[AudioManager] Vibrator cached (API {apiLevel}, vibrator={(vibrator != null)})");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AudioManager] Failed to cache vibrator: {e.Message}");
        }
#endif
    }

    // ===== PUBLIC PLAY METHODS =====

    public void PlayShoot()    => Play(clipShoot, 0.5f);
    public void PlayAttach()   => Play(clipAttach, 0.4f);
    public void PlayMatch3()   => Play(clipMatch3, 0.7f); // no haptic for 3-match
    public void PlayMatch45()  { Play(clipMatch45, 0.9f); Vibrate(100); }
    public void PlayCombo()    { Play(clipCombo, 0.8f); Vibrate(200); }
    public void PlayBHAbsorb() => Play(clipBHAbsorb, 0.6f);
    public void PlayBounce()   => Play(clipBounce, 0.25f);
    public void PlayWin()      => Play(clipWin, 0.8f);
    public void PlayLose()     => Play(clipLose, 0.6f);

    void Play(AudioClip clip, float volumeScale)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume * volumeScale);
    }

    // ===== HAPTIC FEEDBACK =====

    /// <summary>Trigger device vibration (Android only, ignored on editor/iOS).</summary>
    void Vibrate(int milliseconds)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // NOTE: Referencing Handheld.Vibrate() anywhere in the project signals Unity's
        // build pipeline to auto-inject <uses-permission android:name="android.permission.VIBRATE" />
        // into the generated AndroidManifest.xml. Without this reference the JNI Vibrator
        // service exists but will not actually buzz on most Android devices.
        if (vibrator == null)
        {
            // Fallback path (also ensures the permission reference is emitted)
            Handheld.Vibrate();
            return;
        }
        try
        {
            if (apiLevel >= 26 && vibrationEffectClass != null)
            {
                // API 26+: VibrationEffect.createOneShot(ms, DEFAULT_AMPLITUDE=-1)
                using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", (long)milliseconds, -1))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                // Legacy API
                vibrator.Call("vibrate", (long)milliseconds);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AudioManager] Vibrate failed: {e.Message}");
            // Fallback to built-in Unity API if JNI call fails
            Handheld.Vibrate();
        }
#endif
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (vibrator != null) { vibrator.Dispose(); vibrator = null; }
        if (vibrationEffectClass != null) { vibrationEffectClass.Dispose(); vibrationEffectClass = null; }
#endif
    }

    // ===== SYNTH CLIP GENERATION =====
    // Simple procedural audio. Sample arrays are generated on a background
    // ThreadPool thread (pure CPU math — Mathf.Sin/Lerp are thread-safe), then
    // uploaded to Unity's AudioClip on the main thread. This removes the startup
    // frame spike that used to happen when all nine clips were built in Awake.

    enum ToneType { Sine, Square, Noise }

    /// <summary>
    /// Per-clip specification built on the background thread. Holds only plain
    /// data — no Unity objects, safe to construct off the main thread.
    /// </summary>
    struct ClipSpec
    {
        public string name;
        public float[] data;
        public int sampleRate;
    }

    /// <summary>
    /// Main-thread-only: create the Unity AudioClip and upload pre-computed samples.
    /// </summary>
    static AudioClip BuildClip(ClipSpec s)
    {
        var clip = AudioClip.Create(s.name, s.data.Length, 1, s.sampleRate, false);
        clip.SetData(s.data, 0);
        return clip;
    }

    /// <summary>
    /// Background-thread entry point. Must NOT touch any Unity API — only pure math.
    /// Uses System.Random for noise (UnityEngine.Random is not thread-safe).
    /// </summary>
    static ClipSpec[] BuildAllClipData()
    {
        var rng = new System.Random();
        const int sr = 44100;
        return new[]
        {
            new ClipSpec { name = "shoot",    sampleRate = sr, data = BuildToneData(0.08f, 880f, 0.3f,  ToneType.Sine,   fadeOut: true,  pitchDecay: 0f,   rng: rng) },
            new ClipSpec { name = "attach",   sampleRate = sr, data = BuildToneData(0.05f, 440f, 0.2f,  ToneType.Noise,  fadeOut: true,  pitchDecay: 0f,   rng: rng) },
            new ClipSpec { name = "match3",   sampleRate = sr, data = BuildChirpData(0.15f, 600f, 1200f, 0.4f) },
            new ClipSpec { name = "match45",  sampleRate = sr, data = BuildChirpData(0.25f, 400f, 1600f, 0.5f) },
            new ClipSpec { name = "combo",    sampleRate = sr, data = BuildArpeggioData(new[] { 523f, 659f, 784f, 1047f }, 0.08f, 0.4f) },
            new ClipSpec { name = "bhAbsorb", sampleRate = sr, data = BuildToneData(0.2f,  120f, 0.4f,  ToneType.Sine,   fadeOut: true,  pitchDecay: 0f,   rng: rng) },
            new ClipSpec { name = "bounce",   sampleRate = sr, data = BuildToneData(0.03f, 660f, 0.15f, ToneType.Square, fadeOut: true,  pitchDecay: 0f,   rng: rng) },
            new ClipSpec { name = "win",      sampleRate = sr, data = BuildArpeggioData(new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.12f, 0.5f) },
            new ClipSpec { name = "lose",     sampleRate = sr, data = BuildToneData(0.4f,  200f, 0.4f,  ToneType.Sine,   fadeOut: true,  pitchDecay: 0.5f, rng: rng) },
        };
    }

    static float[] BuildToneData(float duration, float freq, float amp, ToneType type,
                                 bool fadeOut, float pitchDecay, System.Random rng)
    {
        const int sampleRate = 44100;
        int samples = (int)(duration * sampleRate);
        var data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float f = freq * (1f - pitchDecay * t / duration);
            float val = 0;
            switch (type)
            {
                case ToneType.Sine:
                    val = Mathf.Sin(2f * Mathf.PI * f * t);
                    break;
                case ToneType.Square:
                    val = Mathf.Sin(2f * Mathf.PI * f * t) > 0 ? 1f : -1f;
                    break;
                case ToneType.Noise:
                    // System.Random — UnityEngine.Random is not thread-safe
                    val = (float)(rng.NextDouble() * 2.0 - 1.0);
                    break;
            }
            float envelope = fadeOut ? (1f - (float)i / samples) : 1f;
            data[i] = val * amp * envelope;
        }
        return data;
    }

    static float[] BuildChirpData(float duration, float startFreq, float endFreq, float amp)
    {
        const int sampleRate = 44100;
        int samples = (int)(duration * sampleRate);
        var data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float progress = (float)i / samples;
            float freq = Mathf.Lerp(startFreq, endFreq, progress);
            float envelope = 1f - progress;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * envelope;
        }
        return data;
    }

    static float[] BuildArpeggioData(float[] freqs, float noteDuration, float amp)
    {
        const int sampleRate = 44100;
        int samplesPerNote = (int)(noteDuration * sampleRate);
        int totalSamples = samplesPerNote * freqs.Length;
        var data = new float[totalSamples];

        for (int n = 0; n < freqs.Length; n++)
        {
            for (int i = 0; i < samplesPerNote; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f - (float)i / samplesPerNote;
                int idx = n * samplesPerNote + i;
                data[idx] = Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * amp * envelope;
            }
        }
        return data;
    }
}
