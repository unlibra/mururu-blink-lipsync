using UnityEditor;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    [CustomEditor(typeof(MururuBlinkLipSyncInstallRecord))]
    public sealed class MururuBlinkLipSyncInstallRecordEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MururuBlinkLipSyncInstallRecord record =
                (MururuBlinkLipSyncInstallRecord)target;

            EditorGUILayout.LabelField(
                "Mururu Blink & LipSync",
                EditorStyles.boldLabel);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    "導入時のバージョン",
                    string.IsNullOrEmpty(record.installedVersion)
                        ? "不明"
                        : record.installedVersion);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("設定ウィンドウを開く"))
            {
                MururuBlinkLipSyncSetupWindow.OpenForRecord(record);
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "誤って編集しないよう、内部データは非表示にしています。" +
                "BlinkとLipSyncの設定は設定ウィンドウから変更してください。",
                MessageType.Info);
        }
    }
}
