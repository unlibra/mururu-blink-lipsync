using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class GeneratedStorageTests : MururuAvatarTestBase
    {
        [Test]
        public void Storage_InstallUsesProjectAssetsWithoutChoosingFolder()
        {
            var record = Install(SpawnMururu("Mururu"));
            var set = record.generatedAssetSet as MururuGeneratedAssetSet;
            Assert.IsNotNull(set);
            string folder = MururuGeneratedStorage.Folder(set);
            StringAssert.StartsWith(MururuGeneratedStorage.ResolveRoot(false) + "/Install_", folder);
            Assert.That(record.generatedAssetPaths.All(p => p.StartsWith(folder + "/", StringComparison.Ordinal)));
            Assert.AreEqual(4, set.assets.Count(a => a != null));
        }

        [Test]
        public void Storage_SameNameAvatarsHaveSeparateAssets()
        {
            var first = Install(SpawnMururu("SameName"));
            var second = Install(SpawnMururu("SameName"));
            Assert.AreNotEqual(first.generatedAssetSet, second.generatedAssetSet);
            Assert.IsEmpty(first.generatedAssetPaths.Intersect(second.generatedAssetPaths));
        }

        [Test]
        public void Storage_MovedRootIsFollowedByNextInstall()
        {
            var first = Install(SpawnMururu("First"));
            string oldRoot = MururuGeneratedStorage.ResolveRoot(false);
            EnsureTempFolder();
            string moved = TempFolder + "/MovedRoot";
            Assert.IsEmpty(AssetDatabase.MoveAsset(oldRoot, moved));
            try
            {
                Assert.AreEqual(moved, MururuGeneratedStorage.ResolveRoot(false));
                var second = Install(SpawnMururu("Second"));
                StringAssert.StartsWith(moved + "/", AssetDatabase.GetAssetPath(second.generatedAssetSet));
                Assert.IsNotNull(first.installedController);
            }
            finally { Assert.IsEmpty(AssetDatabase.MoveAsset(moved, oldRoot)); }
        }

        [Test]
        public void Storage_MovedInstallationCanApplyAndRemove()
        {
            var avatar = SpawnMururu("Mururu");
            var record = Install(avatar);
            string oldFolder = MururuGeneratedStorage.Folder((MururuGeneratedAssetSet)record.generatedAssetSet);
            EnsureTempFolder();
            string moved = TempFolder + "/MovedInstallation";
            Assert.IsEmpty(AssetDatabase.MoveAsset(oldFolder, moved));
            SetTarget(avatar);
            Assert.IsNull(Run(window.ApplyExpressionSettings));
            Assert.That(record.generatedAssetPaths.All(p => p.StartsWith(moved + "/", StringComparison.Ordinal)));
            string mesh = AssetDatabase.GetAssetPath(record.installedLipSyncMouthRenderer.sharedMesh);
            Assert.IsNull(Run(window.UninstallAuto));
            Assert.IsFalse(AssetExists(mesh));
        }

        [Test]
        public void Storage_UnknownAssetInsideOwnedFolderIsNotDeleted()
        {
            var record = Install(SpawnMururu("Mururu"));
            string folder = MururuGeneratedStorage.Folder((MururuGeneratedAssetSet)record.generatedAssetSet);
            string foreign = folder + "/UserMesh.asset";
            AssetDatabase.CreateAsset(new Mesh(), foreign);
            record.generatedAssetPaths.Add(foreign);
            try
            {
                Assert.IsNull(Run(window.UninstallAuto));
                Assert.IsTrue(AssetExists(foreign));
            }
            finally { AssetDatabase.DeleteAsset(foreign); }
        }

        [Test]
        public void Storage_LegacyGeneratedIsReadOnlyDuringUpgrade()
        {
            var record = Install(SpawnMururu("Mururu"));
            EnsureTempFolder();
            string legacy = TempFolder + "/LegacyGenerated";
            AssetDatabase.CreateFolder(TempFolder, "LegacyGenerated");
            var paths = record.generatedAssetPaths.ToArray();
            foreach (string asset in paths)
                Assert.IsEmpty(AssetDatabase.MoveAsset(asset, legacy + "/" + Path.GetFileName(asset)));
            string[] oldGuids = paths.Select(p => AssetDatabase.AssetPathToGUID(legacy + "/" + Path.GetFileName(p))).ToArray();
            record.generatedAssetSet = null;
            record.installSchemaVersion = 4;
            Assert.IsNull(Run(window.RepairInstallationAuto));
            Assert.IsNotNull(record.generatedAssetSet);
            foreach (string guid in oldGuids) StringAssert.StartsWith(legacy + "/", AssetDatabase.GUIDToAssetPath(guid));
            Assert.That(record.generatedAssetPaths.Where(AssetExists).Any(p => p.StartsWith(MururuGeneratedStorage.ResolveRoot(false) + "/", StringComparison.Ordinal)));
        }

        [Test]
        public void Storage_UserPrefabInsideInstallFolderKeepsReferencedAssets()
        {
            var avatar = SpawnMururu("Mururu");
            var record = Install(avatar);
            string folder = MururuGeneratedStorage.Folder((MururuGeneratedAssetSet)record.generatedAssetSet);
            string backup = folder + "/UserBackup.prefab";
            string[] assets = record.generatedAssetPaths.ToArray();
            PrefabUtility.SaveAsPrefabAsset(avatar.gameObject, backup);
            try
            {
                Assert.IsNull(Run(window.UninstallAuto));
                Assert.That(assets.All(AssetExists));
            }
            finally { AssetDatabase.DeleteAsset(backup); }
        }

        [Test]
        public void Storage_LiveUnrelatedRendererKeepsItsMesh()
        {
            var record = Install(SpawnMururu("Mururu"));
            var other = new GameObject("Other avatar without installation metadata").AddComponent<MeshFilter>();
            other.sharedMesh = record.installedLipSyncMouthRenderer.sharedMesh;
            string meshPath = AssetDatabase.GetAssetPath(other.sharedMesh);
            Assert.IsNull(Run(window.UninstallAuto));
            Assert.IsTrue(AssetExists(meshPath));
        }

        [Test]
        public void Storage_UnsavedPreviewSceneReferenceKeepsMesh()
        {
            var record = Install(SpawnMururu("Mururu"));
            string meshPath = AssetDatabase.GetAssetPath(record.installedLipSyncMouthRenderer.sharedMesh);
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var other = new GameObject("Unsaved prefab/preview mesh reference").AddComponent<MeshFilter>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(other.gameObject, preview);
                other.sharedMesh = record.installedLipSyncMouthRenderer.sharedMesh;
                Assert.IsNull(Run(window.UninstallAuto));
                Assert.IsTrue(AssetExists(meshPath));
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
        }

        [Test]
        public void Storage_ForeignPackageMeshIsPreservedOnRemove()
        {
            var record = Install(SpawnMururu("Mururu"));
            var foreign = AssetDatabase.FindAssets("t:Mesh", new[] { "Packages" })
                .Select(AssetDatabase.GUIDToAssetPath).SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                .OfType<Mesh>().FirstOrDefault();
            Assert.IsNotNull(foreign, "Package mesh fixture is required.");
            string foreignPath = AssetDatabase.GetAssetPath(foreign);
            record.installedLipSyncMouthRenderer.sharedMesh = foreign;
            Assert.IsNull(Run(window.UninstallAuto));
            Assert.IsTrue(AssetExists(foreignPath));
        }

        [Test]
        public void Storage_MissingManifestStopsBeforeEditingAvatar()
        {
            var record = Install(SpawnMururu("Mururu"));
            var controller = record.installedController;
            record.generatedAssetSet = null;
            AssertFailedWith(Run(window.ApplyExpressionSettings), "管理情報");
            Assert.AreSame(controller, record.installedController);
        }
    }
}
