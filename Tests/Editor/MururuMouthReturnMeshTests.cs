using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnMeshTests
    {
        private Mesh body, sdk;

        [SetUp]
        public void CreateOriginalSdkFixture()
        {
            const string path = "Assets/Lethal Cats/Mururu/FBX/Mururu.fbx";
            body = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().SingleOrDefault(m => m.name == "Body");
            if (body == null)
                Assert.Ignore("標準Mururu FBXがないため結合テストを実行しません。");
            int[] mapping = MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(body);
            Type type = typeof(MururuBlinkLipSyncSetupWindow);
            int[] triangles = (int[])type.GetMethod("BuildNeutralTriangles", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { body, mapping });
            sdk = (Mesh)type.GetMethod("GenerateLipSyncMouth", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { body, mapping, triangles });
        }

        [TearDown]
        public void DestroyFixture()
        {
            if (sdk != null)
                UnityEngine.Object.DestroyImmediate(sdk);
        }

        [Test]
        public void CurrentSdkGenerator_HasApprovedContentFingerprint()
        {
            Assert.AreEqual(1, sdk.subMeshCount, "Material selection is a Renderer concern; current generator always creates one submesh.");
            Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(sdk, out string actual, out _));
            Assert.IsTrue(actual == MururuMouthReturnMeshAssembler.StandardSdkFingerprint ||
                actual == MururuMouthReturnMeshAssembler.StandardPackageSdkFingerprint, "SDK fingerprint calibration result");
        }

        [Test]
        public void Assembly_PreservesInputAndAllCopiedAttributes()
        {
            string beforeBody = Fingerprint(body), beforeSdk = Fingerprint(sdk);
            Vector3[] neutral = (Vector3[])MururuNeutralMouthData.NeutralPositions.Clone();
            sdk.name = "Independent SDK clone without a path";
            Mesh output = Assemble();
            try
            {
                Assert.AreNotSame(sdk, output);
                Assert.AreEqual(beforeBody, Fingerprint(body));
                Assert.AreEqual(beforeSdk, Fingerprint(sdk));
                Assert.AreEqual(neutral, MururuNeutralMouthData.NeutralPositions);
                Assert.AreEqual(sdk.normals, output.normals);
                Assert.AreEqual(sdk.tangents, output.tangents);
                Assert.AreEqual(sdk.colors, output.colors);
                Assert.AreEqual(sdk.boneWeights, output.boneWeights);
                Assert.AreEqual(sdk.bindposes, output.bindposes);
                var originalBones = sdk.GetBonesPerVertex();
                var outputBones = output.GetBonesPerVertex();
                Assert.AreEqual(originalBones.ToArray(), outputBones.ToArray());
                Assert.AreEqual(sdk.GetAllBoneWeights().ToArray(), output.GetAllBoneWeights().ToArray());
                for (int channel = 0; channel < 8; ++channel)
                {
                    var originalUv = new System.Collections.Generic.List<Vector4>();
                    var outputUv = new System.Collections.Generic.List<Vector4>();
                    sdk.GetUVs(channel, originalUv); output.GetUVs(channel, outputUv);
                    Assert.AreEqual(originalUv, outputUv, "UV" + channel);
                }
                Assert.AreEqual(sdk.subMeshCount, output.subMeshCount);
                for (int submesh = 0; submesh < sdk.subMeshCount; ++submesh)
                {
                    Assert.AreEqual(sdk.GetTopology(submesh), output.GetTopology(submesh));
                    Assert.AreEqual(sdk.GetIndices(submesh), output.GetIndices(submesh));
                }
                Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(body, out var targets, out _));
                Assert.AreEqual(targets.Neutral, output.vertices);
                Assert.AreEqual(sdk.blendShapeCount + 28, output.blendShapeCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(output); }
        }

        [Test]
        public void SdkFrames_KeepVowelAbsoluteEndpointsAndNoneHideRelativeDeltas()
        {
            Mesh output = Assemble();
            try
            {
                Vector3[] oldBase = sdk.vertices, newBase = output.vertices;
                for (int shape = 0; shape < sdk.blendShapeCount; ++shape)
                {
                    string name = sdk.GetBlendShapeName(shape);
                    Assert.AreEqual(name, output.GetBlendShapeName(shape));
                    Assert.AreEqual(sdk.GetBlendShapeFrameCount(shape), output.GetBlendShapeFrameCount(shape));
                    Frame original = Read(sdk, name), changed = Read(output, name);
                    Assert.AreEqual(sdk.GetBlendShapeFrameWeight(shape, 0), output.GetBlendShapeFrameWeight(shape, 0));
                    Assert.AreEqual(original.Normal, changed.Normal, name);
                    Assert.AreEqual(original.Tangent, changed.Tangent, name);
                    for (int i = 0; i < newBase.Length; ++i)
                        Assert.IsTrue(output.bounds.Contains(newBase[i] + changed.Position[i]), name);
                    if (name == "SE_mouth_none" || name == "SE_hide_self")
                        Assert.AreEqual(original.Position, changed.Position, name);
                    else
                        for (int i = 0; i < oldBase.Length; ++i)
                            Assert.That(Vector3.Distance(oldBase[i] + original.Position[i], newBase[i] + changed.Position[i]), Is.LessThan(.0000002f), name);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(output); }
        }

        [Test]
        public void BridgeBaseAndShapeDeltas_ReconstructAll27PositionNormalTangentEndpoints()
        {
            Assert.IsTrue(MururuMouthReturnTargetGenerator.TryGenerate(body, out var targets, out _));
            Mesh output = Assemble();
            Mesh temporary = UnityEngine.Object.Instantiate(sdk);
            try
            {
                temporary.ClearBlendShapes();
                Frame basis = Read(output, MururuMouthReturnMeshAssembler.DefaultShapeName);
                Vector3[] basePosition = output.vertices, baseNormal = output.normals;
                Vector4[] baseTangent = output.tangents;
                Vector3[] sourceNormal = body.normals;
                Vector4[] sourceTangent = body.tangents;
                int changedHandedness = 0;
                foreach (string name in targets.Targets.Keys)
                {
                    Frame shape = name == "default" ? Zero(output.vertexCount)
                        : Read(output, MururuMouthReturnMeshAssembler.TargetShapeName(name));
                    Vector3[] expectedNormal, expectedTangent;
                    if (targets.DirectSurface[name])
                    {
                        Frame source = Read(body, name);
                        expectedNormal = targets.SurfaceMapping.Select(i => sourceNormal[i] + source.Normal[i]).ToArray();
                        expectedTangent = targets.SurfaceMapping.Select(i => (Vector3)sourceTangent[i] + source.Tangent[i]).ToArray();
                    }
                    else
                    {
                        temporary.vertices = targets.Targets[name];
                        temporary.RecalculateNormals(); temporary.RecalculateTangents();
                        expectedNormal = temporary.normals;
                        Vector4[] tangent = temporary.tangents;
                        expectedTangent = tangent.Select(t => (Vector3)t).ToArray();
                        changedHandedness += tangent.Where((t, i) => t.w != baseTangent[i].w).Count();
                    }
                    for (int i = 0; i < output.vertexCount; ++i)
                    {
                        Assert.That(Vector3.Distance(targets.Targets[name][i], basePosition[i] + basis.Position[i] + shape.Position[i]), Is.LessThan(.0000002f), name);
                        Assert.That(Vector3.Distance(expectedNormal[i], baseNormal[i] + basis.Normal[i] + shape.Normal[i]), Is.LessThan(.000002f), name);
                        Assert.That(Vector3.Distance(expectedTangent[i], (Vector3)baseTangent[i] + basis.Tangent[i] + shape.Tangent[i]), Is.LessThan(.000002f), name);
                        Assert.IsTrue(output.bounds.Contains(targets.Targets[name][i]), name);
                    }
                }
                Debug.Log("MouthReturnClosedTangentHandednessDifferences=" + changedHandedness);
                Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(output, out _, out _), "Every serialized output value must be finite.");
                Frame clock = Read(output, MururuMouthReturnMeshAssembler.ClockShapeName);
                Assert.AreEqual(Zero(output.vertexCount).Position, clock.Position);
                Assert.AreEqual(Zero(output.vertexCount).Normal, clock.Normal);
                Assert.AreEqual(Zero(output.vertexCount).Tangent, clock.Tangent);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
                UnityEngine.Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void Assembly_IsRepeatableAndExistingBridgeIsExplicitlyRejected()
        {
            Mesh first = Assemble(), second = Assemble();
            try
            {
                Assert.AreEqual(Fingerprint(first), Fingerprint(second));
                string before = Fingerprint(first);
                Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(body, first, out var duplicate, out var reason));
                Assert.IsNull(duplicate);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.BridgeShapeConflict, reason);
                Assert.AreEqual(before, Fingerprint(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [TestCase("position")]
        [TestCase("uv")]
        [TestCase("submesh")]
        [TestCase("missingShape")]
        [TestCase("unknownShape")]
        public void UnrecognizedSdkContent_IsRejectedBeforeAllocatingOutput(string change)
        {
            switch (change)
            {
                case "position": var p = sdk.vertices; p[0].x += .0001f; sdk.vertices = p; break;
                case "uv": var uv = sdk.uv; uv[0].x += .01f; sdk.uv = uv; break;
                case "submesh": sdk.subMeshCount = 2; break;
                case "missingShape": sdk.ClearBlendShapes(); break;
                case "unknownShape": var zero = new Vector3[sdk.vertexCount]; sdk.AddBlendShapeFrame("unknown_sdk_shape", 100, zero, zero, zero); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
            string before = Fingerprint(sdk);
            Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out var output, out var reason));
            Assert.IsNull(output);
            Assert.AreEqual(MururuMouthReturnUnsupportedReason.UnrecognizedSdkMouth, reason);
            Assert.AreEqual(before, Fingerprint(sdk));
        }

        [Test]
        public void UnrecognizedBodyAndNonFiniteSdk_AreExplicitlyRejected()
        {
            Mesh changed = UnityEngine.Object.Instantiate(body);
            try
            {
                changed.ClearBlendShapes();
                Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(changed, sdk, out var output, out var reason));
                Assert.IsNull(output);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.UnrecognizedContent, reason);
                var colors = new Color[sdk.vertexCount]; colors[0].r = float.NaN; sdk.colors = colors;
                Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out output, out reason));
                Assert.IsNull(output);
                Assert.AreEqual(MururuMouthReturnUnsupportedReason.NonFiniteContent, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(changed); }
        }

        [TestCase("mouth_tongue", false)] [TestCase("mouth_tongue", true)]
        [TestCase("mouth_A", false)] [TestCase("mouth_A", true)]
        public void UnavailableShape_DoesNotDisableOtherMouths(string unavailable, bool extraFrame)
        {
            Mesh changed = CopyWithMouthChange(unavailable, extraFrame, false);
            Mesh output = null;
            try
            {
                Assert.IsTrue(MururuMouthReturnMeshAssembler.TryAssemble(changed, sdk, out output, out var reason), reason.ToString());
                Assert.That(output.GetBlendShapeIndex(MururuMouthReturnMeshAssembler.TargetShapeName(unavailable)), Is.EqualTo(-1));
                Assert.That(output.GetBlendShapeIndex(MururuMouthReturnMeshAssembler.TargetShapeName("mouth_good")), Is.GreaterThanOrEqualTo(0));
                var available = Enumerable.Range(0, output.blendShapeCount).Select(output.GetBlendShapeName).ToArray();
                var values = MururuMouthReturnFxRoutesPose.Properties.ToDictionary(n => n, _ => 0f);
                values[unavailable] = 100;
                Assert.IsFalse(MururuMouthReturnFxRoutesPose.Classify(values).WithGeometry(available).Supported);
                values[unavailable] = 0; values["mouth_good"] = 100;
                Assert.IsTrue(MururuMouthReturnFxRoutesPose.Classify(values).WithGeometry(available).Supported);
                values["mouth_good"] = 0;
                Assert.IsTrue(MururuMouthReturnFxRoutesPose.Classify(values).WithGeometry(available).Supported);
            }
            finally { if (output) UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(changed); }
        }

        [Test]
        public void UnavailableRibbon_KeepsIndependentOpenEndpoints()
        {
            Mesh changed = CopyWithMouthChange(null, false, true);
            Mesh output = null;
            try
            {
                Assert.IsTrue(MururuMouthReturnMeshAssembler.TryAssemble(changed, sdk, out output, out var reason), reason.ToString());
                var available = Enumerable.Range(0, output.blendShapeCount).Select(output.GetBlendShapeName).ToArray();
                Assert.That(output.GetBlendShapeIndex(MururuMouthReturnMeshAssembler.DefaultShapeName), Is.EqualTo(-1));
                var values = MururuMouthReturnFxRoutesPose.Properties.ToDictionary(n => n, _ => 0f);
                Assert.IsFalse(MururuMouthReturnFxRoutesPose.Classify(values).WithGeometry(available).Supported);
                values["mouth_A"] = 100;
                Assert.IsTrue(MururuMouthReturnFxRoutesPose.Classify(values).WithGeometry(available).Supported);
            }
            finally { if (output) UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(changed); }
        }

        Mesh CopyWithMouthChange(string changedName, bool extraFrame, bool removeRibbon)
        {
            var copy = UnityEngine.Object.Instantiate(body); copy.ClearBlendShapes();
            for (int shape = 0; shape < body.blendShapeCount; shape++)
            {
                string name = body.GetBlendShapeName(shape);
                if (name == changedName && !extraFrame) continue;
                var p = new Vector3[body.vertexCount]; var n = new Vector3[p.Length]; var t = new Vector3[p.Length];
                body.GetBlendShapeFrameVertices(shape, 0, p, n, t);
                if (removeRibbon && new[] { "mouth_A", "mouth_I", "mouth_U", "mouth_E", "mouth_O" }.Contains(name))
                    for (int i = 0; i < p.Length; i++) if (p[i].z < -.005f) p[i] = Vector3.zero;
                if (name == changedName && extraFrame) copy.AddBlendShapeFrame(name, 50, p, n, t);
                copy.AddBlendShapeFrame(name, 100, p, n, t);
            }
            return copy;
        }

        private Mesh Assemble()
        {
            Assert.IsTrue(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out var output, out var reason), reason.ToString());
            return output;
        }

        private static string Fingerprint(Mesh mesh)
        {
            Assert.IsTrue(MururuMouthReturnFingerprint.TryCompute(mesh, out string hash, out _));
            return hash;
        }

        private sealed class Frame
        {
            internal Vector3[] Position, Normal, Tangent;
        }

        private static Frame Zero(int count) => new Frame
        {
            Position = new Vector3[count], Normal = new Vector3[count], Tangent = new Vector3[count]
        };

        private static Frame Read(Mesh mesh, string name)
        {
            int index = mesh.GetBlendShapeIndex(name);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
            var frame = Zero(mesh.vertexCount);
            mesh.GetBlendShapeFrameVertices(index, 0, frame.Position, frame.Normal, frame.Tangent);
            return frame;
        }
    }

    internal sealed class MururuMouthReturnMeshInputTests
    {
        [Test]
        public void BridgeNames_RejectUnknownSourcesAndRecognizeDefault()
        {
            Assert.AreEqual(MururuMouthReturnMeshAssembler.DefaultShapeName,
                MururuMouthReturnMeshAssembler.TargetShapeName("default"));
            Assert.AreEqual("MBLS_Bridge_mouth_A", MururuMouthReturnMeshAssembler.TargetShapeName("mouth_A"));
            Assert.Throws<ArgumentNullException>(() => MururuMouthReturnMeshAssembler.TargetShapeName(null));
            Assert.Throws<ArgumentException>(() => MururuMouthReturnMeshAssembler.TargetShapeName("unknown"));
        }

        [Test]
        public void MissingSdk_ReturnsExplicitReason()
        {
            Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(null, null, out var output, out var reason));
            Assert.IsNull(output);
            Assert.AreEqual(MururuMouthReturnUnsupportedReason.MissingSdkMouth, reason);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidSdk_IsRejectedWithoutRequiringSourceFixture(bool collision)
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                if (collision)
                {
                    var zero = new Vector3[3];
                    mesh.AddBlendShapeFrame(MururuMouthReturnMeshAssembler.ClockShapeName, 100, zero, zero, zero);
                }
                else
                    mesh.UploadMeshData(true);
                Assert.IsFalse(MururuMouthReturnMeshAssembler.TryAssemble(null, mesh, out var output, out var reason));
                Assert.IsNull(output);
                Assert.AreEqual(collision ? MururuMouthReturnUnsupportedReason.BridgeShapeConflict
                    : MururuMouthReturnUnsupportedReason.SdkMouthNotReadable, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
