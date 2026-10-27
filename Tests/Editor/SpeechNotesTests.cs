using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class SpeechNotesTests : MururuAvatarTestBase
    {
        readonly List<Object> temporary = new List<Object>();
        T Keep<T>(T value) where T : Object { temporary.Add(value); return value; }

        [TearDown]
        public void DestroyTemporaryObjects()
        {
            foreach (var value in temporary.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
            temporary.Clear();
        }

        static AnimatorController Fx(VRCAvatarDescriptor avatar) => (AnimatorController)avatar.baseAnimationLayers
            .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;

        static void SetFx(VRCAvatarDescriptor avatar, AnimatorController controller)
        {
            var slots = avatar.baseAnimationLayers;
            int index = System.Array.FindIndex(slots, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
            slots[index].animatorController = controller;
            avatar.baseAnimationLayers = slots;
        }

        [Test]
        public void Setting_ApplyReloadRepairAndReset()
        {
            var avatar = SpawnMururu("Speech notes settings");
            var record = Install(avatar);
            Assert.That(record.hideSpeechNotes, Is.False);
            window.hideSpeechNotes = true;
            Assert.IsTrue(window.HasUnappliedSettings(record));
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Assert.That(record.hideSpeechNotes, Is.True);
            Assert.IsFalse(window.HasUnappliedSettings(record));
            var other = SpawnMururu("Other avatar");
            SetTarget(other); Assert.That(window.hideSpeechNotes, Is.False);
            SetTarget(avatar); Assert.That(window.hideSpeechNotes, Is.True);
            var scenePath = EnsureTempFolder() + "/SpeechNotes.unity";
            EditorSceneManager.SaveScene(avatar.gameObject.scene, scenePath);
            EditorSceneManager.OpenScene(scenePath);
            avatar = Object.FindObjectsOfType<VRCAvatarDescriptor>().Single(a => a.name == "Speech notes settings");
            SetTarget(avatar);
            Assert.That(window.hideSpeechNotes, Is.True);
            Assert.That(Run(window.RepairInstallationAuto), Is.Null);
            Assert.That(RecordOf(avatar).hideSpeechNotes, Is.True);
            window.ResetExpressionSettingsToMururuDefaults();
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Assert.That(RecordOf(avatar).hideSpeechNotes, Is.False);
        }

        [Test]
        public void Setting_ApplyCanBeUndone()
        {
            var record = Install(SpawnMururu("Undo speech notes"));
            Keep(Object.Instantiate(record.avatarDescriptor.gameObject)).name = "Undo asset owner";
            window.hideSpeechNotes = true;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(record.hideSpeechNotes, Is.False);
            Assert.IsTrue(window.HasUnappliedSettings(record));
        }

        [Test]
        public void Prepare_ChangesOnlyTheBuildControllerLayerWeight()
        {
            var avatar = SpawnMururu("Source");
            Install(avatar).hideSpeechNotes = true;
            Unpack(avatar);
            var source = Fx(avatar);
            var before = source.layers.Select(layer => layer.defaultWeight).ToArray();
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            MururuMouthReturnPlugin.PrepareSpeechNotes(clone);
            var result = Keep(Fx(clone.GetComponent<VRCAvatarDescriptor>()));
            Assert.That(result, Is.Not.SameAs(source));
            Assert.That(Fx(avatar), Is.SameAs(source));
            CollectionAssert.AreEqual(before, source.layers.Select(layer => layer.defaultWeight));
            for (int i = 0; i < result.layers.Length; i++)
            {
                Assert.That(result.layers[i].defaultWeight, Is.EqualTo(source.layers[i].name == "Facial LipSync" ? 0f : before[i]));
                Assert.That(result.layers[i].stateMachine, Is.SameAs(source.layers[i].stateMachine));
            }
            MururuMouthReturnPlugin.PrepareSpeechNotes(clone);
            Assert.That(Fx(clone.GetComponent<VRCAvatarDescriptor>()), Is.SameAs(result));
        }

        [TestCase(0f)] [TestCase(1f)]
        public void Unchecked_DoesNotOverrideExistingWeight(float weight)
        {
            var avatar = SpawnMururu("Existing notes");
            Install(avatar);
            var source = Keep(Object.Instantiate(Fx(avatar)));
            var layers = source.layers;
            layers.Single(layer => layer.name == "Facial LipSync").defaultWeight = weight;
            source.layers = layers; SetFx(avatar, source);
            MururuMouthReturnPlugin.PrepareSpeechNotes(avatar.gameObject);
            Assert.That(Fx(avatar), Is.SameAs(source));
            Assert.That(source.layers.Single(layer => layer.name == "Facial LipSync").defaultWeight, Is.EqualTo(weight));
        }

        [TestCase("missing")] [TestCase("duplicate")] [TestCase("first")]
        public void UnsupportedLayer_DoesNotModifyController(string kind)
        {
            var avatar = SpawnMururu("Unsupported notes");
            Install(avatar).hideSpeechNotes = true;
            var source = Keep(Object.Instantiate(Fx(avatar)));
            var layers = source.layers;
            int index = System.Array.FindIndex(layers, layer => layer.name == "Facial LipSync");
            if (kind == "missing") layers[index].name = "Renamed notes";
            else if (kind == "duplicate") layers[0].name = "Facial LipSync";
            else { var first = layers[0]; layers[0] = layers[index]; layers[index] = first; }
            source.layers = layers; SetFx(avatar, source);
            var before = EditorJsonUtility.ToJson(source);
            LogAssert.Expect(LogType.Warning, MururuMouthReturnPlugin.SpeechNotesUnavailable);
            MururuMouthReturnPlugin.PrepareSpeechNotes(avatar.gameObject);
            Assert.That(Fx(avatar), Is.SameAs(source));
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
        }

        [TestCase(false)] [TestCase(true)]
        public void RealBuild_RespectsSettingWithoutChangingSource(bool hide)
        {
            var avatar = SpawnMururu("Build source");
            Install(avatar).hideSpeechNotes = hide;
            Unpack(avatar);
            var source = Fx(avatar);
            string path = AssetDatabase.GetAssetPath(source);
            var hash = File.ReadAllBytes(path);
            var before = EditorJsonUtility.ToJson(source);
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            AvatarProcessor.ProcessAvatar(clone);
            var output = Fx(clone.GetComponent<VRCAvatarDescriptor>()).layers
                .Where(layer => layer.name == "Facial LipSync").ToArray();
            if (hide) Assert.That(output.All(layer => layer.defaultWeight == 0f), Is.True);
            else { Assert.That(output.Length, Is.EqualTo(1)); Assert.That(output[0].defaultWeight, Is.EqualTo(1f)); }
            Assert.That(Fx(avatar), Is.SameAs(source));
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
            CollectionAssert.AreEqual(hash, File.ReadAllBytes(path));
        }
    }
}
