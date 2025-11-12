using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class MaxSizeChecker : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<TextureListItem> foundTextures = new List<TextureListItem>();
    private readonly HashSet<string> selectedPaths = new HashSet<string>();
    private readonly List<DefaultAsset> excludedFolders = new List<DefaultAsset>();
    private Vector2 scrollPosition;
    
    // Virtual scrolling optimization
    private const float ITEM_HEIGHT = 20f;
    private const int VISIBLE_ITEM_COUNT = 50; // Number of items to render at once
    
    private readonly int[] maxSizeOptions = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
    private int selectedCheckMaxSizeIndex = 4; // Default to 512
    private int checkMaxSize = 512;
    private int selectedTargetMaxSizeIndex = 4; // Default to 512
    private int targetMaxSize = 512;
    
    private class TextureListItem
    {
        public string Path;
        public int MaxDetectedSize;
    }

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
        EditorGUILayout.LabelField("Excluded Folders", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            for (int i = 0; i < excludedFolders.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    excludedFolders[i] = (DefaultAsset)EditorGUILayout.ObjectField($"Exclude {i + 1}", excludedFolders[i], typeof(DefaultAsset), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        excludedFolders.RemoveAt(i);
                        i--;
                        continue;
                    }
                }

                // Optional validation message
                var excludedPath = excludedFolders.ElementAtOrDefault(i) != null ? AssetDatabase.GetAssetPath(excludedFolders[i]) : string.Empty;
                if (!string.IsNullOrEmpty(excludedPath) && !AssetDatabase.IsValidFolder(excludedPath))
                {
                    EditorGUILayout.HelpBox("Not a valid folder. Select a folder from the Project window.", MessageType.Warning);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Exclusion", GUILayout.Width(140)))
                {
                    excludedFolders.Add(null);
                }
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
            EditorGUILayout.LabelField($"Count: {foundTextures.Count}");
            
            // Select All / Deselect All buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All", GUILayout.Width(100)))
                {
                    selectedPaths.Clear();
                    foreach (var item in foundTextures)
                    {
                        selectedPaths.Add(item.Path);
                    }
                }
                if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
                {
                    selectedPaths.Clear();
                }
            }

            // True virtual scrolling: only render visible items using GUI
            float scrollViewHeight = 220f;
            Rect scrollViewRect = GUILayoutUtility.GetRect(0, scrollViewHeight, GUILayout.ExpandWidth(true));
            
            // Calculate total content height
            float totalContentHeight = foundTextures.Count * ITEM_HEIGHT;
            
            // Begin scroll view
            scrollPosition = GUI.BeginScrollView(scrollViewRect, scrollPosition, new Rect(0, 0, scrollViewRect.width - 20, totalContentHeight));
            
            if (foundTextures.Count > 0)
            {
                // Calculate visible range
                int startIndex = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / ITEM_HEIGHT) - 5);
                int endIndex = Mathf.Min(foundTextures.Count, Mathf.CeilToInt((scrollPosition.y + scrollViewHeight) / ITEM_HEIGHT) + 5);
                
                // Draw only visible items
                for (int i = startIndex; i < endIndex; i++)
                {
                    var item = foundTextures[i];
                    var path = item.Path;
                    float yPos = i * ITEM_HEIGHT;
                    
                    // Only draw if in visible area
                    if (yPos + ITEM_HEIGHT >= scrollPosition.y && yPos <= scrollPosition.y + scrollViewHeight)
                    {
                        Rect itemRect = new Rect(0, yPos, scrollViewRect.width - 20, ITEM_HEIGHT);
                        
                        // Toggle checkbox
                        Rect toggleRect = new Rect(itemRect.x + 2, itemRect.y + 2, 20, ITEM_HEIGHT - 4);
                        bool isSelected = selectedPaths.Contains(path);
                        bool newSelected = GUI.Toggle(toggleRect, isSelected, "");
                        
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
                        
                        // Path button
                        float pingWidth = 50f;
                        float maxLabelWidth = 90f;
                        float pathWidth = Mathf.Max(50f, itemRect.width - toggleRect.width - pingWidth - maxLabelWidth - 24f);
                        Rect pathRect = new Rect(toggleRect.xMax + 5, itemRect.y, pathWidth, ITEM_HEIGHT);
                        if (GUI.Button(pathRect, path, EditorStyles.linkLabel))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null)
                            {
                                Selection.activeObject = asset;
                                EditorGUIUtility.PingObject(asset);
                            }
                        }
                        
                        Rect maxLabelRect = new Rect(pathRect.xMax + 5, itemRect.y, maxLabelWidth, ITEM_HEIGHT);
                        GUI.Label(maxLabelRect, $"Max: {item.MaxDetectedSize}", EditorStyles.miniLabel);
                        
                        // Ping button
                        Rect pingRect = new Rect(maxLabelRect.xMax + 5, itemRect.y, pingWidth, ITEM_HEIGHT);
                        if (GUI.Button(pingRect, "Ping"))
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
            }
            
            GUI.EndScrollView();
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
        foundTextures.Clear();
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

            // Prepare excluded folder paths
            var excludedPaths = new List<string>();
            foreach (var excluded in excludedFolders)
            {
                if (excluded == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(excluded);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    var normalized = path.Replace("\\", "/").TrimEnd('/');
                    excludedPaths.Add(normalized);
                }
            }

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // Skip excluded folders
                var normalizedPath = path.Replace("\\", "/");
                bool isExcluded = false;
                foreach (var excludedPath in excludedPaths)
                {
                    if (normalizedPath.Equals(excludedPath, StringComparison.OrdinalIgnoreCase) ||
                        normalizedPath.StartsWith(excludedPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        isExcluded = true;
                        break;
                    }
                }

                if (isExcluded)
                {
                    continue;
                }
                
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
                    foundTextures.Add(new TextureListItem
                    {
                        Path = path,
                        MaxDetectedSize = maxMaxSize
                    });
                }
            }

            EditorUtility.ClearProgressBar();

            if (foundTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("Search Complete", $"No textures with maxSize > {checkMaxSize} found in:\n{folderPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Search Complete", $"Found {foundTextures.Count} texture(s) with maxSize > {checkMaxSize} in:\n{folderPath}", "OK");
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
