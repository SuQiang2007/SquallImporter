using UnityEditor;
using UnityEngine;

namespace SquallImporter
{
    [CustomEditor(typeof(AudioImportConfig))]
    public class AudioImportConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 1. 绘制默认 Inspector（显示 audioRootPath、fileNameReg、nameHeads）
            DrawDefaultInspector();

            // 2. 添加自定义按钮
            AudioImportConfig config = (AudioImportConfig)target;
            if (GUILayout.Button("Apply To All", GUILayout.Height(30)))
            {
                config.ApplyToAll();
            }
        }
    }
}