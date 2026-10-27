using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnTargetTests
    {
        private Mesh standard;

        [SetUp]
        public void LoadStandardBody()
        {
            // Fixture discovery only. Production authentication has no asset path.
            const string path = "Assets/Lethal Cats/Mururu/FBX/Mururu.fbx";
            standard = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()
                .SingleOrDefault(mesh => mesh.name == "Body");
            if (standard == null)
                Assert.Ignore("標準Mururu FBXがないため結合テストを実行しません。");
            Assert.IsTrue(standard.isReadable, "Fixture must retain standard readable import settings.");
        }

        [Test]
        public void StandardBody_HasApprovedContentFingerprint()
        {
            Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(standard, out string actual, out var reason));
            Assert.AreEqual(MururuMouthReturnUnsupportedReason.None, reason);
            Assert.IsTrue(actual == MururuMouthReturnFingerprint.Standard ||
                actual == MururuMouthReturnFingerprint.StandardPackage, "Standard fingerprint calibration result");
        }

        [Test]
        public void StandardClone_IsAcceptedRegardlessOfName()
        {
            Mesh copy = UnityEngine.Object.Instantiate(standard);
            try
            {
                copy.name = "Independent purchaser copy";
                Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(copy, out var targets, out var reason));
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.None, reason);
                Assert.AreEqual(84, targets.Neutral.Length);
                Assert.AreEqual(246, targets.Triangles.Length);
                Assert.AreEqual(27, targets.Targets.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        [Test]
        public void Generation_IsRepeatableAndLeavesBodyAndBuiltInNeutralUnchanged()
        {
            Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(standard, out string before, out _));
            Vector3[] neutralBefore = (Vector3[])MururuNeutralMouthData.NeutralPositions.Clone();
            Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(standard, out var first, out _));
            Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(standard, out var second, out _));
            Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(standard, out string after, out _));
            Assert.AreEqual(before, after);
            Assert.AreEqual(neutralBefore, MururuNeutralMouthData.NeutralPositions);
            Assert.AreEqual(first.Neutral, second.Neutral);
            Assert.That(first.NeutralOffsetY * 1000, Is.EqualTo(-.226438f).Within(.001f));
            foreach (string name in first.Targets.Keys)
                Assert.AreEqual(first.Targets[name], second.Targets[name], name);
            float defaultCenter = (first.Targets["default"].Min(p => p.y) + first.Targets["default"].Max(p => p.y)) * .5f;
            float neutralCenter = (first.Neutral.Min(p => p.y) + first.Neutral.Max(p => p.y)) * .5f;
            Assert.That(neutralCenter, Is.EqualTo(defaultCenter).Within(.0000002f));
            first.Neutral[0] = Vector3.zero;
            first.Targets["default"][0] = Vector3.zero;
            Assert.AreNotEqual(first.Neutral[0], second.Neutral[0]);
            Assert.AreNotEqual(first.Targets["default"][0], second.Targets["default"][0]);
            Assert.AreEqual(neutralBefore, MururuNeutralMouthData.NeutralPositions);
        }

        [Test]
        public void OpenEndpoints_CopyTheActualBodySurfaceForEveryDirectTarget()
        {
            Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(standard, out var targets, out _));
            int directCount = 0;
            Vector3[] positions = standard.vertices;
            foreach (var entry in targets.DirectSurface.Where(p => p.Value))
            {
                ++directCount;
                var delta = new Vector3[standard.vertexCount];
                standard.GetBlendShapeFrameVertices(standard.GetBlendShapeIndex(entry.Key), 0,
                    delta, new Vector3[delta.Length], new Vector3[delta.Length]);
                for (int i = 0; i < targets.SurfaceMapping.Length; ++i)
                {
                    int source = targets.SurfaceMapping[i];
                    Assert.AreEqual(positions[source] + delta[source], targets.Targets[entry.Key][i], entry.Key);
                }
            }
            Assert.AreEqual(18, directCount);
        }

        [Test]
        public void ClosedEndpoints_IncludeEveryOriginalRibbonCorner()
        {
            Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(standard, out var targets, out _));
            var a = new Vector3[standard.vertexCount];
            standard.GetBlendShapeFrameVertices(standard.GetBlendShapeIndex("mouth_A"), 0,
                a, new Vector3[a.Length], new Vector3[a.Length]);
            Vector3[] positions = standard.vertices;
            int[] corners = Enumerable.Range(0, a.Length).Where(i => a[i].z < -.005f)
                .GroupBy(i => positions[i]).Select(g => g.First()).ToArray();
            Assert.AreEqual(10, corners.Length);
            foreach (var entry in targets.DirectSurface.Where(p => !p.Value))
            {
                var delta = new Vector3[a.Length];
                if (entry.Key != "default")
                    standard.GetBlendShapeFrameVertices(standard.GetBlendShapeIndex(entry.Key), 0,
                        delta, new Vector3[a.Length], new Vector3[a.Length]);
                foreach (int corner in corners)
                    Assert.IsTrue(targets.Targets[entry.Key].Any(p => Vector3.Distance(p, positions[corner] + delta[corner]) < 1e-9f), entry.Key);
            }
        }

        [TestCase("position")]
        [TestCase("normal")]
        [TestCase("tangent")]
        [TestCase("uv")]
        [TestCase("uv7")]
        [TestCase("color")]
        [TestCase("topology")]
        [TestCase("skin")]
        [TestCase("bindpose")]
        [TestCase("shapeDelta")]
        [TestCase("shapeNormal")]
        [TestCase("shapeTangent")]
        [TestCase("unknownShape")]
        public void UnrelatedBodyChanges_DoNotRejectMouth(string change)
        {
            Mesh copy = UnityEngine.Object.Instantiate(standard);
            try
            {
                switch (change)
                {
                    case "position": var p = copy.vertices; p[0].x += .0001f; copy.vertices = p; break;
                    case "normal": var n = copy.normals; n[0].x += .1f; copy.normals = n; break;
                    case "tangent": var t = copy.tangents; t[0].w = -t[0].w; copy.tangents = t; break;
                    case "uv": var u = copy.uv; u[0].x += .01f; copy.uv = u; break;
                    case "uv7": copy.SetUVs(7, new Vector4[copy.vertexCount]); break;
                    case "color": var c = new Color[copy.vertexCount]; c[0] = Color.red; copy.colors = c; break;
                    case "topology": var indices = copy.GetTriangles(0); int first = indices[0]; indices[0] = indices[1]; indices[1] = first; copy.SetTriangles(indices, 0); break;
                    case "skin": var w = copy.boneWeights; w[0].boneIndex0 = (w[0].boneIndex0 + 1) % copy.bindposes.Length; copy.boneWeights = w; break;
                    case "bindpose": var b = copy.bindposes; b[0][0, 0] += .01f; copy.bindposes = b; break;
                    case "shapeDelta": RebuildShapes(copy, 0); break;
                    case "shapeNormal": RebuildShapes(copy, 1); break;
                    case "shapeTangent": RebuildShapes(copy, 2); break;
                    case "missingShape": copy.ClearBlendShapes(); break;
                    case "unknownShape": var zero = new Vector3[copy.vertexCount]; copy.AddBlendShapeFrame("custom_unconfirmed_mouth", 100, zero, zero, zero); break;
                    default: throw new ArgumentOutOfRangeException(nameof(change));
                }
                Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(copy, out var targets, out var reason));
                Assert.AreEqual(27, targets.Targets.Count);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.None, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        [Test]
        public void UnknownShapes_AreDistinguishedByTheirEffectOnTheMouth()
        {
            Mesh copy = UnityEngine.Object.Instantiate(standard);
            try
            {
                var a = new Vector3[copy.vertexCount];
                int index = copy.GetBlendShapeIndex("mouth_A");
                copy.GetBlendShapeFrameVertices(index, 0, a, null, null);
                var moved = new Vector3[a.Length];
                moved[Array.FindIndex(a, p => p.z > .005f)] = new Vector3(.001f, 0, 0);
                copy.AddBlendShapeFrame("CustomSmile", 100, moved, null, null);
                moved = new Vector3[a.Length];
                moved[Array.FindIndex(a, p => p == Vector3.zero)] = new Vector3(.001f, 0, 0);
                copy.AddBlendShapeFrame("CustomBody", 100, moved, null, null);
                Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(copy, out var targets, out _));
                Assert.That(targets.Targets.Count, Is.EqualTo(27));
                var unknown = MururuMouthReturnTargetGenerator.UnknownMouthShapes(copy);
                Assert.IsTrue(unknown.Contains("CustomSmile"));
                Assert.IsFalse(unknown.Contains("CustomBody"));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        private void RebuildShapes(Mesh copy, int changedChannel)
        {
            copy.ClearBlendShapes();
            for (int shape = 0; shape < standard.blendShapeCount; ++shape)
            {
                for (int frame = 0; frame < standard.GetBlendShapeFrameCount(shape); ++frame)
                {
                    var position = new Vector3[standard.vertexCount];
                    var normal = new Vector3[position.Length];
                    var tangent = new Vector3[position.Length];
                    standard.GetBlendShapeFrameVertices(shape, frame, position, normal, tangent);
                    if (standard.GetBlendShapeName(shape) == "mouth_A" && frame == 0)
                    {
                        Vector3[] channel = changedChannel == 0 ? position : changedChannel == 1 ? normal : tangent;
                        channel[0].x += .0001f;
                    }
                    copy.AddBlendShapeFrame(standard.GetBlendShapeName(shape), standard.GetBlendShapeFrameWeight(shape, frame), position, normal, tangent);
                }
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UnrelatedColorData_DoesNotRejectMouth(float invalid)
        {
            Mesh copy = UnityEngine.Object.Instantiate(standard);
            try
            {
                var colors = new Color[copy.vertexCount];
                colors[0].r = invalid;
                copy.colors = colors;
                Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(copy, out var targets, out var reason));
                Assert.AreEqual(27, targets.Targets.Count);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.None, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
    }

    internal sealed class MururuMouthReturnInputTests
    {
        [Test]
        public void MissingBody_ReturnsExplicitReason()
        {
            Assert.IsFalse(MururuMouthReturnTargetGenerator.TryGenerate(null, out var targets, out var reason));
            Assert.IsNull(targets);
            Assert.AreEqual(MururuMouthReturnUnsupportedReason.MissingBody, reason);
        }

        [Test]
        public void UnreadableBody_ReturnsExplicitReason()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.UploadMeshData(true);
                Assert.IsFalse(MururuMouthReturnTargetGenerator.TryGenerate(mesh, out var targets, out var reason));
                Assert.IsNull(targets);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.BodyNotReadable, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
