using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;

namespace MururuBlinkLipSync.Editor
{
    internal enum MururuMouthReturnUnsupportedReason
    {
        None,
        MissingBody,
        BodyNotReadable,
        NonFiniteContent,
        UnrecognizedContent,
        MissingSdkMouth,
        SdkMouthNotReadable,
        UnrecognizedSdkMouth,
        BridgeShapeConflict,
        BindposeMismatch
    }

    // Content fingerprinting for finite-data validation and regression comparisons.
    // Float32 values and array order are exact: no rounding or almost-equal match.
    internal static class MururuMouthReturnFingerprint
    {
        // Measured from the standard FBX with its original import metadata.
        internal const string Standard = "B661B90D47851317FD3E057F27C453914CA5FB3EB89372CD7C8625D12EFE1F9D";
        // Standard distributed FBX: two bindpose elements differ by one Float32 ULP.
        internal const string StandardPackage = "62B4E109A448A11847176980C5CA745E9B2AFD41586D55DBC7E2265FCAE90EB5";

        internal static bool TryCompute(
            Mesh body,
            out string fingerprint,
            out MururuMouthReturnUnsupportedReason reason)
        {
            fingerprint = null;
            if (body == null)
            {
                reason = MururuMouthReturnUnsupportedReason.MissingBody;
                return false;
            }
            if (!body.isReadable)
            {
                reason = MururuMouthReturnUnsupportedReason.BodyNotReadable;
                return false;
            }

            using (var data = new MemoryStream())
            using (var writer = new ContentWriter(data))
            {
                // Version the byte format so later additions cannot silently use
                // the old allowlist. BinaryWriter specifies little-endian values.
                writer.Write("MururuMouthReturnMesh/v1");
                writer.Write(body.vertexCount);
                writer.Write(body.vertices);
                writer.Write(body.normals);
                writer.Write(body.tangents);
                var colors = body.colors;
                writer.Write(colors.Length);
                foreach (Color color in colors)
                {
                    writer.Write(color.r); writer.Write(color.g);
                    writer.Write(color.b); writer.Write(color.a);
                }

                var uv = new List<Vector4>();
                for (int channel = 0; channel < 8; ++channel)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    writer.Write(body.HasVertexAttribute(attribute));
                    if (!body.HasVertexAttribute(attribute))
                        continue;
                    writer.Write(body.GetVertexAttributeDimension(attribute));
                    uv.Clear();
                    body.GetUVs(channel, uv);
                    writer.Write(uv.Count);
                    foreach (Vector4 value in uv)
                        writer.Write(value);
                }

                // These NativeArrays are read-only views owned by Mesh, not
                // allocations owned by this method. Do not dispose them.
                var bonesPerVertex = body.GetBonesPerVertex();
                var weights = body.GetAllBoneWeights();
                {
                    writer.Write(bonesPerVertex.Length);
                    foreach (byte count in bonesPerVertex)
                        writer.Write(count);
                    writer.Write(weights.Length);
                    foreach (BoneWeight1 weight in weights)
                    {
                        writer.Write(weight.boneIndex);
                        writer.Write(weight.weight);
                    }
                }
                Matrix4x4[] bindposes = body.bindposes;
                writer.Write(bindposes.Length);
                foreach (Matrix4x4 bindpose in bindposes)
                    for (int row = 0; row < 4; ++row)
                        for (int column = 0; column < 4; ++column)
                            writer.Write(bindpose[row, column]);

                writer.Write(body.bounds.center);
                writer.Write(body.bounds.size);
                writer.Write(body.subMeshCount);
                for (int submesh = 0; submesh < body.subMeshCount; ++submesh)
                {
                    writer.Write((int)body.GetTopology(submesh));
                    int[] indices = body.GetIndices(submesh);
                    writer.Write(indices.Length);
                    foreach (int index in indices)
                        writer.Write(index);
                }

                var position = new Vector3[body.vertexCount];
                var normal = new Vector3[body.vertexCount];
                var tangent = new Vector3[body.vertexCount];
                writer.Write(body.blendShapeCount);
                for (int shape = 0; shape < body.blendShapeCount; ++shape)
                {
                    writer.Write(body.GetBlendShapeName(shape));
                    int frames = body.GetBlendShapeFrameCount(shape);
                    writer.Write(frames);
                    for (int frame = 0; frame < frames; ++frame)
                    {
                        writer.Write(body.GetBlendShapeFrameWeight(shape, frame));
                        body.GetBlendShapeFrameVertices(shape, frame, position, normal, tangent);
                        writer.Write(position);
                        writer.Write(normal);
                        writer.Write(tangent);
                    }
                }

                if (writer.HasNonFiniteValue)
                {
                    reason = MururuMouthReturnUnsupportedReason.NonFiniteContent;
                    return false;
                }

                writer.Flush();
                data.Position = 0;
                using (SHA256 hash = SHA256.Create())
                    fingerprint = BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
            }

            reason = MururuMouthReturnUnsupportedReason.None;
            return true;
        }

        private sealed class ContentWriter : BinaryWriter
        {
            internal bool HasNonFiniteValue { get; private set; }

            internal ContentWriter(Stream output) : base(output) { }

            public override void Write(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    HasNonFiniteValue = true;
                base.Write(value);
            }

            internal void Write(Vector3 value)
            {
                Write(value.x); Write(value.y); Write(value.z);
            }

            internal void Write(Vector4 value)
            {
                Write(value.x); Write(value.y); Write(value.z); Write(value.w);
            }

            internal void Write(Vector3[] values)
            {
                Write(values.Length);
                foreach (Vector3 value in values)
                    Write(value);
            }

            internal void Write(Vector4[] values)
            {
                Write(values.Length);
                foreach (Vector4 value in values)
                    Write(value);
            }
        }
    }
}
