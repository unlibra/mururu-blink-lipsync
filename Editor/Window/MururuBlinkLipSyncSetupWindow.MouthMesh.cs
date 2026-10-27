using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using nadena.dev.modular_avatar.core;

namespace MururuBlinkLipSync.Editor
{
public partial class MururuBlinkLipSyncSetupWindow
{
    // Returns the Body submesh (= material slot) that owns most mouth triangles.
    private static int FindMouthSubMeshIndex(
        Mesh body,
        int[] mapping)
    {
        HashSet<int> mouthVertices =
            new HashSet<int>(mapping);

        int bestSubMesh = 0;
        int bestCount = -1;

        for (int subMesh = 0; subMesh < body.subMeshCount; subMesh++)
        {
            int[] triangles =
                body.GetTriangles(subMesh);

            int count = 0;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (mouthVertices.Contains(triangles[i]) &&
                    mouthVertices.Contains(triangles[i + 1]) &&
                    mouthVertices.Contains(triangles[i + 2]))
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestSubMesh = subMesh;
            }
        }

        return bestSubMesh;
    }

    // Returns the vertex mapping and triangles so callers do not rebuild them.
    // Both builders throw when the vertex or index count does not match.
    private static int[] ValidateNeutralMouthSource(
        Mesh body,
        out int[] triangles)
    {
        int[] mapping =
            BuildNeutralSourceMapping(body);

        triangles =
            BuildNeutralTriangles(
                body,
                mapping);

        List<string> warnings =
            CollectNeutralMouthWarnings(
                body,
                mapping);

        if (warnings.Count == 0)
            return mapping;

        bool proceed =
            EditorUtility.DisplayDialog(
                "Mururu Blink & LipSync - 確認",
                "このBodyは標準のMururuと異なる可能性があります。\n\n" +
                string.Join(
                    "\n\n",
                    warnings) +
                "\n\n" +
                "ずれはUnity上では見えず、VRChatで発話したときに表示されます。\n" +
                $"続行しますか？問題があれば「{RemoveButtonLabel}」で元に戻せます。中止すると何も変更しません。",
                "続行",
                "中止");

        if (!proceed)
            throw new OperationCanceledException();

        return mapping;
    }

    // Counts alone pass a Body with the same topology but a changed UV
    // layout, which would scramble NeutralPositions onto the wrong vertices.
    // These checks only warn: they measure Body positions as a proxy, so
    // an edited Body can still produce a usable mouth. The user decides.
    internal static List<string> CollectNeutralMouthWarnings(
        Mesh body,
        int[] mapping)
    {
        float shapeError =
            MaxCentroidAlignedDistance(
                body.vertices,
                mapping,
                MururuNeutralMouthData.NeutralPositions,
                out Vector3 centroidOffset);

        // The generated mouth uses absolute positions, so a Body whose mouth sits
        // elsewhere (another body shape, scale or origin) would float off the face.
        float offsetError =
            Vector3.Distance(
                centroidOffset,
                MururuNeutralMouthData.ExpectedHiddenSurfaceOffset);

        List<string> warnings =
            new List<string>();

        if (shapeError > MururuNeutralMouthData.ShapeToleranceMeters)
        {
            warnings.Add(
                $"口周辺の頂点配置の差: {shapeError * 1000f:F2}mm / 目安: {MururuNeutralMouthData.ShapeToleranceMeters * 1000f:F2}mm\n" +
                "口周辺のUVや頂点を改変しているか、FBXのMesh CompressionがOff以外になっている可能性があります。" +
                "発話中の口の形が崩れることがあります。");
        }

        if (offsetError > MururuNeutralMouthData.OffsetToleranceMeters)
        {
            warnings.Add(
                $"口の位置の差: {offsetError * 1000f:F1}mm / 目安: {MururuNeutralMouthData.OffsetToleranceMeters * 1000f:F1}mm\n" +
                "MururuChildなど別体型のBodyや、頭部の位置や大きさを改変したBody、FBXのScale設定を変更したBodyの可能性があります。" +
                "発話中の口が顔からずれて表示されることがあります。");
        }

        return warnings;
    }

