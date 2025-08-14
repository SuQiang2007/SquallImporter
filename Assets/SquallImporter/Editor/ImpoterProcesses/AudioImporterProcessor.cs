// #if EDITOR_RES_CHECK

using System.IO;
using UnityEditor;
using UnityEngine;

namespace SquallImporter
{
    public class AudioImporterProcessor : AssetPostprocessor
    {
        //BG Use CompressedInMemory, otherwise DecompressOnLoad
        private void OnPostprocessAudio(AudioClip arg)
        {
            // 在这里读取AudioImporterConfig.so中的内容
            // 假设AudioImporterConfig.so是一个ScriptableObject，路径为"Assets/AudioImporterConfig.so"
            AudioImportConfig config = AssetDatabase.LoadAssetAtPath<AudioImportConfig>("Assets/AudioImporterConfig.so");
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
        
            if(CheckConfig(config, importer) || CheckFileNameHead(config) || CheckFileNameIllegal(config))
            {
                DeleteThisImport();
                return;
            }

            var settingDefault = importer.defaultSampleSettings;

            var name = Path.GetFileName(assetPath).ToLower();
        
            importer.forceToMono = true;
    
            settingDefault.compressionFormat = AudioCompressionFormat.Vorbis;
            settingDefault.quality = 0.7f;

            //startWith "bg_" means bgAudio 
            var isBgAudio = name.StartsWith("bg_");
            if (isBgAudio)
            {
                settingDefault.loadType = AudioClipLoadType.CompressedInMemory;
            }

            importer.defaultSampleSettings = settingDefault;
        }

        private bool CheckConfig(AudioImportConfig config, AudioImporter importer)
        {
            if (config == null || importer == null)
            {
                EditorUtility.DisplayDialog(
                    "音频导入配置错误", 
                    $"找不到配置文件或importer。\n\n" +
                    $"配置文件是否存在: {(config == null ? "否" : "是")}\n" +
                    $"Importer是否存在: {(importer == null ? "否" : "是")}\n" +
                    $"资源路径: {assetPath}", 
                    "确定"
                );
                return true;
            }

            return false;
        }

        private void DeleteThisImport()
        {
            // 在这里删除arg代表的音频文件，因为不合法
            if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                Debug.LogWarning($"[AudioImporterProcessor] 已删除不合法音频文件: {assetPath}");
            }
        }

        private bool CheckFileNameIllegal(AudioImportConfig config)
        {
            // 检查音频文件名是否符合config中的正则表达式
            if (config != null && !string.IsNullOrEmpty(config.audioNamePattern))
            {
                var fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, config.audioNamePattern))
                {
                    EditorUtility.DisplayDialog(
                        "音频命名错误",
                        $"音频文件名不符合命名规范！\n\n" +
                        $"文件路径: {assetPath}\n" +
                        $"文件名: {fileName}\n" +
                        $"应匹配正则: {config.audioNamePattern}",
                        "确定"
                    );
                    Debug.LogWarning(
                        $"[AudioImporterProcessor] 音频文件名不符合命名规范: \n" +
                        $"assetPath: {assetPath}\n" +
                        $"fileName: {fileName}\n" +
                        $"pattern: {config.audioNamePattern}"
                    );
                    return true;
                }
            }

            return false;
        }

        private bool CheckFileNameHead(AudioImportConfig config)
        {
            string head = GetHead(assetPath, config.audioNamePattern);
            // 获取当前audioclip的父文件夹名字
            string parentFolderName = null;
            {
                var dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    parentFolderName = Path.GetFileName(dir);
                }
            }
            if (parentFolderName != head)
            {
                EditorUtility.DisplayDialog(
                    "音频导入错误",
                    $"文件名与存放路径不匹配！\n\n" +
                    $"文件路径: {assetPath}\n" +
                    $"父文件夹名: {parentFolderName}\n" +
                    $"文件名推断的类型: {head}\n\n" +
                    $"请确保音频文件的父文件夹名与文件名的类型前缀一致。",
                    "确定"
                );
                Debug.LogWarning(
                    $"[AudioImporterProcessor] 文件名与存放路径不匹配: \n" +
                    $"assetPath: {assetPath}\n" +
                    $"parentFolderName: {parentFolderName}\n" +
                    $"head(from name): {head}"
                );
                return true;
            }

            return false;
        }

        private string GetHead(string fileName, string pattern)
        {
            // 根据AudioImportConfig中的IsValid方法中的逻辑，只返回first就可以
            // 1. 先检查基础格式（小写字母+下划线）
            var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
            if (!match.Success) return null;
            // 2. 提取第一部分
            return match.Groups[1].Value;
        }
    }
}

// #endif