using UnityEngine;
using System.Collections.Generic;

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

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        GenerateAllClips();
        CacheVibrator();
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
            Debug.Log($"[AudioManager] Vibrator cached (API {apiLevel}, vibrator={(vibrator != null)})");
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
    // Simple procedural audio — replace with real AudioClips when available.

    void GenerateAllClips()
    {
        clipShoot   = GenerateTone(0.08f, 880, 0.3f, ToneType.Sine, fadeOut: true);
        clipAttach  = GenerateTone(0.05f, 440, 0.2f, ToneType.Noise, fadeOut: true);
        clipMatch3  = GenerateChirp(0.15f, 600, 1200, 0.4f);
        clipMatch45 = GenerateChirp(0.25f, 400, 1600, 0.5f);
        clipCombo   = GenerateArpeggio(new[] { 523f, 659f, 784f, 1047f }, 0.08f, 0.4f);
        clipBHAbsorb = GenerateTone(0.2f, 120, 0.4f, ToneType.Sine, fadeOut: true);
        clipBounce  = GenerateTone(0.03f, 660, 0.15f, ToneType.Square, fadeOut: true);
        clipWin     = GenerateArpeggio(new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.12f, 0.5f);
        clipLose    = GenerateTone(0.4f, 200, 0.4f, ToneType.Sine, fadeOut: true, pitchDecay: 0.5f);
    }

    enum ToneType { Sine, Square, Noise }

    AudioClip GenerateTone(float duration, float freq, float amp, ToneType type,
                           bool fadeOut = false, float pitchDecay = 0f)
    {
        int sampleRate = 44100;
        int samples = (int)(duration * sampleRate);
        var clip = AudioClip.Create("synth", samples, 1, sampleRate, false);
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
                    val = Random.Range(-1f, 1f);
                    break;
            }
            float envelope = fadeOut ? (1f - (float)i / samples) : 1f;
            data[i] = val * amp * envelope;
        }
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateChirp(float duration, float startFreq, float endFreq, float amp)
    {
        int sampleRate = 44100;
        int samples = (int)(duration * sampleRate);
        var clip = AudioClip.Create("chirp", samples, 1, sampleRate, false);
        var data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float progress = (float)i / samples;
            float freq = Mathf.Lerp(startFreq, endFreq, progress);
            float envelope = 1f - progress;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * envelope;
        }
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateArpeggio(float[] freqs, float noteDuration, float amp)
    {
        int sampleRate = 44100;
        int samplesPerNote = (int)(noteDuration * sampleRate);
        int totalSamples = samplesPerNote * freqs.Length;
        var clip = AudioClip.Create("arpeggio", totalSamples, 1, sampleRate, false);
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
        clip.SetData(data, 0);
        return clip;
    }
}
