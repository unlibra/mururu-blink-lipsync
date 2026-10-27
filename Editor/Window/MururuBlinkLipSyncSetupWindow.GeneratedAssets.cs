using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using nadena.dev.modular_avatar.core;

namespace MururuBlinkLipSync.Editor
{
public partial class MururuBlinkLipSyncSetupWindow
{
    private const string EditorAssemblyName = "MururuBlinkLipSync.Editor";

    // The package root is the parent of the folder holding the Editor asmdef, so
    // files below Editor may be organized into any subfolders. The asmdef itself
    // must stay directly in <root>/Editor.
    private static string GetPackageRoot()
    {
        // A second copy of the package only causes a compile error; the editor
        // keeps running the previously compiled tool, so detect it explicitly
        // before Generated could be placed in the wrong copy.
        List<string> asmdefCopies =
            AssetDatabase.FindAssets($"{EditorAssemblyName} t:AssemblyDefinitionAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(path),
                        $"{EditorAssemblyName}.asmdef",
                        StringComparison.Ordinal))
                .ToList();

        if (asmdefCopies.Count > 1)
        {
            throw new Exception(
                "MururuBlinkLipSync が複数存在します。\n" +
                "重複したパッケージを削除してください:\n" +
                string.Join("\n", asmdefCopies));
        }

        string asmdefPath =
            UnityEditor.Compilation.CompilationPipeline
                .GetAssemblyDefinitionFilePathFromAssemblyName(EditorAssemblyName);

        if (string.IsNullOrEmpty(asmdefPath))
        {
            throw new Exception(
                $"{EditorAssemblyName}.asmdef の配置場所を特定できません。");
        }

        asmdefPath =
            asmdefPath.Replace('\\', '/');

        string editorFolder =
            Path.GetDirectoryName(asmdefPath);

        string packageRoot =
            string.IsNullOrEmpty(editorFolder)
                ? null
                : Path.GetDirectoryName(editorFolder);

        if (string.IsNullOrEmpty(packageRoot))
            throw new Exception("MururuBlinkLipSyncのパッケージルートを特定できません。");

        packageRoot =
            packageRoot.Replace('\\', '/');

        if (!(packageRoot == "Assets" ||
              packageRoot.StartsWith("Assets/", StringComparison.Ordinal) ||
              packageRoot.StartsWith("Packages/", StringComparison.Ordinal)))
        {
            throw new Exception(
                "MururuBlinkLipSyncの配置を確認してください。\n" +
                $"現在のパッケージルート: {packageRoot}");
        }

        return packageRoot;
    }

    private static Mesh UpdateOrCreateTrackedMeshAsset(
        Mesh generatedMesh,
        SkinnedMeshRenderer existingRenderer,
        MururuBlinkLipSyncInstallRecord record)
    {
        if (generatedMesh == null)
            throw new ArgumentNullException(nameof(generatedMesh));

        Mesh existingMesh =
            existingRenderer != null
                ? existingRenderer.sharedMesh
                : null;

        string existingPath =
            existingMesh != null
                ? AssetDatabase.GetAssetPath(existingMesh)
                : null;

        bool canUpdateInPlace =
            existingMesh != null &&
            !string.IsNullOrEmpty(existingPath) &&
            IsInsideGeneratedFolder(existingPath) &&
            !IsAssetReferencedElsewhere(record, existingPath);

        if (canUpdateInPlace)
        {
            Undo.RegisterCompleteObjectUndo(
                existingMesh,
                "Mururu Blink & LipSync - Update LipSync Mesh");

            EditorUtility.CopySerialized(
                generatedMesh,
                existingMesh);

            existingMesh.name =
                "LipSync_Mouth_Generated";

            EditorUtility.SetDirty(existingMesh);
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            TrackAsset(record, existingPath);
            return existingMesh;
        }

        string path =
            CreateTrackedMeshAsset(
                generatedMesh,
                $"{OutputFolder}/LipSync_Mouth_Generated.asset",
                record);

        if (!string.IsNullOrEmpty(existingPath) &&
            existingPath != path)
        {
            DetachTrackedAssetAndDeleteIfUnshared(
                record,
                existingPath);
        }

        Mesh created =
            AssetDatabase.LoadAssetAtPath<Mesh>(path);

        return created != null
            ? created
            : generatedMesh;
    }

