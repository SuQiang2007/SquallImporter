using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FbxCompressor : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> foundFbxAssetPaths = new List<string>();
    private Vector2 scrollPosition;

    private bool readWriteEnabled = false;
    private ModelImporterMeshCompression meshCompression = ModelImporterMeshCompression.Medium;
    private bool verboseLogging = false;

    [MenuItem("Tools/SquallTools/Optimise/FBX Compressor")]
    public static void ShowWindow()
    {
        var window = GetWindow<FbxCompressor>();
        window.titleContent = new GUIContent("Fbx Compressor");
        window.minSize = new Vector2(520, 360);
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
            using (new EditorGUI.DisabledScope(searchFolder == null))
            {
                if (GUILayout.Button("Find", GUILayout.Width(120)))
                {
                    FindFbxAssets();
                }
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Found FBX Assets", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {foundFbxAssetPaths.Count}");
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(180));
            foreach (var path in foundFbxAssetPaths)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(path, EditorStyles.linkLabel))
                    {
                        SelectAndPing(path);
                    }
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    {
                        SelectAndPing(path);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        readWriteEnabled = EditorGUILayout.Toggle("Read/Write Enabled", readWriteEnabled);
        meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup("Mesh Compression", meshCompression);
        verboseLogging = EditorGUILayout.Toggle("Verbose Logging", verboseLogging);

        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(foundFbxAssetPaths.Count == 0))
        {
            if (GUILayout.Button("Compress / Apply", GUILayout.Height(28)))
            {
                ApplySettingsToFound();
            }
        }
    }

    private void FindFbxAssets()
    {
        foundFbxAssetPaths.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : null;
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        // Search for model assets (includes .fbx). Using t:Model is robust across importers.
        var guids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                foundFbxAssetPaths.Add(path);
            }
        }

        // Fallback: if none via t:Model (rare), enumerate by extension
        if (foundFbxAssetPaths.Count == 0)
        {
            var possibleGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var guid in possibleGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    foundFbxAssetPaths.Add(path);
                }
            }
        }

        if (foundFbxAssetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No FBX Found", $"No .fbx files found under folder:\n{folderPath}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("FBX Found", $"Found {foundFbxAssetPaths.Count} .fbx file(s) under:\n{folderPath}", "OK");
        }

        Repaint();
    }

    private void SelectAndPing(string assetPath)
    {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
    }

    private void ApplySettingsToFound()
    {
        if (foundFbxAssetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Assets", "No FBX assets found. Click Find first.", "OK");
            return;
        }

        var errorSummaries = new List<string>();

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            for (int i = 0; i < foundFbxAssetPaths.Count; i++)
            {
                var path = foundFbxAssetPaths[i];
                EditorUtility.DisplayProgressBar("Applying FBX Settings", path, (float)(i + 1) / foundFbxAssetPaths.Count);

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                bool changed = false;
                var prevReadable = importer.isReadable;
                var prevCompression = importer.meshCompression;

                if (verboseLogging)
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    Debug.Log($"[FbxCompressor] Preparing import\n- Path: {path}\n- GUID: {guid}\n- Prev Read/Write: {prevReadable}\n- Prev Compression: {prevCompression}\n- Target Read/Write: {readWriteEnabled}\n- Target Compression: {meshCompression}");
                }

                if (importer.isReadable != readWriteEnabled)
                {
                    importer.isReadable = readWriteEnabled;
                    changed = true;
                }
                if (importer.meshCompression != meshCompression)
                {
                    importer.meshCompression = meshCompression;
                    changed = true;
                }

                if (changed)
                {
                    try
                    {
                        importer.SaveAndReimport();
                        modified++;
                        if (verboseLogging)
                        {
                            Debug.Log($"[FbxCompressor] Applied settings OK: {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(path);
                        string scan = ScanAssetForNaNInfo(path);
                        string msg = $"[FbxCompressor] ERROR reimporting FBX\n- Path: {path}\n- GUID: {guid}\n- Prev Read/Write: {prevReadable}\n- Prev Compression: {prevCompression}\n- Target Read/Write: {readWriteEnabled}\n- Target Compression: {meshCompression}\n- Exception: {ex}\n- NaN Scan: {scan}";
                        Debug.LogError(msg);
                        errorSummaries.Add($"Path: {path}\nReason: {ex.Message}");
                    }
                }
            }

            if (errorSummaries.Count > 0)
            {
                EditorUtility.DisplayDialog("Done with Errors", $"Applied settings to {modified} FBX asset(s).\nErrors: {errorSummaries.Count}\nSee Console for details.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Done", $"Applied settings to {modified} FBX asset(s).", "OK");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"FbxCompressor error: {e}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }
    }

    private string ScanAssetForNaNInfo(string assetPath)
    {
        try
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null)
            {
                return "GameObject not loadable for scan.";
            }

            var results = new List<string>();
            var transforms = go.GetComponentsInChildren<Transform>(true);
            int reported = 0;
            foreach (var t in transforms)
            {
                if (TransformHasNaN(t))
                {
                    results.Add(GetTransformPath(t));
                    reported++;
                    if (reported >= 10)
                    {
                        results.Add("... (truncated)");
                        break;
                    }
                }
            }

            if (results.Count == 0)
            {
                return "No NaN found in Transform local values (current imported state).";
            }

            return "NaN Transforms: " + string.Join(" | ", results);
        }
        catch (Exception ex)
        {
            return $"Scan failed: {ex.Message}";
        }
    }

    private bool TransformHasNaN(Transform t)
    {
        Vector3 lp = t.localPosition;
        Quaternion lr = t.localRotation;
        Vector3 ls = t.localScale;
        return float.IsNaN(lp.x) || float.IsNaN(lp.y) || float.IsNaN(lp.z)
            || float.IsNaN(lr.x) || float.IsNaN(lr.y) || float.IsNaN(lr.z) || float.IsNaN(lr.w)
            || float.IsNaN(ls.x) || float.IsNaN(ls.y) || float.IsNaN(ls.z);
    }

    private string GetTransformPath(Transform t)
    {
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
