using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility to configure Android build settings.
/// Run from Unity menu: Tools > Gravity Match > Setup Android Build.
/// </summary>
public static class AndroidBuildSetup
{
    [MenuItem("Tools/Gravity Match/Setup Android Build")]
    public static void SetupAndroidBuild()
    {
        // Company and product
        PlayerSettings.companyName = "Lyta Studio";
        PlayerSettings.productName = "Gravity Match";

        // Android package
        PlayerSettings.SetApplicationIdentifier(
            UnityEditor.Build.NamedBuildTarget.Android, "com.lytastudio.gravitymatch");

        // API levels
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24; // Android 7.0
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34; // Android 14

        // Scripting backend: IL2CPP for performance
        PlayerSettings.SetScriptingBackend(
            UnityEditor.Build.NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP);

        // Target ARM64 only (modern devices)
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Install location
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

        // Splash screen: disable Unity logo
        PlayerSettings.SplashScreen.showUnityLogo = false;

        // Screen orientation: portrait only
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        // Icon setup (after running Generate App Assets)
        string iconPath = "Assets/Icons/app_icon.png";
        var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        if (iconTex != null)
        {
            // Ensure texture is readable and set as icon
            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            // Set as default icon
            var icons = new Texture2D[] { iconTex };
            var iconSizes = new int[] { 512 };
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, icons);
            Debug.Log("[AndroidBuildSetup] App icon set.");
        }
        else
        {
            Debug.LogWarning($"[AndroidBuildSetup] Icon not found at {iconPath}. Run 'Tools > Gravity Match > Generate App Assets' first.");
        }

        Debug.Log("[AndroidBuildSetup] Android build settings configured successfully.");
        Debug.Log("  Company: Lyta Studio");
        Debug.Log("  Package: com.lytastudio.gravitymatch");
        Debug.Log("  Min API: 24 (Android 7.0)");
        Debug.Log("  Target API: 34 (Android 14)");
        Debug.Log("  Backend: IL2CPP, ARM64");
    }
}
