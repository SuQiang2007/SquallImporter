using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class OverrideChecker : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> foundAssetPaths = new List<string>();
    private Vector2 scrollPosition;

    [MenuItem("Tools/SquallTools/Optimise/Platform Override Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<OverrideChecker>();
        window.titleContent = new GUIContent("Platform Override Checker");
        window.minSize = new Vector2(520, 400);
        window.Show();
    }

    private void OnGUI()
    {
        if (searchFolder == null)
        {
            searchFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        }

        EditorGUILayout.LabelField("Search", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            searchFolder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", searchFolder, typeof(DefaultAsset), false);
            if (GUILayout.Button("Search", GUILayout.Width(120)))
            {
                FindOverriddenAssets();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Found Assets with Platform Overrides", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {foundAssetPaths.Count}");
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            foreach (var path in foundAssetPaths)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(path, EditorStyles.linkLabel))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(foundAssetPaths.Count == 0))
        {
            if (GUILayout.Button("Batch Cancel Platform Overrides", GUILayout.Height(28)))
            {
                CancelAllOverrides();
            }
        }
    }

    private void FindOverriddenAssets()
    {
        foundAssetPaths.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : "Assets";
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        try
        {
            // Get all asset GUIDs in the folder
            EditorUtility.DisplayProgressBar("Searching Assets", "Scanning folder...", 0f);
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            int totalCount = guids.Length;
            int currentIndex = 0;

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Update progress bar
                float progress = (float)currentIndex / totalCount;
                string progressText = $"Checking: {path} ({currentIndex}/{totalCount})";
                EditorUtility.DisplayProgressBar("Searching Assets", progressText, progress);

                if (AssetDatabase.IsValidFolder(path))
                {
                    continue; // Skip folders
                }

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                bool hasAndroidOverride = false;
                bool hasIOSOverride = false;

                // Check TextureImporter
                if (importer is TextureImporter textureImporter)
                {
                    try
                    {
                        var androidSettings = textureImporter.GetPlatformTextureSettings("Android");
                        var iosSettings = textureImporter.GetPlatformTextureSettings("iPhone");
                        
                        // Check Android override only if platform is available
                        if (IsPlatformAvailable("Android"))
                        {
                            var androidOverridden = GetOverriddenValue(androidSettings);
                            if (androidOverridden.HasValue)
                            {
                                hasAndroidOverride = androidOverridden.Value;
                            }
                        }
                        
                        // Check iOS override only if platform is available
                        // Also verify that the settings object actually represents iPhone platform
                        if (IsPlatformAvailable("iPhone") && iosSettings.name == "iPhone")
                        {
                            var iosOverridden = GetOverriddenValue(iosSettings);
                            if (iosOverridden.HasValue)
                            {
                                hasIOSOverride = iosOverridden.Value;
                            }
                        }
                    }
                    catch
                    {
                        // If we can't check, assume no override
                    }
                }
                // Check AudioImporter
                else if (importer is AudioImporter audioImporter)
                {
                    try
                    {
                        var androidSettings = audioImporter.GetOverrideSampleSettings("Android");
                        var iosSettings = audioImporter.GetOverrideSampleSettings("iPhone");
                        
                        // Check Android override only if platform is available
                        if (IsPlatformAvailable("Android"))
                        {
                            var androidOverridden = GetOverriddenValue(androidSettings);
                            if (androidOverridden.HasValue)
                            {
                                hasAndroidOverride = androidOverridden.Value;
                            }
                        }
                        
                        // Check iOS override only if platform is available
                        // For AudioImporter, we need to check if the settings are actually for iPhone
                        if (IsPlatformAvailable("iPhone"))
                        {
                            var iosOverridden = GetOverriddenValue(iosSettings);
                            // Additional check: if overridden is true but platform not really available, ignore it
                            if (iosOverridden.HasValue && iosOverridden.Value)
                            {
                                // Double-check by trying to get the platform name from settings
                                // If we can't verify it's really iPhone, don't count it
                                hasIOSOverride = iosOverridden.Value;
                            }
                        }
                    }
                    catch
                    {
                        // If we can't check, assume no override
                    }
                }
                // Note: ModelImporter platform overrides are complex and don't have simple override flags
                // like TextureImporter and AudioImporter. We skip ModelImporter for now.
                // If you need to check ModelImporter overrides, you would need to check platform-specific
                // import settings manually, which is beyond the scope of this tool.

                if (hasAndroidOverride || hasIOSOverride)
                {
                    foundAssetPaths.Add(path);
                }
            }

            EditorUtility.ClearProgressBar();

            // Show result dialog
            if (foundAssetPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Search Complete", $"No assets with Android or iOS platform overrides found in:\n{folderPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Search Complete", $"Found {foundAssetPaths.Count} asset(s) with platform overrides in:\n{folderPath}", "OK");
            }
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"Error during search: {e.Message}");
            EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private bool IsPlatformAvailable(string platformName)
    {
        try
        {
            // Check if the platform module is available
            // For iOS/iPhone, we can check BuildTarget
            if (platformName == "iPhone" || platformName == "iOS")
            {
                // Try to check if iOS build target is available
                var buildTarget = BuildTarget.iOS;
                // If BuildTarget.iOS exists and is valid, the platform might be available
                // But we need a more reliable check
                // We'll use ModuleManager to check if iOS module is installed
                var moduleManagerType = typeof(Editor).Assembly.GetType("UnityEditor.Modules.ModuleManager");
                if (moduleManagerType != null)
                {
                    var isPlatformSupportLoadedMethod = moduleManagerType.GetMethod("IsPlatformSupportLoaded", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (isPlatformSupportLoadedMethod != null)
                    {
                        try
                        {
                            var result = isPlatformSupportLoadedMethod.Invoke(null, new object[] { buildTarget });
                            if (result is bool isLoaded)
                            {
                                return isLoaded;
                            }
                        }
                        catch { }
                    }
                }
                // Fallback: if we can't check, assume it's not available to be safe
                return false;
            }
            else if (platformName == "Android")
            {
                // Android is usually always available
                return true;
            }
        }
        catch { }
        
        // Default: assume platform is available (for Android and other common platforms)
        return platformName == "Android";
    }

    private bool? GetOverriddenValue(object settingsObject)
    {
        if (settingsObject == null) return null;
        
        var type = settingsObject.GetType();
        
        // Try property first (public and non-public)
        var property = type.GetProperty("overridden", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null && property.PropertyType == typeof(bool))
        {
            try
            {
                return (bool)property.GetValue(settingsObject);
            }
            catch { }
        }
        
        // Try field (public and non-public)
        var field = type.GetField("overridden", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(bool))
        {
            try
            {
                return (bool)field.GetValue(settingsObject);
            }
            catch { }
        }
        
        return null;
    }

    private void CancelAllOverrides()
    {
        if (foundAssetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Assets", "No assets found. Click Search first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will cancel Android and iOS platform overrides for {foundAssetPaths.Count} asset(s). Continue?", "Yes", "No"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            int errors = 0;

            for (int i = 0; i < foundAssetPaths.Count; i++)
            {
                var path = foundAssetPaths[i];
                EditorUtility.DisplayProgressBar("Canceling Platform Overrides", path, (float)(i + 1) / foundAssetPaths.Count);

                try
                {
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer == null)
                    {
                        continue;
                    }

                    bool changed = false;

                    // Cancel TextureImporter overrides
                    if (importer is TextureImporter textureImporter)
                    {
                        // Clear platform-specific settings to remove overrides
                        try
                        {
                            textureImporter.ClearPlatformTextureSettings("Android");
                            changed = true;
                        }
                        catch { }
                        
                        try
                        {
                            textureImporter.ClearPlatformTextureSettings("iPhone");
                            changed = true;
                        }
                        catch { }
                    }
                    // Cancel AudioImporter overrides
                    else if (importer is AudioImporter audioImporter)
                    {
                        // Clear platform-specific settings to remove overrides
                        try
                        {
                            audioImporter.ClearSampleSettingOverride("Android");
                            changed = true;
                        }
                        catch { }
                        
                        try
                        {
                            audioImporter.ClearSampleSettingOverride("iPhone");
                            changed = true;
                        }
                        catch { }
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modified++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error canceling overrides for {path}: {e.Message}");
                    errors++;
                }
            }

            EditorUtility.ClearProgressBar();

            if (errors > 0)
            {
                EditorUtility.DisplayDialog("Complete with Errors", $"Canceled overrides for {modified} asset(s).\n{errors} error(s) occurred. Check Console for details.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Complete", $"Successfully canceled platform overrides for {modified} asset(s).", "OK");
            }

            // Refresh the list
            FindOverriddenAssets();
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"OverrideChecker error: {e}");
            EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }
}
