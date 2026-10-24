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
    public sealed class CameraGaitReviewTests
    {
        [Serializable] private sealed class Metrics
        {
            public string name;public float surfaceAngle,reachCorrection,penetration,stanceSpeed,maxKnee,footLift,strideRange,pelvisRange;
            public int plantedPairs,stancePairs;public float coverage;
            public int worstSurfaceStep;public Vector3 worstSurfacePoint;
        }
        [Serializable] private sealed class Report { public string status;public Metrics[] cases; }
        private Scene _scene;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/camera-gait-20260910"));
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private PhysicsJumpReview Actor(Vector3 point,bool candidate)
        {
            var a=PhysicsJumpReview.Create(point,true,PhysicsJumpReview.LatWeightTransportPath);SceneManager.MoveGameObjectToScene(a.gameObject,_scene);a.ReadKeyboard=false;a.Motor.ManualStep=true;a.Motor.Body.interpolation=RigidbodyInterpolation.None;
            if(candidate)a.UseCameraGaitCandidate();return a;
        }
        private void Floor()
        {var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(30,.2f,30);}
        internal static List<Vector2Int> Pairs(int[] triangles)
        {
            var pending=new Dictionary<ulong,int>();var pairs=new List<Vector2Int>();
            for(var f=0;f<triangles.Length/3;f++)for(var e=0;e<3;e++)
            {var a=triangles[f*3+e];var b=triangles[f*3+(e+1)%3];var key=((ulong)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);if(pending.TryGetValue(key,out var other)){pairs.Add(new Vector2Int(other,f));pending.Remove(key);}else pending[key]=f;}
            Assert.That(pending.Count,Is.Zero);return pairs;
        }
        internal static float Surface(Vector3[] vertices,int[] triangles,List<Vector2Int> pairs,out Vector3 location)
        {
            var normals=new Vector3[triangles.Length/3];
            for(var f=0;f<normals.Length;f++){var a=vertices[triangles[f*3]];var n=Vector3.Cross(vertices[triangles[f*3+1]]-a,vertices[triangles[f*3+2]]-a);if(n.sqrMagnitude<=1e-30f)Assert.Fail("Degenerate skin triangle");normals[f]=n/n.magnitude;}
            var min=1f;var worst=0;foreach(var p in pairs){var d=Vector3.Dot(normals[p.x],normals[p.y]);if(d<min){min=d;worst=p.x;}}location=(vertices[triangles[worst*3]]+vertices[triangles[worst*3+1]]+vertices[triangles[worst*3+2]])/3;return Mathf.Acos(Mathf.Clamp(min,-1,1))*Mathf.Rad2Deg;
        }
        [UnityTest] public IEnumerator JumpTurnStopAndRestartPreservePhysicsAcrossEightDirections()
        {
            var records=new List<Metrics>();var failures=new List<string>();Directory.CreateDirectory(Output);
            foreach(var yaw in new[]{0,45,90,135,180,225,270,315})
            {
                _scene=SceneManager.CreateScene("Camera gait transition "+yaw,new CreateSceneParameters(LocalPhysicsMode.Physics3D));Floor();
                var forward=Quaternion.Euler(0,yaw,0)*Vector3.forward;var actors=new[]{Actor(new Vector3(-3,.02f,0),false),Actor(new Vector3(3,.02f,0),true)};
                var skin=actors[1].Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=Pairs(triangles);var m=new Metrics{name="Transition_"+yaw};
                for(var step=0;step<330;step++)
                {
                    foreach(var a in actors)
                    {
                        var direction=step<145?forward:-forward;a.Motor.MoveInput=step>=60&&(step<210||step>=260)?new Vector2(direction.x,direction.z):Vector2.zero;
                        a.Motor.SprintRequested=true;if(step==120)a.Motor.RequestJump();a.Motor.Step(1f/60);
                    }
                    _scene.GetPhysicsScene().Simulate(1f/60);foreach(var a in actors)a.ApplyPresentation(1f/60);
                    Assert.That(Vector3.Distance(actors[0].Motor.Body.linearVelocity,actors[1].Motor.Body.linearVelocity),Is.LessThan(.0001f));
                    m.reachCorrection=Mathf.Max(m.reachCorrection,actors[1].Pose.MaximumReachCorrection);
                    if(step%6==0||(step>=110&&step<=180))
                    {
                        skin.BakeMesh(mesh);var vertices=mesh.vertices;var angle=Surface(vertices,triangles,pairs,out var location);if(angle>m.surfaceAngle){m.surfaceAngle=angle;m.worstSurfaceStep=step;m.worstSurfacePoint=location;}
                        if(actors[1].Motor.Grounded)m.penetration=Mathf.Max(m.penetration,-vertices.Min(v=>skin.transform.TransformPoint(v).y));
                    }
                }
                Assert.That(actors[1].Motor.JumpCount,Is.EqualTo(1));Assert.That(actors[1].Motor.LandingCount,Is.EqualTo(actors[0].Motor.LandingCount));
                if(m.surfaceAngle>=45||m.reachCorrection>.003f||m.penetration>.008f)failures.Add(JsonUtility.ToJson(m));records.Add(m);
                File.WriteAllText(Path.Combine(Output,"Transitions.json"),JsonUtility.ToJson(new Report{status="OBSERVED",cases=records.ToArray()},true));
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));File.WriteAllText(Path.Combine(Output,"Transitions.json"),JsonUtility.ToJson(new Report{status="PASS",cases=records.ToArray()},true));
        }
        [UnityTest] public IEnumerator CandidateQueryRigAndRecoveryReturnStayAligned()
        {
            _scene=SceneManager.CreateScene("Camera gait combat recovery",new CreateSceneParameters(LocalPhysicsMode.Physics3D));Floor();var actor=Actor(new Vector3(0,.02f,0),true);
            actor.EnableGrabReview();actor.Hands.ConfigurePunchTiming(1.4);actor.Hands.PreciseHits=true;actor.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
            var receiver=actor.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.Bind(actor);var rag=receiver.Ragdoll;var rig=actor.GetComponent<PunchHitRig>();
            var nodes=actor.Pose.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);var maxParity=0f;var sawReturn=false;var returnDrop=0f;
            for(var step=0;step<800;step++)
            {
                if(step==180)rag.BeginDown(Vector3.zero,Vector3.zero);
                rag.Down.Step(1f/60);rag.Step(1f/60);receiver.Step();actor.Motor.MoveInput=step>=60&&step<180?Vector2.up:Vector2.zero;actor.Motor.SprintRequested=true;
                actor.Motor.Step(1f/60);actor.Hands.SubmitButtons(false,false,step==100,false,1f/60);actor.Hands.Step(1f/60);_scene.GetPhysicsScene().Simulate(1f/60);
                if(rag.Active)rag.ApplyPose();else
                {
                    var before=nodes["Pelvis"].position;actor.ApplyPresentation(1f/60);
                    if(!sawReturn&&rag.Recoveries>0){sawReturn=true;returnDrop=Vector3.Distance(before,nodes["Pelvis"].position);}
                    if(step<180||rag.Recoveries>0)foreach(var name in new[]{"HandLogical_L","Foot_L","Foot_R","Pelvis"})
                    {
                        var a=actor.transform.InverseTransformPoint(nodes[name].position);var b=rig.Bone("Root").parent.InverseTransformPoint(rig.Bone(name).position);maxParity=Mathf.Max(maxParity,Vector3.Distance(a,b));
                    }
                }
            }
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"Integration.json"),$"{{\"queryPoseError\":{maxParity.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"returnPelvisShift\":{returnDrop.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"recoveries\":{rag.Recoveries}}}");
            Assert.That(maxParity,Is.LessThan(.03f));Assert.That(sawReturn,Is.True);Assert.That(rag.Recoveries,Is.EqualTo(1));Assert.That(returnDrop,Is.LessThan(.008f),"stale running pose must not pull the pelvis down after get-up");
            yield return null;
        }
        [UnityTest] public IEnumerator CaptureSameSharedCameraAtSixtyFramesPerSecond()
        {
            Directory.CreateDirectory(Output);var roots=new List<Vector3[]>();var cameraPos=new List<Vector3>();
            foreach(var candidate in new[]{false,true})
            {
                _scene=SceneManager.CreateScene("Camera gait capture "+candidate,new CreateSceneParameters(LocalPhysicsMode.Physics3D));var arena=SharedArenaReview.Create(_scene,4,candidate,movementScale:1f);arena.ReadKeyboard=false;arena.ShowHelp=false;arena.SharedCamera.ManualStep=true;arena.SharedCamera.View.enabled=false;
                foreach(var a in arena.Actors){a.Motor.ManualStep=true;a.Motor.Body.interpolation=RigidbodyInterpolation.None;}arena.InitializeParticipants();
                var camera=arena.SharedCamera.View;camera.aspect=16f/9;var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                var dir=Path.Combine(Output,candidate?"Candidate60":"Previous60");Directory.CreateDirectory(dir);var frames=0;
                for(var step=0;step<390;step++)
                {
                    foreach(var a in arena.Actors){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(1f/60);r.Ragdoll.Step(1f/60);r.Step();}
                    for(var i=0;i<4;i++)
                    {
                        var a=arena.Actors[i];var offset=i*6;var walk=step>=60+offset&&step<180+offset;var sprint=step>=240+offset&&step<330+offset;
                        var direction=i==0?Vector2.left:i==1?Vector2.right:i==2?Vector2.up:Vector2.down;a.Motor.MoveInput=walk||sprint?direction:Vector2.zero;a.Motor.SprintRequested=sprint;a.Motor.Step(1f/60);
                    }
                    foreach(var a in arena.Actors)a.Hands.Step(1f/60);_scene.GetPhysicsScene().Simulate(1f/60);foreach(var a in arena.Actors)a.ApplyPresentation(1f/60);
                    arena.SharedCamera.SampleAuthority();arena.SharedCamera.Present(1f/60);Assert.That(arena.SharedCamera.CapacityExceeded,Is.False);
                    if(step>=30)
                    {
                        if(!candidate){roots.Add(arena.Actors.Select(a=>a.Motor.Body.position).ToArray());cameraPos.Add(camera.transform.position);}
                        else{for(var i=0;i<4;i++)Assert.That(Vector3.Distance(arena.Actors[i].Motor.Body.position,roots[frames][i]),Is.LessThan(.0001f));Assert.That(Vector3.Distance(camera.transform.position,cameraPos[frames]),Is.LessThan(.0001f));}
                        var previousFrame=Time.frameCount;yield return null;Assert.That(Time.frameCount,Is.GreaterThan(previousFrame));camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(dir,$"Frame_{frames:D4}.png"),image.EncodeToPNG());frames++;
                    }
                }
                Assert.That(frames,Is.EqualTo(360));File.WriteAllText(Path.Combine(dir,"Report.json"),$"{{\"status\":\"PASS\",\"candidate\":{candidate.ToString().ToLowerInvariant()},\"frames\":360,\"fps\":60,\"seconds\":6,\"fixture\":\"Identical staggered walk, stop, sprint, stop commands; same shared camera profile; no synthetic frame interpolation.\"}}");
                camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator SteadyWalkSprintPreserveSpeedAndStayWithinRigReach()
        {
            Directory.CreateDirectory(Output);var results=new List<Metrics>();var failures=new List<string>();
            var yaws=Environment.GetEnvironmentVariable("GAIT_REVIEW_QUICK")=="1" ? new[]{0} : new[]{0,45,90,135,180,225,270,315};
            foreach(var sprint in new[]{false,true})foreach(var yaw in yaws)
            {
                _scene=SceneManager.CreateScene("Camera gait "+sprint+" "+yaw,new CreateSceneParameters(LocalPhysicsMode.Physics3D));Floor();
                var rotation=Quaternion.Euler(0,yaw,0);var forward=rotation*Vector3.forward;var right=rotation*Vector3.right;
                var actors=new[]{Actor(right*-1.5f+Vector3.up*.02f,false),Actor(right*1.5f+Vector3.up*.02f,true)};
                foreach(var a in actors){a.transform.rotation=rotation;a.Motor.Body.rotation=rotation;a.Motor.SprintRequested=sprint;}
                UnityEngine.Physics.SyncTransforms();
                var skins=actors.Select(a=>a.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var triangles=skins[1].sharedMesh.triangles;var pairs=Pairs(triangles);var mesh=new Mesh();
                var metrics=new[]{new Metrics{name=$"{(sprint?"Sprint":"Walk")}_{yaw}_Previous"},new Metrics{name=$"{(sprint?"Sprint":"Walk")}_{yaw}_Candidate"}};
                var feet=actors.Select(a=>a.Pose.GetComponentsInChildren<Transform>().Where(t=>t.name=="Foot_L"||t.name=="Foot_R").ToArray()).ToArray();
                var last=new Vector3[2,2];var planted=new bool[2,2];var stance=new bool[2,2];
                var minY=new[]{100f,100f};var maxY=new[]{-100f,-100f};var minZ=new[]{100f,100f};var maxZ=new[]{-100f,-100f};var minPelvis=new[]{100f,100f};var maxPelvis=new[]{-100f,-100f};
                var nodes=actors.Select(a=>a.Pose.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t)).ToArray();
                for(var step=0;step<300;step++)
                {
                    foreach(var a in actors){a.Motor.MoveInput=step>=60?new Vector2(forward.x,forward.z):Vector2.zero;a.Motor.Step(1f/60);}
                    _scene.GetPhysicsScene().Simulate(1f/60);foreach(var a in actors)a.ApplyPresentation(1f/60);
                    Assert.That(Vector3.Distance(actors[0].Motor.Body.linearVelocity,actors[1].Motor.Body.linearVelocity),Is.LessThan(.0001f),"visual profile must not change physics speed");
                    for(var i=0;i<2;i++)
                    {
                        var a=actors[i];var m=metrics[i];m.reachCorrection=Mathf.Max(m.reachCorrection,a.Pose.MaximumReachCorrection);
                        for(var side=0;side<2;side++)
                        {
                            var foot=feet[i][side].position;var cp=Mathf.Repeat(a.Pose.GaitPhase+side*.5f-.25f,1);var inStance=cp<.5f;
                            if(step>=150)
                            {
                                if(inStance&&stance[i,side])m.stancePairs++;
                                if(a.Pose.IsFootPlanted(side)&&planted[i,side]){m.plantedPairs++;m.stanceSpeed=Mathf.Max(m.stanceSpeed,Vector3.Distance(foot,last[i,side])*60);}
                                if(side==0){minY[i]=Mathf.Min(minY[i],foot.y);maxY[i]=Mathf.Max(maxY[i],foot.y);var z=Vector3.Dot(foot-a.Motor.Body.position,forward);minZ[i]=Mathf.Min(minZ[i],z);maxZ[i]=Mathf.Max(maxZ[i],z);}
                                var p=nodes[i]["Pelvis"].position.y;minPelvis[i]=Mathf.Min(minPelvis[i],p);maxPelvis[i]=Mathf.Max(maxPelvis[i],p);
                            }
                            last[i,side]=foot;planted[i,side]=a.Pose.IsFootPlanted(side);stance[i,side]=inStance;
                            var suffix=side==0?"L":"R";var thigh=nodes[i]["Thigh_"+suffix];var calf=nodes[i]["Calf_"+suffix];
                            m.maxKnee=Mathf.Max(m.maxKnee,Vector3.Angle(calf.position-thigh.position,feet[i][side].position-calf.position));
                        }
                        // Include every 60 Hz pose over the final second, which
                        // covers more than a complete walk and sprint cycle.
                        if(step%4==0||step>=240)
                        {
                            skins[i].BakeMesh(mesh);var vertices=mesh.vertices;var angle=Surface(vertices,triangles,pairs,out var location);if(angle>m.surfaceAngle){m.surfaceAngle=angle;m.worstSurfaceStep=step;m.worstSurfacePoint=location;}
                            m.penetration=Mathf.Max(m.penetration,-vertices.Min(v=>skins[i].transform.TransformPoint(v).y));
                        }
                    }
                }
                for(var i=0;i<2;i++)
                {
                    var m=metrics[i];m.footLift=maxY[i]-minY[i];m.strideRange=maxZ[i]-minZ[i];m.pelvisRange=maxPelvis[i]-minPelvis[i];m.coverage=m.stancePairs>0?(float)m.plantedPairs/m.stancePairs:0;results.Add(m);
                    if(m.surfaceAngle>=45||m.reachCorrection>.003f||m.penetration>.008f||m.stanceSpeed>.01f||m.coverage<.95f)failures.Add(m.name+" "+JsonUtility.ToJson(m));
                }
                File.WriteAllText(Path.Combine(Output,"Steady.json"),JsonUtility.ToJson(new Report{status="OBSERVED",cases=results.ToArray()},true));
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));File.WriteAllText(Path.Combine(Output,"Steady.json"),JsonUtility.ToJson(new Report{status="PASS",cases=results.ToArray()},true));
        }
    }
}
