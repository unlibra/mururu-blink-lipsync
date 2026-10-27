using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MururuBlinkLipSync.Editor
{
    internal enum MururuMouthReturnPoseFamily { Unsupported, Default, Closed, Open, Special }
    internal enum MururuMouthReturnPoseRefusal
    { None, MissingCoverage, UnknownProperty, NonFinite, OutOfRange, Hidden, OpenPartial, ClosedOverflow, SpecialPartialOrMixed, MissingGeometry }

    // A complete source endpoint and a separate P2 Bridge endpoint. No geometry or source curves are edited.
    internal sealed class MururuMouthReturnFxRoutesPose
    {
        private static readonly string[] Open = { "mouth_A", "mouth_I", "mouth_U", "mouth_E", "mouth_O" };
        private static readonly string[] Closed = { "mouth_good", "mouth_good2", "mouth_bad", "mouth_impassive", "mouth_serious", "mouth_smirk", "mouth_cynical" };
        private static readonly string[] Special = { "mouth_excited", "mouth_grin", "mouth_pout", "mouth_angry", "mouth_clench", "mouth_circle",
            "mouth_triangle", "mouth_square", "mouth_square2", "mouth_malon", "mouth_drool", "mouth_oops", "mouth_tongue", "mouth_tongue_good" };
        internal static IReadOnlyList<string> Properties { get; } = Array.AsReadOnly(Open.Concat(Closed).Concat(Special).Concat(new[] { "mouth_off" }).ToArray());
        internal IReadOnlyDictionary<string, float> Raw { get; }
        internal IReadOnlyDictionary<string, float> Bridge { get; }
        internal MururuMouthReturnPoseFamily Family { get; }
        internal MururuMouthReturnPoseRefusal Refusal { get; }
        internal bool Supported => Refusal == MururuMouthReturnPoseRefusal.None;

        private MururuMouthReturnFxRoutesPose(Dictionary<string, float> raw, MururuMouthReturnPoseFamily family,
            MururuMouthReturnPoseRefusal refusal, Dictionary<string, float> bridge)
        { Raw = new ReadOnlyDictionary<string, float>(raw); Family = family; Refusal = refusal; Bridge = new ReadOnlyDictionary<string, float>(bridge); }

        internal MururuMouthReturnFxRoutesPose WithGeometry(IReadOnlyCollection<string> available)
        {
            if (!Supported) return this;
            bool hasDefault = available.Contains(MururuMouthReturnMeshAssembler.DefaultShapeName);
            bool missing = Bridge.Any(p => p.Key != MururuMouthReturnMeshAssembler.DefaultShapeName &&
                p.Value != 0 && !available.Contains(p.Key));
            // Full-weight endpoints can stand alone even when the default/closed ribbon cannot be generated.
            if (!hasDefault && Bridge.Where(p => p.Key != MururuMouthReturnMeshAssembler.DefaultShapeName).Sum(p => p.Value) != 100)
                missing = true;
            return new MururuMouthReturnFxRoutesPose(Raw.ToDictionary(p => p.Key, p => p.Value),
                missing ? MururuMouthReturnPoseFamily.Unsupported : Family,
                missing ? MururuMouthReturnPoseRefusal.MissingGeometry : Refusal,
                missing ? new Dictionary<string, float>() : Bridge.Where(p => available.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
        }

        internal static MururuMouthReturnFxRoutesPose Classify(IReadOnlyDictionary<string, float> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var raw = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            MururuMouthReturnPoseRefusal refusal;
            MururuMouthReturnPoseFamily family = MururuMouthReturnPoseFamily.Unsupported;
            if (raw.Keys.Any(key => !Properties.Contains(key))) refusal = MururuMouthReturnPoseRefusal.UnknownProperty;
            else if (Properties.Any(key => !raw.ContainsKey(key))) refusal = MururuMouthReturnPoseRefusal.MissingCoverage;
            else if (raw.Values.Any(value => float.IsNaN(value) || float.IsInfinity(value))) refusal = MururuMouthReturnPoseRefusal.NonFinite;
            else if (raw.Values.Any(value => value < 0f || value > 100f)) refusal = MururuMouthReturnPoseRefusal.OutOfRange;
            else if (raw["mouth_off"] != 0f) refusal = MururuMouthReturnPoseRefusal.Hidden;
            else
            {
                double open = Open.Sum(key => (double)raw[key]);
                double closed = Closed.Sum(key => (double)raw[key]);
                var special = Special.Where(key => raw[key] != 0f).ToArray();
                if (special.Length != 0)
                {
                    refusal = special.Length == 1 && raw[special[0]] == 100f && open == 0 && closed == 0
                        ? MururuMouthReturnPoseRefusal.None : MururuMouthReturnPoseRefusal.SpecialPartialOrMixed;
                    if (refusal == MururuMouthReturnPoseRefusal.None) family = MururuMouthReturnPoseFamily.Special;
                }
                else if (closed > 100) refusal = MururuMouthReturnPoseRefusal.ClosedOverflow;
                else if (open != 0 && open != 100) refusal = MururuMouthReturnPoseRefusal.OpenPartial;
                else if (open == 100) { refusal = MururuMouthReturnPoseRefusal.None; family = MururuMouthReturnPoseFamily.Open; }
                else { refusal = MururuMouthReturnPoseRefusal.None; family = closed == 0 ? MururuMouthReturnPoseFamily.Default : MururuMouthReturnPoseFamily.Closed; }
            }
            var bridge = new Dictionary<string, float>(StringComparer.Ordinal);
            if (refusal == MururuMouthReturnPoseRefusal.None)
            {
                bridge.Add(MururuMouthReturnMeshAssembler.DefaultShapeName, 100f);
                foreach (string key in Properties.Where(key => key != "mouth_off"))
                    bridge.Add(MururuMouthReturnMeshAssembler.TargetShapeName(key),
                        family == MururuMouthReturnPoseFamily.Open && Closed.Contains(key) ? 0f : raw[key]);
            }
            return new MururuMouthReturnFxRoutesPose(raw, family, refusal, bridge);
        }
    }
}
