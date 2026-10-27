using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnFxRoutesPoseTests
    {
        private static Dictionary<string, float> Complete() => MururuMouthReturnFxRoutesPose.Properties.ToDictionary(key => key, key => 0f);
        private static string Shape(string key) => MururuMouthReturnMeshAssembler.TargetShapeName(key);

        [Test] public void DefaultIsCompleteAndMapsOnlyToDedicatedP2Shapes()
        {
            var pose = MururuMouthReturnFxRoutesPose.Classify(Complete());
            Assert.That(pose.Family, Is.EqualTo(MururuMouthReturnPoseFamily.Default)); Assert.That(pose.Supported, Is.True);
            Assert.That(pose.Raw.Count, Is.EqualTo(27)); Assert.That(pose.Bridge.Count, Is.EqualTo(27));
            Assert.That(pose.Bridge[MururuMouthReturnMeshAssembler.DefaultShapeName], Is.EqualTo(100));
            Assert.That(pose.Bridge.Where(pair => pair.Key != MururuMouthReturnMeshAssembler.DefaultShapeName).All(pair => pair.Value == 0f), Is.True);
            Assert.That(pose.Bridge.ContainsKey("mouth_off"), Is.False);
        }

        [TestCase(37f, 63f)] [TestCase(0f, 0.01f)] [TestCase(0f, 100f)]
        public void ClosedCombinationsWithinTotal100ArePreserved(float good, float bad)
        {
            var raw = Complete(); raw["mouth_good"] = good; raw["mouth_bad"] = bad;
            var pose = MururuMouthReturnFxRoutesPose.Classify(raw);
            Assert.That(pose.Family, Is.EqualTo(MururuMouthReturnPoseFamily.Closed)); Assert.That(pose.Supported, Is.True);
            Assert.That(pose.Bridge[Shape("mouth_good")], Is.EqualTo(good)); Assert.That(pose.Bridge[Shape("mouth_bad")], Is.EqualTo(bad));
            raw["mouth_good"] = 99; Assert.That(pose.Raw["mouth_good"], Is.EqualTo(good), "Snapshot must copy its input");
        }

        [Test] public void FullOpenCombinationDropsEveryClosedContributionOnlyFromBridge()
        {
            var raw = Complete(); raw["mouth_A"] = 37; raw["mouth_I"] = 63;
            foreach (string key in new[] { "mouth_good", "mouth_good2", "mouth_bad", "mouth_impassive", "mouth_serious", "mouth_smirk", "mouth_cynical" }) raw[key] = 10;
            var pose = MururuMouthReturnFxRoutesPose.Classify(raw);
            Assert.That(pose.Family, Is.EqualTo(MururuMouthReturnPoseFamily.Open)); Assert.That(pose.Supported, Is.True);
            foreach (var pair in raw)
            {
                Assert.That(pose.Raw[pair.Key], Is.EqualTo(pair.Value));
                if (pair.Key == "mouth_off") continue;
                Assert.That(pose.Bridge[Shape(pair.Key)], Is.EqualTo(pair.Key == "mouth_A" || pair.Key == "mouth_I" ? pair.Value : 0));
            }
        }

        [Test] public void FullOpenWithClosedTotalExactly100IsSupportedWithoutClosedBridgeContribution()
        {
            var raw = Complete(); raw["mouth_A"] = 100; raw["mouth_good"] = 37; raw["mouth_bad"] = 63;
            var pose = MururuMouthReturnFxRoutesPose.Classify(raw);
            Assert.That(pose.Supported, Is.True); Assert.That(pose.Family, Is.EqualTo(MururuMouthReturnPoseFamily.Open));
            Assert.That(pose.Raw["mouth_good"], Is.EqualTo(37)); Assert.That(pose.Raw["mouth_bad"], Is.EqualTo(63));
            Assert.That(pose.Bridge[Shape("mouth_good")], Is.EqualTo(0)); Assert.That(pose.Bridge[Shape("mouth_bad")], Is.EqualTo(0));
        }

        [TestCase("mouth_A")] [TestCase("mouth_I")] [TestCase("mouth_U")] [TestCase("mouth_E")] [TestCase("mouth_O")]
        [TestCase("mouth_good")] [TestCase("mouth_good2")] [TestCase("mouth_bad")] [TestCase("mouth_impassive")]
        [TestCase("mouth_serious")] [TestCase("mouth_smirk")] [TestCase("mouth_cynical")]
        [TestCase("mouth_excited")] [TestCase("mouth_grin")] [TestCase("mouth_pout")] [TestCase("mouth_angry")]
        [TestCase("mouth_clench")] [TestCase("mouth_circle")] [TestCase("mouth_triangle")] [TestCase("mouth_square")]
        [TestCase("mouth_square2")] [TestCase("mouth_malon")] [TestCase("mouth_drool")] [TestCase("mouth_oops")]
        [TestCase("mouth_tongue")] [TestCase("mouth_tongue_good")]
        public void EverySingle100ShapeHasItsOwnP2Target(string key)
        {
            var raw = Complete(); raw[key] = 100;
            var pose = MururuMouthReturnFxRoutesPose.Classify(raw);
            Assert.That(pose.Supported, Is.True); Assert.That(pose.Bridge[Shape(key)], Is.EqualTo(100));
            Assert.That(pose.Bridge.Count(pair => pair.Value != 0), Is.EqualTo(2));
            var expected = new[] { "mouth_A", "mouth_I", "mouth_U", "mouth_E", "mouth_O" }.Contains(key) ? MururuMouthReturnPoseFamily.Open
                : new[] { "mouth_good", "mouth_good2", "mouth_bad", "mouth_impassive", "mouth_serious", "mouth_smirk", "mouth_cynical" }.Contains(key)
                ? MururuMouthReturnPoseFamily.Closed : MururuMouthReturnPoseFamily.Special;
            Assert.That(pose.Family, Is.EqualTo(expected));
        }

        [TestCase("open partial", MururuMouthReturnPoseRefusal.OpenPartial)]
        [TestCase("open overflow", MururuMouthReturnPoseRefusal.OpenPartial)]
        [TestCase("closed overflow", MururuMouthReturnPoseRefusal.ClosedOverflow)]
        [TestCase("open with closed overflow", MururuMouthReturnPoseRefusal.ClosedOverflow)]
        [TestCase("special partial", MururuMouthReturnPoseRefusal.SpecialPartialOrMixed)]
        [TestCase("special pair", MururuMouthReturnPoseRefusal.SpecialPartialOrMixed)]
        [TestCase("special closed", MururuMouthReturnPoseRefusal.SpecialPartialOrMixed)]
        [TestCase("special open", MururuMouthReturnPoseRefusal.SpecialPartialOrMixed)]
        [TestCase("negative", MururuMouthReturnPoseRefusal.OutOfRange)]
        [TestCase("overflow", MururuMouthReturnPoseRefusal.OutOfRange)]
        [TestCase("nan", MururuMouthReturnPoseRefusal.NonFinite)]
        [TestCase("infinity", MururuMouthReturnPoseRefusal.NonFinite)]
        [TestCase("negative infinity", MururuMouthReturnPoseRefusal.NonFinite)]
        [TestCase("missing", MururuMouthReturnPoseRefusal.MissingCoverage)]
        [TestCase("missing off", MururuMouthReturnPoseRefusal.MissingCoverage)]
        [TestCase("unknown", MururuMouthReturnPoseRefusal.UnknownProperty)]
        [TestCase("hidden", MururuMouthReturnPoseRefusal.Hidden)]
        public void UnsupportedVectorsHaveExplicitReasonsAndNoBridge(string mutation, MururuMouthReturnPoseRefusal reason)
        {
            var raw = Complete(); Assert.That(MururuMouthReturnFxRoutesPose.Classify(raw).Supported, Is.True);
            switch (mutation)
            {
                case "open partial": raw["mouth_A"] = 99.999f; break;
                case "open overflow": raw["mouth_A"] = 100; raw["mouth_I"] = 0.01f; break;
                case "closed overflow": raw["mouth_good"] = 100; raw["mouth_bad"] = 0.01f; break;
                case "open with closed overflow": raw["mouth_A"] = raw["mouth_good"] = 100; raw["mouth_bad"] = 0.01f; break;
                case "special partial": raw["mouth_angry"] = 37; break;
                case "special pair": raw["mouth_angry"] = raw["mouth_pout"] = 100; break;
                case "special closed": raw["mouth_angry"] = 100; raw["mouth_good"] = 1; break;
                case "special open": raw["mouth_angry"] = raw["mouth_A"] = 100; break;
                case "negative": raw["mouth_good"] = -0.01f; break;
                case "overflow": raw["mouth_good"] = 100.01f; break;
                case "nan": raw["mouth_good"] = float.NaN; break;
                case "infinity": raw["mouth_good"] = float.PositiveInfinity; break;
                case "negative infinity": raw["mouth_good"] = float.NegativeInfinity; break;
                case "missing": raw.Remove("mouth_A"); break;
                case "missing off": raw.Remove("mouth_off"); break;
                case "unknown": raw.Add("Unknown", 0); break;
                case "hidden": raw["mouth_off"] = 100; break;
                default: Assert.Fail("Unimplemented mutation"); break;
            }
            var pose = MururuMouthReturnFxRoutesPose.Classify(raw);
            Assert.That(pose.Refusal, Is.EqualTo(reason)); Assert.That(pose.Family, Is.EqualTo(MururuMouthReturnPoseFamily.Unsupported));
            Assert.That(pose.Supported, Is.False); Assert.That(pose.Bridge, Is.Empty);
            Assert.That(pose.Raw.Keys, Is.EquivalentTo(raw.Keys), "Coverage may not be invented");
        }

        [Test] public void PublishedVectorsAndPropertyListCannotBeMutated()
        {
            var pose = MururuMouthReturnFxRoutesPose.Classify(Complete());
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, float>)pose.Raw)["mouth_A"] = 100);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, float>)pose.Bridge)[Shape("mouth_A")] = 100);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)MururuMouthReturnFxRoutesPose.Properties)[0] = "unknown");
        }
    }
}
