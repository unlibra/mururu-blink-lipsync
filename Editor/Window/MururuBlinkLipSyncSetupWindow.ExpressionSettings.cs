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
    internal void UpdateUnsavedChanges()
    {
        hasUnsavedChanges = HasUnappliedSettings(FindInstallRecord());
    }

    public override void SaveChanges()
    {
        UpdateUnsavedChanges();
        if (hasUnsavedChanges)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(PlayModeMessage);

            Exception error = RunOperation(ApplyExpressionSettings);
            // Unity cancels closing when SaveChanges throws. Keep edits on failure.
            if (error != null)
                throw error;
        }

        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        LoadExpressionSettings(FindInstallRecord());
        base.DiscardChanges();
        Repaint();
    }

    internal bool HasUnappliedSettings(MururuBlinkLipSyncInstallRecord record)
    {
        return record != null &&
            (!string.Equals(facialExParameterName, record.facialExParameterName ?? "", StringComparison.Ordinal) ||
             hideSpeechNotes != record.hideSpeechNotes ||
             !SettingsMatch(leftBlinkEnabled, record.leftBlinkEnabled, DefaultLeftBlinkEnabled) ||
             !SettingsMatch(leftLipSyncEnabled, record.leftLipSyncEnabled, DefaultLeftLipSyncEnabled) ||
             !SettingsMatch(rightBlinkEnabled, record.rightBlinkEnabled, DefaultRightBlinkEnabled) ||
             !SettingsMatch(rightLipSyncEnabled, record.rightLipSyncEnabled, DefaultRightLipSyncEnabled) ||
             !SettingsMatch(facialBlinkEnabled, record.facialBlinkEnabled, DefaultFacialBlinkEnabled) ||
             !SettingsMatch(facialLipSyncEnabled, record.facialLipSyncEnabled, DefaultFacialLipSyncEnabled));
    }

    private static bool SettingsMatch(bool[] current, bool[] saved, bool[] defaults)
    {
        // Match LoadExpressionSettings's handling of older records.
        bool[] applied = saved != null && saved.Length == defaults.Length ? saved : defaults;
        return current != null && current.SequenceEqual(applied);
    }

    internal void ResetExpressionSettingsToMururuDefaults()
    {
        ResetExpressionSettingArrays();
        hideSpeechNotes = false;
    }

    private void ResetExpressionSettingArrays()
    {
        leftBlinkEnabled =
            (bool[])DefaultLeftBlinkEnabled.Clone();

        leftLipSyncEnabled =
            (bool[])DefaultLeftLipSyncEnabled.Clone();

        rightBlinkEnabled =
            (bool[])DefaultRightBlinkEnabled.Clone();

        rightLipSyncEnabled =
            (bool[])DefaultRightLipSyncEnabled.Clone();

        facialBlinkEnabled =
            (bool[])DefaultFacialBlinkEnabled.Clone();

        facialLipSyncEnabled =
            (bool[])DefaultFacialLipSyncEnabled.Clone();
    }

    private void EnsureSettingArrays()
    {
        if (leftBlinkEnabled == null || leftBlinkEnabled.Length != GestureCount ||
            leftLipSyncEnabled == null || leftLipSyncEnabled.Length != GestureCount ||
            rightBlinkEnabled == null || rightBlinkEnabled.Length != GestureCount ||
            rightLipSyncEnabled == null || rightLipSyncEnabled.Length != GestureCount ||
            facialBlinkEnabled == null || facialBlinkEnabled.Length != FacialExCount ||
            facialLipSyncEnabled == null || facialLipSyncEnabled.Length != FacialExCount)
        {
            ResetExpressionSettingArrays();
        }
    }

    internal bool TryAutoDetectFacialExParameterName()
    {
        if (avatarDescriptor == null)
            return false;

        List<string> intNames =
            CollectAvatarParameterKinds()
                .Where(pair => pair.Value == AvatarParameterKind.Int)
                .Select(pair => pair.Key)
                .ToList();

        string exact =
            intNames.FirstOrDefault(name =>
                string.Equals(
                    name,
                    "Facial EX",
                    StringComparison.OrdinalIgnoreCase));

        if (exact != null)
        {
            facialExParameterName = exact;
            return true;
        }

        List<string> facialNamed =
            intNames
                .Where(name =>
                    name.IndexOf(
                        "facial",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf(
                        "left",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf(
                        "right",
                        StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

        if (facialNamed.Count == 1)
        {
            facialExParameterName = facialNamed[0];
            return true;
        }

        Dictionary<string, HashSet<int>> menuValues =
            new Dictionary<string, HashSet<int>>();

        CollectFacialExCandidatesFromMenu(
            avatarDescriptor.expressionsMenu,
            new HashSet<VRCExpressionsMenu>(),
            new HashSet<string>(intNames),
            menuValues);

        KeyValuePair<string, HashSet<int>> best =
            menuValues
                .OrderByDescending(pair => pair.Value.Count)
                .FirstOrDefault();

        if (!string.IsNullOrEmpty(best.Key) &&
            best.Value.Count >= 4)
        {
            facialExParameterName = best.Key;
            return true;
        }

        return false;
    }

    private static void CollectFacialExCandidatesFromMenu(
        VRCExpressionsMenu menu,
        HashSet<VRCExpressionsMenu> visited,
        HashSet<string> intNames,
        Dictionary<string, HashSet<int>> values)
    {
        if (menu == null || visited.Contains(menu))
            return;

        visited.Add(menu);

        if (menu.controls == null)
            return;

        foreach (VRCExpressionsMenu.Control control in menu.controls)
        {
            if (control == null)
                continue;

            if (control.parameter != null &&
                !string.IsNullOrEmpty(control.parameter.name) &&
                intNames.Contains(control.parameter.name))
            {
                int value = Mathf.RoundToInt(control.value);

                if (value >= 1 && value <= FacialExCount)
                {
                    if (!values.TryGetValue(
                            control.parameter.name,
                            out HashSet<int> set))
                    {
                        set = new HashSet<int>();
                        values[control.parameter.name] = set;
                    }

                    set.Add(value);
                }
            }

            if (control.subMenu != null)
            {
                CollectFacialExCandidatesFromMenu(
                    control.subMenu,
                    visited,
                    intNames,
                    values);
            }
        }
    }

    private void SaveExpressionSettings(
        MururuBlinkLipSyncInstallRecord record)
    {
        if (record == null)
            return;

        EnsureSettingArrays();

        record.facialExParameterName = facialExParameterName;
        record.hideSpeechNotes = hideSpeechNotes;
        record.leftBlinkEnabled = (bool[])leftBlinkEnabled.Clone();
        record.leftLipSyncEnabled = (bool[])leftLipSyncEnabled.Clone();
        record.rightBlinkEnabled = (bool[])rightBlinkEnabled.Clone();
        record.rightLipSyncEnabled = (bool[])rightLipSyncEnabled.Clone();
        record.facialBlinkEnabled = (bool[])facialBlinkEnabled.Clone();
        record.facialLipSyncEnabled = (bool[])facialLipSyncEnabled.Clone();

        EditorUtility.SetDirty(record);
    }

    private void LoadExpressionSettings(
        MururuBlinkLipSyncInstallRecord record)
    {
        if (record == null)
        {
            ResetExpressionSettingsToMururuDefaults();
            return;
        }

        facialExParameterName =
            record.facialExParameterName ?? "";
        hideSpeechNotes = record.hideSpeechNotes;

        leftBlinkEnabled =
            CloneOrDefault(
                record.leftBlinkEnabled,
                DefaultLeftBlinkEnabled);

        leftLipSyncEnabled =
            CloneOrDefault(
                record.leftLipSyncEnabled,
                DefaultLeftLipSyncEnabled);

        rightBlinkEnabled =
            CloneOrDefault(
                record.rightBlinkEnabled,
                DefaultRightBlinkEnabled);

        rightLipSyncEnabled =
            CloneOrDefault(
                record.rightLipSyncEnabled,
                DefaultRightLipSyncEnabled);

        facialBlinkEnabled =
            CloneOrDefault(
                record.facialBlinkEnabled,
                DefaultFacialBlinkEnabled);

        facialLipSyncEnabled =
            CloneOrDefault(
                record.facialLipSyncEnabled,
                DefaultFacialLipSyncEnabled);

        if (string.IsNullOrWhiteSpace(facialExParameterName))
            TryAutoDetectFacialExParameterName();
    }

    private static bool[] CloneOrDefault(
        bool[] source,
        bool[] fallback)
    {
        if (source == null || source.Length != fallback.Length)
            return (bool[])fallback.Clone();

        return (bool[])source.Clone();
    }

    private void ValidateExpressionBehaviorSettings()
    {
        EnsureSettingArrays();

        bool facialParameterNeeded =
            facialBlinkEnabled.Any(value => !value) ||
            facialLipSyncEnabled.Any(value => !value);

        if (!facialParameterNeeded)
            return;

        if (string.IsNullOrWhiteSpace(facialExParameterName))
        {
            throw new Exception(
                "Facial EXパラメータを特定できません。\n" +
                "詳細設定のFacial EXパラメータを確認してください。");
        }

        // GestureLeft / GestureRight are Int like Facial EX, so the animator's
        // type check cannot catch them; the Facial EX conditions would bind
        // to hand gestures instead.
        if (VrcBuiltInParameterNames.Contains(facialExParameterName))
        {
            throw new Exception(
                $"Facial EXパラメータにVRChatの組み込みパラメータ '{facialExParameterName}' は指定できません。\n" +
                "表情メニュー用に定義したInt型パラメータを指定してください。");
        }

        if (!CollectAvatarParameterKinds()
                .TryGetValue(
                    facialExParameterName,
                    out AvatarParameterKind kind))
        {
            kind = AvatarParameterKind.Missing;
        }

        // Parameters created implicitly by MA Menu Items are not visible here,
        // so a missing name is only a warning. A wrong type is always an error.
        if (kind == AvatarParameterKind.Missing)
        {
            bool proceed =
                EditorUtility.DisplayDialog(
                    "Mururu Blink & LipSync - 確認",
                    $"Facial EXパラメータ '{facialExParameterName}' がExpression ParametersとMA Parametersに見つかりません。\n\n" +
                    "MA Menu Itemなどで作られるパラメータの場合は、このまま続行できます。\n" +
                    "名前が間違っている場合、Facial EXの設定は動作しません。\n\n" +
                    "続行しますか？中止すると何も変更しません。",
                    "続行",
                    "中止");

            if (!proceed)
                throw new OperationCanceledException();
        }

        if (kind == AvatarParameterKind.NotInt)
        {
            throw new Exception(
                $"Facial EXパラメータ '{facialExParameterName}' はInt型ではありません。");
        }
    }

    // Parameters VRChat drives itself. None of them can carry a Facial EX value.
    private static readonly HashSet<string> VrcBuiltInParameterNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "IsLocal", "PreviewMode", "Viseme", "Voice",
            "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
            "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude",
            "Upright", "Grounded", "Seated", "AFK", "TrackingType", "VRMode",
            "MuteSelf", "InStation", "Earmuffs", "IsOnFriendsList", "AvatarVersion",
            "ScaleModified", "ScaleFactor", "ScaleFactorInverse",
            "EyeHeightAsMeters", "EyeHeightAsPercent", "IsAnimatorEnabled"
        };

    internal enum AvatarParameterKind
    {
        Missing,
        Int,
        NotInt,
        // MA "Not Synced" parameters are animator-only; their type is decided by
        // the animators, so it cannot be checked here and is accepted.
        Unknown
    }

    // Parameters as VRChat sees them after the Modular Avatar build: the
    // descriptor's Expression Parameters plus MA Parameters components, which
    // are only merged into the descriptor at build time.
    internal Dictionary<string, AvatarParameterKind> CollectAvatarParameterKinds()
    {
        Dictionary<string, AvatarParameterKind> result =
            new Dictionary<string, AvatarParameterKind>(StringComparer.Ordinal);

        if (avatarDescriptor == null)
            return result;

        VRCExpressionParameters expressionParameters =
            avatarDescriptor.expressionParameters;

        if (expressionParameters != null &&
            expressionParameters.parameters != null)
        {
            foreach (VRCExpressionParameters.Parameter parameter in
                     expressionParameters.parameters)
            {
                if (parameter == null ||
                    string.IsNullOrEmpty(parameter.name))
                {
                    continue;
                }

                result[parameter.name] =
                    parameter.valueType == VRCExpressionParameters.ValueType.Int
                        ? AvatarParameterKind.Int
                        : AvatarParameterKind.NotInt;
            }
        }

        foreach (ModularAvatarParameters component in
                 avatarDescriptor.GetComponentsInChildren<ModularAvatarParameters>(true))
        {
            if (component == null ||
                component.parameters == null ||
                IsEditorOnly(component.transform))
            {
                continue;
            }

            foreach (ParameterConfig config in component.parameters)
            {
                // Internal parameters get a generated name, and prefixes are not
                // single parameters, so neither can be referenced by name.
                if (config.internalParameter || config.isPrefix)
                    continue;

                string name =
                    string.IsNullOrEmpty(config.remapTo)
                        ? config.nameOrPrefix
                        : config.remapTo;

                name =
                    ResolveThroughAncestorParameters(
                        component,
                        name);

                // The descriptor's own definition wins when both exist.
                if (string.IsNullOrEmpty(name) || result.ContainsKey(name))
                    continue;

                result[name] =
                    config.syncType == ParameterSyncType.Int
                        ? AvatarParameterKind.Int
                        : config.syncType == ParameterSyncType.NotSynced
                            ? AvatarParameterKind.Unknown
                            : AvatarParameterKind.NotInt;
            }
        }

        return result;
    }

    // MA applies every enclosing MA Parameters component, nearest first, so an
    // outer component can rename a parameter again or make it internal.
    // Returns null when the parameter ends up internal (not addressable by name).
    private string ResolveThroughAncestorParameters(
        ModularAvatarParameters component,
        string name)
    {
        // Nothing encloses a component on the avatar root itself.
        if (component.transform == avatarDescriptor.transform)
            return name;

        Transform current =
            component.transform.parent;

        while (current != null && !string.IsNullOrEmpty(name))
        {
            // Apply at most one mapping per object, so a renamed parameter is not
            // matched again by another component on the same object.
            bool mapped = false;

            foreach (ModularAvatarParameters outer in
                     current.GetComponents<ModularAvatarParameters>())
            {
                if (outer == null || outer.parameters == null)
                    continue;

                foreach (ParameterConfig config in outer.parameters)
                {
                    if (config.isPrefix || config.nameOrPrefix != name)
                        continue;

                    if (config.internalParameter)
                        return null;

                    if (!string.IsNullOrEmpty(config.remapTo))
                        name = config.remapTo;

                    mapped = true;
                    break;
                }

                if (mapped)
                    break;
            }

            if (current == avatarDescriptor.transform)
                break;

            current = current.parent;
        }

        return name;
    }

    // Objects tagged EditorOnly, or under one, are stripped before the build.
    private static bool IsEditorOnly(
        Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.CompareTag("EditorOnly"))
                return true;
        }

        return false;
    }
}
}
