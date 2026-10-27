using System;
using System.Linq;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    // Material slots for LipSync_Mouth, shared by the setup window (install time)
    // and the NDMF build plugin (build time, after other tools changed Body).
    internal static class MururuLipSyncMaterialSlots
    {
        internal static Material[] Build(
            SkinnedMeshRenderer body,
            int generatedSubMeshCount,
            int mouthMaterialIndex)
        {
            int slotCount =
                Mathf.Max(1, generatedSubMeshCount);

            Material[] source =
                body != null && body.sharedMaterials != null
                    ? body.sharedMaterials
                    : Array.Empty<Material>();

            Material[] result =
                new Material[slotCount];

            if (source.Length == 0)
                return result;

            // -1 means the slot was never computed (records from older versions);
            // those installs always used slot 0.
            int mouthIndex =
                Mathf.Max(0, mouthMaterialIndex);

            // The generated mesh's submeshes start at the Body slot that holds the mouth.
            for (int i = 0; i < slotCount; i++)
            {
                result[i] =
                    source[Mathf.Clamp(mouthIndex + i, 0, source.Length - 1)];
            }

            // Materials beyond Body's submesh count re-render its last submesh as
            // extra passes (e.g. an overlay). Keep them when the mouth is in it.
            int bodySubMeshCount =
                body != null && body.sharedMesh != null
                    ? body.sharedMesh.subMeshCount
                    : source.Length;

            if (mouthIndex == bodySubMeshCount - 1 &&
                source.Length > bodySubMeshCount)
            {
                result =
                    result
                        .Concat(source.Skip(bodySubMeshCount))
                        .ToArray();
            }

            return result;
        }
    }
}
