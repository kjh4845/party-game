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
        private const float Dt=1f/60;
        private Scene _scene;
        private ArenaBoundaryReview _boundary;
        private PhysicsJumpReview Actor=>_boundary.Arena.Actors[0];
        private LedgeGrabReview Ledge=>Actor.GetComponent<LedgeGrabReview>();
        private string Output=>Environment.GetEnvironmentVariable("CODEX_LEDGE_REVIEW_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/ledge-recovery-20260910"));
        [Serializable] private sealed class Row{public int side,hands,assists,acquisitions,breaks;public float initialHeight,recoverySeconds,maximumForce,maximumStress,handError,rootApex;public string lastRelease;public bool recovered;}
        [Serializable] private sealed class Frame{public int tick,assists;public bool held,grounded;public Vector3 position,velocity;public float bottom,force;public string reason;}
        [Serializable] private sealed class Report{public string status;public Row[] cases;public Frame[] frames;}
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(bool hangingPose=true,bool raisedGrab=false)
        {
            _scene=SceneManager.CreateScene("Ledge recovery",new CreateSceneParameters(LocalPhysicsMode.Physics3D));_boundary=LedgeRecoveryReview.Create(_scene,2,hangingPose,raisedGrab);_boundary.ManualStep=true;
            var arena=_boundary.Arena;arena.ReadKeyboard=false;arena.ShowHelp=false;arena.SharedCamera.ManualStep=true;arena.SharedCamera.View.enabled=false;
            foreach(var a in arena.Actors){a.ReadKeyboard=false;a.Motor.ManualStep=true;a.Hands.SubmitButtons(false,false);a.Hands.Grabs.BoundedContactPose=Environment.GetEnvironmentVariable("CODEX_CONTACT_CANDIDATE")=="1";}arena.InitializeParticipants();Directory.CreateDirectory(Output);
        }
        private void Tick()
        {
            foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy)){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.Motor.Step(Dt);
            foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.Hands.Step(Dt);
            GrabPhysicsReview.StepAutomaticLifts(_scene,Dt);_scene.GetPhysicsScene().Simulate(Dt);foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.GetComponent<LedgeGrabReview>().AfterPhysics();_boundary.StepAfterPhysics();
            foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.FixedPose.Advance(Dt);_boundary.Arena.SharedCamera.AdvanceFixed(Dt);
        }
        private Vector3 Outside(int side,float y,float offset=.23f)
        {
            var normal=Quaternion.Euler(0,side*90,0)*Vector3.forward;var edge=side%2==0?_boundary.DepthCU*.5f:_boundary.WidthCU*.5f;return normal*(edge+offset)+Vector3.up*y;
        }
        private void SeedFall(int side,float y)
        {
            var normal=Quaternion.Euler(0,side*90,0)*Vector3.forward;var rotation=Quaternion.LookRotation(-normal);
            var p=Outside(side,y);Actor.transform.SetPositionAndRotation(p,rotation);Actor.Motor.Body.position=p;Actor.Motor.Body.rotation=rotation;
            Actor.Motor.Body.linearVelocity=Vector3.down*(y < -2 ? 6.5f : 2f);Actor.Motor.Body.angularVelocity=Vector3.zero;Actor.Motor.MoveInput=new Vector2(-normal.x,-normal.z);Actor.Motor.SprintRequested=true;
            UnityEngine.Physics.SyncTransforms();Actor.FixedPose.RestoreForEvaluation();
        }
        private void Buttons(int hands,bool down)=>Actor.Hands.SubmitButtons(down&&(hands&1)!=0,down&&(hands&2)!=0);
        private bool Recovered()=>Actor.Motor.Grounded&&Actor.GetComponent<CapsuleCollider>().bounds.min.y>-.02f&&Mathf.Abs(Actor.Motor.Body.position.x)<_boundary.WidthCU*.5f-.20f&&Mathf.Abs(Actor.Motor.Body.position.z)<_boundary.DepthCU*.5f-.20f;
        private void ReadyHold(int hands)
        {for(var i=0;i<60;i++)Tick();Buttons(hands,true);for(var i=0;i<20;i++)Tick();}
        private bool AwaitHold(int maximum=50)
        {for(var i=0;i<maximum&&!_boundary.Out[0];i++){Tick();if(Ledge.IsHolding(0)||Ledge.IsHolding(1))return true;}return false;}
        [UnityTest] public IEnumerator UnmarkedWallsCannotBeGrabbedOrHoldAnAirborneCapsuleByFriction()
        {
            Create();ReadyHold(1);foreach(var surface in _boundary.GetComponentsInChildren<ReviewGrabSurface>())surface.enabled=false;
            SeedFall(0,-.60f);for(var i=0;i<120&&!_boundary.Out[0];i++){Actor.Motor.RequestJump();Tick();}
            Assert.That(Ledge.Acquisitions,Is.Zero);Assert.That(Ledge.TotalAssists,Is.Zero);Assert.That(_boundary.Out[0],Is.True,"Pushing an ungrabbed wall cannot suspend the player.");yield return null;
        }
        [UnityTest] public IEnumerator ReleaseOneHandMenuAndResetNeverThrowTheMap()
        {
            Create();ReadyHold(3);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);for(var i=0;i<10;i++)Tick();Assert.That(Ledge.IsHolding(0)&&Ledge.IsHolding(1),Is.True);
            Actor.Hands.SubmitButtons(false,true);Tick();Assert.That(Ledge.IsHolding(0),Is.False);Assert.That(Ledge.IsHolding(1),Is.True);Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);
            Actor.Hands.SetInputSuppressed(true);var y=Actor.Motor.Body.position.y;for(var i=0;i<20;i++)Tick();Assert.That(Ledge.IsHolding(1),Is.False);Assert.That(Actor.Motor.Body.position.y,Is.LessThan(y-.05f));Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);
            _boundary.Arena.ResetReview();Tick();Assert.That(Ledge.AssistsUsed,Is.Zero);Assert.That(Ledge.TotalAssists,Is.Zero);Assert.That(Actor.Hands.State.PunchCount,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator BothHandsShareOneLaunchAndTheAirBudgetSurvivesHandRelease()
        {
            Create();Ledge.MaximumRiseCU=.10f;ReadyHold(3);SeedFall(0,-1f);Assert.That(AwaitHold(),Is.True);for(var i=0;i<8;i++)Tick();
            Actor.Motor.RequestJump();Tick();Assert.That(Ledge.TotalAssists,Is.EqualTo(1));Assert.That(Ledge.AssistsUsed,Is.EqualTo(1));
            Actor.Motor.RequestJump();Tick();Assert.That(Ledge.TotalAssists,Is.EqualTo(1));Assert.That(Ledge.RejectedRequests,Is.GreaterThanOrEqualTo(1));
            for(var attempt=0;attempt<2;attempt++)
            {
                // Seed a fresh real wall contact without resetting the motor/air episode.
                Ledge.Release(0,"RegripFixture",false);Ledge.Release(1,"RegripFixture",false);Buttons(3,false);Tick();SeedFall(0,-1f);Buttons(3,true);
                Assert.That(AwaitHold(),Is.True);for(var i=0;i<8;i++)Tick();Assert.That(Actor.Motor.Grounded,Is.False);
                var rejected=Ledge.RejectedRequests;Actor.Motor.RequestJump();Tick();Assert.That(Ledge.TotalAssists,Is.EqualTo(2));
                if(attempt==1)Assert.That(Ledge.RejectedRequests,Is.GreaterThan(rejected));
            }
            Buttons(3,false);Tick();Assert.That(Ledge.AssistsUsed,Is.EqualTo(2));Assert.That(Actor.Motor.Grounded,Is.False);Assert.That(Actor.Motor.JumpCount,Is.Zero);
            _boundary.Arena.ResetReview();Tick();Assert.That(Ledge.AssistsUsed,Is.Zero);yield return null;
        }
        private sealed class RecoveryInput
        {
            public int heldTicks,rearm,regripPresses,sinceAssist=1000;public bool wasHolding,finished;
        }
        private bool NeedsAnotherRecoveryAssist()
        {
            if(Ledge.TotalAssists>=Ledge.MaximumAssists)return false;
            var roof=_boundary.Arena.transform.Find("ReviewRoof").GetComponent<Collider>().bounds.max.y;
            var upward=Mathf.Max(0,Actor.Motor.Body.linearVelocity.y);
            var apex=Actor.Motor.Body.position.y+upward*upward/(2*Mathf.Max(.0001f,Mathf.Abs(UnityEngine.Physics.gravity.y)));
            return apex<roof+.05f*_boundary.CharacterUnit;
        }
        private void RecoveryStep(RecoveryInput input,int hands)
        {
            if(input.finished){Actor.Motor.MoveInput=Vector2.zero;Buttons(hands,false);Tick();return;}
            var holding=Ledge.IsHolding(0)||Ledge.IsHolding(1);input.heldTicks=holding?input.heldTicks+1:0;input.sinceAssist++;
            // This is review automation, not player input policy. Keep the
            // existing held button through a sufficient launch. Only synthesize
            // a fresh press when another assist is actually needed and available.
            if(input.wasHolding&&!holding&&NeedsAnotherRecoveryAssist()){Buttons(hands,false);input.rearm=1;}
            else if(input.rearm>0){Buttons(hands,true);input.rearm=0;input.regripPresses++;}
            if(holding&&input.heldTicks>=8&&input.sinceAssist>=24&&Ledge.TotalAssists<2){Actor.Motor.RequestJump();input.sinceAssist=0;}
            Tick();input.wasHolding=holding;if(Recovered())input.finished=true;
        }
        [UnityTest] public IEnumerator LedgeRecoveryIsIndependentOfRenderRate()
        {
            var signatures=new List<string>();var positions=new List<Vector3>();var camera=new List<Vector3>();var errors=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();ReadyHold(3);SeedFall(2,-2.18f);var input=new RecoveryInput();var time=0d;var tick=0;var error=0f;
                while(tick<180)
                {
                    time=Math.Min(3,time+1d/fps);var due=Math.Min(180,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        RecoveryStep(input,3);var signature=$"{Ledge.TotalAssists},{Ledge.AssistsUsed},{Ledge.IsHolding(0)},{Ledge.IsHolding(1)},{Ledge.Acquisitions},{Ledge.Breaks},{Actor.Motor.Grounded},{Actor.Motor.JumpCount},{_boundary.Out[0]}";
                        if(fps==60){signatures.Add(signature);positions.Add(Actor.Motor.Body.position);camera.Add(_boundary.Arena.SharedCamera.View.transform.position);}
                        else{Assert.That(signature,Is.EqualTo(signatures[tick]));error=Mathf.Max(error,Vector3.Distance(Actor.Motor.Body.position,positions[tick]),Vector3.Distance(_boundary.Arena.SharedCamera.View.transform.position,camera[tick]));}tick++;
                    }
                    var alpha=(float)Math.Max(0,Math.Min(1,(time-tick/60d)*60));foreach(var a in _boundary.Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.FixedPose.Present(alpha);_boundary.Arena.SharedCamera.Render(alpha);
                }
                Assert.That(input.finished,Is.True);Assert.That(error,Is.LessThan(.0001f));errors.Add($"{fps}: {error:R}");
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),errors);
        }
        [UnityTest] public IEnumerator CaptureOneAndTwoAssistReturnsAtNativeSixtyFps()
        {
            foreach(var deep in new[]{false,true})
            {
                Create();var hands=deep?3:1;ReadyHold(hands);SeedFall(2,deep?-2.18f:-.60f);var input=new RecoveryInput();var c=_boundary.Arena.SharedCamera;c.View.aspect=16f/9;c.AdvanceFixed(5);
                var rt=new RenderTexture(960,540,24);rt.Create();c.View.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                var folder=Path.Combine(Output,deep?"Deep":"Lip");Directory.CreateDirectory(folder);var metadata=new List<Frame>();
                for(var tick=0;tick<180;tick++)
                {
                    RecoveryStep(input,hands);Assert.That(_boundary.Out[0],Is.False);Assert.That(c.CapacityExceeded,Is.False);
                    metadata.Add(new Frame{tick=tick,assists=Ledge.TotalAssists,held=Ledge.IsHolding(0)||Ledge.IsHolding(1),grounded=Actor.Motor.Grounded,position=Actor.Motor.Body.position,velocity=Actor.Motor.Body.linearVelocity,bottom=Actor.GetComponent<CapsuleCollider>().bounds.min.y,force=Ledge.MaximumObservedForce,reason=Ledge.LastReleaseReason});
                    var frame=Time.frameCount;yield return null;Assert.That(Time.frameCount,Is.GreaterThan(frame));c.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                Assert.That(input.finished,Is.True);Assert.That(Ledge.TotalAssists,Is.EqualTo(deep?2:1));File.WriteAllText(Path.Combine(folder,"Frames.json"),JsonUtility.ToJson(new Report{status="PASS",frames=metadata.ToArray()},true));
                c.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator FourEdgesUseRealContactAndBoundedAssistToReturnToRoof()
        {
            var rows=new List<Row>();var failures=new List<string>();
            foreach(var y in new[]{-.60f,-1.75f,-2.18f})for(var side=0;side<4;side++)
            {
                Create();for(var i=0;i<60;i++)Tick();var hands=y< -1?3:(side%2==0?1:2);Buttons(hands,true);for(var i=0;i<20;i++)Tick();Assert.That(Ledge.Acquisitions,Is.Zero,"A standing hand does not touch the roof surface.");
                SeedFall(side,y);var row=new Row{side=side,hands=hands,initialHeight=y,recoverySeconds=-1,rootApex=y};var heldTicks=0;var rearm=0;var wasHolding=false;var sinceAssist=1000;var frames=new List<Frame>();
                for(var tick=0;tick<360&&!_boundary.Out[0];tick++)
                {
                    var holding=Ledge.IsHolding(0)||Ledge.IsHolding(1);heldTicks=holding?heldTicks+1:0;sinceAssist++;
                    if(wasHolding&&!holding){Buttons(hands,false);rearm=1;}
                    else if(rearm>0){Buttons(hands,true);rearm=0;}
                    if(holding&&heldTicks>=8&&sinceAssist>=24&&Ledge.TotalAssists<2){Actor.Motor.RequestJump();sinceAssist=0;}
                    Tick();wasHolding=holding;
                    frames.Add(new Frame{tick=tick,assists=Ledge.TotalAssists,held=Ledge.IsHolding(0)||Ledge.IsHolding(1),grounded=Actor.Motor.Grounded,position=Actor.Motor.Body.position,velocity=Actor.Motor.Body.linearVelocity,bottom=Actor.GetComponent<CapsuleCollider>().bounds.min.y,force=Ledge.MaximumObservedForce,reason=Ledge.LastReleaseReason});
                    row.rootApex=Mathf.Max(row.rootApex,Actor.Motor.Body.position.y);
                    if(Ledge.IsHolding(0)||Ledge.IsHolding(1))row.handError=Mathf.Max(row.handError,Actor.GrabPose.MaximumHeldTargetError);
                    if(Recovered()){row.recovered=true;row.recoverySeconds=(tick+1)*Dt;break;}
                }
                if(side==0)File.WriteAllText(Path.Combine(Output,y< -1?"DeepTrace.json":"LipTrace.json"),JsonUtility.ToJson(new Report{frames=frames.ToArray()},true));
                row.assists=Ledge.TotalAssists;row.acquisitions=Ledge.Acquisitions;row.breaks=Ledge.Breaks;row.lastRelease=Ledge.LastReleaseReason;row.maximumForce=Ledge.MaximumObservedForce;row.maximumStress=Ledge.MaximumObservedStress;rows.Add(row);
                File.WriteAllText(Path.Combine(Output,"Recovery.json"),JsonUtility.ToJson(new Report{status="OBSERVED",cases=rows.ToArray()},true));
                if(!row.recovered||row.assists>2||row.handError>.03f)failures.Add(JsonUtility.ToJson(row));
                Assert.That(Actor.Hands.State.PunchCount,Is.Zero);Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);Assert.That(Actor.Motor.JumpCount,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));File.WriteAllText(Path.Combine(Output,"Recovery.json"),JsonUtility.ToJson(new Report{status="PASS",cases=rows.ToArray()},true));
        }
    }
}
