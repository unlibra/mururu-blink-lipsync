using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MururuBlinkLipSync.Editor.Tests {
[Category("PrivateAssets")]
internal sealed class PackageMouthProbeTests : MururuAvatarTestBase {
[Test] public void CompareTempPackageInputs() {
var record=Install(SpawnMururu("Probe"));
var body=AssetDatabase.LoadAllAssetsAtPath("Assets/PackageMouthProbe/Temp.fbx").OfType<Mesh>().Single(m=>m.GetBlendShapeIndex("mouth_A")>=0);
var sdk=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/PackageMouthProbe/TempSdk.asset");
CompareBindposes("BODY",record.bodyRenderer.sharedMesh,body); CompareBindposes("SDK",record.installedLipSyncMouthRenderer.sharedMesh,sdk); var fx=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/PackageMouthProbe/TempFX.controller"); var plan=MururuMouthReturnFxRoutesPlan.Capture(fx,record,"Body","LipSync_Mouth",new System.Collections.Generic.HashSet<string>()); Debug.Log("PROBE_FX_SUPPORTED "+plan.Routes.Count(r=>r.Pose.Supported)+"/"+plan.Routes.Count+" unsupported="+string.Join(",",plan.Routes.Where(r=>!r.Pose.Supported).Select(r=>r.Key))); Print("SOURCE_BODY",record.bodyRenderer.sharedMesh); Print("TEMP_BODY",body);
Print("SOURCE_SDK",record.installedLipSyncMouthRenderer.sharedMesh); Print("TEMP_SDK",sdk);
Check("SOURCE",record.bodyRenderer.sharedMesh,record.installedLipSyncMouthRenderer.sharedMesh);
Check("TEMP_BODY",body,record.installedLipSyncMouthRenderer.sharedMesh);
Check("TEMP_SDK",record.bodyRenderer.sharedMesh,sdk); Check("BOTH_TEMP",body,sdk);
var original=record.installedLipSyncMouthRenderer.sharedMesh;
Debug.Log("PROBE_SDK_EQUAL vertices="+original.vertices.SequenceEqual(sdk.vertices)+" normals="+original.normals.SequenceEqual(sdk.normals)+" tangents="+original.tangents.SequenceEqual(sdk.tangents)+" uv="+original.uv.SequenceEqual(sdk.uv)+" bindposes="+original.bindposes.SequenceEqual(sdk.bindposes)+" bones="+original.boneWeights.SequenceEqual(sdk.boneWeights));
}
static void CompareBindposes(string label,Mesh a,Mesh b) {
var am=a.bindposes; var bm=b.bindposes;
Debug.Log("PROBE_BIND_COUNT_"+label+" source="+am.Length+" temp="+bm.Length);
if(am.Length==bm.Length) for(int i=0;i<am.Length;i++)for(int j=0;j<16;j++)if(am[i][j]!=bm[i][j])Debug.Log("PROBE_BIND_DIFF_"+label+" bone="+i+" element="+j+" source="+am[i][j].ToString("R")+" temp="+bm[i][j].ToString("R"));
var copy=Object.Instantiate(b); copy.bindposes=am;
MururuMouthReturnFingerprint.TryCompute(a,out var af,out _); MururuMouthReturnFingerprint.TryCompute(copy,out var bf,out _);
Debug.Log("PROBE_ONLY_BINDPOSE_DIFF_"+label+" "+(af==bf)); Object.DestroyImmediate(copy);
}
static void Print(string label,Mesh mesh) { MururuMouthReturnFingerprint.TryCompute(mesh,out var fp,out var reason); Debug.Log("PROBE_"+label+" vertices="+mesh.vertexCount+" shapes="+mesh.blendShapeCount+" readable="+mesh.isReadable+" fp="+fp+" reason="+reason); }
static void Check(string label,Mesh body,Mesh sdk) { var ok=MururuMouthReturnMeshAssembler.TryAssemble(body,sdk,out var mesh,out var reason); Debug.Log("PROBE_RESULT_"+label+" ok="+ok+" reason="+reason); if(label=="BOTH_TEMP"){Assert.That(ok,Is.True,reason.ToString()); Assert.That(mesh.blendShapeCount,Is.EqualTo(35));} if(mesh)Object.DestroyImmediate(mesh); }
}}