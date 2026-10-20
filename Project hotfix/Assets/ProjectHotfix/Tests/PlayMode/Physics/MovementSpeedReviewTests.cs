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
    public sealed class MovementSpeedReviewTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private SharedArenaReview _arena;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/movement-speed-20260910"));
        [Serializable] private sealed class Metrics
        {
            public float scale,walkSpeed,sprintSpeed,surfaceAngle,reachCorrection,penetration,stanceSpeed,coverage,queryPoseError;
            public int jumps,landings,plantedPairs,stancePairs,worstSurfaceTick;public Vector3 worstSurfacePoint;
        }
        [Serializable] private sealed class GripMetrics { public float scale,travel,maxForce,maxStress;public int jumps,throws; }
        [Serializable] private sealed class GripReport { public string status;public GripMetrics[] cases; }
        [Serializable] private sealed class Report{public string status;public Metrics[] cases;}
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(float scale,int count=2)
        {
            _scene=SceneManager.CreateScene("Movement speed "+scale,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _arena=SharedArenaReview.Create(_scene,count,true,scale);_arena.ReadKeyboard=false;_arena.ShowHelp=false;
            foreach(var a in _arena.Actors){a.ReadKeyboard=false;a.Motor.ManualStep=true;}
            _arena.SharedCamera.ManualStep=true;_arena.SharedCamera.View.enabled=false;_arena.InitializeParticipants();
        }
        private void Tick()
        {
            foreach(var a in _arena.Actors){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in _arena.Actors)a.Motor.Step(Dt);foreach(var a in _arena.Actors)a.Hands.Step(Dt);
            _scene.GetPhysicsScene().Simulate(Dt);foreach(var a in _arena.Actors)a.FixedPose.Advance(Dt);_arena.SharedCamera.AdvanceFixed(Dt);
        }
        [UnityTest] public IEnumerator FasterMotionKeepsRigReachSurfaceAndHitPoseWithinLimits()
        {
            Directory.CreateDirectory(Output);var rows=new List<Metrics>();var failures=new List<string>();
            foreach(var scale in new[]{1f,1.5f,2f})
            {
                Create(scale);var a=_arena.Actors[0];var b=_arena.Actors[1];
                var p=new Vector3(-3,.02f,0);a.transform.SetPositionAndRotation(p,Quaternion.identity);a.Motor.Body.position=p;a.Motor.Body.rotation=Quaternion.identity;
                b.transform.position=new Vector3(8,.02f,0);b.Motor.Body.position=b.transform.position;UnityEngine.Physics.SyncTransforms();
                var skin=a.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
                var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);
                var nodes=a.Pose.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);var rig=a.GetComponent<PunchHitRig>();
                var last=new Vector3[2];var wasPlanted=new bool[2];var wasStance=new bool[2];var m=new Metrics{scale=scale};
                for(var tick=0;tick<420;tick++)
                {
                    a.Motor.MoveInput=tick<60||tick>=380?Vector2.zero:tick<330?Vector2.up:Vector2.down;
                    a.Motor.SprintRequested=tick>=180;if(tick==318)a.Motor.RequestJump();Tick();
                    m.reachCorrection=Mathf.Max(m.reachCorrection,a.Pose.MaximumReachCorrection);
                    if(tick==170)m.walkSpeed=Vector3.ProjectOnPlane(a.Motor.Body.linearVelocity,Vector3.up).magnitude;
                    if(tick==290)m.sprintSpeed=Vector3.ProjectOnPlane(a.Motor.Body.linearVelocity,Vector3.up).magnitude;
                    var steady=(tick>=120&&tick<180)||(tick>=240&&tick<300);
                    if(steady||tick>=300||tick%6==0)
                    {
                        skin.BakeMesh(mesh);var vertices=mesh.vertices;var angle=CameraGaitReviewTests.Surface(vertices,triangles,pairs,out var location);if(angle>m.surfaceAngle){m.surfaceAngle=angle;m.worstSurfaceTick=tick;m.worstSurfacePoint=location;}
                        if(a.Motor.Grounded)m.penetration=Mathf.Max(m.penetration,-vertices.Min(v=>skin.transform.TransformPoint(v).y));
                    }
                    foreach(var name in new[]{"Pelvis","Foot_L","Foot_R","HandLogical_L"})
                        m.queryPoseError=Mathf.Max(m.queryPoseError,Vector3.Distance(a.transform.InverseTransformPoint(nodes[name].position),rig.Bone("Root").parent.InverseTransformPoint(rig.Bone(name).position)));
                    for(var side=0;side<2;side++)
                    {
                        var foot=nodes[side==0?"Foot_L":"Foot_R"].position;var inStance=Mathf.Repeat(a.Pose.GaitPhase+side*.5f-.25f,1)<.5f;
                        if(steady)
                        {
                            if(inStance&&wasStance[side])m.stancePairs++;
                            if(a.Pose.IsFootPlanted(side)&&wasPlanted[side]){m.plantedPairs++;m.stanceSpeed=Mathf.Max(m.stanceSpeed,Vector3.Distance(foot,last[side])/Dt);}
                        }
                        last[side]=foot;wasStance[side]=inStance;wasPlanted[side]=a.Pose.IsFootPlanted(side);
                    }
                }
                m.coverage=m.stancePairs>0?(float)m.plantedPairs/m.stancePairs:0;m.jumps=a.Motor.JumpCount;m.landings=a.Motor.LandingCount;rows.Add(m);
                File.WriteAllText(Path.Combine(Output,"Rig.json"),JsonUtility.ToJson(new Report{status="OBSERVED",cases=rows.ToArray()},true));
                if(m.surfaceAngle>=45||m.reachCorrection>.003f||m.penetration>.008f||m.stanceSpeed>.01f||m.coverage<.95f||m.queryPoseError>.03f)failures.Add(JsonUtility.ToJson(m));
                // Contact friction acts after the velocity target; measure and retain that physical loss.
                Assert.That(m.walkSpeed,Is.InRange(.7f*scale-.12f,.7f*scale+.001f));Assert.That(m.sprintSpeed,Is.InRange(1.05f*scale-.12f,1.05f*scale+.001f));Assert.That(m.jumps,Is.EqualTo(1));
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));File.WriteAllText(Path.Combine(Output,"Rig.json"),JsonUtility.ToJson(new Report{status="PASS",cases=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator FasterGrabbedMovementStillJumpsAndThrowsOnceOnRelease()
        {
            var rows=new List<GripMetrics>();
            foreach(var scale in new[]{1.5f,2f})
            {
                Create(scale);var a=_arena.Actors[0];var b=_arena.Actors[1];
                for(var i=0;i<2;i++){var actor=_arena.Actors[i];var p=new Vector3(0,.02f,i*.4f);actor.transform.SetPositionAndRotation(p,Quaternion.identity);actor.Motor.Body.position=p;actor.Motor.Body.rotation=Quaternion.identity;actor.Hands.SubmitButtons(false,false);}
                UnityEngine.Physics.SyncTransforms();for(var i=0;i<90;i++)Tick();a.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();
                Assert.That(a.Hands.Grabs.IsHolding(0),Is.True);var start=a.Motor.Body.position;
                for(var i=0;i<60;i++)
                {
                    a.Motor.MoveInput=Vector2.up;a.Motor.SprintRequested=true;if(i==25)a.Motor.RequestJump();Tick();
                    Assert.That(a.Hands.Grabs.IsHolding(0),Is.True,$"scale={scale} tick={i} reason={a.Hands.Grabs.LastReleaseReason}");
                }
                Assert.That(a.Motor.JumpCount,Is.EqualTo(1));var travel=Vector3.ProjectOnPlane(a.Motor.Body.position-start,Vector3.up).magnitude;Assert.That(travel,Is.GreaterThan(.2f));
                a.Hands.SubmitButtons(false,false);Tick();for(var i=0;i<30;i++)Tick();Assert.That(a.Hands.Grabs.ThrowCount,Is.EqualTo(1));
                Assert.That(b.GetComponent<PunchDownReview>().Accumulation.Points,Is.Zero);
                rows.Add(new GripMetrics{scale=scale,travel=travel,jumps=a.Motor.JumpCount,throws=a.Hands.Grabs.ThrowCount,maxForce=a.Hands.Grabs.MaximumObservedForce,maxStress=a.Hands.Grabs.MaximumObservedStress});
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"GrabJump.json"),JsonUtility.ToJson(new GripReport{status="PASS",cases=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator CaptureThreeSpeedsWithIdenticalFixedCameraAndInputTiming()
        {
            foreach(var scale in new[]{1f,1.5f,2f})
            {
                Create(scale,4);var camera=_arena.SharedCamera.View;camera.aspect=16f/9;
                var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                var dir=Path.Combine(Output,"Scale"+Mathf.RoundToInt(scale*100));Directory.CreateDirectory(dir);
                var fixedFocus=new Vector3(0,.45f,0);var rotation=_arena.SharedCamera.Orientation;var frames=0;
                for(var step=0;step<420;step++)
                {
                    for(var i=0;i<4;i++)
                    {
                        var a=_arena.Actors[i];var moving=step>=90&&step<270;
                        a.Motor.MoveInput=moving?(i%2==0?Vector2.left:Vector2.right):Vector2.zero;
                        a.Motor.SprintRequested=step>=180;
                    }
                    Tick();Assert.That(_arena.SharedCamera.CapacityExceeded,Is.False);
                    if(step<60)continue;
                    camera.transform.SetPositionAndRotation(fixedFocus-rotation*Vector3.forward*12,rotation);
                    var frame=Time.frameCount;yield return null;Assert.That(Time.frameCount,Is.GreaterThan(frame));camera.Render();
                    var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(dir,$"Frame_{frames:D4}.png"),image.EncodeToPNG());frames++;
                }
                Assert.That(frames,Is.EqualTo(360));camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
                File.WriteAllText(Path.Combine(dir,"Report.json"),$"{{\"scale\":{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"frames\":360,\"fps\":60,\"seconds\":6,\"sameCamera\":true,\"nativeRenderedFrames\":true}}");
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
    }
}
