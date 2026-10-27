using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MururuBlinkLipSync.Editor.Tests {
[Category("PrivateAssets")]
internal sealed class CustomMouthProbeTests {
[Test] public void InspectCopiedMouthSources() {
foreach (var path in new[]{"Assets/PackageMouthProbe/Temp.fbx", "Assets/CustomMouthProbe/Custom.fbx", "Assets/CustomMouthProbe/Merged.asset"}) {
foreach(var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Where(m=>m.GetBlendShapeIndex("mouth_A")>=0)) {
if(!mesh.isReadable) {var so=new SerializedObject(mesh); so.FindProperty("m_IsReadable").boolValue=true; so.ApplyModifiedPropertiesWithoutUndo();}
var index=mesh.GetBlendShapeIndex("mouth_A"); var d=new Vector3[mesh.vertexCount];
mesh.GetBlendShapeFrameVertices(index,mesh.GetBlendShapeFrameCount(index)-1,d,null,null);
Debug.Log("CUSTOM_PROBE path="+path+" mesh="+mesh.name+" vertices="+mesh.vertexCount+" shapes="+mesh.blendShapeCount+" frameWeight="+mesh.GetBlendShapeFrameWeight(index,mesh.GetBlendShapeFrameCount(index)-1)+" moved="+d.Count(v=>v.sqrMagnitude>1e-14f)+" posZ="+d.Count(v=>v.z>0.005f)+" negZ="+d.Count(v=>v.z< -0.005f)+" posY="+d.Count(v=>v.y>0.005f)+" negY="+d.Count(v=>v.y< -0.005f)+" maxDelta="+d.Max(v=>v.magnitude));
try { var map=MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(mesh); Debug.Log("CUSTOM_MAPPING path="+path+" count="+map.Length+" warnings="+string.Join(" | ",MururuBlinkLipSyncSetupWindow.CollectNeutralMouthWarnings(mesh,map))); }
catch(Exception error) {Debug.Log("CUSTOM_MAPPING path="+path+" error="+error.Message.Replace("\n"," | "));}
}
}
Assert.IsNotEmpty(AssetDatabase.LoadAllAssetsAtPath("Assets/CustomMouthProbe/Merged.asset").OfType<Mesh>().ToArray());
}
[Test] public void CompareRotatedMouth() {
var standardPath="Assets/PackageMouthProbe/Temp.fbx"; var customPath="Assets/CustomMouthProbe/Custom.fbx";
var standard=AssetDatabase.LoadAllAssetsAtPath(standardPath).OfType<Mesh>().Single(m=>m.GetBlendShapeIndex("mouth_A")>=0);
var custom=AssetDatabase.LoadAllAssetsAtPath(customPath).OfType<Mesh>().Single(m=>m.GetBlendShapeIndex("mouth_A")>=0);
foreach(var path in new[]{standardPath,customPath}) {
var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);
foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("mouth_A")>=0))
Debug.Log("CUSTOM_TRANSFORM path="+path+" renderer="+r.name+" position="+r.transform.position.ToString("R")+" rotation="+r.transform.rotation.eulerAngles.ToString("R")+" scale="+r.transform.lossyScale.ToString("R"));
}
Func<Mesh,string,Vector3[]> deltas=(mesh,name)=>{var d=new Vector3[mesh.vertexCount];int index=mesh.GetBlendShapeIndex(name);mesh.GetBlendShapeFrameVertices(index,mesh.GetBlendShapeFrameCount(index)-1,d,null,null);return d;};
var sa=deltas(standard,"mouth_A"); var ca=deltas(custom,"mouth_A");
var sm=Enumerable.Range(0,standard.vertexCount).Where(i=>sa[i].sqrMagnitude>1e-14f).ToArray();
var cm=Enumerable.Range(0,custom.vertexCount).Where(i=>ca[i].sqrMagnitude>1e-14f).ToArray();
var su=standard.uv;var cu=custom.uv;var sv=standard.vertices;var cv=custom.vertices;
var rotation=Quaternion.Euler(90,0,0);float maxPosition=0,maxDelta=0,maxUv=0; int comparedShapes=0;
var pairs=sm.Select(i=>new {S=i,C=cm.OrderBy(j=>(su[i]-cu[j]).sqrMagnitude).ThenBy(j=>(sv[i]-rotation*cv[j]).sqrMagnitude).First()}).ToArray();
foreach(var pair in pairs){maxUv=Mathf.Max(maxUv,Vector2.Distance(su[pair.S],cu[pair.C])); maxPosition=Mathf.Max(maxPosition,Vector3.Distance(sv[pair.S],rotation*cv[pair.C]));}
for(int shape=0;shape<standard.blendShapeCount;shape++) { var name=standard.GetBlendShapeName(shape); if(!name.StartsWith("mouth_",StringComparison.Ordinal)||custom.GetBlendShapeIndex(name)<0)continue; comparedShapes++;var sd=deltas(standard,name);var cd=deltas(custom,name);foreach(var pair in pairs)maxDelta=Mathf.Max(maxDelta,Vector3.Distance(sd[pair.S],rotation*cd[pair.C])); }
Debug.Log("CUSTOM_ROTATED_COMPARE vertices="+pairs.Length+" uniqueCustom="+pairs.Select(p=>p.C).Distinct().Count()+" mouthShapes="+comparedShapes+" maxUv="+maxUv.ToString("R")+" maxPositionMeters="+maxPosition.ToString("R")+" maxMouthDeltaMeters="+maxDelta.ToString("R"));
Assert.That(pairs.Select(p=>p.C).Distinct().Count(),Is.EqualTo(sm.Length));Assert.That(maxUv,Is.LessThan(1e-6f));Assert.That(maxPosition,Is.LessThan(1e-5f));Assert.That(maxDelta,Is.LessThan(1e-5f));
}}}