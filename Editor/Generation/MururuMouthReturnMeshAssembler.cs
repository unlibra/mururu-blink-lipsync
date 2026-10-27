using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    // Produces an unattached, caller-owned clone. No assets or renderers are edited.
    internal static class MururuMouthReturnMeshAssembler
    {
        internal const string StandardSdkFingerprint = "9D2A97095BC9C08BF23D782B8E262B81E45BBD6439E3454B11B6AB862E77E2C5";
        internal const string StandardPackageSdkFingerprint = "1AD16E5FD64A1B66CE341FB4B115183A2838FDB481674AFD27D91F8A1C241C75";
        internal const string DefaultShapeName = "MBLS_Bridge_Default";
        internal const string ClockShapeName = "MBLS_Bridge_Clock";
        private const string BridgePrefix = "MBLS_Bridge_";
        private static readonly HashSet<string> Vowels = new HashSet<string>(StringComparer.Ordinal)
        {
            "SE_mouth_A", "SE_mouth_I", "SE_mouth_U", "SE_mouth_E", "SE_mouth_O"
        };

        internal static string TargetShapeName(string sourceName)
        {
            if (sourceName == null)
                throw new ArgumentNullException(nameof(sourceName));
            if (sourceName == "default")
                return DefaultShapeName;
            if (!MururuMouthReturnTargetGenerator.IsSupportedSourceShape(sourceName))
                throw new ArgumentException("Unknown source mouth shape.", nameof(sourceName));
            return BridgePrefix + sourceName;
        }

        internal static bool TryAssemble(
            Mesh body,
            Mesh sdkMouth,
            out Mesh assembled,
            out MururuMouthReturnUnsupportedReason reason)
        {
            assembled = null;
            if (!ValidateSdkMouth(sdkMouth, out reason))
                return false;
            if (!MururuMouthReturnTargetGenerator.TryGenerate(body, out var targets, out reason))
                return false;
            var bodyPoses = body.bindposes; var sdkPoses = sdkMouth.bindposes;
            if (sdkMouth.GetAllBoneWeights().Any(w => w.weight > 0 &&
                (w.boneIndex >= bodyPoses.Length || w.boneIndex >= sdkPoses.Length ||
                 !bodyPoses[w.boneIndex].Equals(sdkPoses[w.boneIndex]))))
            {
                reason = MururuMouthReturnUnsupportedReason.BindposeMismatch;
                return false;
            }
            if (!MatchesBody(body, sdkMouth, targets))
            { reason = MururuMouthReturnUnsupportedReason.UnrecognizedSdkMouth; return false; }

            // Recompute closed targets on the original SDK topology/UV. Open
            // targets retain the purchaser's actual final normal/tangent values.
            Dictionary<string, SurfaceVectors> vectors = BuildSurfaceVectors(body, sdkMouth, targets);
            if (vectors.Count == 0) { reason = MururuMouthReturnUnsupportedReason.UnrecognizedContent; return false; }
            List<Frame> sdkFrames = ReadFrames(sdkMouth);
            Vector3[] baseNormals = sdkMouth.normals;
            Vector3[] baseTangents = TangentXyz(sdkMouth.tangents);

            Mesh copy = UnityEngine.Object.Instantiate(sdkMouth);
            bool completed = false;
            try
            {
                CorrectSdkFrames(copy, sdkMouth.vertices, targets.Neutral, sdkFrames);

                bool hasDefault = vectors.TryGetValue("default", out var defaultVectors);
                if (!hasDefault) defaultVectors = new SurfaceVectors { Normal = baseNormals, Tangent = baseTangents };
                Vector3[] defaultPosition = hasDefault ? targets.Targets["default"] : targets.Neutral;
                if (hasDefault) AddDifference(copy, DefaultShapeName,
                    defaultPosition, targets.Neutral, defaultVectors.Normal, baseNormals, defaultVectors.Tangent, baseTangents);
                foreach (string name in targets.Targets.Keys.Where(n => n != "default").OrderBy(n => n, StringComparer.Ordinal))
                {
                    AddDifference(copy, TargetShapeName(name),
                        targets.Targets[name], defaultPosition,
                        vectors[name].Normal, defaultVectors.Normal,
                        vectors[name].Tangent, defaultVectors.Tangent);
                }
                var zero = new Vector3[copy.vertexCount];
                copy.AddBlendShapeFrame(ClockShapeName, 100f, zero, zero, zero);

                // Enclose all supported endpoint positions, including relative
                // none/hide poses. SkinnedRenderer.localBounds stays the caller's responsibility.
                copy.bounds = CalculateBounds(sdkMouth.bounds, targets.Neutral, targets.Targets.Values, sdkFrames);
                assembled = copy;
                completed = true;
                reason = MururuMouthReturnUnsupportedReason.None;
                return true;
            }
            finally
            {
                // Programming defects propagate; cleanup does not turn them
                // into an unsupported-input result or leak a half-built Mesh.
                if (!completed)
                    UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private static void CorrectSdkFrames(Mesh copy, Vector3[] oldBase, Vector3[] neutral, List<Frame> sdkFrames)
        {
            copy.ClearBlendShapes();
            copy.vertices = neutral;
            foreach (Frame frame in sdkFrames)
            {
                // Translation changes the base only. Preserve SDK vowel
                // absolute endpoints, while none/hide remain relative moves.
                if (Vowels.Contains(frame.Name))
                    for (int i = 0; i < frame.Position.Length; ++i)
                        frame.Position[i] += oldBase[i] - neutral[i];
                copy.AddBlendShapeFrame(frame.Name, frame.Weight, frame.Position, frame.Normal, frame.Tangent);
            }
        }

        private static bool ValidateSdkMouth(Mesh mesh, out MururuMouthReturnUnsupportedReason reason)
        {
            if (mesh == null)
            {
                reason = MururuMouthReturnUnsupportedReason.MissingSdkMouth;
                return false;
            }
            if (!mesh.isReadable)
            {
                reason = MururuMouthReturnUnsupportedReason.SdkMouthNotReadable;
                return false;
            }
            for (int shape = 0; shape < mesh.blendShapeCount; ++shape)
                if (mesh.GetBlendShapeName(shape).StartsWith(BridgePrefix, StringComparison.Ordinal))
                {
                    reason = MururuMouthReturnUnsupportedReason.BridgeShapeConflict;
                    return false;
                }
            if (!MururuMouthReturnFingerprint.TryCompute(mesh, out _, out reason))
                return false;
            string[] names = Vowels.Concat(new[] { "SE_mouth_none", "SE_hide_self" }).ToArray();
            if (mesh.blendShapeCount != names.Length || names.Any(name =>
                mesh.GetBlendShapeIndex(name) < 0 || mesh.GetBlendShapeFrameCount(mesh.GetBlendShapeIndex(name)) != 1 ||
                mesh.GetBlendShapeFrameWeight(mesh.GetBlendShapeIndex(name), 0) != 100))
            { reason = MururuMouthReturnUnsupportedReason.UnrecognizedSdkMouth; return false; }
            reason = MururuMouthReturnUnsupportedReason.None;
            return true;
        }

        private static bool MatchesBody(Mesh body, Mesh sdk, MururuMouthReturnTargets targets)
        {
            var map = targets.SurfaceMapping;
            if (sdk.vertexCount != map.Length || sdk.subMeshCount != 1 ||
                !sdk.vertices.SequenceEqual(MururuNeutralMouthData.NeutralPositions) ||
                !sdk.triangles.SequenceEqual(targets.Triangles)) return false;
            var weights = body.boneWeights;
            if (weights.Length != body.vertexCount || !sdk.boneWeights.SequenceEqual(map.Select(i => weights[i]))) return false;
            for (int channel = 0; channel < 8; channel++)
            {
                var source = new List<Vector4>(); var generated = new List<Vector4>();
                body.GetUVs(channel, source); sdk.GetUVs(channel, generated);
                if (source.Count == 0 ? generated.Count != 0 : !generated.SequenceEqual(map.Select(i => source[i]))) return false;
            }
            foreach (string name in new[] { "SE_mouth_none", "SE_hide_self" })
            {
                var delta = new Vector3[sdk.vertexCount];
                sdk.GetBlendShapeFrameVertices(sdk.GetBlendShapeIndex(name), 0, delta, null, null);
                for (int i = 0; i < delta.Length; i++)
                {
                    Vector3 expected = name == "SE_hide_self" ? new Vector3(0, 0, MururuNeutralMouthData.HideSelfDeltaZ) :
                        new Vector3(0, MururuNeutralMouthData.NoneAdjustmentIndices.Contains(i) ? MururuNeutralMouthData.NoneAdjustmentY : 0, 0);
                    if (!delta[i].Equals(expected)) return false;
                }
            }
            return true;
        }

        private sealed class SurfaceVectors
        {
            internal Vector3[] Normal, Tangent;
        }

        private sealed class Frame
        {
            internal string Name;
            internal float Weight;
            internal Vector3[] Position, Normal, Tangent;
        }

        private static Dictionary<string, SurfaceVectors> BuildSurfaceVectors(
            Mesh body, Mesh sdkMouth, MururuMouthReturnTargets targets)
        {
            var result = new Dictionary<string, SurfaceVectors>(StringComparer.Ordinal);
            Vector3[] bodyNormals = body.normals;
            Vector4[] bodyTangents = body.tangents;
            Mesh temporary = UnityEngine.Object.Instantiate(sdkMouth);
            try
            {
                temporary.ClearBlendShapes();
                foreach (string name in targets.Targets.Keys.ToArray())
                {
                    Vector3[] normal, tangent;
                    if (targets.DirectSurface[name] && bodyNormals.Length == body.vertexCount && bodyTangents.Length == body.vertexCount)
                    {
                        var positionDelta = new Vector3[body.vertexCount];
                        var normalDelta = new Vector3[body.vertexCount];
                        var tangentDelta = new Vector3[body.vertexCount];
                        body.GetBlendShapeFrameVertices(body.GetBlendShapeIndex(name), 0,
                            positionDelta, normalDelta, tangentDelta);
                        normal = targets.SurfaceMapping.Select(i => bodyNormals[i] + normalDelta[i]).ToArray();
                        tangent = targets.SurfaceMapping.Select(i => (Vector3)bodyTangents[i] + tangentDelta[i]).ToArray();
                    }
                    else
                    {
                        temporary.vertices = targets.Targets[name];
                        temporary.RecalculateNormals();
                        temporary.RecalculateTangents();
                        normal = temporary.normals;
                        tangent = TangentXyz(temporary.tangents);
                    }
                    if (!Finite(normal) || !Finite(tangent))
                    { targets.Targets.Remove(name); targets.DirectSurface.Remove(name); continue; }
                    result.Add(name, new SurfaceVectors { Normal = normal, Tangent = tangent });
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
            return result;
        }

        private static List<Frame> ReadFrames(Mesh mesh)
        {
            var frames = new List<Frame>();
            for (int shape = 0; shape < mesh.blendShapeCount; ++shape)
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); ++frame)
                {
                    var value = new Frame
                    {
                        Name = mesh.GetBlendShapeName(shape),
                        Weight = mesh.GetBlendShapeFrameWeight(shape, frame),
                        Position = new Vector3[mesh.vertexCount],
                        Normal = new Vector3[mesh.vertexCount],
                        Tangent = new Vector3[mesh.vertexCount]
                    };
                    mesh.GetBlendShapeFrameVertices(shape, frame, value.Position, value.Normal, value.Tangent);
                    frames.Add(value);
                }
            return frames;
        }

        private static Vector3[] TangentXyz(Vector4[] values) => values.Select(v => (Vector3)v).ToArray();

        private static Bounds CalculateBounds(
            Bounds original, Vector3[] neutral, IEnumerable<Vector3[]> targets, List<Frame> frames)
        {
            // Accumulate exact float extrema rather than repeatedly converting
            // between min/max and Bounds.center/extents, which can lose extrema.
            Vector3 minimum = original.min, maximum = original.max;
            foreach (Vector3 position in neutral)
            {
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
            }
            foreach (Vector3[] endpoint in targets)
                foreach (Vector3 position in endpoint)
                {
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
            foreach (Frame frame in frames)
                for (int i = 0; i < neutral.Length; ++i)
                {
                    Vector3 position = neutral[i] + frame.Position[i];
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }

            // 2^-23 is float32 machine epsilon (not System.Single.Epsilon).
            // Eight relative roundoff units cover the final center/extents
            // conversion and Contains' subtraction without rounding an edge in.
            const float roundoffMargin = 8f * 1.1920928955078125e-7f;
            var magnitude = new Vector3(
                Mathf.Max(Mathf.Abs(minimum.x), Mathf.Abs(maximum.x)),
                Mathf.Max(Mathf.Abs(minimum.y), Mathf.Abs(maximum.y)),
                Mathf.Max(Mathf.Abs(minimum.z), Mathf.Abs(maximum.z)));
            Vector3 padding = magnitude * roundoffMargin;
            var result = new Bounds();
            result.SetMinMax(minimum - padding, maximum + padding);
            return result;
        }

        private static void AddDifference(
            Mesh destination, string name,
            Vector3[] finalPosition, Vector3[] basePosition,
            Vector3[] finalNormal, Vector3[] baseNormal,
            Vector3[] finalTangent, Vector3[] baseTangent)
        {
            Vector3[] position = Difference(finalPosition, basePosition);
            Vector3[] normal = Difference(finalNormal, baseNormal);
            Vector3[] tangent = Difference(finalTangent, baseTangent);
            destination.AddBlendShapeFrame(name, 100f, position, normal, tangent);
        }

        private static Vector3[] Difference(Vector3[] final, Vector3[] basis)
        {
            var result = new Vector3[final.Length];
            for (int i = 0; i < result.Length; ++i)
                result[i] = final[i] - basis[i];
            RequireFinite(result);
            return result;
        }

        private static void RequireFinite(Vector3[] values)
        {
            if (!Finite(values))
                throw new InvalidOperationException("Generated mouth vectors must be finite.");
        }
        private static bool Finite(Vector3[] values) => values.All(v =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
            || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)));
    }
}