    private static string CreateTrackedMeshAsset(
        Mesh mesh,
        string desiredPath,
        MururuBlinkLipSyncInstallRecord record)
    {
        string path =
            AssetDatabase.GenerateUniqueAssetPath(
                desiredPath);

        AssetDatabase.CreateAsset(
            mesh,
            path);

        TrackAsset(
            record,
            path);

        return path;
    }

    private static bool IsAssetReferencedElsewhere(
        MururuBlinkLipSyncInstallRecord currentRecord,
        string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string normalized =
            path.Replace('\\', '/');

        foreach (MururuBlinkLipSyncInstallRecord record in
                 FindSceneInstallRecords())
        {
            if (record == null ||
                record == currentRecord)
            {
                continue;
            }

            if (RecordReferencesAssetPath(
                    record,
                    normalized))
            {
                return true;
            }
        }

        return IsReferencedByUnloadedSceneOrPrefab(normalized);
    }

    private static bool IsReferencedByUnloadedSceneOrPrefab(
        string normalizedPath)
    {
        if (externallyReferencedGeneratedPaths == null)
        {
            externallyReferencedGeneratedPaths =
                CollectExternallyReferencedGeneratedPaths();
        }

        return externallyReferencedGeneratedPaths.Contains(normalizedPath);
    }

    private static HashSet<string> CollectExternallyReferencedGeneratedPaths()
    {
        string outputPrefix =
            OutputFolder.TrimEnd('/') + "/";

        // Loaded scenes and the open Prefab Mode stage are checked in memory by
        // RecordReferencesAssetPath. Their saved files may be stale, e.g. they
        // still reference the asset that the current operation is replacing.
        HashSet<string> inMemoryContainerPaths =
            new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (scene.isLoaded && !string.IsNullOrEmpty(scene.path))
                inMemoryContainerPaths.Add(scene.path.Replace('\\', '/'));
        }

        UnityEditor.SceneManagement.PrefabStage prefabStage =
            UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

        if (prefabStage != null && !string.IsNullOrEmpty(prefabStage.assetPath))
            inMemoryContainerPaths.Add(prefabStage.assetPath.Replace('\\', '/'));

