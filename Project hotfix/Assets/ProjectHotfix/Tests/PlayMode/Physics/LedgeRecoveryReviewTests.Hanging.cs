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
    public sealed partial class LedgeRecoveryReviewTests
    {
        private string HangingOutput=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/hanging-load-20260910"));
        private HangingBodyPose Hang=>Actor.Pose.GetComponent<HangingBodyPose>();
        [Serializable] private sealed class HangFrame
        {public int tick;public bool left,right;public Vector3 root,pelvis;public float roll,drop,scale,load,handError;}
        [Serializable] private sealed class HangReport{public string status;public HangFrame[] frames;}
        private void HoldStill()
        {Actor.Motor.MoveInput=Vector2.zero;Actor.Motor.SprintRequested=false;}
        private HangFrame HangSnapshot(int tick)=>new HangFrame{tick=tick,left=Ledge.IsHolding(0),right=Ledge.IsHolding(1),root=Actor.Motor.Body.position,
            pelvis=Actor.FixedPose.WorldPosition("Pelvis"),roll=Hang!=null?Hang.AppliedRoll:0,drop=Hang!=null?Hang.AppliedDrop:0,scale=Hang!=null?Hang.ContactScale:1,load=Hang!=null?Hang.LoadWeight:0,handError=Actor.GrabPose.MaximumHeldTargetError};
        [UnityTest] public IEnumerator HangingLoadChangesPostureWithoutChangingContactPhysics()
        {
            Directory.CreateDirectory(HangingOutput);var rows=new List<HangFrame>();
            foreach(var hands in new[]{1,2,3})
            {
                var physics=new List<string>();var roots=new List<Vector3>();var velocities=new List<Vector3>();
                foreach(var candidate in new[]{false,true})
                {
                    Create(candidate);ReadyHold(hands);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);HoldStill();
                    for(var tick=0;tick<65;tick++)
                    {
                        if(tick==55)Actor.Motor.RequestJump();Tick();
                        var signature=$"{Ledge.IsHolding(0)},{Ledge.IsHolding(1)},{Ledge.Acquisitions},{Ledge.Breaks},{Ledge.TotalAssists},{Actor.Motor.Grounded}";
                        if(!candidate){physics.Add(signature);roots.Add(Actor.Motor.Body.position);velocities.Add(Actor.Motor.Body.linearVelocity);}
                        else
                        {
                            Assert.That(signature,Is.EqualTo(physics[tick]),$"hands {hands} tick {tick}");
                            Assert.That(Vector3.Distance(Actor.Motor.Body.position,roots[tick]),Is.LessThan(.00001f));
                            Assert.That(Vector3.Distance(Actor.Motor.Body.linearVelocity,velocities[tick]),Is.LessThan(.00001f));
                            if(tick==45)
                            {
                                var row=HangSnapshot(hands);rows.Add(row);
                                Assert.That(row.drop,Is.GreaterThan(.008f));Assert.That(row.handError,Is.LessThan(.0021f));
                                Assert.That(row.left,Is.EqualTo((hands&1)!=0));Assert.That(row.right,Is.EqualTo((hands&2)!=0));
                                if(hands==1)Assert.That(row.roll,Is.LessThan(-4));
                                if(hands==2)Assert.That(row.roll,Is.GreaterThan(4));
                                if(hands==3)Assert.That(Mathf.Abs(row.roll),Is.LessThan(1));
                            }
                        }
                    }
                    var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
                }
            }
            File.WriteAllText(Path.Combine(HangingOutput,"LoadPhysics.json"),JsonUtility.ToJson(new HangReport{status="PASS",frames=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator HangingTransitionAndRenderedBonesAreFrameIndependent()
        {
            Directory.CreateDirectory(HangingOutput);var poses=new List<Vector3[]>();var rows=new List<HangFrame>();var errors=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();ReadyHold(3);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);HoldStill();var clock=0d;var tick=0;var maxError=0f;var previousRoll=Hang.AppliedRoll;var maxRollStep=0f;
                while(tick<100)
                {
                    clock=Math.Min(100d/60,clock+1d/fps);var due=Math.Min(100,(int)Math.Floor(clock*60+1e-8));
                    while(tick<due)
                    {
                        if(tick==28)Buttons(2,true);
                        if(tick==55)Buttons(3,true);
                        if(tick==80)Buttons(3,false);
                        Tick();var bones=new[]{"Pelvis","Head","UpperArm_L","HandLogical_L","HandLogical_R","Foot_L","Foot_R"}.Select(Actor.FixedPose.WorldPosition).ToArray();
                        if(fps==60){poses.Add(bones);rows.Add(HangSnapshot(tick));}
                        else for(var b=0;b<bones.Length;b++)maxError=Mathf.Max(maxError,Vector3.Distance(bones[b],poses[tick][b]));
                        maxRollStep=Mathf.Max(maxRollStep,Mathf.Abs(Hang.AppliedRoll-previousRoll));previousRoll=Hang.AppliedRoll;
                        if(tick==48){Assert.That(Ledge.IsHolding(1)&&!Ledge.IsHolding(0),Is.True);Assert.That(Hang.AppliedRoll,Is.GreaterThan(4));}
                        if(tick==76)Assert.That(Ledge.IsHolding(0)&&Ledge.IsHolding(1),Is.True);
                        if(Ledge.IsHolding(0)||Ledge.IsHolding(1))Assert.That(Actor.GrabPose.MaximumHeldTargetError,Is.LessThan(.0031f));tick++;
                    }
                    var alpha=(float)Math.Max(0,Math.Min(1,(clock-tick/60d)*60));foreach(var a in _boundary.Arena.Actors)a.FixedPose.Present(alpha);
                }
                Assert.That(maxError,Is.LessThan(.00001f));Assert.That(maxRollStep,Is.LessThan(3));Assert.That(Hang.AppliedDrop,Is.LessThan(.008f));
                errors.Add($"{fps} fps: bone error {maxError:R} m, max roll step {maxRollStep:R} degrees");
                _boundary.Arena.ResetReview();Tick();Assert.That(Hang.LoadWeight,Is.Zero);Assert.That(Hang.AppliedDrop,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllLines(Path.Combine(HangingOutput,"FrameRates.txt"),errors);
            File.WriteAllText(Path.Combine(HangingOutput,"Transitions.json"),JsonUtility.ToJson(new HangReport{status="PASS",frames=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator HangingHitRigSkinAndDownHandoffStayConsistent()
        {
            Directory.CreateDirectory(HangingOutput);var evidence=new List<string>();
            foreach(var hands in new[]{1,2,3})
            {
                Create();ReadyHold(hands);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);HoldStill();
                var skin=Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));
                var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);
                var maximumSurface=0f;var maximumQueryError=0f;var maximumHandError=0f;var diagnostics=new List<string>();
                for(var tick=0;tick<70;tick++)
                {
                    if(hands==1&&tick==50)Buttons(3,true);
                    if(hands==1&&tick==51)Buttons(1,true);
                    Tick();
                    PunchHitRig.SyncScene(_scene,Dt);var hit=Actor.GetComponent<PunchHitRig>();
                    var worstName="";var frameQueryError=0f;
                    foreach(var name in new[]{"Pelvis","Head","UpperArm_L","UpperArm_R","Forearm_L","Forearm_R","Foot_L","Foot_R"})
                    {var error=Vector3.Distance(hit.Bone(name).position,Actor.FixedPose.WorldPosition(name));if(error>frameQueryError){frameQueryError=error;worstName=name;}maximumQueryError=Mathf.Max(maximumQueryError,error);}
                    var before=hit.Bone("Head").position;PunchHitRig.SyncScene(_scene,Dt);
                    Assert.That(Vector3.Distance(before,hit.Bone("Head").position),Is.LessThan(.000001f),"A second hit query must not advance the hanging response.");
                    skin.BakeMesh(mesh);var surface=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var location);maximumSurface=Mathf.Max(maximumSurface,surface);
                    Hang.RestoreBaseline();skin.BakeMesh(mesh);var baseline=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var baseLocation);Actor.FixedPose.RestoreForEvaluation();
                    diagnostics.Add($"tick {tick}: query {frameQueryError:R} {worstName}; surface {surface:R} {location:R}; baseline {baseline:R} {baseLocation:R}; roll {Hang.AppliedRoll:R}");
                    File.WriteAllLines(Path.Combine(HangingOutput,$"Diagnostics{hands}.txt"),diagnostics);
                    maximumHandError=Mathf.Max(maximumHandError,Actor.GrabPose.MaximumHeldTargetError);
                }
                evidence.Add($"hands={hands}: query error={maximumQueryError:R} m; skin dihedral={maximumSurface:R} degrees; hand error={maximumHandError:R} m");
                File.WriteAllLines(Path.Combine(HangingOutput,"HitSkinDown.txt"),evidence);
                Assert.That(maximumQueryError,Is.LessThan(.001f));Assert.That(maximumSurface,Is.LessThan(60),"Hanging shoulder surface must remain below a sharp fold; gait has its separate 45-degree review.");Assert.That(maximumHandError,Is.LessThan(.0031f));
                if(hands==1)Assert.That(Actor.Hands.State.PunchCount,Is.Zero,"An airborne tap must not add a ground punch; the existing air action policy is unchanged.");
                var rag=Actor.GetComponent<RagdollRecoveryReview>();rag.BeginDown(Vector3.zero,Vector3.zero);Tick();
                Assert.That(rag.Active,Is.True);Assert.That(Ledge.IsHolding(0)||Ledge.IsHolding(1),Is.False);Assert.That(Hang.LoadWeight,Is.Zero);
                Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);_boundary.Arena.ResetReview();Tick();
                Assert.That(rag.Active,Is.False);Assert.That(Hang.AppliedDrop,Is.Zero);
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator RaisedWallContactsSupportAllFourEdgesAndRecoveryDepths()
        {
            var evidence=new List<string>();
            foreach(var y in new[]{-.60f,-1.75f,-2.18f})for(var side=0;side<4;side++)
            {
                Create(true,true);ReadyHold(y< -1?3:1);SeedFall(side,y);var input=new RecoveryInput();var sawHigh=false;var error=0f;
                for(var tick=0;tick<360&&!_boundary.Out[0]&&!input.finished;tick++)
                {
                    RecoveryStep(input,y< -1?3:1);
                    for(var hand=0;hand<2;hand++)if(Ledge.IsHolding(hand))
                    {
                        var height=(Quaternion.Inverse(Actor.Motor.Body.rotation)*(Ledge.ContactPoint(hand)-Actor.Motor.Body.position)).y;
                        sawHigh|=height>.74f;error=Mathf.Max(error,Actor.GrabPose.MaximumHeldTargetError);
                    }
                }
                evidence.Add($"side={side}, y={y:R}, returned={input.finished}, high={sawHigh}, assists={Ledge.TotalAssists}, hand error={error:R}, release={Ledge.LastReleaseReason}");
                var output=Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/auto-lift-20260911"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"RaisedWalls.txt"),evidence);
                Assert.That(sawHigh,Is.True);Assert.That(input.finished,Is.True);Assert.That(Ledge.TotalAssists,Is.LessThanOrEqualTo(2));Assert.That(error,Is.LessThan(.005f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator CaptureRaisedWallGripAndClimb()
        {
            Create(true,true);ReadyHold(3);SeedFall(2,-.65f);var camera=_boundary.Arena.SharedCamera.View;camera.fieldOfView=32;camera.aspect=16f/9;
            _boundary.Arena.GetComponentInChildren<Light>().intensity=1.2f;
            var folder=Path.Combine(Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/auto-lift-20260911")),"Wall");Directory.CreateDirectory(folder);
            var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
            var input=new RecoveryInput();var frames=new List<HangFrame>();var held=0;var skin=Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;var diagnostics=new List<string>();
            for(var tick=0;tick<150;tick++)
            {
                if(Ledge.IsHolding(0)||Ledge.IsHolding(1))held++;
                if(held>0&&held<35){HoldStill();Tick();}
                else if(held>=35){Actor.Motor.MoveInput=Vector2.up;Actor.Motor.SprintRequested=true;RecoveryStep(input,3);}else Tick();
                frames.Add(HangSnapshot(tick));Assert.That(_boundary.Out[0],Is.False);skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var location);surface=Mathf.Max(surface,angle);
                var grabState=Actor.Hands.Grabs.Capture();Hang.RestoreBaseline();skin.BakeMesh(mesh);var baseline=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _);Actor.FixedPose.RestoreForEvaluation();
                diagnostics.Add($"tick {tick}: skin {angle:R} baseline {baseline:R} location {location:R} holding {Ledge.IsHolding(0)}/{Ledge.IsHolding(1)} lift {grabState.Left.LiftAmount:R}/{grabState.Right.LiftAmount:R} roll {Hang.AppliedRoll:R} load {Hang.LoadWeight:R} root {Actor.Motor.Body.position:R}");File.WriteAllLines(Path.Combine(folder,"SkinTrace.txt"),diagnostics);
                Actor.FixedPose.Present(1);yield return null;FrameHangingReview(camera);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(folder,"Frames.json"),JsonUtility.ToJson(new HangReport{status="PASS",frames=frames.ToArray()},true));File.WriteAllText(Path.Combine(folder,"Skin.txt"),surface.ToString("R"));
            Assert.That(surface,Is.LessThan(60));Assert.That(input.finished,Is.True);camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
        }
        private void FrameHangingReview(Camera camera)
        {
            // Follow a portion of the vertical rise so the whole character stays
            // in shot. Before/after use the same physical root and camera rule.
            var follow=Vector3.up*Mathf.Max(0,Actor.Motor.Body.position.y+.65f)*.8f;
            var eye=new Vector3(1.5f,.8f,-6.0f)+follow;var target=new Vector3(0,-.2f,-3.1f)+follow;
            camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
        }
        [UnityTest] public IEnumerator CaptureLoadDrivenHangingAtNativeSixtyFps()
        {
            foreach(var hands in new[]{1,2,3})foreach(var candidate in new[]{false,true})
            {
                Create(candidate);ReadyHold(hands);SeedFall(2,-.65f);var camera=_boundary.Arena.SharedCamera.View;
                FrameHangingReview(camera);camera.fieldOfView=32;camera.aspect=16f/9;
                var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                var folder=Path.Combine(HangingOutput,(candidate?"After":"Before")+hands);Directory.CreateDirectory(folder);var frames=new List<HangFrame>();var heldTicks=0;var input=new RecoveryInput();
                for(var tick=0;tick<180;tick++)
                {
                    if(Ledge.IsHolding(0)||Ledge.IsHolding(1))heldTicks++;
                    if(heldTicks>0&&heldTicks<55){HoldStill();Tick();}
                    else if(heldTicks>=55){Actor.Motor.MoveInput=Vector2.up;Actor.Motor.SprintRequested=true;RecoveryStep(input,hands);}
                    else Tick();
                    frames.Add(HangSnapshot(tick));Assert.That(_boundary.Out[0],Is.False);
                    // Manual capture presents the fixed snapshot explicitly; yield
                    // then allows Unity skinning buffers to consume that new pose.
                    Actor.FixedPose.Present(1);yield return null;
                    FrameHangingReview(camera);
                    foreach(var point in new[]{Actor.FixedPose.WorldPosition("Head")+Vector3.up*.15f,Actor.FixedPose.WorldPosition("Foot_L")-Vector3.up*.03f,Actor.FixedPose.WorldPosition("Foot_R")-Vector3.up*.03f})
                    {var view=camera.WorldToViewportPoint(point);Assert.That(view.y,Is.InRange(.01f,.99f),$"Capture cut off a body endpoint at tick {tick}");}
                    camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                File.WriteAllText(Path.Combine(folder,"Frames.json"),JsonUtility.ToJson(new HangReport{status=input.finished?"PASS":"INCOMPLETE",frames=frames.ToArray()},true));
                Assert.That(input.finished,Is.True,$"{folder}: {JsonUtility.ToJson(frames.Last())}; assists {Ledge.TotalAssists}; {Ledge.LastReleaseReason}");
                camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
    }
}
