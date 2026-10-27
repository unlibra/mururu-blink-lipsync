using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MururuBlinkLipSync
{
    /// <summary>
    /// Installation metadata stored on the generated MururuBlinkLipSync object.
    /// This component is attached to the Scene, while IEditorOnly keeps it out
    /// of the built VRChat avatar.
    ///
    /// RC17 policy:
    /// - Body Mesh is never replaced by Mururu Blink & LipSync.
    /// - Normal Remove deletes only addon-owned resources that are not still
    ///   referenced by another duplicated installation.
    /// - Blink/Eye Look restoration is ownership-aware so post-install user
    ///   edits are preserved.
    /// - Expression behavior settings are stored per installed avatar.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class MururuBlinkLipSyncInstallRecord : MonoBehaviour, IEditorOnly
    {
        // Internal serialized state. Edit through the Setup Window only.
        [HideInInspector]
        public VRCAvatarDescriptor avatarDescriptor;
        [HideInInspector]
        public SkinnedMeshRenderer bodyRenderer;

        // Legacy / immediate rollback data retained so RC5-derived cleanup code stays safe.
        // RC17 normal Remove does not restore Body to this snapshot.
        [HideInInspector]
        public Mesh originalBodyMesh;
        [HideInInspector]
        public string[] originalBodyBlendShapeNames;
        [HideInInspector]
        public float[] originalBodyBlendShapeWeights;

        // Immediate install rollback / descriptor detach data.
        [HideInInspector]
        public VRC_AvatarDescriptor.LipSyncStyle originalLipSync;
        [HideInInspector]
        public SkinnedMeshRenderer originalVisemeSkinnedMesh;
        [HideInInspector]
        public string[] originalVisemeBlendShapes;

        [HideInInspector]
        public bool originalEnableEyeLook;
        [HideInInspector]
        public VRCAvatarDescriptor.CustomEyeLookSettings originalEyeLookSettings;

        [HideInInspector]
        public List<string> generatedAssetPaths = new List<string>();

        [HideInInspector]
        public string installedVersion;
        [HideInInspector]
        public int installSchemaVersion;

        [HideInInspector]
        public SkinnedMeshRenderer installedLipSyncMouthRenderer;
        [HideInInspector]
        public RuntimeAnimatorController installedController;
        [HideInInspector]
        public AnimationClip installedOnClip;
        [HideInInspector]
        public AnimationClip installedOffClip;

        // Body material slot that holds the mouth. LipSync_Mouth uses the same
        // material so the speaking mouth matches the face. -1 means not computed
        // yet (records from before this field existed load the initializer).
        [HideInInspector]
        public int mouthMaterialIndex = -1;

        // RC17 expression behavior settings.
        [HideInInspector]
        public string facialExParameterName;

        [HideInInspector]
        public bool hideSpeechNotes;

        [HideInInspector]
        public bool[] leftBlinkEnabled;
        [HideInInspector]
        public bool[] leftLipSyncEnabled;

        [HideInInspector]
        public bool[] rightBlinkEnabled;
        [HideInInspector]
        public bool[] rightLipSyncEnabled;

        [HideInInspector]
        public bool[] facialBlinkEnabled;
        [HideInInspector]
        public bool[] facialLipSyncEnabled;
    }
}
