using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MururuBlinkLipSync.Editor.Tests {
[Category("PrivateAssets")]
internal sealed class AxisRepairProbeTests : MururuAvatarTestBase {
[Test] public void InspectReexports() {
foreach(var name in new[]{"units-only","rotation-repaired"}){
var path="Assets/AxisRepairProbe/"+name+".fbx";
var mesh=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single(m=>m.GetBlendShapeIndex("mouth_A")>=0);
int a=mesh.GetBlendShapeIndex("mouth_A"); var delta=new Vector3[mesh.vertexCount]; mesh.GetBlendShapeFrameVertices(a,mesh.GetBlendShapeFrameCount(a)-1,delta,null,null);
var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var renderer=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh==mesh);
Debug.Log("AXIS_EXPORT name="+name+" vertices="+mesh.vertexCount+" shapes="+mesh.blendShapeCount+" z="+delta.Count(v=>v.z>0.005f)+" negZ="+delta.Count(v=>v.z< -0.005f)+" y="+delta.Count(v=>v.y>0.005f)+" rotation="+renderer.transform.rotation.eulerAngles.ToString("R")+" bounds="+mesh.bounds.ToString());
try {var map=MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(mesh); Debug.Log("AXIS_MAPPING name="+name+" count="+map.Length+" warnings="+string.Join(" | ",MururuBlinkLipSyncSetupWindow.CollectNeutralMouthWarnings(mesh,map)));}
catch(Exception ex){Debug.Log("AXIS_MAPPING name="+name+" error="+ex.Message.Replace("\n"," | "));}
}
}
[Test] public void ValidateRepair() {
var original=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CustomMouthProbe/Custom.fbx"));
var repaired=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AxisRepairProbe/rotation-repaired.fbx"));
Mesh sb=null,cb=null,generated=null;
try {
var source=original.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("mouth_A")>=0);
var candidate=repaired.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("mouth_A")>=0);
var sm=source.sharedMesh;var cm=candidate.sharedMesh; for(int i=0;i<sm.blendShapeCount;i++)source.SetBlendShapeWeight(i,0); for(int i=0;i<cm.blendShapeCount;i++)candidate.SetBlendShapeWeight(i,0);
sb=new Mesh();cb=new Mesh();source.BakeMesh(sb);candidate.BakeMesh(cb);
var sv=sb.vertices.Select(v=>source.transform.TransformPoint(v)).ToArray();var cv=cb.vertices.Select(v=>candidate.transform.TransformPoint(v)).ToArray();var su=sm.uv;var cu=cm.uv;
Assert.That(cm.vertexCount,Is.EqualTo(sm.vertexCount));
var groups=Enumerable.Range(0,cu.Length).GroupBy(i=>cu[i]).ToDictionary(g=>g.Key,g=>g.ToList());
var pairs=new int[su.Length];for(int i=0;i<su.Length;i++){Assert.IsTrue(groups.ContainsKey(su[i]),"Missing UV "+su[i]);var options=groups[su[i]];Assert.IsNotEmpty(options);int j=options.OrderBy(k=>(sv[i]-cv[k]).sqrMagnitude).First();pairs[i]=j;options.Remove(j);}
float rest=Enumerable.Range(0,pairs.Length).Max(i=>Vector3.Distance(sv[i],cv[pairs[i]]));float shapes=0;
for(int shape=0;shape<sm.blendShapeCount;shape++){string name=sm.GetBlendShapeName(shape);int ci=cm.GetBlendShapeIndex(name);Assert.That(ci,Is.GreaterThanOrEqualTo(0),name);Assert.That(cm.GetBlendShapeFrameCount(ci),Is.EqualTo(sm.GetBlendShapeFrameCount(shape)),name);for(int frame=0;frame<sm.GetBlendShapeFrameCount(shape);frame++){Assert.That(cm.GetBlendShapeFrameWeight(ci,frame),Is.EqualTo(sm.GetBlendShapeFrameWeight(shape,frame)),name);var sd=new Vector3[sm.vertexCount];var cd=new Vector3[cm.vertexCount];sm.GetBlendShapeFrameVertices(shape,frame,sd,null,null);cm.GetBlendShapeFrameVertices(ci,frame,cd,null,null);for(int i=0;i<pairs.Length;i++)shapes=Mathf.Max(shapes,Vector3.Distance(source.transform.TransformVector(sd[i]),candidate.transform.TransformVector(cd[pairs[i]])));}}
float posed=0;int bones=0;foreach(string part in new[]{"Head","Arm"}){var bone=source.bones.FirstOrDefault(b=>b && b.name.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0);if(!bone)continue;var other=candidate.bones.Single(b=>b && b.name==bone.name);var sr=bone.localRotation;var cr=other.localRotation;bone.localRotation=sr*Quaternion.Euler(11,23,7);other.localRotation=cr*Quaternion.Euler(11,23,7);source.BakeMesh(sb);candidate.BakeMesh(cb);sv=sb.vertices;cv=cb.vertices;for(int i=0;i<pairs.Length;i++)posed=Mathf.Max(posed,Vector3.Distance(source.transform.TransformPoint(sv[i]),candidate.transform.TransformPoint(cv[pairs[i]])));bone.localRotation=sr;other.localRotation=cr;bones++;}
var map=MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(cm);Assert.IsEmpty(MururuBlinkLipSyncSetupWindow.CollectNeutralMouthWarnings(cm,map));var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;var type=typeof(MururuBlinkLipSyncSetupWindow);var triangles=(int[])type.GetMethod("BuildNeutralTriangles",flags).Invoke(null,new object[]{cm,map});generated=(Mesh)type.GetMethod("GenerateLipSyncMouth",flags).Invoke(null,new object[]{cm,map,triangles});
Assert.That(generated.vertexCount,Is.EqualTo(84));Assert.That(generated.blendShapeCount,Is.EqualTo(7));
Debug.Log("AXIS_REPAIR_VALIDATED vertices="+pairs.Length+" shapes="+sm.blendShapeCount+" restMeters="+rest.ToString("R")+" shapeDeltaMeters="+shapes.ToString("R")+" bonePoseMeters="+posed.ToString("R")+" boneSamples="+bones+" generatedShapes="+generated.blendShapeCount);
Assert.That(bones,Is.EqualTo(2));Assert.That(rest,Is.LessThan(1e-5f));Assert.That(shapes,Is.LessThan(1e-5f));Assert.That(posed,Is.LessThan(1e-4f));
}finally{if(generated)UnityEngine.Object.DestroyImmediate(generated);if(sb)UnityEngine.Object.DestroyImmediate(sb);if(cb)UnityEngine.Object.DestroyImmediate(cb);UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(repaired);}
}[Test] public void CorrectedCustomAvatar_RealBuildKeepsInputAndUsesSmoothMouth() {
var avatar=SpawnMururu("Corrected custom build");Unpack(avatar);
var imported=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AxisRepairProbe/rotation-repaired.fbx").GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("mouth_A")>=0);
var renderer=Body(avatar);renderer.sharedMesh=imported.sharedMesh;
var bones=avatar.GetComponentsInChildren<Transform>(true);
renderer.bones=imported.bones.Select(b=>bones.Single(t=>t.name==b.name)).ToArray();
var record=Install(avatar);var original=record.installedLipSyncMouthRenderer.sharedMesh;
var clone=UnityEngine.Object.Instantiate(avatar.gameObject);
try {nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone);var mouth=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="LipSync_Mouth");Assert.That(mouth.sharedMesh.blendShapeCount,Is.EqualTo(35));CollectionAssert.AreEqual(imported.sharedMesh.bindposes,mouth.sharedMesh.bindposes);Assert.That(record.installedLipSyncMouthRenderer.sharedMesh,Is.SameAs(original));Assert.That(original.blendShapeCount,Is.EqualTo(7));Assert.That(renderer.sharedMesh,Is.SameAs(imported.sharedMesh));Debug.Log("PER_POSE_CUSTOM_REAL_BUILD shapes=35 inputShapes=7 sourceUnchanged=true");}
finally {UnityEngine.Object.DestroyImmediate(clone);}
}}}