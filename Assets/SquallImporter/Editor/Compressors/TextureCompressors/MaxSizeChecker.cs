using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class MaxSizeChecker : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> foundTexturePaths = new List<string>();
    private readonly HashSet<string> selectedPaths = new HashSet<string>();
    private Vector2 scrollPosition;
    
    private readonly int[] maxSizeOptions = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
    private int selectedCheckMaxSizeIndex = 4; // Default to 512
    private int checkMaxSize = 512;
    private int selectedTargetMaxSizeIndex = 4; // Default to 512
    private int targetMaxSize = 512;

    [MenuItem("Tools/SquallTools/Optimise/Texture Max Size Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<MaxSizeChecker>();
        window.titleContent = new GUIContent("Texture Max Size Checker");
        window.minSize = new Vector2(600, 450);
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
                FindTexturesWithLargeMaxSize();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Check Max Size >", GUILayout.Width(120));
            selectedCheckMaxSizeIndex = EditorGUILayout.Popup(selectedCheckMaxSizeIndex, maxSizeOptions.Select(s => s.ToString()).ToArray(), GUILayout.Width(100));
            checkMaxSize = maxSizeOptions[selectedCheckMaxSizeIndex];
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Target Max Size", GUILayout.Width(120));
            selectedTargetMaxSizeIndex = EditorGUILayout.Popup(selectedTargetMaxSizeIndex, maxSizeOptions.Select(s => s.ToString()).ToArray(), GUILayout.Width(100));
            targetMaxSize = maxSizeOptions[selectedTargetMaxSizeIndex];
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Found Textures", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {foundTexturePaths.Count}");
            
            // Select All / Deselect All buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All", GUILayout.Width(100)))
                {
                    selectedPaths.Clear();
                    foreach (var path in foundTexturePaths)
                    {
                        selectedPaths.Add(path);
                    }
                }
                if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
                {
                    selectedPaths.Clear();
                }
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(220));
            foreach (var path in foundTexturePaths)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isSelected = selectedPaths.Contains(path);
                    bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                    
                    if (newSelected != isSelected)
                    {
                        if (newSelected)
                        {
                            selectedPaths.Add(path);
                        }
                        else
                        {
                            selectedPaths.Remove(path);
                        }
                    }

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
        using (new EditorGUI.DisabledScope(selectedPaths.Count == 0))
        {
            EditorGUILayout.LabelField($"Selected: {selectedPaths.Count} texture(s)");
            if (GUILayout.Button($"Batch Set Max Size to {targetMaxSize}", GUILayout.Height(28)))
            {
                BatchSetMaxSize();
            }
        }
    }

    private void FindTexturesWithLargeMaxSize()
    {
        foundTexturePaths.Clear();
        selectedPaths.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : "Assets";
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Searching Textures", "Scanning folder...", 0f);
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            int totalCount = guids.Length;
            int currentIndex = 0;

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                float progress = (float)currentIndex / totalCount;
                string progressText = $"Checking: {path} ({currentIndex}/{totalCount})";
                EditorUtility.DisplayProgressBar("Searching Textures", progressText, progress);

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                // Get all platform settings and find the maximum maxTextureSize
                int maxMaxSize = 0;
                
                // Check default platform
                var defaultSettings = importer.GetDefaultPlatformTextureSettings();
                maxMaxSize = Math.Max(maxMaxSize, defaultSettings.maxTextureSize);

                // Check all platform overrides
                // We need to get all available platforms
                var allPlatforms = new[] { "Android", "iPhone", "Standalone", "WebGL", "tvOS", "PS4", "XboxOne", "Switch" };
                foreach (var platform in allPlatforms)
                {
                    try
                    {
                        var platformSettings = importer.GetPlatformTextureSettings(platform);
                        if (platformSettings.name == platform)
                        {
                            maxMaxSize = Math.Max(maxMaxSize, platformSettings.maxTextureSize);
                        }
                    }
                    catch
                    {
                        // Platform not available, skip
                    }
                }

                // If the maximum maxSize is greater than the threshold, add to list
                if (maxMaxSize > checkMaxSize)
                {
                    foundTexturePaths.Add(path);
                }
            }

            EditorUtility.ClearProgressBar();

            if (foundTexturePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Search Complete", $"No textures with maxSize > {checkMaxSize} found in:\n{folderPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Search Complete", $"Found {foundTexturePaths.Count} texture(s) with maxSize > {checkMaxSize} in:\n{folderPath}", "OK");
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

    private void BatchSetMaxSize()
    {
        if (selectedPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select textures to process.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will set maxSize to {targetMaxSize} for {selectedPaths.Count} texture(s) on all platforms. Continue?", "Yes", "No"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            int errors = 0;
            int index = 0;

            foreach (var path in selectedPaths)
            {
                index++;
                EditorUtility.DisplayProgressBar("Setting Max Size", path, (float)index / selectedPaths.Count);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    bool changed = false;

                    // Set default platform maxSize (always set, regardless of current value)
                    var defaultSettings = importer.GetDefaultPlatformTextureSettings();
                    if (defaultSettings.maxTextureSize != targetMaxSize)
                    {
                        defaultSettings.maxTextureSize = targetMaxSize;
                        importer.SetPlatformTextureSettings(defaultSettings);
                        changed = true;
                    }

                    // Set all platform overrides
                    var allPlatforms = new[] { "Android", "iPhone", "Standalone", "WebGL", "tvOS", "PS4", "XboxOne", "Switch" };
                    foreach (var platform in allPlatforms)
                    {
                        try
                        {
                            var platformSettings = importer.GetPlatformTextureSettings(platform);
                            // Only set if the platform override exists and maxSize is different
                            if (platformSettings.name == platform)
                            {
                                // Check if this platform has an override (overridden flag)
                                var overriddenValue = GetOverriddenValue(platformSettings);
                                bool isOverridden = overriddenValue.HasValue && overriddenValue.Value;
                                
                                // If platform is overridden and maxSize is different, update it
                                if (isOverridden && platformSettings.maxTextureSize != targetMaxSize)
                                {
                                    platformSettings.maxTextureSize = targetMaxSize;
                                    importer.SetPlatformTextureSettings(platformSettings);
                                    changed = true;
                                }
                            }
                        }
                        catch
                        {
                            // Platform not available, skip
                        }
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modified++;
                    }
                    else
                    {
                        // Even if nothing changed, we should still reimport to ensure settings are applied
                        // But only if we actually tried to set something
                        Debug.Log($"No changes needed for {path} (maxSize already set correctly)");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error setting maxSize for {path}: {e.Message}");
                    errors++;
                }
            }

            EditorUtility.ClearProgressBar();

            if (errors > 0)
            {
                EditorUtility.DisplayDialog("Complete with Errors", $"Set maxSize for {modified} texture(s).\n{errors} error(s) occurred. Check Console for details.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Complete", $"Successfully set maxSize to {targetMaxSize} for {modified} texture(s).", "OK");
            }

            // Refresh the list
            FindTexturesWithLargeMaxSize();
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"MaxSizeChecker error: {e}");
            EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }
}
