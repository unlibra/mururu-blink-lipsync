using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuLipSyncMaterialSlotsTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }

            created.Clear();
        }

        [Test]
        public void Build_NullBody_ReturnsEmptySlots()
        {
            Material[] result =
                MururuLipSyncMaterialSlots.Build(null, 2, 0);

            Assert.AreEqual(2, result.Length);
            Assert.IsNull(result[0]);
            Assert.IsNull(result[1]);
        }

        [Test]
        public void Build_ZeroSubMeshCount_ReturnsOneSlot()
        {
            Material[] result =
                MururuLipSyncMaterialSlots.Build(null, 0, 0);

            Assert.AreEqual(1, result.Length);
        }

        [Test]
        public void Build_UsesMouthMaterialIndex()
        {
            Material a = CreateMaterial("A");
            Material b = CreateMaterial("B");
            SkinnedMeshRenderer body = CreateBody(2, a, b);

            Material[] result =
                MururuLipSyncMaterialSlots.Build(body, 1, 1);

            CollectionAssert.AreEqual(new[] { b }, result);
        }

        [Test]
        public void Build_UncomputedIndex_FallsBackToSlotZero()
        {
            Material a = CreateMaterial("A");
            Material b = CreateMaterial("B");
            SkinnedMeshRenderer body = CreateBody(2, a, b);

            Material[] result =
                MururuLipSyncMaterialSlots.Build(body, 1, -1);

            CollectionAssert.AreEqual(new[] { a }, result);
        }

        [Test]
        public void Build_SlotsBeyondBodyMaterials_ClampToLast()
        {
            Material a = CreateMaterial("A");
            Material b = CreateMaterial("B");
            SkinnedMeshRenderer body = CreateBody(2, a, b);

            Material[] result =
                MururuLipSyncMaterialSlots.Build(body, 3, 1);

            CollectionAssert.AreEqual(new[] { b, b, b }, result);
        }

        [Test]
        public void Build_MouthInLastSubMesh_KeepsOverlayMaterials()
        {
            Material a = CreateMaterial("A");
            Material b = CreateMaterial("B");
            Material overlay = CreateMaterial("Overlay");
            SkinnedMeshRenderer body = CreateBody(2, a, b, overlay);

            Material[] result =
                MururuLipSyncMaterialSlots.Build(body, 1, 1);

            CollectionAssert.AreEqual(new[] { b, overlay }, result);
        }

        [Test]
        public void Build_MouthNotInLastSubMesh_DropsOverlayMaterials()
        {
            Material a = CreateMaterial("A");
            Material b = CreateMaterial("B");
            Material overlay = CreateMaterial("Overlay");
            SkinnedMeshRenderer body = CreateBody(2, a, b, overlay);

            Material[] result =
                MururuLipSyncMaterialSlots.Build(body, 1, 0);

            CollectionAssert.AreEqual(new[] { a }, result);
        }

        private Material CreateMaterial(string name)
        {
            Material material =
                new Material(Shader.Find("Hidden/Internal-Colored")) { name = name };

            created.Add(material);
            return material;
        }

        private SkinnedMeshRenderer CreateBody(
            int subMeshCount,
            params Material[] materials)
        {
            Mesh mesh = new Mesh { subMeshCount = subMeshCount };
            created.Add(mesh);

            GameObject go = new GameObject("Body");
            created.Add(go);

            SkinnedMeshRenderer renderer =
                go.AddComponent<SkinnedMeshRenderer>();

            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            return renderer;
        }
    }
}