        var ownedPaths = new HashSet<string>(operationAssetSet != null
            ? operationAssetSet.assets.Where(asset => asset != null).Select(AssetDatabase.GetAssetPath)
            : Enumerable.Empty<string>(), StringComparer.Ordinal);
        string[] containerPaths = AssetDatabase.GetAllAssetPaths()
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                !AssetDatabase.IsValidFolder(path) && !inMemoryContainerPaths.Contains(path) &&
                !ownedPaths.Contains(path) &&
                AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(MururuGeneratedAssetSet) &&
                AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(MururuGeneratedRoot))
            .ToArray();

        HashSet<string> result =
            new HashSet<string>(StringComparer.Ordinal);

        Queue<string> pending =
            new Queue<string>();

        var livePaths = new HashSet<string>(StringComparer.Ordinal);
        Transform ownRoot = operationRecord != null ? operationRecord.transform : null;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var component in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true)))
            {
                if (component == null || ownRoot != null && (component.transform == ownRoot || component.transform.IsChildOf(ownRoot))) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
                        string assetPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                        if (!string.IsNullOrEmpty(assetPath)) livePaths.Add(assetPath);
                    }
                }
            }
        }
        containerPaths = containerPaths.Concat(livePaths).Distinct().ToArray();
        if (containerPaths.Length > 0)
        {
            foreach (string dependency in
                     AssetDatabase.GetDependencies(containerPaths, false))
            {
                string normalized = dependency.Replace('\\', '/');

                if (normalized.StartsWith(outputPrefix, StringComparison.Ordinal) &&
                    result.Add(normalized))
                {
                    pending.Enqueue(normalized);
                }
            }
        }

        foreach (string path in livePaths)
            if (path.StartsWith(outputPrefix, StringComparison.Ordinal) && result.Add(path)) pending.Enqueue(path);

        // A kept controller or menu must keep the generated clips / submenus it
        // points at, so follow references between generated assets as well.
        while (pending.Count > 0)
        {
            foreach (string dependency in
                     AssetDatabase.GetDependencies(pending.Dequeue(), false))
            {
                string normalized = dependency.Replace('\\', '/');

                if (normalized.StartsWith(outputPrefix, StringComparison.Ordinal) &&
                    result.Add(normalized))
                {
                    pending.Enqueue(normalized);
                }
            }
        }

        return result;
    }

    private static bool RecordReferencesAssetPath(
        MururuBlinkLipSyncInstallRecord record,
        string normalizedPath)
    {
        if (record == null ||
            string.IsNullOrEmpty(normalizedPath))
        {
            return false;
        }

        if (AssetPathEquals(record.generatedAssetSet, normalizedPath) ||
            AssetPathEquals(record.installedController, normalizedPath) ||
            AssetPathEquals(record.installedOnClip, normalizedPath) ||
            AssetPathEquals(record.installedOffClip, normalizedPath))
        {
            return true;
        }

        if (record.installedLipSyncMouthRenderer != null &&
            AssetPathEquals(
                record.installedLipSyncMouthRenderer.sharedMesh,
                normalizedPath))
        {
            return true;
        }

        ModularAvatarMergeAnimator merge =
            record.GetComponent<ModularAvatarMergeAnimator>();

        if (merge != null &&
            AssetPathEquals(
                merge.animator,
                normalizedPath))
        {
            return true;
        }

        if (record.generatedAssetPaths != null &&
            record.generatedAssetPaths.Any(path =>
                !string.IsNullOrEmpty(path) &&
                string.Equals(
                    path.Replace('\\', '/'),
                    normalizedPath,
                    StringComparison.Ordinal)))
        {
            return true;
        }

        foreach (ModularAvatarMenuInstaller installer in
                 record.GetComponents<ModularAvatarMenuInstaller>())
        {
            if (installer != null &&
                AssetPathEquals(
                    installer.menuToAppend,
                    normalizedPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AssetPathEquals(
        UnityEngine.Object asset,
        string normalizedPath)
    {
        if (asset == null ||
            string.IsNullOrEmpty(normalizedPath))
        {
            return false;
        }

        string assetPath =
            AssetDatabase.GetAssetPath(asset);

        return !string.IsNullOrEmpty(assetPath) &&
               string.Equals(
                   assetPath.Replace('\\', '/'),
                   normalizedPath,
                   StringComparison.Ordinal);
    }

    private static void UntrackAsset(
        MururuBlinkLipSyncInstallRecord record,
        string path)
    {
        if (record == null ||
            record.generatedAssetPaths == null ||
            string.IsNullOrEmpty(path))
        {
            return;
        }

        string normalized =
            path.Replace('\\', '/');

        int removed =
            record.generatedAssetPaths.RemoveAll(existing =>
                !string.IsNullOrEmpty(existing) &&
                string.Equals(
                    existing.Replace('\\', '/'),
                    normalized,
                    StringComparison.Ordinal));

        if (removed > 0)
            EditorUtility.SetDirty(record);
    }

    private static void DetachTrackedAssetAndDeleteIfUnshared(
        MururuBlinkLipSyncInstallRecord record,
        string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        string normalized =
            path.Replace('\\', '/');

        bool owned =
            IsInsideGeneratedFolder(normalized);

        bool shared =
            IsAssetReferencedElsewhere(
                record,
                normalized);

        UntrackAsset(
            record,
            normalized);

        if (!owned)
            return;

        if (shared)
        {
            Debug.Log(
                "[MururuBlinkLipSync] Generated asset kept because another avatar, scene or prefab still uses it: " +
                normalized);
            return;
        }

        if (AssetDatabase.LoadMainAssetAtPath(normalized) == null)
            return;

        if (!AssetDatabase.DeleteAsset(normalized))
        {
            Debug.LogWarning(
                "[MururuBlinkLipSync] Generated asset could not be deleted: " +
                normalized);
        }
    }

    // Only files inside the Generated folder may ever be updated in place or
    // deleted. generatedAssetPaths is serialized scene data and can come from
    // another project or a distributed prefab, so membership in that list alone
    // must never authorize touching an asset.
    private static bool IsInsideGeneratedFolder(
        string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string normalized =
            path.Replace('\\', '/');

        // Reject relative segments so "Generated/../../X" cannot escape the prefix.
        if (normalized.Split('/').Any(segment => segment == "." || segment == ".."))
            return false;

        if (operationAssetSet == null) return false;
        string outputFolder = MururuGeneratedStorage.Folder(operationAssetSet).TrimEnd('/');
        if (!normalized.StartsWith(outputFolder + "/", StringComparison.Ordinal)) return false;
        MururuGeneratedStorage.ValidateWritablePath(normalized);

        return normalized.StartsWith(outputFolder + "/", StringComparison.Ordinal) &&
            operationAssetSet.assets.Any(asset => asset != null && AssetPathEquals(asset, normalized));
    }

    private static void RefreshTrackedAssetPaths(
        MururuBlinkLipSyncInstallRecord record)
    {
        if (record == null)
            return;

        UseRecordStorage(record);
        if (record.generatedAssetPaths == null)
        {
            record.generatedAssetPaths =
                new List<string>();
        }

        // String paths do not follow Asset moves. Rebuild the known owned paths
        // from serialized Object references so moving the whole package, including
        // Generated, keeps rebuild/update/remove operations reliable.
        record.generatedAssetPaths.RemoveAll(path =>
            string.IsNullOrEmpty(path) ||
            AssetDatabase.LoadMainAssetAtPath(path) == null);

        TrackCurrentAsset(record, record.installedController);
        TrackCurrentAsset(record, record.installedOnClip);
        TrackCurrentAsset(record, record.installedOffClip);

        if (record.installedLipSyncMouthRenderer != null)
        {
            TrackCurrentAsset(
                record,
                record.installedLipSyncMouthRenderer.sharedMesh);
        }

        foreach (ModularAvatarMenuInstaller installer in
                 record.GetComponents<ModularAvatarMenuInstaller>())
        {
            if (installer != null)
                TrackCurrentAsset(record, installer.menuToAppend);
        }

        EditorUtility.SetDirty(record);
    }

    private static void TrackCurrentAsset(
        MururuBlinkLipSyncInstallRecord record,
        UnityEngine.Object asset)
    {
        if (record == null || asset == null)
            return;

        string path =
            AssetDatabase.GetAssetPath(asset);

        if (string.IsNullOrEmpty(path))
            return;

        path =
            path.Replace('\\', '/');

        if (IsInsideGeneratedFolder(path))
            TrackAsset(record, path);
    }

    private static void TrackAsset(
        MururuBlinkLipSyncInstallRecord record,
        string path)
    {
        if (record.generatedAssetPaths == null)
        {
            record.generatedAssetPaths =
                new List<string>();
        }

        path = path.Replace('\\', '/');
        if (operationAssetSet == null || !path.StartsWith(MururuGeneratedStorage.Folder(operationAssetSet) + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("生成したアセットが専用の保存領域外です。");
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) throw new InvalidOperationException("生成したアセットを読み取れません。");
        if (!operationAssetSet.assets.Contains(asset))
        {
            Undo.RecordObject(operationAssetSet, "Mururu Blink & LipSync - Track Asset");
            operationAssetSet.assets.Add(asset);
            EditorUtility.SetDirty(operationAssetSet);
            operationCreatedAssetGuids.Add(AssetDatabase.AssetPathToGUID(path));
        }
        if (!record.generatedAssetPaths.Contains(path))
        {
            record.generatedAssetPaths.Add(path);
            EditorUtility.SetDirty(record);
        }
    }

    private static void EnsureFolder(
        string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts =
            folder.Split('/');

        string current =
            parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next =
                $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(
                    current,
                    parts[i]);
            }

            current =
                next;
        }
    }
}
}