    // Returns the largest per-vertex distance after aligning both centroids,
    // and the offset from the expected centroid to the mapped Body centroid.
    private static float MaxCentroidAlignedDistance(
        Vector3[] bodyVertices,
        int[] mapping,
        Vector3[] expected,
        out Vector3 centroidOffset)
    {
        if (expected == null ||
            expected.Length != mapping.Length)
        {
            throw new Exception("内蔵Neutral口データが破損しています。");
        }

        Vector3 sourceCentroid = Vector3.zero;
        Vector3 expectedCentroid = Vector3.zero;

        for (int i = 0; i < mapping.Length; i++)
        {
            sourceCentroid += bodyVertices[mapping[i]];
            expectedCentroid += expected[i];
        }

        sourceCentroid /= mapping.Length;
        expectedCentroid /= mapping.Length;
        centroidOffset = sourceCentroid - expectedCentroid;

        float maxDistance = 0f;

        for (int i = 0; i < mapping.Length; i++)
        {
            float distance =
                Vector3.Distance(
                    bodyVertices[mapping[i]] - sourceCentroid,
                    expected[i] - expectedCentroid);

            maxDistance = Mathf.Max(maxDistance, distance);
        }

        return maxDistance;
    }

    internal static int[] BuildNeutralSourceMapping(
        Mesh body)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));

        Vector2[] bodyUv =
            body.uv;

        if (bodyUv == null ||
            bodyUv.Length != body.vertexCount)
        {
            throw new Exception("BodyのUV0を取得できません。");
        }

        int aIndex =
            body.GetBlendShapeIndex("mouth_A");

        if (aIndex < 0)
            throw new Exception("Bodyにmouth_Aがありません。");

        int aFrame =
            body.GetBlendShapeFrameCount(aIndex) - 1;

        if (aFrame < 0)
            throw new Exception("Bodyのmouth_Aに有効なBlendShape Frameがありません。");

        Vector3[] aDelta =
            new Vector3[body.vertexCount];

        Vector3[] dn =
            new Vector3[body.vertexCount];

        Vector3[] dt =
            new Vector3[body.vertexCount];

        body.GetBlendShapeFrameVertices(
            aIndex,
            aFrame,
            aDelta,
            dn,
            dt);

        List<int> mouthSurfaceVertices =
            new List<int>();

        // Mururu's mouth_A moves the hidden AIUEO mouth surface forward while
        // moving the ordinary default mouth backward. This separates the source
        // mouth surface without embedding Mururu topology/UV data in the package.
        for (int i = 0; i < body.vertexCount; i++)
        {
            if (aDelta[i].z > MouthDeltaThreshold)
                mouthSurfaceVertices.Add(i);
        }

        if (mouthSurfaceVertices.Count != MururuNeutralMouthData.VertexCount)
        {
            string rotatedAxis = FindRotatedMouthAxis(body, bodyUv, aDelta);
            if (rotatedAxis != null)
            {
                throw new Exception(
                    "Bodyのメッシュ内の座標軸が標準ムルルと異なる可能性があります。\n" +
                    $"口の前方向: {rotatedAxis}（標準: +Z）\n" +
                    "FBXの軸変換により、口の形を編集していなくても発生します。\n" +
                    "ドキュメントサイトのFAQ「Bodyの座標軸が標準ムルルと異なる」を確認してください。");
            }

            throw new Exception(
                "mouth_Aから対応するAIUEO口頂点を特定できませんでした。\n" +
                $"想定: {MururuNeutralMouthData.VertexCount} / " +
                $"検出: {mouthSurfaceVertices.Count}\n" +
                "Bodyの口周辺メッシュまたはmouth_Aを改変している可能性があります。");
        }

        // Use the user's own UVs only to establish deterministic vertex order.
        // No source UV coordinates are distributed with this package.
        mouthSurfaceVertices.Sort(
            (left, right) =>
            {
                int x =
                    bodyUv[left].x.CompareTo(
                        bodyUv[right].x);

                if (x != 0)
                    return x;

                int y =
                    bodyUv[left].y.CompareTo(
                        bodyUv[right].y);

                if (y != 0)
                    return y;

                return left.CompareTo(right);
            });

        return mouthSurfaceVertices.ToArray();
    }

    // Diagnosis only: never accept or rewrite a mesh based on this result.
    private static string FindRotatedMouthAxis(Mesh body, Vector2[] uv, Vector3[] delta)
    {
        Vector3[] axes = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.back };
        string[] names = { "+Y", "-Y", "+X", "-X", "-Z" };
        Vector3[] vertices = body.vertices;
        for (int axis = 0; axis < axes.Length; axis++)
        {
            int[] mapping = Enumerable.Range(0, vertices.Length)
                .Where(i => Vector3.Dot(delta[i], axes[axis]) > MouthDeltaThreshold)
                .OrderBy(i => uv[i].x).ThenBy(i => uv[i].y).ThenBy(i => i).ToArray();
            if (mapping.Length != MururuNeutralMouthData.VertexCount)
                continue;

            Quaternion rotation = Quaternion.FromToRotation(axes[axis], Vector3.forward);
            Vector3[] aligned = vertices.Select(v => rotation * v).ToArray();
            float shapeError = MaxCentroidAlignedDistance(aligned, mapping,
                MururuNeutralMouthData.NeutralPositions, out Vector3 offset);
            if (shapeError <= MururuNeutralMouthData.ShapeToleranceMeters &&
                Vector3.Distance(offset, MururuNeutralMouthData.ExpectedHiddenSurfaceOffset) <=
                MururuNeutralMouthData.OffsetToleranceMeters)
                return names[axis];
        }

        return null;
    }

    private static int[] BuildNeutralTriangles(
        Mesh body,
        int[] mapping)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));

        if (mapping == null ||
            mapping.Length != MururuNeutralMouthData.VertexCount)
        {
            throw new ArgumentException(
                "Neutral口の頂点対応数が不正です。",
                nameof(mapping));
        }

        Dictionary<int, int> sourceToDestination =
            new Dictionary<int, int>();

        for (int i = 0; i < mapping.Length; i++)
        {
            sourceToDestination.Add(
                mapping[i],
                i);
        }

        List<int> result =
            new List<int>();

        for (int subMesh = 0; subMesh < body.subMeshCount; subMesh++)
        {
            int[] sourceTriangles =
                body.GetTriangles(subMesh);

            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                int a;
                int b;
                int c;

                if (!sourceToDestination.TryGetValue(sourceTriangles[i], out a) ||
                    !sourceToDestination.TryGetValue(sourceTriangles[i + 1], out b) ||
                    !sourceToDestination.TryGetValue(sourceTriangles[i + 2], out c))
                {
                    continue;
                }

                result.Add(a);
                result.Add(b);
                result.Add(c);
            }
        }

        if (result.Count != MururuNeutralMouthData.TriangleIndexCount)
        {
            throw new Exception(
                "BodyからNeutral口のTopologyを再構成できませんでした。\n" +
                $"想定インデックス数: {MururuNeutralMouthData.TriangleIndexCount} / " +
                $"検出: {result.Count}\n" +
                "Bodyの口周辺メッシュ構造を改変している可能性があります。");
        }

        return result.ToArray();
    }

    private static Mesh GenerateLipSyncMouth(
        Mesh body,
        int[] mapping,
        int[] neutralTriangles)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));

        if (mapping == null ||
            mapping.Length != MururuNeutralMouthData.VertexCount)
        {
            throw new ArgumentException(
                "Neutral口の頂点対応数が不正です。",
                nameof(mapping));
        }

        Vector3[] authoredNeutralPositions =
            MururuNeutralMouthData.NeutralPositions;

        if (authoredNeutralPositions == null ||
            authoredNeutralPositions.Length != MururuNeutralMouthData.VertexCount)
        {
            throw new Exception("内蔵Neutral口データが破損しています。");
        }

        Mesh generated =
            new Mesh
            {
                name = "LipSync_Mouth_Generated"
            };

        generated.vertices =
            (Vector3[])authoredNeutralPositions.Clone();

        CopyMappedUvChannel(
            body.uv,
            mapping,
            values => generated.uv = values);

        CopyMappedUvChannel(
            body.uv2,
            mapping,
            values => generated.uv2 = values);

        CopyMappedUvChannel(
            body.uv3,
            mapping,
            values => generated.uv3 = values);

        CopyMappedUvChannel(
            body.uv4,
            mapping,
            values => generated.uv4 = values);

        Color32[] bodyColors =
            body.colors32;

        if (bodyColors != null &&
            bodyColors.Length == body.vertexCount)
        {
            Color32[] colors =
                new Color32[mapping.Length];

            for (int i = 0; i < mapping.Length; i++)
                colors[i] = bodyColors[mapping[i]];

            generated.colors32 =
                colors;
        }

        BoneWeight[] bodyBoneWeights =
            body.boneWeights;

        if (bodyBoneWeights != null &&
            bodyBoneWeights.Length == body.vertexCount)
        {
            BoneWeight[] boneWeights =
                new BoneWeight[mapping.Length];

            for (int i = 0; i < mapping.Length; i++)
                boneWeights[i] = bodyBoneWeights[mapping[i]];

            generated.boneWeights =
                boneWeights;
        }

        Matrix4x4[] bindposes =
            body.bindposes;

        if (bindposes != null &&
            bindposes.Length > 0)
        {
            generated.bindposes =
                (Matrix4x4[])bindposes.Clone();
        }

        generated.subMeshCount =
            1;

        generated.SetTriangles(
            neutralTriangles,
            0,
            true);

        generated.RecalculateBounds();

        Vector3[] bodyNormals =
            body.normals;

        Vector4[] bodyTangents =
            body.tangents;

        int aIndex =
            body.GetBlendShapeIndex("mouth_A");

        int aFrame =
            aIndex >= 0
                ? body.GetBlendShapeFrameCount(aIndex) - 1
                : -1;

        Vector3[] aNormalDelta =
            new Vector3[body.vertexCount];

        Vector3[] aTangentDelta =
            new Vector3[body.vertexCount];

        if (aFrame >= 0)
        {
            Vector3[] ignoredPositionDelta =
                new Vector3[body.vertexCount];

            body.GetBlendShapeFrameVertices(
                aIndex,
                aFrame,
                ignoredPositionDelta,
                aNormalDelta,
                aTangentDelta);
        }

        if (bodyNormals != null &&
            bodyNormals.Length == body.vertexCount)
        {
            Vector3[] neutralNormals =
                new Vector3[mapping.Length];

            for (int i = 0; i < mapping.Length; i++)
            {
                int sourceIndex =
                    mapping[i];

                neutralNormals[i] =
                    bodyNormals[sourceIndex] +
                    aNormalDelta[sourceIndex];
            }

            generated.normals =
                neutralNormals;
        }
        else
        {
            generated.RecalculateNormals();
        }

        if (bodyTangents != null &&
            bodyTangents.Length == body.vertexCount)
        {
            Vector4[] neutralTangents =
                new Vector4[mapping.Length];

            for (int i = 0; i < mapping.Length; i++)
            {
                int sourceIndex =
                    mapping[i];

                Vector4 sourceTangent =
                    bodyTangents[sourceIndex];

                Vector3 tangent =
                    new Vector3(
                        sourceTangent.x,
                        sourceTangent.y,
                        sourceTangent.z) +
                    aTangentDelta[sourceIndex];

                neutralTangents[i] =
                    new Vector4(
                        tangent.x,
                        tangent.y,
                        tangent.z,
                        sourceTangent.w);
            }

            generated.tangents =
                neutralTangents;
        }
        else if (generated.uv != null &&
                 generated.uv.Length == generated.vertexCount)
        {
            generated.RecalculateTangents();
        }

        AddNeutralMouthBlendShapes(
            generated);

        Vector3[] bodyVertices =
            body.vertices;

        Vector3[] seVertices =
            generated.vertices;

        Vector3[] seNormals =
            generated.normals;

        Vector4[] seTangents =
            generated.tangents;

        for (int n = 0; n < SourceNames.Length; n++)
        {
            AddGeneratedBlendShape(
                body,
                generated,
                bodyVertices,
                bodyNormals,
                bodyTangents,
                seVertices,
                seNormals,
                seTangents,
                mapping,
                SourceNames[n],
                TargetNames[n]);
        }

        return generated;
    }

    private static void CopyMappedUvChannel(
        Vector2[] source,
        int[] mapping,
        Action<Vector2[]> assign)
    {
        if (source == null ||
            source.Length == 0)
        {
            return;
        }

        Vector2[] destination =
            new Vector2[mapping.Length];

        for (int i = 0; i < mapping.Length; i++)
        {
            int sourceIndex =
                mapping[i];

            if (sourceIndex < 0 ||
                sourceIndex >= source.Length)
            {
                return;
            }

            destination[i] =
                source[sourceIndex];
        }

        assign(destination);
    }

    private static void AddNeutralMouthBlendShapes(
        Mesh generated)
    {
        int vertexCount =
            generated.vertexCount;

        Vector3[] zeroNormals =
            new Vector3[vertexCount];

        Vector3[] zeroTangents =
            new Vector3[vertexCount];

        Vector3[] noneDelta =
            new Vector3[vertexCount];

        foreach (int index in MururuNeutralMouthData.NoneAdjustmentIndices)
        {
            if (index < 0 ||
                index >= vertexCount)
            {
                throw new Exception("内蔵SE_mouth_noneデータが破損しています。");
            }

            noneDelta[index] =
                new Vector3(
                    0f,
                    MururuNeutralMouthData.NoneAdjustmentY,
                    0f);
        }

        generated.AddBlendShapeFrame(
            NoneShapeName,
            100f,
            noneDelta,
            zeroNormals,
            zeroTangents);

        Vector3[] hideDelta =
            new Vector3[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            hideDelta[i] =
                new Vector3(
                    0f,
                    0f,
                    MururuNeutralMouthData.HideSelfDeltaZ);
        }

        generated.AddBlendShapeFrame(
            HideSelfShapeName,
            100f,
            hideDelta,
            zeroNormals,
            zeroTangents);
    }

    private static void AddGeneratedBlendShape(
        Mesh body,
        Mesh destination,
        Vector3[] bodyVertices,
        Vector3[] bodyNormals,
        Vector4[] bodyTangents,
        Vector3[] seBaseVertices,
        Vector3[] seBaseNormals,
        Vector4[] seBaseTangents,
        int[] mapping,
        string sourceShapeName,
        string targetShapeName)
    {
        int shapeIndex =
            body.GetBlendShapeIndex(sourceShapeName);

        int frameIndex =
            body.GetBlendShapeFrameCount(shapeIndex) - 1;

        Vector3[] sourceDv =
            new Vector3[body.vertexCount];

        Vector3[] sourceDn =
            new Vector3[body.vertexCount];

        Vector3[] sourceDt =
            new Vector3[body.vertexCount];

        body.GetBlendShapeFrameVertices(
            shapeIndex,
            frameIndex,
            sourceDv,
            sourceDn,
            sourceDt);

        Vector3[] targetDv =
            new Vector3[destination.vertexCount];

        Vector3[] targetDn =
            new Vector3[destination.vertexCount];

        Vector3[] targetDt =
            new Vector3[destination.vertexCount];

        bool mapNormals =
            bodyNormals != null &&
            bodyNormals.Length == body.vertexCount &&
            seBaseNormals != null &&
            seBaseNormals.Length == destination.vertexCount;

        bool mapTangents =
            bodyTangents != null &&
            bodyTangents.Length == body.vertexCount &&
            seBaseTangents != null &&
            seBaseTangents.Length == destination.vertexCount;

        for (int j = 0; j < destination.vertexCount; j++)
        {
            int i =
                mapping[j];

            Vector3 finalPosition =
                bodyVertices[i] + sourceDv[i];

            targetDv[j] =
                finalPosition - seBaseVertices[j];

            if (mapNormals)
            {
                Vector3 finalNormal =
                    bodyNormals[i] + sourceDn[i];

                targetDn[j] =
                    finalNormal - seBaseNormals[j];
            }

            if (mapTangents)
            {
                Vector3 bodyTangent =
                    new Vector3(
                        bodyTangents[i].x,
                        bodyTangents[i].y,
                        bodyTangents[i].z);

                Vector3 seTangent =
                    new Vector3(
                        seBaseTangents[j].x,
                        seBaseTangents[j].y,
                        seBaseTangents[j].z);

                targetDt[j] =
                    (bodyTangent + sourceDt[i]) -
                    seTangent;
            }
        }

        destination.AddBlendShapeFrame(
            targetShapeName,
            100f,
            targetDv,
            targetDn,
            targetDt);
    }
}
}
