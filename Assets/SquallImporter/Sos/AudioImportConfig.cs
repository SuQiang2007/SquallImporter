using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Serialization;

namespace SquallImporter
{
    [CreateAssetMenu(fileName = "AudioImportConfig", menuName = "Assets/Squall/AudioImportConfig")]
    public class AudioImportConfig : ScriptableObject
    {
        [Tooltip("音量根目录")]
        public string audioRootPath;
        [Tooltip("音效类型，每一个音效类型会对应一个存储路径")]
        public string[] nameHeads;
        
        [HideInInspector]
        public string audioNamePattern = @"^[a-z]+_(?:[a-z0-9]+_)+[a-z0-9]+$";

        public void ApplyToAll()
        {
            if(!CheckParams()) return;
            
            foreach (var head in nameHeads)
            {
                if (string.IsNullOrEmpty(head)) continue;
                // 将首字母大写
                var folderName = char.ToUpper(head[0]) + head.Substring(1);
                string fullAudioRootPath = IsFolderExist(folderName);

                if (!System.IO.Directory.Exists(fullAudioRootPath))
                {
                    System.IO.Directory.CreateDirectory(fullAudioRootPath);
                    continue;
                }

                var files = System.IO.Directory.GetFiles(fullAudioRootPath, "*", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var ext = System.IO.Path.GetExtension(file).ToLower();
                    if (ext != ".ogg" && ext != ".mp3" && ext != ".wav")
                    {
                        UnityEditor.EditorUtility.DisplayDialog("文件类型错误", $"文件 {file} 不是支持的音频格式（ogg, mp3, wav）！", "确定");
                        return;
                    }
                    
                    if(!IsValid(file, nameHeads))
                    {
                        UnityEditor.EditorUtility.DisplayDialog("文件名错误", $"文件 {file} 不符合命名规则！", "确定");
                        return;
                    }
                }
            }
            UnityEditor.EditorUtility.DisplayDialog("操作完成", "所有音频文件检查完毕。", "确定");
        }

        public string IsFolderExist(string folderName)
        {
            string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "."));
            string fullAudioRootPath = System.IO.Path.Combine(projectPath, audioRootPath.TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                folderName);

            return fullAudioRootPath;
        }

        private bool IsValid(string input, string[] validPrefixes)
        {
            // 1. 先检查基础格式（小写字母+下划线）
            Match match = Regex.Match(input, audioNamePattern);
            if (!match.Success) return false;

            // 2. 提取第一部分
            string firstPart = match.Groups[1].Value; // Groups[1] 对应第一个捕获组

            // 3. 检查是否在 validPrefixes 数组中
            return System.Array.Exists(validPrefixes, prefix => prefix == firstPart);
        }

        private bool CheckParams()
        {
            if (string.IsNullOrEmpty(audioRootPath))
            {
                UnityEditor.EditorUtility.DisplayDialog("参数错误", "音量根目录 (audioRootPath) 不能为空！", "确定");
                return false;
            }

            // 在 audioRootPath 前拼上项目路径
            string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "."));
            string fullAudioRootPath = System.IO.Path.Combine(projectPath, audioRootPath.TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (!System.IO.Directory.Exists(fullAudioRootPath))
            {
                UnityEditor.EditorUtility.DisplayDialog("参数错误", $"音量根目录 (audioRootPath) 路径不存在: {fullAudioRootPath}", "确定");
                return false;
            }

            if (nameHeads == null || nameHeads.Length == 0)
            {
                UnityEditor.EditorUtility.DisplayDialog("参数错误", "音效类型 (nameHeads) 不能为空！", "确定");
                return false;
            }
            foreach (var head in nameHeads)
            {
                if (string.IsNullOrEmpty(head))
                {
                    UnityEditor.EditorUtility.DisplayDialog("参数错误", "音效类型 (nameHeads) 中存在空项！", "确定");
                    return false;
                }
            }
            return true;
        }
    }
}