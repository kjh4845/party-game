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
    public sealed partial class AutomaticLiftReviewTests
    {
        private const float Dt=1f/60;
        private Scene _scene;private SharedArenaReview _arena;
        private PhysicsJumpReview A=>_arena.Actors[0];private PhysicsJumpReview B=>_arena.Actors[1];
        private GrabPhysicsReview G=>A.Hands.Grabs;
        private string Output=>Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/auto-lift-20260911"));
        [Serializable] private sealed class Frame {public int tick,held,throws;public bool mutual;public float lift,height,force,handError;public Vector3 owner,target;}
        [Serializable] private sealed class Report {public string status;public Frame[] frames;}
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(bool native=false)
        {
            _scene=SceneManager.CreateScene("Automatic raised grab",new CreateSceneParameters(native?LocalPhysicsMode.None:LocalPhysicsMode.Physics3D));
            _arena=SharedArenaReview.Create(_scene,2,true,2,new[]{new Vector3(0,.02f,-.22f),new Vector3(0,.02f,.22f)});
            _arena.ReadKeyboard=_arena.ShowHelp=false;_arena.SharedCamera.ManualStep=true;_arena.SharedCamera.View.enabled=false;
            foreach(var a in _arena.Actors){a.ReadKeyboard=false;a.Motor.ManualStep=!native;a.Hands.Grabs.EnableRaisedGrab();a.Hands.Grabs.BoundedContactPose=Environment.GetEnvironmentVariable("CODEX_CONTACT_CANDIDATE")=="1";a.Hands.SubmitButtons(false,false);}
            _arena.InitializeParticipants();Directory.CreateDirectory(Output);if(!native)for(var i=0;i<60;i++)Tick();
        }
        private void Tick(bool reverse=false,bool present=true)
        {
            var actors=reverse?_arena.Actors.Reverse().ToArray():_arena.Actors;
            foreach(var a in actors){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in actors)a.Motor.Step(Dt);
            foreach(var a in actors)a.Hands.Step(Dt);
            GrabPhysicsReview.StepAutomaticLifts(_scene,Dt);_scene.GetPhysicsScene().Simulate(Dt);
            if(present)foreach(var a in actors)a.FixedPose.Advance(Dt);
        }
        private void Buttons(PhysicsJumpReview a,int hands)=>a.Hands.SubmitButtons((hands&1)!=0,(hands&2)!=0);
        private Frame Sample(int tick)=>new Frame{tick=tick,held=(G.IsHolding(0)?1:0)+(G.IsHolding(1)?1:0),throws=G.ThrowCount,mutual=G.MutualLiftBlocked(0)||G.MutualLiftBlocked(1),lift=Mathf.Max(G.LiftAmount(0),G.LiftAmount(1)),height=B.Motor.Body.position.y,force=G.MaximumObservedForce,handError=A.GrabPose.MaximumHeldTargetError,owner=A.Motor.Body.position,target=B.Motor.Body.position};
        private bool WaitForGrip(int maximum=45)
        {for(var i=0;i<maximum;i++){Tick();if(G.IsHolding(0)||G.IsHolding(1))return true;}return false;}
        [UnityTest] public IEnumerator HoldRaisesRealMassAndReleaseNeverWaitsForLift()
        {
            var rows=new List<Frame>();
            foreach(var hands in new[]{1,2,3})foreach(var early in new[]{false,true})
            {
                Create();var initial=B.Motor.Body.position.y;Buttons(A,hands);Assert.That(WaitForGrip(),Is.True);
                for(var i=0;i<(early?1:42);i++)Tick();var row=Sample(hands);rows.Add(row);
                File.WriteAllText(Path.Combine(Output,"LiftRelease.json"),JsonUtility.ToJson(new Report{status="OBSERVED",frames=rows.ToArray()},true));
                if(!early){Assert.That(row.lift,Is.GreaterThan(.95f));Assert.That(row.height-initial,Is.GreaterThan(.20f));Assert.That(row.handError,Is.LessThan(.008f));}
                else Assert.That(row.lift,Is.LessThan(.1f));
                if(hands==3){Buttons(A,2);Tick();Assert.That(G.ThrowCount,Is.Zero);Assert.That(G.IsHolding(1),Is.True);}
                Buttons(A,0);Tick();Assert.That(G.ThrowCount,Is.EqualTo(1));Assert.That(G.LastThrowImpulse,Is.EqualTo(30));
                Assert.That(G.IsHolding(0)||G.IsHolding(1),Is.False);Assert.That(B.GetComponent<PunchDownReview>().Accumulation.Hits,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"LiftRelease.json"),JsonUtility.ToJson(new Report{status="PASS",frames=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator MutualGripBlocksBothActorsInEitherProcessingOrder()
        {
            foreach(var reverse in new[]{false,true})foreach(var hands in new[]{1,3})
            {
                Create();var ay=A.Motor.Body.position.y;var by=B.Motor.Body.position.y;Buttons(A,hands);Buttons(B,hands);var saw=false;
                for(var i=0;i<80;i++)
                {
                    Tick(reverse);saw|=G.MutualLiftBlocked(0);
                    Assert.That(G.LiftAmount(0),Is.Zero);Assert.That(B.Hands.Grabs.LiftAmount(0),Is.Zero);
                    Assert.That(A.Motor.Body.position.y-ay,Is.LessThan(.02f));Assert.That(B.Motor.Body.position.y-by,Is.LessThan(.02f));
                }
                Assert.That(saw,Is.True);Assert.That(G.IsHolding(0)&&B.Hands.Grabs.IsHolding(0),Is.True);Assert.That(G.ThrowCount+B.Hands.Grabs.ThrowCount,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator ReciprocalContactFreezesExistingLiftAndResumeIsContinuous()
        {
            Create();Buttons(A,1);Assert.That(WaitForGrip(),Is.True);for(var i=0;i<5;i++)Tick();Buttons(B,2);
            var trace=new List<string>();
            for(var i=0;i<40&&!G.MutualLiftBlocked(0);i++)
            {Tick();trace.Add($"tick {i} A {A.Motor.Body.position:R} B {B.Motor.Body.position:R} Bheading {B.Motor.Body.rotation.eulerAngles:R} Bphase {B.Hands.State.Phase(0)} Bseek {B.Hands.State.SeekAge(0)} Alift {G.LiftAmount(0)} Bgoal {B.Hands.Grabs.ContactPoint(0):R}");}
            File.WriteAllLines(Path.Combine(Output,"ReciprocalTrace.txt"),trace);Assert.That(G.MutualLiftBlocked(0),Is.True);
            var lift=G.LiftAmount(0);var height=B.Motor.Body.position.y;
            for(var i=0;i<20;i++){Tick();Assert.That(G.LiftAmount(0),Is.EqualTo(lift));}
            Assert.That(B.Motor.Body.position.y-height,Is.LessThan(.025f));
            B.Hands.Grabs.ReleaseAll("Review reciprocal release");Buttons(B,0);Tick();Assert.That(G.MutualLiftBlocked(0),Is.False);
            Assert.That(G.LiftAmount(0),Is.GreaterThan(lift));Assert.That(G.LiftAmount(0)-lift,Is.LessThan(.03f));Assert.That(G.ThrowCount+B.Hands.Grabs.ThrowCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator CeilingDownAndMenuCannotKeepDrivingTheLift()
        {
            foreach(var mode in new[]{"Ceiling","OwnerDown","TargetDown","Menu","Reset"})
            {
                Create();var initial=B.Motor.Body.position.y;
                if(mode=="Ceiling")
                {
                    var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(ceiling,_scene);ceiling.transform.position=new Vector3(0,1.12f,.22f);ceiling.transform.localScale=new Vector3(.6f,.1f,.5f);UnityEngine.Physics.SyncTransforms();
                }
                Buttons(A,1);Assert.That(WaitForGrip(),Is.True);for(var i=0;i<8;i++)Tick();
                if(mode=="OwnerDown")A.GetComponent<RagdollRecoveryReview>().BeginDown(Vector3.zero,Vector3.zero);
                if(mode=="TargetDown")B.GetComponent<RagdollRecoveryReview>().BeginDown(Vector3.zero,Vector3.zero);
                if(mode=="Menu")A.Hands.SetInputSuppressed(true);
                if(mode=="Reset")_arena.ResetReview();
                for(var i=0;i<35;i++)Tick();
                Assert.That(G.ThrowCount,Is.Zero);Assert.That(B.Motor.Body.linearVelocity.magnitude,Is.LessThan(8));
                if(mode=="Ceiling"){Assert.That(G.LiftBlockedCount,Is.GreaterThan(0));Assert.That(B.Motor.Body.position.y-initial,Is.LessThan(.12f));}
                else if(mode!="TargetDown")Assert.That(G.IsHolding(0)||G.IsHolding(1),Is.False);
                else
                {
                    Assert.That(B.GetComponent<RagdollRecoveryReview>().Active,Is.True);Assert.That(G.IsHolding(0),Is.True,"Raised grip survives the target becoming a ragdoll.");
                    Assert.That(B.GetComponent<StrikeReviewTarget>().InteractionMass,Is.EqualTo(20));Buttons(A,0);Tick();Assert.That(G.ThrowCount,Is.EqualTo(1));Assert.That(G.LastThrowImpulse,Is.EqualTo(30));
                }
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NativeFixedLoopRunsLiftOnceAfterBothHands()
        {
            var capture=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;Create(true);
                for(var i=0;i<30;i++)yield return null;
                Buttons(A,3);Buttons(B,3);
                for(var i=0;i<40;i++){yield return null;Assert.That(G.LiftAmount(0),Is.Zero);Assert.That(B.Hands.Grabs.LiftAmount(0),Is.Zero);}
                Assert.That(G.MutualLiftBlocked(0),Is.True);B.Hands.Grabs.ReleaseAll("Review release");Buttons(B,0);
                var previous=G.LiftAmount(0);var tick=A.Motor.SimulationTick;
                for(var i=0;i<45;i++)
                {
                    yield return null;var elapsed=A.Motor.SimulationTick-tick;
                    Assert.That(G.LiftAmount(0)-previous,Is.LessThanOrEqualTo(elapsed*Dt/G.LiftSeconds+.00001f));
                    previous=G.LiftAmount(0);tick=A.Motor.SimulationTick;
                }
                Assert.That(G.LiftAmount(0),Is.GreaterThan(.95f));Assert.That(B.Motor.Body.position.y,Is.GreaterThan(.20f));
            }
            finally{Time.captureDeltaTime=capture;}
        }
        [UnityTest] public IEnumerator CaptureRaiseReleaseAndMutualBlock()
        {
            foreach(var mutual in new[]{false,true})
            {
                Create();var camera=_arena.SharedCamera.View;camera.orthographic=true;camera.orthographicSize=.95f;camera.aspect=16f/9;
                camera.transform.position=new Vector3(2.4f,1.2f,1.25f);camera.transform.LookAt(new Vector3(0,.65f,.12f));
                _arena.GetComponentInChildren<Light>().intensity=1.2f;
                var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                var folder=Path.Combine(Output,mutual?"Mutual":"RaiseThrow");Directory.CreateDirectory(folder);var rows=new List<Frame>();
                var skin=A.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;
                Buttons(A,3);if(mutual)Buttons(B,3);
                for(var tick=0;tick<150;tick++)
                {
                    if(!mutual&&tick==64)Buttons(A,2);if(!mutual&&tick==72)Buttons(A,0);
                    if(mutual&&tick==90)Buttons(B,0);
                    Tick();rows.Add(Sample(tick));
                    if(mutual&&tick>=25&&tick<90){Assert.That(G.MutualLiftBlocked(0),Is.True);Assert.That(G.LiftAmount(0),Is.Zero);Assert.That(B.Hands.Grabs.LiftAmount(0),Is.Zero);}
                    skin.BakeMesh(mesh);surface=Mathf.Max(surface,CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _));
                    foreach(var a in _arena.Actors)a.FixedPose.Present(1);yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                File.WriteAllText(Path.Combine(folder,"Frames.json"),JsonUtility.ToJson(new Report{status="PASS",frames=rows.ToArray()},true));File.WriteAllText(Path.Combine(folder,"Skin.txt"),surface.ToString("R"));
                Assert.That(surface,Is.LessThan(60));Assert.That(G.ThrowCount,Is.EqualTo(mutual?0:1));if(mutual)Assert.That(G.LiftAmount(0),Is.GreaterThan(.95f));
                camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator LiftAndMutualBlockingDoNotDependOnRenderRate()
        {
            var reference=new List<Frame>();var errors=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();var time=0d;var tick=0;var error=0f;
                while(tick<120)
                {
                    time=Math.Min(2,time+1d/fps);var due=Math.Min(120,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        if(tick==0)Buttons(A,1);if(tick==23)Buttons(B,2);
                        if(tick==65){B.Hands.Grabs.ReleaseAll("Review release");Buttons(B,0);}if(tick==95)Buttons(A,0);
                        Tick();var row=Sample(tick);
                        if(fps==60)reference.Add(row);else{var old=reference[tick];Assert.That(row.held,Is.EqualTo(old.held));Assert.That(row.mutual,Is.EqualTo(old.mutual));Assert.That(row.throws,Is.EqualTo(old.throws));error=Mathf.Max(error,Vector3.Distance(row.owner,old.owner),Vector3.Distance(row.target,old.target),Mathf.Abs(row.lift-old.lift));}tick++;
                    }
                    var alpha=(float)Math.Clamp((time-tick/60d)*60,0,1);foreach(var a in _arena.Actors)a.FixedPose.Present(alpha);
                }
                Assert.That(reference.Any(x=>x.mutual),Is.True);Assert.That(error,Is.LessThan(.0001f));errors.Add($"{fps} FPS: {error:R}");
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),errors);
        }
    }
}
