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
    public sealed class LatWeightCandidateTests
    {
        [Serializable] private sealed class BoneSample { public string name;public Vector3 position;public Quaternion rotation; }
        [Serializable] private sealed class BonePose { public BoneSample[] bones; }
        private Scene _scene;
        private string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/lat-weight-20260909/Unity"));
        [UnityTearDown] public IEnumerator Cleanup()
        { if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null; }
        private PhysicsJumpReview Actor(Vector3 position,bool candidate)
        {
            var a=PhysicsJumpReview.Create(position,true,candidate ? PhysicsJumpReview.LatWeightTransportPath : null);
            SceneManager.MoveGameObjectToScene(a.gameObject,_scene);a.ReadKeyboard=false;a.Motor.ManualStep=true;a.Motor.Body.interpolation=RigidbodyInterpolation.None;
            a.EnableGrabReview();a.Hands.ConfigurePunchTiming(1.4);return a;
        }
        private void Floor()
        {
            var f=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(f,_scene);f.transform.position=Vector3.down*.1f;f.transform.localScale=new Vector3(20,.2f,20);
        }
        [UnityTest] public IEnumerator CandidateSkinRetainsPrecisePointThresholdAndNextDownRequirement()
        {
            _scene=SceneManager.CreateScene("Lat skin hit integration",new CreateSceneParameters(LocalPhysicsMode.Physics3D));Floor();
            var actor=Actor(new Vector3(0,.02f,0),true);actor.Hands.PreciseHits=true;
            var target=Actor(new Vector3(-.179f,0,.4f),true);target.Motor.Body.useGravity=false;target.Motor.Body.constraints=RigidbodyConstraints.FreezeAll;
            target.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
            var receiver=target.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.Bind(target);
            for(var step=0;step<500&&receiver.Ragdoll.Down.State.DownCount==0;step++)
            {
                receiver.Ragdoll.Down.Step(1f/60);receiver.Ragdoll.Step(1f/60);receiver.Step();actor.Motor.Step(1f/60);
                actor.Hands.SubmitButtons(false,false,step>=90&&step%34==0,false,1f/60);actor.Hands.Step(1f/60);_scene.GetPhysicsScene().Simulate(1f/60);
            }
            Assert.That(receiver.Target.HitCount,Is.EqualTo(10));Assert.That(receiver.Ragdoll.Down.State.DownCount,Is.EqualTo(1));
            Assert.That(receiver.RequiredPoints,Is.EqualTo(9));Assert.That(receiver.Accumulation.Points,Is.Zero);
            yield return null;
        }
        private static SkinnedMeshRenderer Body(PhysicsJumpReview a) => a.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
        private static List<Vector2Int> Pairs(int[] triangles)
        {
            var pending=new Dictionary<ulong,int>();var result=new List<Vector2Int>();
            for(var f=0;f<triangles.Length/3;f++)for(var e=0;e<3;e++)
            {
                var a=triangles[f*3+e];var b=triangles[f*3+(e+1)%3];var key=((ulong)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                if(pending.TryGetValue(key,out var other)){result.Add(new Vector2Int(other,f));pending.Remove(key);}else pending.Add(key,f);
            }
            Assert.That(pending.Count,Is.Zero);return result;
        }
        private static float Surface(Mesh mesh,int[] triangles,List<Vector2Int> pairs)
        {
            var vertices=mesh.vertices;var normals=new Vector3[triangles.Length/3];
            for(var f=0;f<normals.Length;f++)
            {
                var a=vertices[triangles[f*3]];var cross=Vector3.Cross(vertices[triangles[f*3+1]]-a,vertices[triangles[f*3+2]]-a);
                if(cross.sqrMagnitude<=1e-30f)Assert.Fail("Degenerate surface triangle");normals[f]=cross/cross.magnitude;
            }
            var min=1f;foreach(var pair in pairs)min=Mathf.Min(min,Vector3.Dot(normals[pair.x],normals[pair.y]));return Mathf.Acos(Mathf.Clamp(min,-1,1))*Mathf.Rad2Deg;
        }
        [UnityTest] public IEnumerator WeightOnlyCandidatePreservesBonesAndShowsSynchronizedPunchComparison()
        {
            _scene=SceneManager.CreateScene("Lat weight comparison",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actors=new[]{Actor(new Vector3(-.55f,.02f,0),false),Actor(new Vector3(.55f,.02f,0),true)};Floor();
            var skins=actors.Select(Body).ToArray();var sources=skins.Select(s=>s.sharedMesh).ToArray();
            CollectionAssert.AreEqual(sources[0].vertices,sources[1].vertices);CollectionAssert.AreEqual(sources[0].triangles,sources[1].triangles);
            for(var i=0;i<sources[0].bindposes.Length;i++)for(var element=0;element<16;element++)Assert.That(sources[1].bindposes[i][element],Is.EqualTo(sources[0].bindposes[i][element]).Within(.000001),"bind matrix float roundoff under different actor positions");
            var sourceVertices=sources[0].vertices;var beforeWeights=sources[0].boneWeights;var afterWeights=sources[1].boneWeights;var changed=0;
            for(var i=0;i<sourceVertices.Length;i++)
            {
                if(!beforeWeights[i].Equals(afterWeights[i]))changed++;
                if(sourceVertices[i].y<.28f)Assert.That(afterWeights[i],Is.EqualTo(beforeWeights[i]),"approved lower-body weights");
                Assert.That(afterWeights[i].weight0+afterWeights[i].weight1+afterWeights[i].weight2+afterWeights[i].weight3,Is.EqualTo(1).Within(.000001));
            }
            Assert.That(changed,Is.GreaterThan(0));
            var nodes=actors.Select(a=>a.Pose.GetComponentsInChildren<Transform>().Where(t=>t.name=="Pelvis"||t.name=="Chest"||t.name=="Spine"||t.name.StartsWith("Clavicle_")||t.name.StartsWith("UpperArm_")||t.name.StartsWith("Forearm_")||t.name.StartsWith("HandLogical_")).ToArray()).ToArray();
            foreach(var skin in actors[1].Pose.GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterial.SetColor("_BaseColor",new Color(.92f,.48f,.16f));
            var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,_scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.84f;
            var lightObject=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var rt=new RenderTexture(960,600,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,600,TextureFormat.RGB24,false);
            var dir=Path.Combine(Output,"Comparison");Directory.CreateDirectory(dir);var triangles=sources[1].triangles;var pairs=Pairs(triangles);var baked=new Mesh();var maxAngle=0f;var maxBoneError=0f;
            for(var step=0;step<240;step++)
            {
                foreach(var a in actors)
                {
                    a.Motor.Step(1f/60);var pulse=step>=90&&step<220&&(step-90)%36==0;
                    a.Hands.SubmitButtons(false,false,pulse,false,1f/60);a.Hands.Step(1f/60);
                }
                _scene.GetPhysicsScene().Simulate(1f/60);foreach(var a in actors)a.ApplyPresentation(1f/60);
                for(var i=0;i<nodes[0].Length;i++)maxBoneError=Mathf.Max(maxBoneError,Quaternion.Angle(nodes[0][i].localRotation,nodes[1][i].localRotation));
                Assert.That(Vector3.Distance(actors[0].Motor.Body.linearVelocity,actors[1].Motor.Body.linearVelocity),Is.LessThan(.0001f));
                if(step>=85&&step%3==0){skins[1].BakeMesh(baked);maxAngle=Mathf.Max(maxAngle,Surface(baked,triangles,pairs));}
                if(step>=75&&step%2==0)
                {
                    yield return null;
                    foreach(var view in new[]{"Front","Back"})
                    {
                        camera.transform.position=view=="Front" ? new Vector3(.8f,1.1f,3.8f) : new Vector3(-.8f,1.1f,-3.8f);camera.transform.LookAt(new Vector3(0,.48f,.04f));
                        camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(dir,$"{view}_{(step-76)/2:D3}.png"),image.EncodeToPNG());
                    }
                }
            }
            Assert.That(maxAngle,Is.LessThan(45));Assert.That(maxBoneError,Is.LessThan(.001f));
            File.WriteAllText(Path.Combine(dir,"Report.json"),$"{{\"status\":\"PASS\",\"changedVertices\":{changed},\"maximumSurfaceAngle\":{maxAngle.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"maximumBoneAngleError\":{maxBoneError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"punchSeconds\":0.5}}");
            camera.targetTexture=null;rt.Release();foreach(var item in new UnityEngine.Object[]{rt,image,baked})UnityEngine.Object.Destroy(item);
        }
        [UnityTest] public IEnumerator CandidateWalkRunGrabAndGetUpKeepSurfaceBelowFoldLimit()
        {
            var records=new List<string>();var errors=new List<string>();
            var modes=new[]{"Walk","Run","Grab","FaceUp","FaceDown","LeftSide","RightSide"};var filter=Environment.GetEnvironmentVariable("LAT_REVIEW_MODE");
            if(!string.IsNullOrEmpty(filter)){Assert.That(modes.Contains(filter),Is.True);modes=new[]{filter};}
            foreach(var mode in modes)
            {
                var recovery=mode.StartsWith("Face")||mode.EndsWith("Side");
                _scene=SceneManager.CreateScene("Lat regression "+mode,new CreateSceneParameters(LocalPhysicsMode.Physics3D));var actor=Actor(new Vector3(0,recovery ? .65f : .02f,0),true);Floor();
                RagdollRecoveryReview rag=null;
                if(recovery)
                {
                    actor.transform.rotation=mode.EndsWith("Side") ? Quaternion.Euler(0,0,mode=="LeftSide" ? 90 : -90) : Quaternion.Euler(mode=="FaceUp" ? -90 : 90,0,0);actor.Motor.Body.rotation=actor.transform.rotation;
                    rag=actor.gameObject.AddComponent<RagdollRecoveryReview>();rag.ArticulatedTorso=true;rag.Bind(actor);rag.BeginDown(Vector3.zero,Vector3.zero);
                }
                if(mode=="Grab")
                {
                    var target=Actor(new Vector3(0,.02f,.40f),true);target.Motor.Body.constraints=RigidbodyConstraints.FreezeAll;
                    var t=target.gameObject.AddComponent<StrikeReviewTarget>();t.GrabEligible=true;actor.Hands.ReviewTargets=new[]{t};
                }
                var skin=Body(actor);var triangles=skin.sharedMesh.triangles;var pairs=Pairs(triangles);var mesh=new Mesh();var maxAngle=0f;var baselineAngle=0f;var sawGrab=false;var sawRecovery=false;
                var reference=Actor(new Vector3(5,2,0),false);reference.gameObject.SetActive(false);
                var referenceObject=new GameObject("ReferenceSkin");referenceObject.transform.SetParent(actor.Pose.transform,false);
                var referenceSkin=referenceObject.AddComponent<SkinnedMeshRenderer>();referenceSkin.sharedMesh=Body(reference).sharedMesh;referenceSkin.bones=skin.bones;referenceSkin.rootBone=skin.rootBone;referenceSkin.enabled=false;var referenceMesh=new Mesh();
                for(var step=0;step<(rag==null ? 240 : 800);step++)
                {
                    if(rag!=null){rag.Down.Step(1f/60);rag.Step(1f/60);}
                    actor.Motor.MoveInput=mode=="Walk"||mode=="Run" ? Vector2.up : Vector2.zero;actor.Motor.SprintRequested=mode=="Run";
                    actor.Motor.Step(1f/60);actor.Hands.SubmitButtons(mode=="Grab"&&step>90,mode=="Grab"&&step>90);actor.Hands.Step(1f/60);
                    _scene.GetPhysicsScene().Simulate(1f/60);
                    if(rag!=null&&rag.Active)rag.ApplyPose();else actor.ApplyPresentation(1f/60);
                    sawGrab|=actor.Hands.Grabs.IsHolding(0)&&actor.Hands.Grabs.IsHolding(1);sawRecovery|=rag!=null&&rag.GetUpPose.Playing;
                    if(step%6==0)
                    {
                        skin.BakeMesh(mesh);var angle=Surface(mesh,triangles,pairs);
                        if(angle>maxAngle&&mode=="Grab")
                        {
                            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"WorstGrabPose.json"),JsonUtility.ToJson(new BonePose{bones=skin.bones.Select(b=>new BoneSample{name=b.name,position=actor.Pose.transform.InverseTransformPoint(b.position),rotation=Quaternion.Inverse(actor.Pose.transform.rotation)*b.rotation}).ToArray()},true));
                        }
                        maxAngle=Mathf.Max(maxAngle,angle);referenceSkin.BakeMesh(referenceMesh);baselineAngle=Mathf.Max(baselineAngle,Surface(referenceMesh,triangles,pairs));
                    }
                }
                if(mode=="Grab")Assert.That(sawGrab,Is.True);
                if(rag!=null){Assert.That(sawRecovery,Is.True);Assert.That(rag.Recoveries,Is.EqualTo(1));}
                records.Add($"{{\"mode\":\"{mode}\",\"surfaceAngle\":{maxAngle.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"baselineAngle\":{baselineAngle.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}}}");
                Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"Regression.json"),"{\"status\":\"OBSERVED\",\"cases\":["+string.Join(",",records)+"]}");
                if(maxAngle>=45)errors.Add(mode+" "+maxAngle);
                UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(referenceMesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(errors,Is.Empty,string.Join(",",errors));
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"Regression.json"),"{\"status\":\"PASS\",\"cases\":["+string.Join(",",records)+"]}");
        }
    }
}
