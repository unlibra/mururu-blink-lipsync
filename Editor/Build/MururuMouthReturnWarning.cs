using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace MururuBlinkLipSync.Editor
{
    // Diagnostics only: never changes which routes the controller accepts.
    internal static class MururuMouthReturnWarning
    {
        static readonly Localizer Messages = new Localizer("ja", () => new List<(string, Func<string, string>)>
        {
            ("ja", key => key == "fallback" ? "Mururu Blink & LipSync: 滑らかな口の復帰を使えない条件があります"
                : key == "fallback:description" ? "以下の表情・条件では、全部または一部の組み合わせで、発話終了から0.08秒後に口が瞬時切り替えになります。アップロードは継続できます。\n\n{0}"
                : key == "fallback:hint" ? "瞬時切り替えのまま使う場合は操作不要です。滑らかに復帰させるには、対象表情の口のアニメーションや生成できない口の形状を確認してください。\nhttps://docs.unlibra.com/mururu-blink-lipsync/" : null)
        });

        internal static void Report(MururuBlinkLipSyncInstallRecord record, MururuMouthReturnUnsupportedReason reason,
            MururuMouthReturnFxRoutesPlan plan)
        {
            var details = Details(reason, plan);
            if (details.Length == 0) return;
            using (ErrorReport.WithContextObject(record.bodyRenderer))
                ErrorReport.ReportError(Messages, ErrorSeverity.NonFatal, "fallback", details);
        }

        internal static string Details(MururuMouthReturnUnsupportedReason reason,
            MururuMouthReturnFxRoutesPlan plan)
        {
            if (reason != MururuMouthReturnUnsupportedReason.None)
                return "口メッシュ全体: " + MeshReason(reason) + "。";
            return string.Join("\n", plan.Warnings);
        }

        internal static bool Enabled(MururuBlinkLipSyncInstallRecord settings, int left, int ex)
            => (left == 0 || settings.leftLipSyncEnabled[left - 1]) && (ex == 0 || settings.facialLipSyncEnabled[ex - 1]);

        static string MeshReason(MururuMouthReturnUnsupportedReason reason)
        {
            switch (reason)
            {
                case MururuMouthReturnUnsupportedReason.MissingBody: return "Bodyメッシュがありません";
                case MururuMouthReturnUnsupportedReason.BodyNotReadable: return "Bodyのメッシュデータを読み取れません";
                case MururuMouthReturnUnsupportedReason.NonFiniteContent: return "メッシュに不正な数値が含まれています";
                case MururuMouthReturnUnsupportedReason.MissingSdkMouth: return "リップシンク口のメッシュがありません";
                case MururuMouthReturnUnsupportedReason.SdkMouthNotReadable: return "リップシンク口のメッシュデータを読み取れません";
                case MururuMouthReturnUnsupportedReason.UnrecognizedSdkMouth: return "リップシンク口と現在のBodyの対応を確認できません";
                case MururuMouthReturnUnsupportedReason.BridgeShapeConflict: return "生成する口シェイプと同名のシェイプが存在します";
                case MururuMouthReturnUnsupportedReason.BindposeMismatch: return "Bodyとリップシンク口の骨の基準姿勢が一致しません";
                default: return "現在の口の形状から滑らかな復帰用の口を生成できません";
            }
        }

        internal static string RouteName(string key)
        {
            string primary = key.Split('_')[0];
            return primary.StartsWith("Left", StringComparison.Ordinal) ? "左手 " + primary.Substring(4)
                : primary.StartsWith("Ex", StringComparison.Ordinal) ? "Facial EX " + primary.Substring(2) : primary;
        }

        internal static string Describe(MururuMouthReturnFxRoutesPose pose)
        {
            switch (pose.Refusal)
            {
                case MururuMouthReturnPoseRefusal.UnknownProperty:
                    var names = pose.Raw.Keys.Where(k => !MururuMouthReturnFxRoutesPose.Properties.Contains(k)).ToArray();
                    if (names.Any(k => k.StartsWith("@", StringComparison.Ordinal))) return "FXによるメッシュや口の制御内容を確認できません。";
                    return "対応外の口シェイプの影響を除外できません（" + string.Join(", ", names.OrderBy(k => k, StringComparer.Ordinal)) + "）。";
                case MururuMouthReturnPoseRefusal.MissingGeometry: return "復帰先の口の形状を生成できません。";
                case MururuMouthReturnPoseRefusal.MissingCoverage: return "FXから復帰先の口の値を確定できません。";
                case MururuMouthReturnPoseRefusal.NonFinite: return "口の値が変化するか、直前の状態から残る可能性があり、復帰先を確定できません。";
                case MururuMouthReturnPoseRefusal.OutOfRange: return "口シェイプの値が0～100の範囲を超えています。";
                case MururuMouthReturnPoseRefusal.Hidden: return "口を非表示にするシェイプが有効です。";
                case MururuMouthReturnPoseRefusal.OpenPartial: return "開いた口が中間の値になっており、復帰先の形状を扱えません。";
                case MururuMouthReturnPoseRefusal.ClosedOverflow: return "閉じた口のシェイプの合計が100を超えています。";
                default: return "特殊な口が中間の値になっているか、ほかの口と重なっています。";
            }
        }

        internal static string AdditionalSubject(MururuMouthReturnFxRoutesPlan.AdditionalFx additional,
            Dictionary<int, MururuMouthReturnFxRoutesPlan.AdditionalFx.Group> actual)
        {
            return string.Join(" / ", additional.Writers.Select(w => {
                var states = actual[w.Index].States;
                if (w.Name == "[ USER EDIT ] FACE EMOTE PLAYER")
                {
                    // These are known FaceEmo control states. A missing clip alone
                    // does not turn a named user expression into an internal state.
                    var controls = new[] { "in OVERRIDE", "BYPASS", "AFK Standby", "AFK Enter", "AFK", "AFK Exit" };
                    var expressions = states.Where(s => !controls.Contains(s.name))
                        .Select(s => "「" + (s.name == "Facial_idle" ? "通常の表情" : s.name) + "」").ToArray();
                    string label = expressions.Length == 0 ? "" : "FaceEmo " + string.Join("・", expressions);
                    if (states.Any(s => controls.Contains(s.name)))
                        label += (label.Length == 0 ? "" : " / ") + "FaceEmoの内部制御状態";
                    return label;
                }
                return w.Name + " [" + string.Join(", ", states.Select(s => s.name)) + "]";
            }).Distinct());
        }
    }
}
