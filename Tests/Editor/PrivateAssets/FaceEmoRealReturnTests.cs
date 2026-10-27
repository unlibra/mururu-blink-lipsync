using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;
namespace MururuBlinkLipSync.Editor.Tests {
[Category("PrivateAssets")]
internal sealed class FaceEmoRealReturnTests : MururuAvatarTestBase {
IEnumerable<AnimatorState> States(AnimatorStateMachine sm) { return sm.states.Select(s=>s.state).Concat(sm.stateMachines.SelectMany(s=>States(s.stateMachine))); }
[Test] public void ActualFaceEmo_FinalBridgeTargetsAndRebuildInputs() {
var extra=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/MBLSFaceEmoDiagnostic/FaceEmo_FX.controller"); Assert.That(extra,Is.Not.Null);
var path=AssetDatabase.GetAssetPath(extra); var bytes=File.ReadAllBytes(path);
var inputs=States(extra.layers.Single(l=>l.name=="[ USER EDIT ] FACE EMOTE PLAYER").stateMachine).ToArray();
var inputJson=inputs.Select(EditorJsonUtility.ToJson).ToArray();
var avatar=SpawnMururu("FaceEmo real data"); var record=Install(avatar); var mesh=record.installedLipSyncMouthRenderer.sharedMesh;
var holder=new GameObject("FaceEmo"); holder.transform.SetParent(avatar.transform,false);
var merge=holder.AddComponent<ModularAvatarMergeAnimator>(); merge.animator=extra; merge.layerType=VRCAvatarDescriptor.AnimLayerType.FX; merge.pathMode=MergeAnimatorPathMode.Absolute; merge.layerPriority=0;
var report=new List<string>();
for(int run=0;run<2;run++) {
var clone=Object.Instantiate(avatar.gameObject);
try {
var errors=ErrorReport.CaptureErrors(()=>AvatarProcessor.ProcessAvatar(clone)); Assert.That(errors.Where(e=>e.TheError.Severity>=ErrorSeverity.Error),Is.Empty);
var final=(AnimatorController)clone.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l=>l.type==VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
var player=final.layers.Single(l=>l.name=="[ USER EDIT ] FACE EMOTE PLAYER");
var phase=final.layers.Single(l=>l.name==MururuMouthReturnControllerGenerator.PhaseName).stateMachine;
var wait=phase.states.Single(s=>s.state.name=="Wait").state;
var expected=new Dictionary<string,string>{{"Facial_idle",null},{"ショック","mouth_triangle"},{"笑い","mouth_A"},{"ドヤ","mouth_excited"},{"決め顔","mouth_excited"},{"ジト目","mouth_pout"}};
foreach(var pair in expected) {
var state=States(player.stateMachine).Single(s=>s.name==pair.Key);
var driver=state.behaviours.OfType<VRCAvatarParameterDriver>().Single(d=>d.parameters.Any(p=>p.name.StartsWith("MBLS_Return_Actual")));
Assert.That(driver.localOnly,Is.False); var actual=driver.parameters.Single(p=>p.name.StartsWith("MBLS_Return_Actual"));
var transitions=wait.transitions.Where(t=>t.conditions.Any(c=>c.parameter==actual.name&&c.threshold==actual.value)).ToArray(); Assert.That(transitions.Length,Is.GreaterThan(0),pair.Key);
foreach(var t in transitions) {
var clip=(AnimationClip)t.destinationState.motion;
foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.StartsWith("blendShape.MBLS_Bridge_")&&b.propertyName!="blendShape."+MururuMouthReturnMeshAssembler.DefaultShapeName)) {
float value=AnimationUtility.GetEditorCurve(clip,binding).keys.Last().value;
Assert.That(value,Is.EqualTo(pair.Value!=null && binding.propertyName=="blendShape."+MururuMouthReturnMeshAssembler.TargetShapeName(pair.Value)?100:0),pair.Key+" "+binding.propertyName);
}
}
report.Add("run="+run+" state="+pair.Key+" actual="+actual.value+" routes="+transitions.Length+" target="+(pair.Value??"default"));
}
foreach(var label in new[]{"目を閉じる","うっかり"}) {
var eyeState=States(player.stateMachine).Single(s=>s.name==label);
var eyeActual=eyeState.behaviours.OfType<VRCAvatarParameterDriver>().SelectMany(d=>d.parameters).Single(p=>p.name.StartsWith("MBLS_Return_Actual"));
var eyeRoutes=wait.transitions.Where(t=>t.conditions.Any(c=>c.parameter==eyeActual.name&&c.threshold==eyeActual.value)).ToArray();
Assert.That(eyeRoutes.Length,Is.EqualTo(17),label);
foreach(int left in new[]{0,4}) {
var route=eyeRoutes.Single(t=>t.conditions.Any(c=>c.parameter=="GestureLeft"&&c.mode==AnimatorConditionMode.Equals&&c.threshold==left));
var clip=(AnimationClip)route.destinationState.motion;
foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.StartsWith("blendShape.MBLS_Bridge_")&&b.propertyName!="blendShape."+MururuMouthReturnMeshAssembler.DefaultShapeName))
Assert.That(AnimationUtility.GetEditorCurve(clip,binding).keys.Last().value,Is.EqualTo(left==4&&binding.propertyName=="blendShape."+MururuMouthReturnMeshAssembler.TargetShapeName("mouth_A")?100:0),label+" left="+left+" "+binding.propertyName);
}
report.Add("run="+run+" state="+label+" routes="+eyeRoutes.Length+" targets=lower default/left4 mouth_A");
}
Assert.That(errors.Any(e=>e.TheError.ToMessage().Contains("目を閉じる")||e.TheError.ToMessage().Contains("うっかり")),Is.False,"Eye-only expressions must not be misreported as unsupported mouths");report.Add("run="+run+" bridges="+phase.states.Count(s=>s.state.name.StartsWith("Bridge_"))+" params="+final.parameters.Count(p=>p.name.StartsWith("MBLS_Return_Actual"))+" drivers="+final.layers.SelectMany(l=>States(l.stateMachine)).SelectMany(s=>s.behaviours).OfType<VRCAvatarParameterDriver>().Count(d=>d.parameters.Any(p=>p.name.StartsWith("MBLS_Return_Actual"))));
Assert.That(clone.GetComponent<VRCAvatarDescriptor>().expressionParameters.parameters.Any(p=>p.name.StartsWith("MBLS_Return_Actual")),Is.False);
} finally {Object.DestroyImmediate(clone);}
CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
CollectionAssert.AreEqual(inputJson,inputs.Select(EditorJsonUtility.ToJson).ToArray()); Assert.That(record.installedLipSyncMouthRenderer.sharedMesh,Is.SameAs(mesh));
}
Directory.CreateDirectory("Library/MBLS-Release/MouthReturnInvestigation");
File.WriteAllLines("Library/MBLS-Release/MouthReturnInvestigation/faceemo-return-build.txt",report);
}
}}

