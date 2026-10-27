using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor.Tests
{
    // TEST_CASES.md G. 削除
    internal sealed class RemoveTests : MururuAvatarTestBase
    {
        [Test]
        public void G1_Remove_DeletesFilesAndRestoresDescriptor()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            VRC_AvatarDescriptor.LipSyncStyle lipSync = mururu.lipSync;
            SkinnedMeshRenderer viseme = mururu.VisemeSkinnedMesh;
            bool enableEyeLook = mururu.enableEyeLook;
            VRCAvatarDescriptor.EyelidType eyelidType = mururu.customEyeLookSettings.eyelidType;
            SkinnedMeshRenderer eyelids = mururu.customEyeLookSettings.eyelidsSkinnedMesh;

            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            List<string> paths = record.generatedAssetPaths.ToList();

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.IsNull(mururu.transform.Find(MururuBlinkLipSyncSetupWindow.RootObjectName));
            Assert.IsFalse(paths.Any(AssetExists), string.Join("\n", paths.Where(AssetExists)));
            Assert.AreEqual(lipSync, mururu.lipSync);
            Assert.AreEqual(viseme, mururu.VisemeSkinnedMesh);
            Assert.AreEqual(enableEyeLook, mururu.enableEyeLook);
            Assert.AreEqual(eyelidType, mururu.customEyeLookSettings.eyelidType);
            Assert.AreEqual(eyelids, mururu.customEyeLookSettings.eyelidsSkinnedMesh);
        }

        [Test]
        public void G2_Remove_KeepsVisemeChangedAfterInstall()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);
            Install(mururu);

            mururu.VisemeSkinnedMesh = body;

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.AreEqual(body, mururu.VisemeSkinnedMesh);
        }

        [Test]
        public void G3_Remove_KeepsEyeLookChangedAfterInstall()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            Install(mururu);

            VRCAvatarDescriptor.CustomEyeLookSettings eyeLook = mururu.customEyeLookSettings;
            eyeLook.eyelidType = VRCAvatarDescriptor.EyelidType.None;
            mururu.customEyeLookSettings = eyeLook;

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.AreEqual(VRCAvatarDescriptor.EyelidType.None, mururu.customEyeLookSettings.eyelidType);
        }

        [Test]
        public void G4_Remove_KeepsFilesUsedByDuplicate()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            string controllerPath = AssetDatabase.GetAssetPath(record.installedController);

            Object.Instantiate(mururu.gameObject).name = "Mururu Copy";
            SetTarget(mururu);

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.IsTrue(AssetExists(controllerPath));
        }

        [Test]
        public void G5_Remove_KeepsFilesUsedByClosedScene()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            string controllerPath = AssetDatabase.GetAssetPath(record.installedController);

            // An untitled scene cannot stay open next to another one.
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), $"{EnsureTempFolder()}/G5_Main.unity"));

            Scene other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(Object.Instantiate(mururu.gameObject), other);
            Assert.IsTrue(EditorSceneManager.SaveScene(other, $"{EnsureTempFolder()}/G5.unity"));
            EditorSceneManager.CloseScene(other, true);

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.IsTrue(AssetExists(controllerPath));
        }

        [Test]
        public void G6_Remove_KeepsFilesUsedByPrefab()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            string controllerPath = AssetDatabase.GetAssetPath(record.installedController);

            PrefabUtility.SaveAsPrefabAsset(
                mururu.gameObject,
                $"{EnsureTempFolder()}/G6_Mururu.prefab");

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.IsTrue(AssetExists(controllerPath));
        }

        [Test]
        public void G7_RemoveAfterLipSyncMouthDeleted_RestoresViseme()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            VRC_AvatarDescriptor.LipSyncStyle lipSync = mururu.lipSync;
            SkinnedMeshRenderer viseme = mururu.VisemeSkinnedMesh;
            MururuBlinkLipSyncInstallRecord record = Install(mururu);

            Object.DestroyImmediate(record.installedLipSyncMouthRenderer.gameObject);

            Assert.IsNull(Run(window.UninstallAuto));

            Assert.AreEqual(lipSync, mururu.lipSync);
            Assert.AreEqual(viseme, mururu.VisemeSkinnedMesh);
            Assert.IsFalse(IsMissing(mururu, "VisemeSkinnedMesh"));
        }

        // 要確認: whether Unity lets the tool delete its root from a Prefab
        // instance. Either outcome is acceptable as long as a failure changes
        // nothing; the outcome is written to the test output.
        [Test]
        public void G8_RemoveOnPrefabInstance_SucceedsOrChangesNothing()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            SkinnedMeshRenderer lipSyncMouth = record.installedLipSyncMouthRenderer;

            PrefabUtility.SaveAsPrefabAssetAndConnect(
                mururu.gameObject,
                $"{EnsureTempFolder()}/G8_Mururu.prefab",
                InteractionMode.AutomatedAction);

            LogAssert.ignoreFailingMessages = true;
            Exception error = Run(window.UninstallAuto);
            LogAssert.ignoreFailingMessages = false;

            if (error == null)
            {
                TestContext.Out.WriteLine("G-8: 削除できた");
                Assert.IsNull(RecordOf(mururu));
                return;
            }

            TestContext.Out.WriteLine($"G-8: エラーになった: {error.Message}");
            Assert.IsNotNull(RecordOf(mururu));
            Assert.AreEqual(lipSyncMouth, mururu.VisemeSkinnedMesh);
        }

        private static bool IsMissing(
            Object owner,
            string propertyPath)
        {
            SerializedProperty property =
                new SerializedObject(owner).FindProperty(propertyPath);

            return property.objectReferenceValue == null &&
                   property.objectReferenceInstanceIDValue != 0;
        }
    }
}
