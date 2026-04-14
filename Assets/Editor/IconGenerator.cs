using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Editor utility to generate app icon and splash screen images.
/// Run from Unity menu: Tools > Gravity Match > Generate App Assets.
/// </summary>
public static class IconGenerator
{
    [MenuItem("Tools/Gravity Match/Generate App Assets")]
    public static void GenerateAll()
    {
        GenerateAppIcon();
        GenerateSplashScreen();
        AssetDatabase.Refresh();
        Debug.Log("[IconGenerator] App assets generated successfully.");
    }

    static void GenerateAppIcon()
    {
        int size = 512;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // Background color #0B0D14
        Color bg = new Color(0.043f, 0.051f, 0.078f, 1f);
        Color purple = new Color(0.55f, 0.15f, 0.85f, 1f);
        Color purpleDark = new Color(0.35f, 0.08f, 0.55f, 1f);
        Color red = new Color(0.886f, 0.294f, 0.290f, 1f);
        Color blue = new Color(0.216f, 0.541f, 0.867f, 1f);
        Color green = new Color(0.114f, 0.620f, 0.459f, 1f);
        Color amber = new Color(0.937f, 0.624f, 0.153f, 1f);

        // Fill background
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        // Draw purple ring (black hole) at center
        float cx = size / 2f, cy = size / 2f;
        float ringOuterR = 160f, ringInnerR = 120f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist >= ringInnerR && dist <= ringOuterR)
                {
                    // Gradient: brighter at center of ring band
                    float mid = (ringInnerR + ringOuterR) / 2f;
                    float t = 1f - Mathf.Abs(dist - mid) / ((ringOuterR - ringInnerR) / 2f);
                    pixels[y * size + x] = Color.Lerp(purpleDark, purple, t * t);
                }
                // Dark center
                else if (dist < ringInnerR)
                {
                    float t = dist / ringInnerR;
                    pixels[y * size + x] = Color.Lerp(bg, new Color(0.06f, 0.06f, 0.10f), t * 0.3f);
                }
            }
        }

        // Draw small colored balls around the ring
        DrawBall(pixels, size, cx - 100, cy + 130, 30, red);
        DrawBall(pixels, size, cx + 120, cy + 90, 28, blue);
        DrawBall(pixels, size, cx - 130, cy - 80, 26, green);
        DrawBall(pixels, size, cx + 80, cy - 130, 32, amber);

        // Small ball near BH center
        DrawBall(pixels, size, cx + 20, cy - 30, 18, red);

        tex.SetPixels(pixels);
        tex.Apply();

        string path = "Assets/Icons/app_icon.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"[IconGenerator] App icon saved to {path}");
    }

    static void GenerateSplashScreen()
    {
        int width = 1080, height = 1920;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        Color bg = new Color(0.043f, 0.051f, 0.078f, 1f);
        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        // Draw a small purple ring in the center
        float cx = width / 2f, cy = height / 2f + 100;
        float ringOuterR = 80f, ringInnerR = 55f;
        Color purple = new Color(0.55f, 0.15f, 0.85f, 0.6f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist >= ringInnerR && dist <= ringOuterR)
                {
                    float mid = (ringInnerR + ringOuterR) / 2f;
                    float t = 1f - Mathf.Abs(dist - mid) / ((ringOuterR - ringInnerR) / 2f);
                    pixels[y * width + x] = Color.Lerp(bg, purple, t * t);
                }
            }
        }

        // Note: Text rendering on Texture2D is not practical without a font texture.
        // The splash text ("Lyta Studio", "Gravity Match", "Event Horizon") should be
        // rendered using Unity's Splash Screen settings or a splash scene with TMP text.

        tex.SetPixels(pixels);
        tex.Apply();

        string dir = "Assets/Icons";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = $"{dir}/splash_bg.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"[IconGenerator] Splash background saved to {path}");
    }

    static void DrawBall(Color[] pixels, int texSize, float bx, float by, float radius, Color color)
    {
        int xMin = Mathf.Max(0, (int)(bx - radius - 2));
        int xMax = Mathf.Min(texSize - 1, (int)(bx + radius + 2));
        int yMin = Mathf.Max(0, (int)(by - radius - 2));
        int yMax = Mathf.Min(texSize - 1, (int)(by + radius + 2));

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = x - bx, dy = y - by;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= radius)
                {
                    // Smooth edge (anti-alias)
                    float edge = Mathf.Clamp01(1f - (dist - radius + 1.5f) / 1.5f);
                    Color existing = pixels[y * texSize + x];
                    pixels[y * texSize + x] = Color.Lerp(existing, color, edge);
                }
            }
        }
    }
}
