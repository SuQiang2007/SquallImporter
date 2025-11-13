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
    private DefaultAsset referenceSearchFolder;
    private readonly List<DuplicateGroup> duplicateGroups = new List<DuplicateGroup>();
    private Vector2 scrollPosition;
    private Vector2 fileTypeScrollPosition;
    private readonly Dictionary<string, bool> fileTypeFilters = new Dictionary<string, bool>();
    
    private static readonly string[] CommonFileExtensions = new[]
    {
        ".prefab",
        ".mat",
        ".asset",
        ".unity",
        ".controller",
        ".anim",
        ".cs",
        ".shader",
        ".compute",
        ".hlsl",
        ".cginc",
        ".cg",
        ".glslinc",
        ".json",
        ".txt",
        ".xml",
        ".yaml",
        ".yml"
    };

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

    private void OnEnable()
    {
        // Initialize file type filters with default selections
        if (fileTypeFilters.Count == 0)
        {
            // Default: enable common Unity resource file types
            foreach (var ext in CommonFileExtensions)
            {
                fileTypeFilters[ext] = true;
            }
        }
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

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Batch Replace Settings", EditorStyles.boldLabel);
        referenceSearchFolder = (DefaultAsset)EditorGUILayout.ObjectField("Reference Search Range", referenceSearchFolder, typeof(DefaultAsset), false);
        if (referenceSearchFolder == null)
        {
            EditorGUILayout.HelpBox("If not set, will search entire project. Set a folder to limit reference search scope for better performance.", MessageType.Info);
        }
        else
        {
            var refPath = AssetDatabase.GetAssetPath(referenceSearchFolder);
            if (!string.IsNullOrEmpty(refPath) && !AssetDatabase.IsValidFolder(refPath))
            {
                EditorGUILayout.HelpBox("Please select a valid folder.", MessageType.Warning);
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("File Type Filters", EditorStyles.boldLabel);
        int enabledCount = fileTypeFilters.Values.Count(v => v);
        EditorGUILayout.HelpBox($"Select file types to scan for texture references. Only checked file types will be processed during batch replace. ({enabledCount}/{CommonFileExtensions.Length} enabled)", MessageType.Info);
        
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All", GUILayout.Width(100)))
                {
                    foreach (var ext in CommonFileExtensions)
                    {
                        fileTypeFilters[ext] = true;
                    }
                }
                if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
                {
                    foreach (var ext in CommonFileExtensions)
                    {
                        fileTypeFilters[ext] = false;
                    }
                }
                GUILayout.FlexibleSpace();
            }

            fileTypeScrollPosition = EditorGUILayout.BeginScrollView(fileTypeScrollPosition, GUILayout.Height(120));
            int columns = 3;
            int rowCount = (CommonFileExtensions.Length + columns - 1) / columns;
            
            for (int row = 0; row < rowCount; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int col = 0; col < columns; col++)
                    {
                        int index = row * columns + col;
                        if (index < CommonFileExtensions.Length)
                        {
                            var ext = CommonFileExtensions[index];
                            if (!fileTypeFilters.ContainsKey(ext))
                            {
                                fileTypeFilters[ext] = false;
                            }
                            fileTypeFilters[ext] = EditorGUILayout.ToggleLeft(ext, fileTypeFilters[ext], GUILayout.Width(120));
                        }
                        else
                        {
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Duplicated Textures", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            int duplicateCount = duplicateGroups.Sum(g => Mathf.Max(0, g.Paths.Count - 1));
            EditorGUILayout.LabelField($"Groups: {duplicateGroups.Count}   Duplicates: {duplicateCount}");

            const float ROW_HEIGHT = 22f;
            const int PRELOAD_ROWS = 10;
            float scrollViewHeight = 260f;
            Rect scrollViewRect = GUILayoutUtility.GetRect(0, scrollViewHeight, GUILayout.ExpandWidth(true));
            scrollViewRect.height = scrollViewHeight;

            var rows = BuildDisplayRows();
            float totalContentHeight = Mathf.Max(rows.Count * ROW_HEIGHT, scrollViewHeight);
            Rect contentRect = new Rect(0, 0, scrollViewRect.width - 20f, totalContentHeight);

            scrollPosition = GUI.BeginScrollView(scrollViewRect, scrollPosition, contentRect);

            if (rows.Count == 0)
            {
                var emptyRect = new Rect(0, 0, contentRect.width, ROW_HEIGHT);
                GUI.Label(emptyRect, "No duplicated textures found.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                int startIndex = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / ROW_HEIGHT) - PRELOAD_ROWS);
                int endIndex = Mathf.Min(rows.Count, Mathf.CeilToInt((scrollPosition.y + scrollViewHeight) / ROW_HEIGHT) + PRELOAD_ROWS);

                for (int rowIndex = startIndex; rowIndex < endIndex; rowIndex++)
                {
                    var row = rows[rowIndex];
                    float yPos = rowIndex * ROW_HEIGHT;
                    Rect rowRect = new Rect(0, yPos, contentRect.width, ROW_HEIGHT);

                    if (row.IsHeader)
                    {
                        GUI.Label(rowRect, $"Group {row.GroupId} (Count: {row.GroupCount})", EditorStyles.boldLabel);
                    }
                    else
                    {
                        float idWidth = 40f;
                        float pingWidth = 60f;
                        Rect idRect = new Rect(rowRect.x, rowRect.y, idWidth, ROW_HEIGHT);
                        Rect pingRect = new Rect(rowRect.xMax - pingWidth, rowRect.y, pingWidth, ROW_HEIGHT);
                        Rect pathRect = new Rect(idRect.xMax + 4f, rowRect.y, rowRect.width - idWidth - pingWidth - 8f, ROW_HEIGHT);

                        GUI.Label(idRect, $"#{row.GroupId}", EditorStyles.miniLabel);

                        if (GUI.Button(pathRect, row.Path, EditorStyles.linkLabel))
                        {
                            PingAsset(row.Path);
                        }

                        if (GUI.Button(pingRect, "Ping"))
                        {
                            PingAsset(row.Path);
                        }
                    }
                }
            }

            GUI.EndScrollView();
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

    private struct DisplayRow
    {
        public bool IsHeader;
        public int GroupId;
        public int GroupCount;
        public string Path;
    }

    private List<DisplayRow> BuildDisplayRows()
    {
        var rows = new List<DisplayRow>();
        for (int i = 0; i < duplicateGroups.Count; i++)
        {
            var group = duplicateGroups[i];
            int groupId = i + 1;

            rows.Add(new DisplayRow
            {
                IsHeader = true,
                GroupId = groupId,
                GroupCount = group.Paths?.Count ?? 0,
                Path = string.Empty
            });

            if (group.Paths == null)
            {
                continue;
            }

            foreach (var path in group.Paths)
            {
                rows.Add(new DisplayRow
                {
                    IsHeader = false,
                    GroupId = groupId,
                    GroupCount = group.Paths.Count,
                    Path = path
                });
            }
        }

        return rows;
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
        
        // Determine reference search path
        string referenceSearchPath = null;
        if (referenceSearchFolder != null)
        {
            referenceSearchPath = AssetDatabase.GetAssetPath(referenceSearchFolder);
            if (string.IsNullOrEmpty(referenceSearchPath) || !AssetDatabase.IsValidFolder(referenceSearchPath))
            {
                EditorUtility.DisplayDialog("Invalid Reference Search Folder", "Please select a valid folder for reference search range, or leave it empty to search entire project.", "OK");
                return;
            }
        }
        
        var allAssetPaths = CollectAllAssetPaths(referenceSearchPath);

        int replacedReferences = 0;
        int deletedTextures = 0;
        int errors = 0;

        try
        {
            // Step 1: Build reference index (scan all assets once)
            string searchScope = referenceSearchPath ?? "entire project";
            EditorUtility.DisplayProgressBar("Building Reference Index", $"Scanning assets in {searchScope}...", 0f);
            var guidToReferencingAssets = BuildReferenceIndex(allAssetPaths, groupsToProcess, projectRoot);

            // Step 2: Collect all GUID replacements
            var guidReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assetsToModify = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var group in groupsToProcess)
            {
                string primaryPath = group.Paths[0];
                string primaryGuid = AssetDatabase.AssetPathToGUID(primaryPath);

                for (int i = 1; i < group.Paths.Count; i++)
                {
                    string duplicatePath = group.Paths[i];
                    string duplicateGuid = AssetDatabase.AssetPathToGUID(duplicatePath);
                    guidReplacements[duplicateGuid] = primaryGuid;

                    if (guidToReferencingAssets.TryGetValue(duplicateGuid, out var referencingAssets))
                    {
                        foreach (var assetPath in referencingAssets)
                        {
                            assetsToModify.Add(assetPath);
                        }
                    }
                }
            }

            // Step 3: Batch replace GUIDs in all affected files
            AssetDatabase.StartAssetEditing();
            
            int totalAssetsToModify = assetsToModify.Count;
            int processedAssets = 0;

            foreach (var assetPath in assetsToModify)
            {
                processedAssets++;
                float progress = totalAssetsToModify == 0 ? 1f : (float)processedAssets / totalAssetsToModify;
                EditorUtility.DisplayProgressBar("Updating References", $"Processing {assetPath} ({processedAssets}/{totalAssetsToModify})", progress);

                if (TryBatchReplaceGuidsInAsset(projectRoot, assetPath, guidReplacements, out int replacements))
                {
                    if (replacements > 0)
                    {
                        replacedReferences += replacements;
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    }
                }
                else
                {
                    errors++;
                }
            }

            // Step 4: Delete duplicate textures
            int processedDuplicates = 0;
            foreach (var group in groupsToProcess)
            {
                for (int i = 1; i < group.Paths.Count; i++)
                {
                    string duplicatePath = group.Paths[i];
                    processedDuplicates++;
                    float progress = duplicateCount == 0 ? 1f : (float)processedDuplicates / duplicateCount;
                    EditorUtility.DisplayProgressBar("Deleting Duplicates", $"Deleting {duplicatePath} ({processedDuplicates}/{duplicateCount})", progress);

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

    private List<string> CollectAllAssetPaths(string searchPath = null)
    {
        string[] searchPaths = null;
        if (!string.IsNullOrEmpty(searchPath))
        {
            searchPaths = new[] { searchPath };
        }

        var guids = AssetDatabase.FindAssets(string.Empty, searchPaths);
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

            // If searchPath is specified, ensure the path is within that folder
            if (!string.IsNullOrEmpty(searchPath))
            {
                string normalizedSearchPath = searchPath.Replace("\\", "/").TrimEnd('/');
                string normalizedPath = path.Replace("\\", "/");
                
                if (!normalizedPath.Equals(normalizedSearchPath, StringComparison.OrdinalIgnoreCase) &&
                    !normalizedPath.StartsWith(normalizedSearchPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Filter by file type
            if (!IsFileTypeEnabled(path))
            {
                continue;
            }

            assetPaths.Add(path);
        }

        return assetPaths;
    }

    private bool IsFileTypeEnabled(string assetPath)
    {
        string extension = Path.GetExtension(assetPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            // Files without extension are not processed
            return false;
        }

        // Check if this extension is in our filter list
        if (fileTypeFilters.TryGetValue(extension, out bool enabled))
        {
            return enabled;
        }

        // If extension is not in the filter list, don't process it
        // This ensures only explicitly selected file types are processed
        return false;
    }

    private Dictionary<string, List<string>> BuildReferenceIndex(List<string> allAssetPaths, List<DuplicateGroup> groupsToProcess, string projectRoot)
    {
        var guidToReferencingAssets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        
        // Collect all duplicate GUIDs we need to track
        var duplicateGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var group in groupsToProcess)
        {
            for (int i = 1; i < group.Paths.Count; i++)
            {
                string duplicatePath = group.Paths[i];
                string duplicateGuid = AssetDatabase.AssetPathToGUID(duplicatePath);
                duplicateGuids.Add(duplicateGuid);
                duplicatePaths.Add(duplicatePath);
            }
        }

        if (duplicateGuids.Count == 0)
        {
            return guidToReferencingAssets;
        }

        // Scan all assets once
        int totalAssets = allAssetPaths.Count;
        int processedAssets = 0;

        foreach (var assetPath in allAssetPaths)
        {
            processedAssets++;
            
            // Skip duplicate textures themselves
            if (duplicatePaths.Contains(assetPath))
            {
                continue;
            }

            float progress = totalAssets == 0 ? 1f : (float)processedAssets / totalAssets;
            EditorUtility.DisplayProgressBar("Building Reference Index", $"Scanning {assetPath} ({processedAssets}/{totalAssets})", progress);

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

            // Check which duplicate GUIDs are referenced in this file
            foreach (var duplicateGuid in duplicateGuids)
            {
                if (text.IndexOf(duplicateGuid, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!guidToReferencingAssets.TryGetValue(duplicateGuid, out var list))
                    {
                        list = new List<string>();
                        guidToReferencingAssets[duplicateGuid] = list;
                    }
                    list.Add(assetPath);
                }
            }
        }

        return guidToReferencingAssets;
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

    private bool TryBatchReplaceGuidsInAsset(string projectRoot, string assetPath, Dictionary<string, string> guidReplacements, out int replacementCount)
    {
        replacementCount = 0;
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
            var updatedText = text;
            bool changed = false;

            // Replace all GUIDs in one pass
            foreach (var kvp in guidReplacements)
            {
                string oldGuid = kvp.Key;
                string newGuid = kvp.Value;

                if (updatedText.IndexOf(oldGuid, StringComparison.Ordinal) >= 0)
                {
                    updatedText = updatedText.Replace(oldGuid, newGuid);
                    changed = true;
                    replacementCount++;
                }
            }

            if (changed)
            {
                File.WriteAllText(fullPath, updatedText);
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"DumplicatedChecker: Failed to update {assetPath}. {e.Message}");
            return false;
        }
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