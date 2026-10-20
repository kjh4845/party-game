using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class FixedInputClockTests : InputTestFixture
    {
        [Test] public void HardwareMouseDurationsSurviveLowFpsAndFixedCatchUp()
        {
            var mouse=InputSystem.AddDevice<Mouse>();
            foreach(var fps in new[]{15,20,30,60,120,144,240})
            {
                ReviewInputClock.Install();Assert.That(InputSystem.settings.updateMode,Is.EqualTo(InputSettings.UpdateMode.ProcessEventsInFixedUpdate));
                var state=new GroundHandActionStateMachine();var sequence=0UL;var offset=currentTime+10;
                var times=new[]{.0991,.2461,.8991,1.0521,1.7001,1.7101};
                for(var i=0;i<times.Length;i++)InputSystem.QueueStateEvent(mouse,new MouseState{buttons=(ushort)(i%2==0?3:0)},offset+times[i]);
                var tick=0;var renderTime=0d;var edges=0;var durations=new List<double>();
                while(tick<150)
                {
                    renderTime=Math.Min(2.5,renderTime+1d/fps);var due=Math.Min(150,(int)Math.Floor(renderTime*60+1e-8));
                    while(tick<due)
                    {
                        tick++;var time=tick/60d;currentTime=offset+time;
                        // The package exposes its fixed test clock only internally. Reflection is test-only.
                        var runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(this);
                        runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime,offset+time);
                        runtime.GetType().GetMethod("Update").Invoke(runtime,new object[]{InputUpdateType.Fixed});
                        foreach(var edge in ReviewInputClock.MouseEdges)
                            if(!edge.Down&&!double.IsNaN(edge.HeldSeconds))state.PrepareTimedRelease(edge.Side,time,edge.HeldSeconds);
                        foreach(var edge in ReviewInputClock.MouseEdges)
                        {
                            edges++;double? duration=double.IsNaN(edge.HeldSeconds)?null:(double?)edge.HeldSeconds;
                            state.Receive(++sequence,edge.Side,edge.Down,time,true,true,duration);
                            if(duration.HasValue)durations.Add(duration.Value);
                        }
                        state.Advance(time);
                    }
                }
                Assert.That(edges,Is.EqualTo(12),fps.ToString());Assert.That(state.PunchCount,Is.EqualTo(4),"147ms tap /153ms hold /10ms tap at "+fps);
                Assert.That(durations[0],Is.EqualTo(.147).Within(1e-8));Assert.That(durations[2],Is.EqualTo(.153).Within(1e-8));
            }
        }
    }
    public sealed class FrameIndependenceTests
    {
        private Scene _scene;
        private SharedArenaReview _arena;
        private const float Dt=1f/60;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,
            Environment.GetEnvironmentVariable("MOVEMENT_REVIEW_OUTPUT") ?? (Environment.GetEnvironmentVariable("MOVEMENT_REVIEW_SCALE")==null ? "../../.codex_tmp/frame-independence-20260910" : "../../.codex_tmp/movement-speed-20260910/FrameIndependence")));
        private SharedArenaReview CreateArena()
        {
            var scale=Environment.GetEnvironmentVariable("MOVEMENT_REVIEW_SCALE");
            return scale==null ? SharedArenaReview.Create(_scene,2) : SharedArenaReview.Create(_scene,2,movementScale:float.Parse(scale,System.Globalization.CultureInfo.InvariantCulture));
        }
        private sealed class TickState
        {
            public Vector3[] bonePositions,ragPositions,ragVelocities,ragAngular;public Quaternion[] boneRotations,ragRotations;
            public Vector3[] positions,velocities;public Quaternion[] rotations;public string signature;public float gait;public Vector3 focus;public float dolly;
        }
        [Serializable] private sealed class Result { public string schedule;public int ticks,punches,hits,throws,downs,recoveries;public float rootError,velocityError,rotationError,gaitError,cameraError,boneError,boneRotationError,ragdollError,ragdollRotationError,ragdollVelocityError,ragdollAngularError; }
        [Serializable] private sealed class Report { public string status;public Result[] cases; }
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create()
        {
            _scene=SceneManager.CreateScene("FPS replay",new CreateSceneParameters(LocalPhysicsMode.Physics3D));_arena=CreateArena();_arena.ReadKeyboard=false;_arena.ShowHelp=false;_arena.SharedCamera.ManualStep=true;_arena.SharedCamera.View.enabled=false;
            foreach(var a in _arena.Actors)a.Motor.ManualStep=true;_arena.InitializeParticipants();
            foreach(var a in _arena.Actors)a.Hands.SubmitButtons(false,false);
        }
        [UnityTest] public IEnumerator NativePlayerLoopEvaluatesOnceAfterEveryPhysicsTick()
        {
            var capture=Time.captureDeltaTime;
            try
            {
                _scene=SceneManager.CreateScene("Native fixed loop");
                _arena=CreateArena();_arena.ReadKeyboard=false;
                foreach(var a in _arena.Actors){a.ReadKeyboard=false;a.Motor.MoveInput=Vector2.right;}
                ReviewInputClock.Install();
                foreach(var fps in new[]{15,120})
                {
                    Time.captureDeltaTime=1f/fps;
                    for(var frame=0;frame<12;frame++)
                    {
                        yield return null;
                        foreach(var a in _arena.Actors)
                        {
                            Assert.That(a.FixedPose.EvaluationCount,Is.EqualTo(a.Motor.SimulationTick));
                            Assert.That(a.FixedPose.LastEvaluatedSimulationTick,Is.EqualTo(a.Motor.SimulationTick));
                            var snapshot=(Vector3)typeof(FixedReviewPose).GetField("_root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(a.FixedPose);
                            Assert.That(Vector3.Distance(snapshot,a.Motor.Body.position),Is.LessThan(.000001f),"Snapshot must follow native physics, not precede it.");
                        }
                    }
                }
                Assert.That(_arena.Actors[0].Motor.SimulationTick,Is.GreaterThan(30));
                var camera=_arena.SharedCamera;camera.Actors=Array.Empty<PhysicsJumpReview>();camera.SampleAuthority();
                camera.Render(0);var held=camera.View.transform.position;camera.Render(1);
                Assert.That(camera.View.transform.position,Is.EqualTo(held),"An empty roster must freeze the last camera snapshot.");
            }
            finally{Time.captureDeltaTime=capture;}
        }
        private static float RotationError(Quaternion a,Quaternion b)=>a.Equals(b)?0:Quaternion.Angle(a,b);
        private void Place(PhysicsJumpReview a,Vector3 p)
        {a.transform.SetPositionAndRotation(p,Quaternion.identity);a.Motor.Body.position=p;a.Motor.Body.rotation=Quaternion.identity;a.Motor.Body.linearVelocity=a.Motor.Body.angularVelocity=Vector3.zero;}
        private void Input(int tick)
        {
            var a=_arena.Actors[0];var b=_arena.Actors[1];
            a.Motor.MoveInput=tick>=60&&tick<150?Vector2.right:tick>=680&&tick<740?Vector2.down:Vector2.zero;a.Motor.SprintRequested=tick>=100;
            if(tick==110)a.Motor.RequestJump();
            if(tick==200){Place(a,new Vector3(0,.02f,0));Place(b,new Vector3(0,.02f,.4f));UnityEngine.Physics.SyncTransforms();}
            if(tick==230)a.Hands.SubmitTimedButtonEdge(0,true);
            if(tick==270)a.Hands.SubmitTimedButtonEdge(0,false,40d/60);
            if(tick==330){Place(a,new Vector3(0,.02f,0));Place(b,new Vector3(-.179f,.02f,.4f));b.Motor.Body.constraints=RigidbodyConstraints.FreezeAll;UnityEngine.Physics.SyncTransforms();}
            if(tick>=350&&tick<=658)
            {
                var age=tick-350;if(age%34==0)a.Hands.SubmitTimedButtonEdge(0,true);
                if(age%34==2)a.Hands.SubmitTimedButtonEdge(0,false,2d/60);
            }
        }
        private TickState Tick(int tick)
        {
            Input(tick);
            foreach(var a in _arena.Actors){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in _arena.Actors)a.Motor.Step(Dt);foreach(var a in _arena.Actors)a.Hands.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);
            foreach(var a in _arena.Actors)a.FixedPose.Advance(Dt);_arena.SharedCamera.AdvanceFixed(Dt);
            var signature=string.Join("|",_arena.Actors.Select(a=>{var r=a.GetComponent<PunchDownReview>();return $"{a.Motor.JumpCount},{a.Hands.State.PunchCount},{r.Target.HitCount},{a.Hands.Grabs.ThrowCount},{r.Ragdoll.Down.State.DownCount},{r.Accumulation.Points},{r.Ragdoll.Down.State.Phase},{r.Ragdoll.Down.State.RemainingSeconds:R},{r.Ragdoll.Recoveries}";}));
            var bodies=new List<Rigidbody>();var bonePositions=new List<Vector3>();var boneRotations=new List<Quaternion>();
            foreach(var a in _arena.Actors)
            {
                var buffer=new Rigidbody[16];var count=a.GetComponent<RagdollRecoveryReview>().CopyDynamicBodies(buffer);bodies.AddRange(buffer.Take(count));
                foreach(var bone in a.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones)
                {bonePositions.Add(a.FixedPose.WorldPosition(bone.name));boneRotations.Add(a.FixedPose.WorldRotation(bone.name));}
            }
            return new TickState{bonePositions=bonePositions.ToArray(),boneRotations=boneRotations.ToArray(),ragPositions=bodies.Select(b=>b.position).ToArray(),ragRotations=bodies.Select(b=>b.rotation).ToArray(),ragVelocities=bodies.Select(b=>b.linearVelocity).ToArray(),ragAngular=bodies.Select(b=>b.angularVelocity).ToArray(),positions=_arena.Actors.Select(a=>a.Motor.Body.position).ToArray(),velocities=_arena.Actors.Select(a=>a.Motor.Body.linearVelocity).ToArray(),rotations=_arena.Actors.Select(a=>a.Motor.Body.rotation).ToArray(),signature=signature,gait=_arena.Actors[0].Pose.GaitPhase,focus=_arena.SharedCamera.Focus,dolly=_arena.SharedCamera.Dolly};
        }
        [UnityTest] public IEnumerator SameFixedInputHasSameGameplayWithDifferentRenderSchedules()
        {
            Directory.CreateDirectory(Output);var baseline=new List<TickState>();var results=new List<Result>();
            foreach(var schedule in new[]{"60","15","20","30","90","120","144","240","Jitter","Hitch200ms","NoRender"})
            {
                Create();var tick=0;var frame=0;var time=0d;var result=new Result{schedule=schedule};
                while(tick<1080)
                {
                    var seconds=schedule=="NoRender" ? .1 : schedule=="Jitter" ? new[]{1d/144,1d/30,1d/90,1d/20}[frame%4] : schedule=="Hitch200ms" ? (frame%37==18 ? .2 : 1d/60) : 1d/int.Parse(schedule);
                    time=Math.Min(18,time+seconds);var due=Math.Min(1080,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        var current=Tick(++tick);
                        if(schedule=="60")baseline.Add(current);
                        else
                        {
                            var expected=baseline[tick-1];Assert.That(current.signature,Is.EqualTo(expected.signature),$"{schedule} tick {tick} gameplay");
                            for(var i=0;i<2;i++){result.rootError=Mathf.Max(result.rootError,Vector3.Distance(current.positions[i],expected.positions[i]));result.velocityError=Mathf.Max(result.velocityError,Vector3.Distance(current.velocities[i],expected.velocities[i]));result.rotationError=Mathf.Max(result.rotationError,Quaternion.Angle(current.rotations[i],expected.rotations[i]));}
                            Assert.That(current.ragPositions.Length,Is.EqualTo(expected.ragPositions.Length));
                            for(var i=0;i<current.ragPositions.Length;i++)
                            {
                                result.ragdollError=Mathf.Max(result.ragdollError,Vector3.Distance(current.ragPositions[i],expected.ragPositions[i]));
                                result.ragdollRotationError=Mathf.Max(result.ragdollRotationError,RotationError(current.ragRotations[i],expected.ragRotations[i]));
                                result.ragdollVelocityError=Mathf.Max(result.ragdollVelocityError,Vector3.Distance(current.ragVelocities[i],expected.ragVelocities[i]));
                                result.ragdollAngularError=Mathf.Max(result.ragdollAngularError,Vector3.Distance(current.ragAngular[i],expected.ragAngular[i]));
                            }
                            for(var i=0;i<current.bonePositions.Length;i++)
                            {
                                result.boneError=Mathf.Max(result.boneError,Vector3.Distance(current.bonePositions[i],expected.bonePositions[i]));
                                result.boneRotationError=Mathf.Max(result.boneRotationError,RotationError(current.boneRotations[i],expected.boneRotations[i]));
                            }
                            result.gaitError=Mathf.Max(result.gaitError,Mathf.Abs(current.gait-expected.gait));result.cameraError=Mathf.Max(result.cameraError,Vector3.Distance(current.focus,expected.focus),Mathf.Abs(current.dolly-expected.dolly));
                        }
                    }
                    if(schedule!="NoRender")
                    {
                        var alpha=(float)Math.Max(0,Math.Min(1,(time-tick/60d)*60));var before=_arena.Actors.Select(a=>a.Motor.Body.position).ToArray();var phases=_arena.Actors.Select(a=>a.Pose.GaitPhase).ToArray();
                        foreach(var a in _arena.Actors)a.FixedPose.Present(alpha);_arena.SharedCamera.Render(alpha);
                        for(var i=0;i<2;i++){Assert.That(_arena.Actors[i].Motor.Body.position,Is.EqualTo(before[i]));Assert.That(_arena.Actors[i].Pose.GaitPhase,Is.EqualTo(phases[i]));}
                    }
                    frame++;
                }
                var attacker=_arena.Actors[0];var target=_arena.Actors[1].GetComponent<PunchDownReview>();
                result.ticks=tick;result.punches=attacker.Hands.State.PunchCount;result.hits=target.Target.HitCount;result.throws=attacker.Hands.Grabs.ThrowCount;result.downs=(int)target.Ragdoll.Down.State.DownCount;result.recoveries=target.Ragdoll.Recoveries;results.Add(result);
                File.WriteAllText(Path.Combine(Output,"Replay.json"),JsonUtility.ToJson(new Report{status="OBSERVED",cases=results.ToArray()},true));
                Assert.That(result.rootError,Is.LessThan(.0001f));Assert.That(result.velocityError,Is.LessThan(.0001f));Assert.That(result.rotationError,Is.LessThan(.001f));Assert.That(result.gaitError,Is.LessThan(.000001f));Assert.That(result.cameraError,Is.LessThan(.0001f));
                Assert.That(result.boneError,Is.LessThan(.0001f));Assert.That(result.boneRotationError,Is.LessThan(.001f));
                Assert.That(result.ragdollError,Is.LessThan(.0001f));Assert.That(result.ragdollRotationError,Is.LessThan(.001f));Assert.That(result.ragdollVelocityError,Is.LessThan(.0001f));Assert.That(result.ragdollAngularError,Is.LessThan(.0001f));
                Assert.That(result.punches,Is.EqualTo(10));Assert.That(result.hits,Is.EqualTo(10));Assert.That(result.throws,Is.EqualTo(1));Assert.That(result.downs,Is.EqualTo(1));Assert.That(result.recoveries,Is.EqualTo(1));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"Replay.json"),JsonUtility.ToJson(new Report{status="PASS",cases=results.ToArray()},true));
        }
    }
}
