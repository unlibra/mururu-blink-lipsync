using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MururuBlinkLipSync.Editor.Tests
{
    // TEST_CASES.md H. ビルド
    internal sealed class BuildTests : MururuAvatarTestBase
    {
        [Test]
        public void H1_Build_CopiesBodyMaterialToLipSyncMouth()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            SkinnedMeshRenderer mouth = record.installedLipSyncMouthRenderer;
            int mouthIndex = record.mouthMaterialIndex;

            Material replaced = NewMaterial("H1 Replaced");
            Material[] materials = body.sharedMaterials;
            materials[mouthIndex] = replaced;
            body.sharedMaterials = materials;

            // NDMF refuses Prefab instances; a real build processes an unpacked clone.
            Unpack(mururu);
            AvatarProcessor.ProcessAvatar(mururu.gameObject);

            Assert.AreEqual(replaced.name, mouth.sharedMaterials[0].name);
            Assert.AreEqual(body.sharedMaterials[mouthIndex], mouth.sharedMaterials[0]);
        }

        [Test]
        public void H2_Build_FollowsMaterialSetterInitialState()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            SkinnedMeshRenderer mouth = record.installedLipSyncMouthRenderer;
            int mouthIndex = record.mouthMaterialIndex;

            Material replaced = NewMaterial("H2 Replaced");

            GameObject setterObject = new GameObject("Material Setter");
            setterObject.transform.SetParent(mururu.transform, false);

            setterObject.AddComponent<ModularAvatarMaterialSetter>().Objects =
                new List<MaterialSwitchObject>
                {
                    new MaterialSwitchObject
                    {
                        Object = new AvatarObjectReference(body.gameObject),
                        Material = replaced,
                        MaterialIndex = mouthIndex
                    }
                };

            // NDMF refuses Prefab instances; a real build processes an unpacked clone.
            Unpack(mururu);
            AvatarProcessor.ProcessAvatar(mururu.gameObject);

            Assert.AreEqual(replaced.name, mouth.sharedMaterials[0].name);
        }

        [TestCase(false)] [TestCase(true)]
        public void H3_MouthReturn_ReportsActiveCustomShapeAndKeepsInputs(bool active)
        {
            var mururu = SpawnMururu("Fallback warning");
            var body = Body(mururu);
            var mesh = Object.Instantiate(body.sharedMesh);
            try
            {
                var delta = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex("mouth_A"), 0, delta, null, null);
                int vertex = System.Array.FindIndex(delta, d => Mathf.Abs(d.z) > .005f);
                Assert.That(vertex, Is.GreaterThanOrEqualTo(0));
                System.Array.Clear(delta, 0, delta.Length); delta[vertex] = new Vector3(.0001f, 0, 0);
                mesh.AddBlendShapeFrame("MBLS_Test_Unsupported", 100, delta, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
                body.sharedMesh = mesh;
                body.SetBlendShapeWeight(mesh.GetBlendShapeIndex("MBLS_Test_Unsupported"), active ? 100 : 0);
                var record = Install(mururu);
                var originalController = record.installedController;
                var originalMouth = record.installedLipSyncMouthRenderer.sharedMesh;
                Unpack(mururu);
                var errors = ErrorReport.CaptureErrors(() => MururuMouthReturnPlugin.Prepare(mururu.gameObject));
                Assert.That(errors.Count, Is.EqualTo(active ? 1 : 0));
                if (active)
                {
                    Assert.That(errors.Single().TheError.Severity, Is.EqualTo(ErrorSeverity.NonFatal));
                    Assert.That(errors.Single().TheError.ToMessage(), Does.Contain("MBLS_Test_Unsupported"));
                }
                Assert.That(record.installedController, Is.Not.SameAs(originalController), "Preparation continues after the warning");
                Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.Not.SameAs(originalMouth));
                Assert.That(body.sharedMesh, Is.SameAs(mesh));
                Assert.That(body.GetBlendShapeWeight(mesh.GetBlendShapeIndex("MBLS_Test_Unsupported")), Is.EqualTo(active ? 100 : 0));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static Material NewMaterial(
            string name)
        {
            return new Material(Shader.Find("Standard")) { name = name };
        }
    }
}
