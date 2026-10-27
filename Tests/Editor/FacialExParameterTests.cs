using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class FacialExParameterTests : MururuAvatarTestBase
    {
        [Test]
        public void D1_IntExpressionParameter_IsAutoDetected()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SetTarget(mururu);
            window.facialExParameterName = "";

            Assert.IsTrue(window.TryAutoDetectFacialExParameterName());
            Assert.AreEqual("Facial EX", window.facialExParameterName);
        }

        [Test]
        public void D2_MaParametersDefinition_IsAutoDetected()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            mururu.expressionParameters = CreateExpressionParameters("Empty");
            SetTarget(mururu);
            window.facialExParameterName = "";

            Assert.IsFalse(
                window.TryAutoDetectFacialExParameterName(),
                "MA Parametersを足す前から検出されている");

            AddMaParameters(
                mururu.transform,
                "Parameters",
                new ParameterConfig
                {
                    nameOrPrefix = "Facial EX",
                    syncType = ParameterSyncType.Int
                });

            Assert.IsTrue(window.TryAutoDetectFacialExParameterName());
            Assert.AreEqual("Facial EX", window.facialExParameterName);
        }

        [Test]
        public void D3_FloatParameter_IsRejected()
        {
            VRCAvatarDescriptor mururu =
                SpawnMururu("Mururu", VRCExpressionParameters.ValueType.Float);

            SetTarget(mururu);
            window.facialExParameterName = "Facial EX";

            AssertFailedWith(
                Run(window.Install),
                "Int型ではありません");

            Assert.IsNull(RecordOf(mururu));
        }

        [Test]
        public void D4_BuiltInVoice_IsRejectedOnApply()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            MururuBlinkLipSyncInstallRecord record = Install(mururu);
            Object controller = record.GetComponent<ModularAvatarMergeAnimator>().animator;

            window.facialLipSyncEnabled[0] = false;
            window.facialExParameterName = "Voice";

            AssertFailedWith(
                Run(window.ApplyExpressionSettings),
                "組み込みパラメータ");

            Assert.AreEqual(controller, record.GetComponent<ModularAvatarMergeAnimator>().animator);
            Assert.AreEqual("Facial EX", record.facialExParameterName);
        }

        [Test]
        public void D5_NameRemappedByNearestAncestor_IsFound()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            mururu.expressionParameters = CreateExpressionParameters("Empty");

            // One mapping applies per object: A becomes B, not B then C.
            Transform outer = AddMaParameters(
                mururu.transform,
                "Outer",
                new ParameterConfig { nameOrPrefix = "A", remapTo = "B" },
                new ParameterConfig { nameOrPrefix = "B", remapTo = "C" }).transform;

            AddMaParameters(
                outer,
                "Inner",
                new ParameterConfig
                {
                    nameOrPrefix = "A",
                    syncType = ParameterSyncType.Int
                });

            SetTarget(mururu);

            Assert.IsTrue(window.CollectAvatarParameterKinds().ContainsKey("B"));
        }

        [Test]
        public void D6_BuiltInGestureLeft_IsRejectedOnInstall()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SetTarget(mururu);
            window.facialBlinkEnabled[2] = false;
            window.facialExParameterName = "GestureLeft";

            AssertFailedWith(
                Run(window.Install),
                "組み込みパラメータ");

            Assert.IsNull(RecordOf(mururu));
        }

        private static ModularAvatarParameters AddMaParameters(
            Transform parent,
            string name,
            params ParameterConfig[] configs)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            ModularAvatarParameters parameters =
                holder.AddComponent<ModularAvatarParameters>();

            parameters.parameters = new List<ParameterConfig>(configs);

            return parameters;
        }
    }
}
