/*
 * 通过降低float精度，去除无用的scale曲线
 * 从而降低动画的存储占用、内存占用和加载时间.
 *
 * 使用方法
 * 通过菜单Tools/Optimise/ClipOpt打开窗口，
 * 选择要搜索的路径，点击Load Clips from Path按钮加载动画，
 * 然后点击Optimize按钮，等待一段时间即可
 */

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace SquallImporter
{
    /// <summary>
    /// 动画优化，存储占用/内存占用/加载时间
    /// </summary>
    public class ClipOptimize : EditorWindow
    {
        private bool m_excludeScale;
        private Vector2 m_scroll;
        private List<AnimationClip> m_clips = new List<AnimationClip>();
        private string m_selectedPath = "Assets"; // 选定的搜索路径
        private const float PRECISION_MULTIPLIER = 1000f; // 保留3位小数

        [MenuItem("Tools/SquallTools/Optimise/ClipOpt")]
        protected static void Open()
        {
            GetWindow<ClipOptimize>();
        }

        public void OnGUI()
        {
            // 显示clip列表
            using (var svs = new EditorGUILayout.ScrollViewScope(m_scroll))
            {
                m_scroll = svs.scrollPosition;
                EditorGUILayout.LabelField($"Selected Clips: {m_clips.Count}", EditorStyles.boldLabel);
                foreach (var clip in m_clips)
                {
                    EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false);
                }
            }

            EditorGUILayout.Space();

            // 路径选择区域
            EditorGUILayout.LabelField("Search Path", EditorStyles.boldLabel);
            
            // 通过拖拽选择文件夹（主要选择方式）
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder:", GUILayout.Width(50));
                Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(m_selectedPath);
                Object newFolder = EditorGUILayout.ObjectField(folderObj, typeof(Object), false);
                if (newFolder != folderObj)
                {
                    string newPath = AssetDatabase.GetAssetPath(newFolder);
                    if (!string.IsNullOrEmpty(newPath))
                    {
                        // 确保是文件夹路径
                        if (AssetDatabase.IsValidFolder(newPath))
                        {
                            m_selectedPath = newPath;
                        }
                        else
                        {
                            // 如果是文件，获取其所在文件夹
                            m_selectedPath = Path.GetDirectoryName(newPath).Replace('\\', '/');
                            if (!m_selectedPath.StartsWith("Assets"))
                            {
                                m_selectedPath = "Assets";
                            }
                        }
                    }
                }
                
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    SelectSearchPath();
                }
                
                if (GUILayout.Button("Clear", GUILayout.Width(50)))
                {
                    m_selectedPath = "Assets";
                }
            }
            
            // 路径只读显示（方便查看完整路径）
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Path:", GUILayout.Width(50));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(m_selectedPath);
                EditorGUI.EndDisabledGroup();
            }
            
            EditorGUILayout.HelpBox("Drag a folder from Project window or click Browse to select a folder. Leave as 'Assets' to search all.", MessageType.Info);

            EditorGUILayout.Space();

            // 选项和按钮
            using (new EditorGUILayout.HorizontalScope())
            {
                m_excludeScale = EditorGUILayout.ToggleLeft("Exclude Scale", m_excludeScale);
                if (GUILayout.Button("Load Clips from Path"))
                {
                    LoadAllClips();
                }
            }

            EditorGUILayout.Space();

            // 优化按钮
            EditorGUI.BeginDisabledGroup(m_clips.Count == 0);
            if (GUILayout.Button("Optimize", GUILayout.Height(30)))
            {
                StartOptimization();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void SelectSearchPath()
        {
            // 获取当前路径的完整系统路径
            string currentPath = Application.dataPath;
            if (!string.IsNullOrEmpty(m_selectedPath) && m_selectedPath.StartsWith("Assets"))
            {
                string relativePath = m_selectedPath.Substring(6); // 移除 "Assets"
                if (!string.IsNullOrEmpty(relativePath))
                {
                    currentPath = Path.Combine(Application.dataPath, relativePath);
                }
            }

            // 打开文件夹选择对话框
            string selectedPath = EditorUtility.OpenFolderPanel("Select Folder to Search", currentPath, "");
            
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // 转换为Assets相对路径
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    m_selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Path", "Selected folder must be within the Assets directory.", "OK");
                }
            }
        }

        private void LoadAllClips()
        {
            m_clips.Clear();
            
            // 验证路径
            if (string.IsNullOrEmpty(m_selectedPath) || !m_selectedPath.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog("Invalid Path", "Please select a valid path within Assets directory.", "OK");
                m_selectedPath = "Assets";
                return;
            }

            try
            {
                // 显示搜索进度
                string searchPath = m_selectedPath == "Assets" ? "Assets (全部)" : m_selectedPath;
                EditorUtility.DisplayProgressBar("Loading Clips", $"正在检测文件夹: {searchPath}", 0f);

                // 在指定路径下搜索AnimationClip
                string searchFilter = "t:AnimationClip";
                string[] guids;
                
                if (m_selectedPath == "Assets")
                {
                    // 搜索所有Assets
                    EditorUtility.DisplayProgressBar("Loading Clips", $"正在搜索所有Assets文件夹...", 0.1f);
                    guids = AssetDatabase.FindAssets(searchFilter);
                }
                else
                {
                    // 在指定路径下搜索
                    EditorUtility.DisplayProgressBar("Loading Clips", $"正在搜索文件夹: {m_selectedPath}", 0.1f);
                    guids = AssetDatabase.FindAssets(searchFilter, new[] { m_selectedPath });
                }

                m_clips.Capacity = guids.Length;

                // 加载所有找到的clips
                for (int i = 0; i < guids.Length; i++)
                {
                    // 更新进度条
                    float progress = 0.2f + (i / (float)guids.Length) * 0.8f;
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Loading Clips", 
                        $"正在加载: {Path.GetFileName(assetPath)} ({i + 1}/{guids.Length})", progress);
                    
                    // 额外验证：确保路径在选定目录下（处理子目录情况）
                    if (m_selectedPath != "Assets" && !assetPath.StartsWith(m_selectedPath))
                    {
                        continue;
                    }
                    
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                    if (clip != null)
                    {
                        m_clips.Add(clip);
                    }
                }
            }
            finally
            {
                // 清除进度条
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Loaded {m_clips.Count} animation clips from path: {m_selectedPath}");
        }

        private void StartOptimization()
        {
            // 直接开始批量处理，不使用逐帧处理
            ProcessAllClips();
        }

        private void ProcessAllClips()
        {
            if (m_clips.Count == 0)
            {
                return;
            }

            int processedCount = 0;
            System.DateTime startTime = System.DateTime.Now;

            try
            {
                // 禁用资源导入，提高性能
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < m_clips.Count; i++)
                {
                    var clip = m_clips[i];
                    if (clip == null)
                    {
                        continue;
                    }

                    // 更新进度条
                    float progress = (i + 1f) / m_clips.Count;
                    var info = $"Processing: {clip.name} ({i + 1}/{m_clips.Count})";
                    if (EditorUtility.DisplayCancelableProgressBar("Optimize Clips", info, progress))
                    {
                        Debug.LogWarning("Optimization cancelled by user.");
                        break;
                    }

                    // 处理clip
                    FixFloatAtClip(clip, m_excludeScale);
                    processedCount++;
                }
            }
            finally
            {
                // 恢复资源导入
                AssetDatabase.StopAssetEditing();
                
                // 清除进度条
                EditorUtility.ClearProgressBar();
                
                // 批量保存和刷新
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                System.TimeSpan duration = System.DateTime.Now - startTime;
                if (processedCount > 0)
                {
                    Debug.Log($"Optimization completed! Processed {processedCount}/{m_clips.Count} clips in {duration.TotalSeconds:F2} seconds. Average: {duration.TotalSeconds / processedCount:F2} seconds per clip.");
                }
                else
                {
                    Debug.Log($"Optimization completed! No clips were processed.");
                }
            }
        }

        /// <summary>
        /// 优化动画clip的float精度
        /// </summary>
        private static void FixFloatAtClip(AnimationClip clip, bool excludeScale)
        {
            if (clip == null)
            {
                return;
            }

            // 检查资源是否可编辑
            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning($"Clip {clip.name} is not a saved asset, skipping.");
                return;
            }

            try
            {
                // 获取所有曲线绑定（只调用一次）
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);
                if (curveBindings == null || curveBindings.Length == 0)
                {
                    return;
                }

                bool hasChanges = false;

                // 如果需要排除scale，先删除所有scale曲线
                if (excludeScale)
                {
                    foreach (var binding in curveBindings)
                    {
                        if (binding.propertyName.ToLower().Contains("scale"))
                        {
                            AnimationUtility.SetEditorCurve(clip, binding, null);
                            hasChanges = true;
                        }
                    }
                }

                // 优化所有曲线的float精度
                // 注意：如果scale曲线已删除，GetEditorCurve会返回null，自然会被跳过
                foreach (var binding in curveBindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys == null || curve.keys.Length == 0)
                    {
                        continue;
                    }

                    Keyframe[] keyFrames = curve.keys;
                    bool curveChanged = false;

                    // 批量处理所有关键帧
                    for (int i = 0; i < keyFrames.Length; i++)
                    {
                        var key = keyFrames[i];
                        float oldValue = key.value;
                        float oldInTangent = key.inTangent;
                        float oldOutTangent = key.outTangent;

                        // 使用数学方法降低精度，比ToString+Parse更高效和精确
                        float newValue = RoundToPrecision(key.value);
                        float newInTangent = RoundToPrecision(key.inTangent);
                        float newOutTangent = RoundToPrecision(key.outTangent);

                        // 只有值发生变化时才更新
                        if (newValue != oldValue || newInTangent != oldInTangent || newOutTangent != oldOutTangent)
                        {
                            key.value = newValue;
                            key.inTangent = newInTangent;
                            key.outTangent = newOutTangent;
                            curveChanged = true;
                        }

                        keyFrames[i] = key;
                    }

                    if (curveChanged)
                    {
                        curve.keys = keyFrames;
                        clip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
                        hasChanges = true;
                    }
                }

                // 标记资源为dirty（批量保存时统一处理）
                if (hasChanges)
                {
                    EditorUtility.SetDirty(clip);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"OptimizeAnimationClip Failed! Clip: {clip.name}, Error: {e}");
            }
        }

        /// <summary>
        /// 将float值四舍五入到3位小数精度
        /// </summary>
        private static float RoundToPrecision(float value)
        {
            return Mathf.Round(value * PRECISION_MULTIPLIER) / PRECISION_MULTIPLIER;
        }
    }
}
