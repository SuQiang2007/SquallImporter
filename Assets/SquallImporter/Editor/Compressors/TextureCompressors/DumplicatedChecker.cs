using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public class DumplicatedChecker : EditorWindow
{
    private DefaultAsset searchFolder;
    private readonly List<DuplicateGroup> duplicateGroups = new List<DuplicateGroup>();
    private Vector2 scrollPosition;

    private class DuplicateGroup
    {
        public string Hash;
        public List<string> Paths;
    }

    [MenuItem("Tools/SquallTools/Optimise/Duplicated Texture Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<DumplicatedChecker>();
        window.titleContent = new GUIContent("Duplicated Checker");
        window.minSize = new Vector2(540, 360);
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
                FindDuplicatedTextures();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Duplicated Textures", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            int duplicateCount = duplicateGroups.Sum(g => Mathf.Max(0, g.Paths.Count - 1));
            EditorGUILayout.LabelField($"Groups: {duplicateGroups.Count}   Duplicates: {duplicateCount}");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(260));
            if (duplicateGroups.Count == 0)
            {
                GUILayout.Label("No duplicated textures found.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < duplicateGroups.Count; i++)
                {
                    var group = duplicateGroups[i];
                    EditorGUILayout.LabelField($"Group {i + 1} (Count: {group.Paths.Count})", EditorStyles.boldLabel);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        int groupId = i + 1;
                        foreach (var path in group.Paths)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Label($"#{groupId}", GUILayout.Width(40));

                                if (GUILayout.Button(path, EditorStyles.linkLabel))
                                {
                                    PingAsset(path);
                                }

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                                {
                                    PingAsset(path);
                                }
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(duplicateGroups.Count == 0))
        {
            if (GUILayout.Button("Batch Replace Duplicates", GUILayout.Height(28)))
            {
                BatchReplaceDuplicates();
            }
        }
    }

    private void PingAsset(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }

    private void FindDuplicatedTextures()
    {
        duplicateGroups.Clear();

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
            int skipped = 0;

            var hashToPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);

                float progress = totalCount == 0 ? 1f : (float)currentIndex / totalCount;
                string progressText = $"Checking: {path} ({currentIndex}/{totalCount})";
                EditorUtility.DisplayProgressBar("Searching Textures", progressText, progress);

                try
                {
                    if (!TryComputeHash(path, out var hash))
                    {
                        skipped++;
                        continue;
                    }

                    if (!hashToPaths.TryGetValue(hash, out var list))
                    {
                        list = new List<string>();
                        hashToPaths[hash] = list;
                    }
                    list.Add(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"DumplicatedChecker: Unable to process {path}. {e.Message}");
                    skipped++;
                }
            }

            foreach (var pair in hashToPaths.Where(p => p.Value.Count > 1))
            {
                duplicateGroups.Add(new DuplicateGroup
                {
                    Hash = pair.Key,
                    Paths = pair.Value.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            duplicateGroups.Sort((a, b) => string.CompareOrdinal(a.Paths[0], b.Paths[0]));

            EditorUtility.ClearProgressBar();

            string message = duplicateGroups.Count == 0
                ? $"No duplicated textures found in:\n{folderPath}"
                : $"Found {duplicateGroups.Count} group(s) of duplicated textures in:\n{folderPath}";

            if (skipped > 0)
            {
                message += $"\n\nSkipped {skipped} texture(s) that could not be processed.";
            }

            EditorUtility.DisplayDialog("Search Complete", message, "OK");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"DumplicatedChecker error: {e}");
            EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private void BatchReplaceDuplicates()
    {
        var groupsToProcess = duplicateGroups.Where(g => g.Paths != null && g.Paths.Count > 1).ToList();
        if (groupsToProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("No Duplicates", "No duplicate texture groups available for processing.", "OK");
            return;
        }

        int duplicateCount = groupsToProcess.Sum(g => g.Paths.Count - 1);
        if (duplicateCount == 0)
        {
            EditorUtility.DisplayDialog("No Duplicates", "No duplicate texture groups available for processing.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will replace references and delete {duplicateCount} duplicate texture(s). Continue?", "Yes", "No"))
        {
            return;
        }

        string projectRoot = GetProjectRoot();
        var allAssetPaths = CollectAllAssetPaths();

        int processedDuplicates = 0;
        int replacedReferences = 0;
        int deletedTextures = 0;
        int errors = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var group in groupsToProcess)
            {
                string primaryPath = group.Paths[0];
                string primaryGuid = AssetDatabase.AssetPathToGUID(primaryPath);

                for (int i = 1; i < group.Paths.Count; i++)
                {
                    string duplicatePath = group.Paths[i];
                    string duplicateGuid = AssetDatabase.AssetPathToGUID(duplicatePath);

                    float baseProgress = duplicateCount == 0 ? 1f : (float)processedDuplicates / duplicateCount;
                    EditorUtility.DisplayProgressBar("Processing Duplicate Textures", $"Processing {duplicatePath}", Mathf.Clamp01(baseProgress));

                    var referencingAssets = FindAssetsReferencingGuid(allAssetPaths, duplicateGuid, duplicatePath, primaryPath, projectRoot);

                    for (int r = 0; r < referencingAssets.Count; r++)
                    {
                        var assetPath = referencingAssets[r];
                        float progress = duplicateCount == 0 ? 1f : (processedDuplicates + (float)r / Math.Max(1, referencingAssets.Count)) / duplicateCount;
                        EditorUtility.DisplayProgressBar("Processing Duplicate Textures", $"Updating references in {assetPath}", Mathf.Clamp01(progress));

                        if (TryReplaceGuidInAsset(projectRoot, assetPath, duplicateGuid, primaryGuid, out bool changed))
                        {
                            if (changed)
                            {
                                replacedReferences++;
                                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                            }
                        }
                        else
                        {
                            errors++;
                        }
                    }

                    processedDuplicates++;
                    EditorUtility.DisplayProgressBar("Processing Duplicate Textures", $"Deleting {duplicatePath}", Mathf.Clamp01(duplicateCount == 0 ? 1f : (float)processedDuplicates / duplicateCount));

                    if (AssetDatabase.DeleteAsset(duplicatePath))
                    {
                        deletedTextures++;
                    }
                    else
                    {
                        Debug.LogError($"DumplicatedChecker: Failed to delete {duplicatePath}");
                        errors++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            errors++;
            Debug.LogError($"DumplicatedChecker batch error: {e}");
            EditorUtility.DisplayDialog("Batch Error", $"An error occurred during processing:\n{e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog(
            "Batch Complete",
            $"Replaced references: {replacedReferences}\nDeleted textures: {deletedTextures}\nErrors: {errors}",
            "OK");

        FindDuplicatedTextures();
    }

    private string GetProjectRoot()
    {
        return Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
    }

    private List<string> CollectAllAssetPaths()
    {
        var guids = AssetDatabase.FindAssets(string.Empty);
        var assetPaths = new List<string>(guids.Length);

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                continue;
            }

            assetPaths.Add(path);
        }

        return assetPaths;
    }

    private List<string> FindAssetsReferencingGuid(List<string> allAssetPaths, string targetGuid, string duplicatePath, string primaryPath, string projectRoot)
    {
        var results = new List<string>();

        foreach (var assetPath in allAssetPaths)
        {
            if (string.Equals(assetPath, duplicatePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = GetFullPath(projectRoot, assetPath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (IsBinaryFile(fullPath))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"DumplicatedChecker: Unable to read {assetPath}. {e.Message}");
                continue;
            }

            if (text.IndexOf(targetGuid, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(assetPath);
            }
        }

        return results;
    }

    private bool TryReplaceGuidInAsset(string projectRoot, string assetPath, string oldGuid, string newGuid, out bool changed)
    {
        changed = false;
        var fullPath = GetFullPath(projectRoot, assetPath);

        if (!File.Exists(fullPath))
        {
            return false;
        }

        if (IsBinaryFile(fullPath))
        {
            return true;
        }

        try
        {
            var text = File.ReadAllText(fullPath);
            if (text.IndexOf(oldGuid, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            var updatedText = text.Replace(oldGuid, newGuid);
            if (!string.Equals(updatedText, text, StringComparison.Ordinal))
            {
                File.WriteAllText(fullPath, updatedText);
                changed = true;
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"DumplicatedChecker: Failed to update {assetPath}. {e.Message}");
            return false;
        }
    }

    private string GetFullPath(string projectRoot, string assetPath)
    {
        var normalizedPath = assetPath.Replace("/", Path.DirectorySeparatorChar.ToString());
        return Path.Combine(projectRoot, normalizedPath);
    }

    private bool IsBinaryFile(string fullPath)
    {
        try
        {
            const int sampleSize = 512;
            using (var stream = File.OpenRead(fullPath))
            {
                int length = (int)Math.Min(sampleSize, stream.Length);
                if (length <= 0)
                {
                    return false;
                }

                var buffer = new byte[length];
                stream.Read(buffer, 0, length);
                for (int i = 0; i < length; i++)
                {
                    if (buffer[i] == 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private bool TryComputeHash(string assetPath, out string hash)
    {
        hash = string.Empty;

        try
        {
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string normalizedPath = assetPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            string fullPath = Path.Combine(projectRoot, normalizedPath);

            if (!File.Exists(fullPath))
            {
                return false;
            }

            byte[] fileData = File.ReadAllBytes(fullPath);
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(fileData);
                hash = BitConverter.ToString(hashBytes).Replace("-", string.Empty);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

}