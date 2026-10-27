using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    internal static class MururuGeneratedStorage
    {
        internal const string InitialRoot = "Assets/ZZZ_MururuBlinkLipSyncAssets";
        private const string RegistryPath = "ProjectSettings/MururuBlinkLipSyncAssets.json";
        private const string MarkerName = "MururuGeneratedRoot.asset";

        [Serializable]
        private sealed class Registry { public string rootGuid; }

        internal static void ValidateWritablePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Split('/').Any(p => p == "." || p == ".." || p.Length == 0) || path.Contains('\\'))
                throw new InvalidOperationException("生成物の保存先はAssets内の専用フォルダである必要があります。");
            string full = Path.GetFullPath(path);
            string assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("生成物の保存先がAssets外です。");
            for (var directory = new DirectoryInfo(full); directory != null; directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("リンクされたフォルダには生成物を保存できません。");
                if (string.Equals(directory.FullName, Path.GetFullPath("Assets"), StringComparison.OrdinalIgnoreCase)) break;
            }
            string probe = path;
            while (probe != "Assets")
            {
                if (File.Exists(probe + "/MururuBlinkLipSync.Editor.asmdef") ||
                    File.Exists(probe + "/Editor/MururuBlinkLipSync.Editor.asmdef"))
                    throw new InvalidOperationException("製品コードの内側には生成物を保存できません。");
                probe = Path.GetDirectoryName(probe)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(probe)) break;
            }
        }

        private static string ValidateRoot(MururuGeneratedRoot root)
        {
            string folder = AssetDatabase.GUIDToAssetPath(root.folderGuid);
            string marker = AssetDatabase.GetAssetPath(root);
            if (root.schema != 1 || !AssetDatabase.IsValidFolder(folder) ||
                Path.GetDirectoryName(marker)?.Replace('\\', '/') != folder)
                throw new InvalidOperationException("生成物の管理情報が一致しません。フォルダと.metaを復元してください。");
            ValidateWritablePath(folder);
            return folder;
        }

        internal static string ResolveRoot(bool create)
        {
            Registry registry = null;
            if (File.Exists(RegistryPath))
            {
                if ((File.GetAttributes(RegistryPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("生成物の管理設定がリンクされています。");
                registry = JsonUtility.FromJson<Registry>(File.ReadAllText(RegistryPath));
                if (registry == null || string.IsNullOrEmpty(registry.rootGuid))
                    throw new InvalidOperationException("生成物の管理設定を読み取れません。");
            }
            var roots = AssetDatabase.FindAssets("t:MururuGeneratedRoot", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => AssetDatabase.LoadAssetAtPath<MururuGeneratedRoot>(p)).Where(r => r != null).ToArray();
            if (roots.Length > 1)
                throw new InvalidOperationException("生成物の管理フォルダが複数あります。重複を解消してください。");
            MururuGeneratedRoot root;
            if (registry != null)
            {
                root = AssetDatabase.LoadAssetAtPath<MururuGeneratedRoot>(AssetDatabase.GUIDToAssetPath(registry.rootGuid));
                if (root == null) throw new InvalidOperationException("登録した生成物フォルダが見つかりません。フォルダと.metaを復元してください。");
            }
            else root = roots.SingleOrDefault();
            if (root != null)
            {
                string folder = ValidateRoot(root);
                if (create && registry == null) Register(root);
                return folder;
            }
            ValidateWritablePath(InitialRoot);
            if (!create) return InitialRoot;
            if (Directory.Exists(InitialRoot) || File.Exists(InitialRoot) || File.Exists(InitialRoot + ".meta"))
                throw new InvalidOperationException(InitialRoot + " が既に存在しますが、本ツールの保存先と確認できません。既存の内容は変更しません。");
            string guid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(InitialRoot));
            if (string.IsNullOrEmpty(guid)) throw new IOException("生成物フォルダを作成できません。");
            root = ScriptableObject.CreateInstance<MururuGeneratedRoot>();
            root.folderGuid = guid;
            AssetDatabase.CreateAsset(root, InitialRoot + "/" + MarkerName);
            AssetDatabase.SaveAssetIfDirty(root);
            Register(root);
            return InitialRoot;
        }

        private static void Register(MururuGeneratedRoot root)
        {
            File.WriteAllText(RegistryPath, JsonUtility.ToJson(new Registry {
                rootGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(root))
            }, true) + "\n");
        }

        internal static string Folder(MururuGeneratedAssetSet set)
        {
            if (set == null || set.schema != 1 || set.root == null)
                throw new InvalidOperationException("導入済みアセットの管理情報がありません。");
            ValidateRoot(set.root);
            string folder = AssetDatabase.GUIDToAssetPath(set.folderGuid);
            if (!AssetDatabase.IsValidFolder(folder) || Path.GetDirectoryName(AssetDatabase.GetAssetPath(set))?.Replace('\\', '/') != folder)
                throw new InvalidOperationException("導入済みアセットのフォルダと管理情報が一致しません。");
            ValidateWritablePath(folder);
            return folder;
        }

        internal static MururuGeneratedAssetSet CreateSet()
        {
            string rootPath = ResolveRoot(true);
            var root = AssetDatabase.LoadAssetAtPath<MururuGeneratedRoot>(rootPath + "/" + MarkerName);
            // The marker itself may be renamed while its folder moves.
            if (root == null) root = AssetDatabase.FindAssets("t:MururuGeneratedRoot", new[] { rootPath })
                .Select(AssetDatabase.GUIDToAssetPath).Select(p => AssetDatabase.LoadAssetAtPath<MururuGeneratedRoot>(p)).Single();
            string folderGuid = AssetDatabase.CreateFolder(rootPath, "Install_" + Guid.NewGuid().ToString("N"));
            if (string.IsNullOrEmpty(folderGuid)) throw new IOException("導入先の保存領域を作成できません。");
            var set = ScriptableObject.CreateInstance<MururuGeneratedAssetSet>();
            set.folderGuid = folderGuid;
            set.root = root;
            AssetDatabase.CreateAsset(set, AssetDatabase.GUIDToAssetPath(folderGuid) + "/GeneratedAssetManifest.asset");
            return set;
        }
    }
}
