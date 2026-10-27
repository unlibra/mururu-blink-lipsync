using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(MururuBlinkLipSync.Editor.MururuMaterialSyncPlugin))]

namespace MururuBlinkLipSync.Editor
{
    // LipSync_Mouth copies Body's material at install time. Users often change
    // Body's material afterwards (color variants, MA Material Setter / Swap),
    // which would make the speaking mouth look different from the face.
    // This pass copies Body's final materials at build time, after Modular Avatar.
    // Material changes driven by animations are not reflected.
    internal sealed class MururuMaterialSyncPlugin : Plugin<MururuMaterialSyncPlugin>
    {
        public override string QualifiedName => "dev.unlibra.mururu-blink-lipsync";

        public override string DisplayName => "Mururu Blink & LipSync";

        protected override void Configure()
        {
            // After MA so Material Setter / Swap initial states are visible.
            // Before TexTransTool so atlasing sees Body and LipSync_Mouth with the
            // same material. Constraints on plugins that are not installed are ignored.
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("net.rs64.tex-trans-tool")
                .Run(
                    "Sync LipSync_Mouth materials with Body",
                    ctx => SyncMaterials(ctx.AvatarRootObject));
        }

        private static void SyncMaterials(
            GameObject avatarRoot)
        {
            foreach (MururuBlinkLipSyncInstallRecord record in
                     avatarRoot.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true))
            {
                SkinnedMeshRenderer body =
                    record.bodyRenderer;

                SkinnedMeshRenderer mouth =
                    record.installedLipSyncMouthRenderer;

                if (body == null ||
                    mouth == null ||
                    mouth.sharedMesh == null)
                {
                    continue;
                }

                mouth.sharedMaterials =
                    MururuLipSyncMaterialSlots.Build(
                        body,
                        mouth.sharedMesh.subMeshCount,
                        record.mouthMaterialIndex);
            }
        }
    }
}
