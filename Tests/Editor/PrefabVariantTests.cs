using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class PrefabVariantTests : MururuAvatarTestBase
    {
        const string BaseName = "VariantBase";

        VRCAvatarDescriptor CreateBase()
        {
            var avatar = SpawnMururu(BaseName);
            Unpack(avatar);
            PrefabUtility.SaveAsPrefabAssetAndConnect(avatar.gameObject,
                EnsureTempFolder() + "/" + BaseName + ".prefab", InteractionMode.AutomatedAction);
            return avatar;
        }

        string CreateVariant(VRCAvatarDescriptor parent, string name)
        {
            string path = EnsureTempFolder() + "/" + name + ".prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(parent.gameObject, path);
            Assert.That(PrefabUtility.GetPrefabAssetType(asset), Is.EqualTo(PrefabAssetType.Variant));
            return path;
        }

        static VRCAvatarDescriptor Spawn(string path)
        {
            return ((GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(path))).GetComponent<VRCAvatarDescriptor>();
        }

        static void Apply(VRCAvatarDescriptor avatar)
        {
            PrefabUtility.ApplyPrefabInstance(avatar.gameObject, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
        }

        void ClearScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.Refresh();
        }

        static void AssertInstalled(VRCAvatarDescriptor avatar)
        {
            var record = RecordOf(avatar);
            Assert.That(record, Is.Not.Null);
            Assert.That(avatar.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true).Length, Is.EqualTo(1));
            Assert.That(record.avatarDescriptor, Is.SameAs(avatar), "Inherited object references must point into this instance");
            Assert.That(record.bodyRenderer, Is.SameAs(Body(avatar)));
            Assert.That(avatar.VisemeSkinnedMesh, Is.SameAs(record.installedLipSyncMouthRenderer));
            Assert.That(record.GetComponent<ModularAvatarMergeAnimator>().animator, Is.SameAs(record.installedController));
            Assert.That(record.generatedAssetPaths.All(AssetExists), Is.True);
        }

        static void AssertBuild(VRCAvatarDescriptor avatar)
        {
            Unpack(avatar);
            AvatarProcessor.ProcessAvatar(avatar.gameObject);
            Assert.That(avatar.VisemeSkinnedMesh, Is.Not.Null);
            Assert.That(avatar.VisemeSkinnedMesh.transform.IsChildOf(avatar.transform), Is.True);
            Assert.That(avatar.VisemeBlendShapes, Has.Length.EqualTo(15));
            foreach (string shape in avatar.VisemeBlendShapes)
                Assert.That(avatar.VisemeSkinnedMesh.sharedMesh.GetBlendShapeIndex(shape), Is.GreaterThanOrEqualTo(0), shape);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SavedParentInstallation_IsInheritedBeforeOrAfterVariantCreation(bool installFirst)
        {
            var parent = CreateBase();
            if (installFirst) { Install(parent); Apply(parent); }
            string variantPath = CreateVariant(parent, "Child");
            if (!installFirst) { Install(parent); Apply(parent); }
            ClearScene();

            var child = Spawn(variantPath);
            AssertInstalled(child);
            SetTarget(child);
            Assert.That(window.IsCurrentTargetInstalled(), Is.True);
        }

        [Test]
        public void VariantSettingsAndRepair_AreSavedWithoutChangingParentOrSibling()
        {
            var parent = CreateBase();
            Install(parent); Apply(parent);
            string basePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parent.gameObject);
            string childPath = CreateVariant(parent, "ConfiguredChild");
            string siblingPath = CreateVariant(parent, "Sibling");
            string controllerPath = AssetDatabase.GetAssetPath(RecordOf(parent).installedController);
            string meshPath = AssetDatabase.GetAssetPath(RecordOf(parent).installedLipSyncMouthRenderer.sharedMesh);
            byte[] controllerBytes = System.IO.File.ReadAllBytes(controllerPath);
            byte[] meshBytes = System.IO.File.ReadAllBytes(meshPath);
            ClearScene();

            var child = Spawn(childPath);
            SetTarget(child);
            window.leftLipSyncEnabled[1] = false;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Apply(child);
            ClearScene();
            child = Spawn(childPath);
            AssertVariantSettings(child, basePath, siblingPath, controllerPath, meshPath, controllerBytes, meshBytes);

            // Verify Apply on its own before Repair can mask a missing override.
            SetTarget(child);
            Assert.That(Run(window.RepairInstallationAuto), Is.Null);
            Apply(child);
            ClearScene();
            child = Spawn(childPath);
            AssertVariantSettings(child, basePath, siblingPath, controllerPath, meshPath, controllerBytes, meshBytes);
            // One build covers an inherited Variant with its own saved settings.
            AssertBuild(child);
        }

        static void AssertVariantSettings(VRCAvatarDescriptor child, string basePath, string siblingPath,
            string controllerPath, string meshPath, byte[] controllerBytes, byte[] meshBytes)
        {
            AssertInstalled(child);
            Assert.That(RecordOf(child).leftLipSyncEnabled[1], Is.False);
            Assert.That(AssetDatabase.GetAssetPath(RecordOf(child).installedController), Is.Not.EqualTo(controllerPath));
            foreach (string path in new[] { basePath, siblingPath })
            {
                var other = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<VRCAvatarDescriptor>();
                AssertInstalled(other);
                Assert.That(RecordOf(other).leftLipSyncEnabled[1], Is.True);
                Assert.That(AssetDatabase.GetAssetPath(RecordOf(other).installedController), Is.EqualTo(controllerPath));
                Assert.That(AssetDatabase.GetAssetPath(RecordOf(other).installedLipSyncMouthRenderer.sharedMesh), Is.EqualTo(meshPath));
            }
            CollectionAssert.AreEqual(controllerBytes, System.IO.File.ReadAllBytes(controllerPath), "Parent controller bytes");
            CollectionAssert.AreEqual(meshBytes, System.IO.File.ReadAllBytes(meshPath), "Parent mesh bytes");
        }

        [Test]
        public void SceneOnlyUninstall_KeepsSavedVariantAndItsGeneratedFiles()
        {
            var parent = CreateBase();
            var record = Install(parent);
            string[] paths = record.generatedAssetPaths.ToArray();
            string variantPath = CreateVariant(parent, "SavedInstalledChild");
            SetTarget(parent);
            Assert.That(Run(window.UninstallAuto), Is.Null);
            Assert.That(RecordOf(parent), Is.Null);
            Assert.That(paths.All(AssetExists), Is.True, "A saved Variant still needs these files");
            ClearScene();

            var child = Spawn(variantPath);
            AssertInstalled(child);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UninstallSavedParent_RemovesInheritedInstallationFromVariant(bool overrideSettings)
        {
            var parent = CreateBase();
            Install(parent); Apply(parent);
            string basePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parent.gameObject);
            string childPath = CreateVariant(parent, "ChildToRemove");
            var original = RecordOf(parent);
            var originalLipSync = original.originalLipSync;
            string originalVisemePath = original.originalVisemeSkinnedMesh == null ? null :
                AnimationUtility.CalculateTransformPath(original.originalVisemeSkinnedMesh.transform, parent.transform);
            string[] originalShapes = original.originalVisemeBlendShapes?.ToArray();
            if (overrideSettings)
            {
                var child = Spawn(childPath);
                SetTarget(child); window.leftLipSyncEnabled[1] = false;
                Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
                Apply(child);
            }
            ClearScene();

            var contents = PrefabUtility.LoadPrefabContents(basePath);
            try
            {
                SetTarget(contents.GetComponent<VRCAvatarDescriptor>());
                Assert.That(Run(window.UninstallAuto), Is.Null);
                Assert.That(RecordOf(contents.GetComponent<VRCAvatarDescriptor>()), Is.Null);
                PrefabUtility.SaveAsPrefabAsset(contents, basePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            ClearScene();
            var reloaded = Spawn(childPath);
            Assert.That(RecordOf(reloaded), Is.Null);
            Assert.That(reloaded.transform.Find(MururuBlinkLipSyncSetupWindow.RootObjectName), Is.Null);
            Assert.That(reloaded.lipSync, Is.EqualTo(originalLipSync));
            if (originalVisemePath != null)
                Assert.That(reloaded.VisemeSkinnedMesh, Is.SameAs(
                    reloaded.transform.Find(originalVisemePath).GetComponent<SkinnedMeshRenderer>()));
            else
                Assert.That(reloaded.VisemeSkinnedMesh == null, Is.True);
            if (originalVisemePath == null)
                Assert.That(new SerializedObject(reloaded).FindProperty("VisemeSkinnedMesh").objectReferenceInstanceIDValue, Is.Zero,
                    "A valid empty reference must not become Missing");
            CollectionAssert.AreEqual(originalShapes, reloaded.VisemeBlendShapes);
        }

        [Test]
        public void InheritedSceneInstance_UninstallSurvivesSceneReloadWithoutChangingPrefab()
        {
            var parent = CreateBase();
            Install(parent); Apply(parent);
            string childPath = CreateVariant(parent, "InheritedChild");
            ClearScene();
            var child = Spawn(childPath);
            SetTarget(child);
            string[] paths = RecordOf(child).generatedAssetPaths.ToArray();
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(RecordOf(child).gameObject), Is.True);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(RecordOf(child).gameObject), Is.Not.Null);
            Assert.That(Run(window.UninstallAuto), Is.Null);
            Assert.That(RecordOf(child), Is.Null);
            string scene = EnsureTempFolder() + "/RemovedChild.unity";
            Assert.That(EditorSceneManager.SaveScene(child.gameObject.scene, scene), Is.True);
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var reloaded = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Select(go => go.GetComponent<VRCAvatarDescriptor>()).Single(d => d != null);
            Assert.That(RecordOf(reloaded), Is.Null, "A successful uninstall must persist after saving the scene");
            Assert.That(paths.All(AssetExists), Is.True);
            AssertInstalled(Spawn(childPath));
        }

        [Test]
        public void VariantWithOwnInstallation_UninstallsWithoutChangingParentOrSibling()
        {
            var parent = CreateBase();
            string basePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parent.gameObject);
            string path = CreateVariant(parent, "OwnInstallation");
            string siblingPath = CreateVariant(parent, "IndependentSibling");
            ClearScene();
            var child = Spawn(path);
            var record = Install(child);
            var originalLipSync = record.originalLipSync;
            Apply(child);
            var sibling = Spawn(siblingPath);
            Install(sibling); Apply(sibling);
            Assert.That(RecordOf(child).generatedAssetSet, Is.Not.SameAs(RecordOf(sibling).generatedAssetSet));
            string[] siblingFiles = RecordOf(sibling).generatedAssetPaths.ToArray();
            byte[] siblingPrefab = System.IO.File.ReadAllBytes(siblingPath);
            ClearScene();

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var descriptor = contents.GetComponent<VRCAvatarDescriptor>();
                SetTarget(descriptor);
                Assert.That(Run(window.UninstallAuto), Is.Null);
                Assert.That(RecordOf(descriptor), Is.Null);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            ClearScene();
            var reloaded = Spawn(path);
            Assert.That(RecordOf(reloaded), Is.Null);
            Assert.That(reloaded.transform.Find(MururuBlinkLipSyncSetupWindow.RootObjectName), Is.Null);
            Assert.That(reloaded.lipSync, Is.EqualTo(originalLipSync));
            Assert.That(reloaded.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)), Is.Zero);
            Assert.That(RecordOf(Spawn(basePath)), Is.Null);
            AssertInstalled(Spawn(siblingPath));
            Assert.That(siblingFiles.All(AssetExists), Is.True);
            CollectionAssert.AreEqual(siblingPrefab, System.IO.File.ReadAllBytes(siblingPath));
        }

    }
}
