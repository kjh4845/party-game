using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class ReadableGaitReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Result
        {
            public string mode, version;
            public float lift, strideRange, pelvisLateralRange, pelvisVerticalRange;
            public float minimumKnee, maximumKnee, maximumTargetError, maximumReachCorrection;
            public float maximumPenetration, maximumSurfaceAngle;
            public float maximumJointStep, maximumPlantedSpeed, plantedCoverage;
            public int plantedPairs;
            public int surfaceSamples, hardEdges, foldEdges;
            public Vector3 worstSurfaceLocation;
        }
        [Serializable] private sealed class Report { public string status, outputDirectory; public Result[] cases; }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var operation=SceneManager.UnloadSceneAsync(_scene);while(operation!=null&&!operation.isDone)yield return null;
        }

        [UnityTest] public IEnumerator ReworkedGait_HasVisibleSwingAndWeightTransfer_WithoutStretch() => Review(false);

        [UnityTest] public IEnumerator FootPlantCandidate_HoldsWorldPosition_WithoutStretch() => Review(true);

        private IEnumerator Review(bool candidate)
        {
            if(!File.Exists(PhysicsJumpReview.TransportPath))Assert.Ignore("Local review transport missing.");
            var baseDir=Path.GetFullPath(Path.Combine(Application.dataPath,candidate ? "../../.codex_tmp/foot-plant-candidate-20260906" : "../../.codex_tmp/gait-rework-20260906"));
            var output=Path.Combine(baseDir,"Review-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
            var results=new List<Result>();
            foreach(var mode in new[]{"Walk","Sprint"}) foreach(var version in new[]{"Before","Reworked"})
            {
                _scene=SceneManager.CreateScene("ReadableGait-"+mode+version,new CreateSceneParameters(LocalPhysicsMode.Physics3D));var physics=_scene.GetPhysicsScene();
                var actor=PhysicsJumpReview.Create(new Vector3(0,.02f,0), false);SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);
                actor.ReadKeyboard=false;actor.Motor.ManualStep=true;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;
                actor.Pose.ReadableGait=candidate || version=="Reworked";
                if(candidate && version=="Reworked")actor.UseFootPlantPreset();actor.Motor.SprintRequested=mode=="Sprint";
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(30,.2f,30);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.13f,.20f,.28f));floor.GetComponent<Renderer>().sharedMaterial=material;
                var lineMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));lineMaterial.SetColor("_BaseColor",new Color(.07f,.11f,.17f));
                for(var i=-20;i<=20;i++)
                {
                    var line=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(line,_scene);line.GetComponent<Collider>().enabled=false;
                    line.transform.position=new Vector3(0,.002f,i*.25f);line.transform.localScale=new Vector3(10,.002f,.004f);line.GetComponent<Renderer>().sharedMaterial=lineMaterial;
                }
                var lightObject=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.5f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(45,-35,0);
                var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,_scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.68f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.13f);
                var target=new RenderTexture(512,576,24);target.Create();camera.targetTexture=target;var image=new Texture2D(512,576,TextureFormat.RGB24,false);
                var fixedObject=new GameObject("FixedCamera");SceneManager.MoveGameObjectToScene(fixedObject,_scene);var fixedCamera=fixedObject.AddComponent<Camera>();fixedCamera.enabled=false;fixedCamera.orthographic=true;fixedCamera.orthographicSize=1f;fixedCamera.clearFlags=CameraClearFlags.SolidColor;fixedCamera.backgroundColor=camera.backgroundColor;
                var fixedTarget=new RenderTexture(1280,512,24);fixedTarget.Create();fixedCamera.targetTexture=fixedTarget;var fixedImage=new Texture2D(1280,512,TextureFormat.RGB24,false);
                var nodes=actor.GetComponentsInChildren<Transform>();var pelvis=nodes.Single(t=>t.name=="Pelvis");
                var feet=new[]{nodes.Single(t=>t.name=="Foot_L"),nodes.Single(t=>t.name=="Foot_R")};
                var calves=new[]{nodes.Single(t=>t.name=="Calf_L"),nodes.Single(t=>t.name=="Calf_R")};
                var thighs=new[]{nodes.Single(t=>t.name=="Thigh_L"),nodes.Single(t=>t.name=="Thigh_R")};
                var restFeet=feet.Select(f=>actor.transform.InverseTransformPoint(f.position)).ToArray();
                var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
                var triangles=skin.sharedMesh.triangles;var pairs=EdgePairs(triangles);var mesh=new Mesh();
                var result=new Result {mode=mode,version=version,minimumKnee=180f};
                var lowStride=float.PositiveInfinity;var highStride=float.NegativeInfinity;var lowX=float.PositiveInfinity;var highX=float.NegativeInfinity;var lowY=float.PositiveInfinity;var highY=float.NegativeInfinity;
                var priorFeet=feet.Select(f=>f.position).ToArray();var priorPlanted=new bool[2];var contactCount=0;var plantedCount=0;
                var joints=thighs.Concat(calves).Concat(feet).ToArray();var priorJoints=joints.Select(j=>j.localRotation).ToArray();
                var previousSurfacePhase=0f;var surfaceCycleStarted=false;var surfaceCycleFinished=false;
                for(var step=0;step<240;step++)
                {
                    actor.Motor.MoveInput=step>=20?Vector2.up:Vector2.zero;actor.Motor.Step(1f/60f);physics.Simulate(1f/60f);actor.ApplyPresentation(1f/60f);
                    for(var side=0;side<2;side++)
                    {
                        if(step>=90)
                        {
                            if(Mathf.Repeat(actor.Pose.GaitPhase+side*.5f-.25f,1f)<.5f)contactCount++;
                            if(actor.Pose.IsFootPlanted(side))plantedCount++;
                            if(priorPlanted[side]&&actor.Pose.IsFootPlanted(side))
                            {result.maximumPlantedSpeed=Mathf.Max(result.maximumPlantedSpeed,Vector3.Distance(priorFeet[side],feet[side].position)*60f);result.plantedPairs++;}
                        }
                        priorFeet[side]=feet[side].position;priorPlanted[side]=actor.Pose.IsFootPlanted(side);
                    }
                    for(var j=0;j<joints.Length;j++)
                    {if(step>=90)result.maximumJointStep=Mathf.Max(result.maximumJointStep,Quaternion.Angle(priorJoints[j],joints[j].localRotation));priorJoints[j]=joints[j].localRotation;}
                    var poseFrame=Time.frameCount;yield return null;
                    if(step>=90)
                    {
                        var p=actor.transform.InverseTransformPoint(pelvis.position);lowX=Mathf.Min(lowX,p.x);highX=Mathf.Max(highX,p.x);lowY=Mathf.Min(lowY,p.y);highY=Mathf.Max(highY,p.y);
                        for(var side=0;side<2;side++)
                        {
                            var foot=actor.transform.InverseTransformPoint(feet[side].position)-restFeet[side];result.lift=Mathf.Max(result.lift,foot.y);
                            lowStride=Mathf.Min(lowStride,foot.z);highStride=Mathf.Max(highStride,foot.z);
                            var knee=Vector3.Angle(calves[side].position-thighs[side].position,feet[side].position-calves[side].position);
                            result.minimumKnee=Mathf.Min(result.minimumKnee,knee);result.maximumKnee=Mathf.Max(result.maximumKnee,knee);
                        }
                        result.maximumTargetError=Mathf.Max(result.maximumTargetError,actor.Pose.MaximumFootTargetError);result.maximumReachCorrection=Mathf.Max(result.maximumReachCorrection,actor.Pose.MaximumReachCorrection);
                        skin.BakeMesh(mesh);var vertices=mesh.vertices;result.maximumPenetration=Mathf.Max(result.maximumPenetration,-vertices.Min(v=>skin.transform.TransformPoint(v).y));
                        var phase=actor.Pose.GaitPhase;
                        if(phase<previousSurfacePhase)
                        {
                            if(surfaceCycleStarted)surfaceCycleFinished=true;else surfaceCycleStarted=true;
                        }
                        previousSurfacePhase=phase;
                        if(surfaceCycleStarted&&!surfaceCycleFinished)
                        {
                            Surface(vertices,triangles,pairs,result);result.surfaceSamples++;
                        }
                    }
                    if(step>=60&&step<180)
                    {
                        Assert.That(Time.frameCount,Is.GreaterThan(poseFrame));
                        var center=new Vector3(0,.52f,actor.Motor.Body.position.z);camera.transform.position=center+new Vector3(3f,.35f,1.5f);camera.transform.LookAt(center);
                        Capture(camera,target,image,Path.Combine(output,$"{version}_{mode}_{step-60:D3}.png"));
                        if(step==60){var centerFixed=new Vector3(0,.55f,actor.Motor.Body.position.z+(mode=="Sprint"?2.25f:1.5f));fixedCamera.transform.position=centerFixed+new Vector3(5,.8f,1f);fixedCamera.transform.LookAt(centerFixed);}
                        Capture(fixedCamera,fixedTarget,fixedImage,Path.Combine(output,$"Fixed_{version}_{mode}_{step-60:D3}.png"));
                    }
                }
                result.plantedCoverage=contactCount>0?(float)plantedCount/contactCount:0f;
                result.strideRange=highStride-lowStride;result.pelvisLateralRange=highX-lowX;result.pelvisVerticalRange=highY-lowY;results.Add(result);
                Debug.Log($"READABLE_GAIT {version} {mode} lift={result.lift:F5} stride={result.strideRange:F5} knee={result.minimumKnee:F1}..{result.maximumKnee:F1} ground={result.maximumPenetration:F5} hard={result.hardEdges} folds={result.foldEdges}");
                File.WriteAllText(Path.Combine(output,"ReadableGaitReview.json"),JsonUtility.ToJson(new Report {status="OBSERVED",outputDirectory=output,cases=results.ToArray()},true));
                camera.targetTexture=null;fixedCamera.targetTexture=null;target.Release();fixedTarget.Release();foreach(var obj in new UnityEngine.Object[]{target,fixedTarget,image,fixedImage,mesh,material,lineMaterial})UnityEngine.Object.Destroy(obj);
                var unload=SceneManager.UnloadSceneAsync(_scene);while(unload!=null&&!unload.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir,"LatestReviewPath.txt"),output);
            foreach(var mode in new[]{"Walk","Sprint"})
            {
                var before=results.Single(r=>r.mode==mode&&r.version=="Before");var after=results.Single(r=>r.mode==mode&&r.version=="Reworked");
                if(candidate)
                {
                    Assert.That(after.lift,Is.GreaterThan(.006f));
                    Assert.That(after.plantedPairs,Is.GreaterThan(70));
                    Assert.That(after.plantedCoverage,Is.GreaterThan(.90f));
                    Assert.That(after.maximumPlantedSpeed,Is.LessThan(.001f));
                    Assert.That(after.maximumJointStep,Is.LessThan(20f));
                }
                else
                {
                    Assert.That(after.lift,Is.GreaterThan(before.lift*2.5f),mode+" foot clearance");
                    Assert.That(after.strideRange,Is.GreaterThan(before.strideRange*2f),mode+" readable stride");
                }
                Assert.That(after.pelvisLateralRange,Is.GreaterThan(.006f));Assert.That(after.pelvisVerticalRange,Is.GreaterThan(candidate?.0015f:.002f));
                Assert.That(after.maximumKnee,Is.LessThan(85f));Assert.That(after.maximumTargetError,Is.LessThan(.00001f));Assert.That(after.maximumReachCorrection,Is.LessThan(.003f));
                Assert.That(after.maximumPenetration,Is.LessThan(.008f));Assert.That(after.surfaceSamples,Is.InRange(candidate?12:20,80));Assert.That(after.hardEdges,Is.Zero);Assert.That(after.foldEdges,Is.Zero);
            }
            File.WriteAllText(Path.Combine(output,"ReadableGaitReview.json"),JsonUtility.ToJson(new Report {status="PASS",outputDirectory=output,cases=results.ToArray()},true));
        }

        private static void Capture(Camera camera,RenderTexture target,Texture2D image,string path)
        {camera.Render();var old=RenderTexture.active;RenderTexture.active=target;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(path,image.EncodeToPNG());}

        private static List<Vector2Int> EdgePairs(int[] triangles)
        {
            var pending=new Dictionary<ulong,int>();var pairs=new List<Vector2Int>(triangles.Length/2);
            for(var face=0;face<triangles.Length/3;face++)for(var edge=0;edge<3;edge++)
            {
                var a=triangles[face*3+edge];var b=triangles[face*3+(edge+1)%3];var key=((ulong)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                if(pending.TryGetValue(key,out var other)){pairs.Add(new Vector2Int(other,face));pending.Remove(key);}else pending.Add(key,face);
            }
            Assert.That(pending.Count,Is.Zero);return pairs;
        }

        private static void Surface(Vector3[] vertices,int[] triangles,List<Vector2Int> pairs,Result result)
        {
            var normals=new Vector3[triangles.Length/3];
            for(var face=0;face<normals.Length;face++)
            {
                var a=vertices[triangles[face*3]];var cross=Vector3.Cross(vertices[triangles[face*3+1]]-a,vertices[triangles[face*3+2]]-a);
                var length=cross.magnitude;
                if(length<=1e-20f)Assert.Fail("Degenerate deformed triangle");
                // Vector3.normalized zeros vectors below 1e-5. These dense-mesh
                // triangle cross products are smaller; normalize without that cutoff.
                normals[face]=cross/length;
            }
            var minimum=1f;var worstFace=0;
            foreach(var pair in pairs){var dot=Vector3.Dot(normals[pair.x],normals[pair.y]);if(dot<minimum){minimum=dot;worstFace=pair.x;}if(dot<.7071067f)result.hardEdges++;if(dot<0)result.foldEdges++;}
            var angle=Mathf.Acos(Mathf.Clamp(minimum,-1,1))*Mathf.Rad2Deg;
            if(angle>result.maximumSurfaceAngle){result.maximumSurfaceAngle=angle;result.worstSurfaceLocation=(vertices[triangles[worstFace*3]]+vertices[triangles[worstFace*3+1]]+vertices[triangles[worstFace*3+2]])/3f;}
        }
    }
}
