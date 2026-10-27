using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MururuBlinkLipSync.Editor.Tests
{
    // Shared setup for the tests that install onto a real Mururu. They need the
    // standard Mururu prefab in the project and are ignored without it.
    // Each test starts from an empty scene and deletes the generated files and
    // temporary assets it created, and restores the Body FBX Read/Write flag.
    internal abstract class MururuAvatarTestBase
    {
        protected const string TempFolder = "Assets/MururuBlinkLipSyncTestTemp";

        private const string MururuPrefabName = "Mururu";

        protected MururuBlinkLipSyncSetupWindow window;

        private GameObject mururuPrefab;

        private readonly HashSet<string> generatedPaths =
            new HashSet<string>(StringComparer.Ordinal);

        private string bodyFbxPath;

        private bool bodyFbxReadable;

        [SetUp]
        public void SetUpAvatarScene()
        {
            mururuPrefab = FindMururuPrefab();

            if (mururuPrefab == null)
                Assert.Ignore("標準Mururuのprefabがプロジェクトにないため実行しません。");

            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            Selection.activeObject = null;

            SkinnedMeshRenderer prefabBody =
                MururuBlinkLipSyncSetupWindow.FindCompatibleBodyRenderer(
                    mururuPrefab.GetComponent<VRCAvatarDescriptor>());

            bodyFbxPath = AssetDatabase.GetAssetPath(prefabBody.sharedMesh);
            bodyFbxReadable = BodyImporter.isReadable;
        }

        [TearDown]
        public void TearDownAvatarScene()
        {
            if (mururuPrefab == null)
                return;

            if (window != null)
                UnityEngine.Object.DestroyImmediate(window);

            Selection.activeObject = null;

            CollectGeneratedPaths();

            // Replace the test scene first, so nothing loaded still points at
            // the assets deleted below.
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            Undo.ClearAll();

            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);

            string outputPrefix =
                MururuBlinkLipSyncSetupWindow.OutputFolder.TrimEnd('/') + "/";

            foreach (string path in generatedPaths
                         .Where(p => p.StartsWith(outputPrefix, StringComparison.Ordinal))
                         .OrderByDescending(p => p.Length))
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }

            generatedPaths.Clear();

            ModelImporter importer = BodyImporter;

            if (importer != null && importer.isReadable != bodyFbxReadable)
            {
                importer.isReadable = bodyFbxReadable;
                importer.SaveAndReimport();
            }
        }

        protected ModelImporter BodyImporter =>
            AssetImporter.GetAtPath(bodyFbxPath) as ModelImporter;

        protected void SetBodyFbxReadable(bool readable)
        {
            ModelImporter importer = BodyImporter;

            if (importer.isReadable == readable)
                return;

            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        // A Mururu prefab instance with its own Expression Parameters, so the
        // tests never edit the parameters asset that ships with Mururu.
        protected VRCAvatarDescriptor SpawnMururu(
            string name,
            VRCExpressionParameters.ValueType facialExType =
                VRCExpressionParameters.ValueType.Int)
        {
            GameObject avatar =
                (GameObject)PrefabUtility.InstantiatePrefab(mururuPrefab);

            avatar.name = name;

            VRCAvatarDescriptor descriptor =
                avatar.GetComponent<VRCAvatarDescriptor>();

            descriptor.expressionParameters =
                CreateExpressionParameters(
                    name,
                    new VRCExpressionParameters.Parameter
                    {
                        name = "Facial EX",
                        valueType = facialExType
                    });

            return descriptor;
        }

        protected static void Unpack(
            VRCAvatarDescriptor descriptor)
        {
            PrefabUtility.UnpackPrefabInstance(
                descriptor.gameObject,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
        }

        protected VRCExpressionParameters CreateExpressionParameters(
            string name,
            params VRCExpressionParameters.Parameter[] parameters)
        {
            VRCExpressionParameters asset =
                ScriptableObject.CreateInstance<VRCExpressionParameters>();

            asset.parameters = parameters;

            AssetDatabase.CreateAsset(
                asset,
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{EnsureTempFolder()}/{name}_Parameters.asset"));

            return asset;
        }

        protected static string EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder(
                    Path.GetDirectoryName(TempFolder),
                    Path.GetFileName(TempFolder));
            }

            return TempFolder;
        }

        // Created on demand: OnEnable picks the target from the current scene.
        protected MururuBlinkLipSyncSetupWindow CreateWindow()
        {
            window = ScriptableObject.CreateInstance<MururuBlinkLipSyncSetupWindow>();
            return window;
        }

        // Same as choosing the avatar in the window's target field.
        protected void SetTarget(
            VRCAvatarDescriptor descriptor)
        {
            if (window == null)
                CreateWindow();

            window.avatarDescriptor = descriptor;
            window.RefreshTargetFields();
        }

        // Runs an operation the way its button does, without the result dialog.
        protected Exception Run(
            Action operation)
        {
            Exception error = window.RunOperation(operation);
            CollectGeneratedPaths();
            return error;
        }

        protected MururuBlinkLipSyncInstallRecord Install(
            VRCAvatarDescriptor descriptor)
        {
            SetTarget(descriptor);

            Exception error = Run(window.Install);
            Assert.IsNull(error, error?.ToString());

            MururuBlinkLipSyncInstallRecord record = window.FindInstallRecord();
            Assert.IsNotNull(record);

            return record;
        }

        protected static void AssertFailedWith(
            Exception error,
            string messagePart)
        {
            Assert.IsNotNull(error, "操作がエラーにならなかった");
            Assert.IsNotInstanceOf<OperationCanceledException>(error);
            StringAssert.Contains(messagePart, error.Message);
        }

        protected static SkinnedMeshRenderer Body(
            VRCAvatarDescriptor descriptor)
        {
            return MururuBlinkLipSyncSetupWindow.FindCompatibleBodyRenderer(descriptor);
        }

        protected static MururuBlinkLipSyncInstallRecord RecordOf(
            VRCAvatarDescriptor descriptor)
        {
            return descriptor
                .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                .FirstOrDefault();
        }

        protected static bool AssetExists(
            string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   AssetDatabase.LoadMainAssetAtPath(path) != null;
        }

        // Generated paths are kept as they are seen, because a removed or undone
        // install no longer lists the files it left behind.
        protected void TrackGeneratedPaths(
            MururuBlinkLipSyncInstallRecord record)
        {
            if (record == null || record.generatedAssetPaths == null)
                return;

            foreach (string path in record.generatedAssetPaths)
            {
                if (!string.IsNullOrEmpty(path))
                    generatedPaths.Add(path.Replace('\\', '/'));
            }
        }

        private void CollectGeneratedPaths()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MururuBlinkLipSyncInstallRecord record in
                             root.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true))
                    {
                        TrackGeneratedPaths(record);
                    }
                }
            }
        }

        private static GameObject FindMururuPrefab()
        {
            foreach (string guid in AssetDatabase.FindAssets($"{MururuPrefabName} t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetFileNameWithoutExtension(path) != MururuPrefabName)
                    continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                VRCAvatarDescriptor descriptor =
                    prefab != null
                        ? prefab.GetComponent<VRCAvatarDescriptor>()
                        : null;

                if (descriptor != null &&
                    MururuBlinkLipSyncSetupWindow.FindCompatibleBodyRenderer(descriptor) != null)
                {
                    return prefab;
                }
            }

            return null;
        }
    }
}
