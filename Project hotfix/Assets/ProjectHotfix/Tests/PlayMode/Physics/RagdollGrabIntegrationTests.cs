using System;
using System.IO;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class RagdollGrabIntegrationTests
    {
        private Scene _scene;
        private PhysicsJumpReview _holder,_victim;
        private PunchDownReview _receiver;
        private RagdollRecoveryReview Rag => _receiver.Ragdoll;
        private StrikeReviewTarget Target => _receiver.Target;
        private GrabPhysicsReview Grabs => _holder.Hands.Grabs;
        private void Create(float height=.02f)
        {
            _scene=SceneManager.CreateScene("Ragdoll grab integration",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _holder=PhysicsJumpReview.Create(new Vector3(0,height,0));_victim=PhysicsJumpReview.Create(new Vector3(0,height,.40f));
            foreach(var actor in new[]{_holder,_victim}) { SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);actor.ReadKeyboard=false;actor.Motor.ManualStep=true;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;actor.EnableGrabReview(); }
            _victim.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
            _receiver=_victim.gameObject.AddComponent<PunchDownReview>();_receiver.Bind(_victim);
            _holder.Hands.ReviewTargets=new[]{Target};
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(20,.2f,20);
            UnityEngine.Physics.SyncTransforms();_holder.Hands.SubmitButtons(false,false);_victim.Hands.SubmitButtons(false,false);
        }
        private void Tick()
        {
            Rag.Down.Step(1f/60);Rag.Step(1f/60);_receiver.Step();
            foreach(var actor in new[]{_holder,_victim}) { actor.Motor.Step(1f/60);actor.Hands.Step(1f/60); }
            _scene.GetPhysicsScene().Simulate(1f/60);_holder.ApplyPresentation(1f/60);
            if(Rag.Active)Rag.ApplyPose();else _victim.ApplyPresentation(1f/60);
        }
        private Rigidbody[] Bodies() { var buffer=new Rigidbody[16];var count=Rag.CopyDynamicBodies(buffer);return buffer.Take(count).ToArray(); }
        private static Vector3 Momentum(Rigidbody[] bodies) => bodies.Aggregate(Vector3.zero,(sum,b)=>sum+b.mass*b.linearVelocity);
        [UnityTearDown] public IEnumerator Cleanup() { if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null; }
        [UnityTest] public IEnumerator AssemblyMassVelocityAndThrowImpulseIncludeAllTwelveBodies()
        {
            Create(2);Assert.That(Target.InteractionMass,Is.EqualTo(20));Rag.BeginDown(Vector3.zero,Vector3.zero);_receiver.Synchronize();
            var bodies=Bodies();Assert.That(bodies.Length,Is.EqualTo(12));Assert.That(Target.Body.mass,Is.EqualTo(3));Assert.That(Target.InteractionMass,Is.EqualTo(20));
            foreach(var body in bodies){body.useGravity=false;body.linearVelocity=new Vector3(.3f,-.8f,.2f);}
            Assert.That(Target.InteractionVerticalVelocity,Is.EqualTo(-.8f).Within(.00001));
            Assert.That(Target.AssistInteractionUp(1.25f),Is.EqualTo(25).Within(.0001));
            Assert.That(bodies.All(b=>Mathf.Abs(b.linearVelocity.y-.45f)<.00001f),Is.True);
            var before=Momentum(bodies);var impulse=new Vector3(2,5,7);Target.ApplyInteractionImpulse(impulse);_scene.GetPhysicsScene().Simulate(1f/60);
            Assert.That(Vector3.Distance(Momentum(bodies)-before,impulse),Is.LessThan(.01f));
            Target.ResetReview();Assert.That(Target.InteractionMass,Is.EqualTo(20));Assert.That(Bodies().Length,Is.EqualTo(1));
            yield return null;
        }
        [UnityTest] public IEnumerator StandingGripRebindsToRagdollPartAndReleaseThrowsWholeMass()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();
            Assert.That(Grabs.IsHolding(0),Is.True);var point=Grabs.ContactPoint(0);Rag.BeginDown(Vector3.zero,Vector3.zero);Tick();
            Assert.That(Grabs.IsHolding(0),Is.True,Grabs.LastReleaseReason);Assert.That(Grabs.Joint(0).connectedBody,Is.Not.EqualTo(Target.Body));
            Assert.That(Vector3.Distance(point,Grabs.ContactPoint(0)),Is.LessThan(.02f));Assert.That(Target.InteractionMass,Is.EqualTo(20));Assert.That(_holder.Motor.ExternalLoadAccelerationScale,Is.EqualTo(2));
            _holder.Hands.SubmitButtons(false,false);Tick();Assert.That(Grabs.ThrowCount,Is.EqualTo(1));Assert.That(Grabs.LastThrowImpulse,Is.EqualTo(30));Assert.That(Target.HasIncomingGrips,Is.False);
            for(var i=0;i<30;i++)Tick();Assert.That(_receiver.Accumulation.Hits,Is.Zero);Assert.That(Rag.Down.State.DownCount,Is.EqualTo(1));
            yield return null;
        }
        [UnityTest] public IEnumerator ReleaseOnDownTopologyBoundaryStillThrowsOnce()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();
            Assert.That(Grabs.IsHolding(0),Is.True);
            Rag.BeginDown(Vector3.zero,Vector3.zero);_receiver.Synchronize();
            // Submit the up-edge before any ordinary grab Step can refresh the contact.
            _holder.Hands.SubmitButtons(false,false);_holder.Hands.Step(1f/60);
            Assert.That(Grabs.ThrowCount,Is.EqualTo(1));Assert.That(Grabs.LastThrowImpulse,Is.EqualTo(30));
            Assert.That(Grabs.IsHolding(0),Is.False);Assert.That(Target.HasIncomingGrips,Is.False);
            Tick();Assert.That(Grabs.ThrowCount,Is.EqualTo(1));Assert.That(_receiver.Accumulation.Hits,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator TwoHandsStayAttachedThroughSupportedRecoveryThenThrowOnlyOnLastRelease()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,true);for(var i=0;i<30;i++)Tick();
            Assert.That(Grabs.IsHolding(0)&&Grabs.IsHolding(1),Is.True);
            Rag.BeginDown(Vector3.zero,Vector3.zero);var sawUpright=false;
            for(var i=0;i<900&&Rag.Recoveries==0;i++)
            {
                Tick();sawUpright|=Rag.Down.State.Phase==RecoveryPresentationPhase.UprightReturn;
                Assert.That(Grabs.IsHolding(0)&&Grabs.IsHolding(1),Is.True,$"step={i} reason={Grabs.LastReleaseReason}");
                Assert.That(Grabs.ThrowCount,Is.Zero);
            }
            Assert.That(Rag.Recoveries,Is.EqualTo(1),$"phase={Rag.Down.State.Phase} age={Rag.GetUpAge} angle={Rag.ReadyAngle} distance={Rag.ReadyDistance}");Assert.That(sawUpright,Is.True);
            Assert.That(Grabs.Joint(0).connectedBody,Is.EqualTo(Target.Body));Assert.That(Grabs.Joint(1).connectedBody,Is.EqualTo(Target.Body));Assert.That(_victim.Motor.ActionsBlocked,Is.False);
            _holder.Hands.SubmitButtons(false,true);Tick();Assert.That(Grabs.ThrowCount,Is.Zero);Assert.That(Grabs.IsHolding(1),Is.True);
            _holder.Hands.SubmitButtons(false,false);Tick();Assert.That(Grabs.ThrowCount,Is.EqualTo(1));Assert.That(Target.HasIncomingGrips,Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator HeldJumpAssistsAllPartsOnceForTwoHands()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,true);for(var i=0;i<30;i++)Tick();
            Rag.BeginDown(Vector3.zero,Vector3.zero);Tick();Assert.That(Grabs.IsHolding(0)&&Grabs.IsHolding(1),Is.True);
            var bodies=Bodies();var before=bodies.Select(b=>b.linearVelocity.y).ToArray();
            _holder.Motor.RequestJump();_holder.Motor.Step(1f/60);Assert.That(_holder.Motor.JumpCommittedThisStep,Is.True);_holder.Hands.Step(1f/60);
            Assert.That(Grabs.LastJumpAssistedBodies,Is.EqualTo(1));
            var change=bodies[0].linearVelocity.y-before[0];Assert.That(change,Is.GreaterThan(0));
            for(var i=0;i<bodies.Length;i++)Assert.That(bodies[i].linearVelocity.y-before[i],Is.EqualTo(change).Within(.00001));
            Assert.That(Grabs.LastJumpAssistImpulse,Is.EqualTo(20*change).Within(.001));
            Assert.That(change,Is.LessThanOrEqualTo(_holder.Motor.LastJumpLaunchSpeed*.75f+.00001f));
            var once=bodies.Select(b=>b.linearVelocity.y).ToArray();_holder.Hands.Step(1f/60);
            for(var i=0;i<bodies.Length;i++)Assert.That(bodies[i].linearVelocity.y,Is.EqualTo(once[i]));
            yield return null;
        }
        [UnityTest] public IEnumerator HeldRecoveryWaitsForCeilingClearanceWithoutDroppingGrip()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,true);for(var i=0;i<30;i++)Tick();
            var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(ceiling,_scene);ceiling.transform.position=new Vector3(0,1.03f,.4f);ceiling.transform.localScale=new Vector3(.5f,.1f,.4f);UnityEngine.Physics.SyncTransforms();
            Rag.BeginDown(Vector3.zero,Vector3.zero);for(var i=0;i<210;i++)Tick();
            Assert.That(Rag.Down.State.RemainingSeconds,Is.Zero);Assert.That(Rag.Recoveries,Is.Zero);Assert.That(Grabs.IsHolding(0)&&Grabs.IsHolding(1),Is.True);
            ceiling.SetActive(false);UnityEngine.Physics.SyncTransforms();for(var i=0;i<600&&Rag.Recoveries==0;i++)Tick();
            Assert.That(Rag.Recoveries,Is.EqualTo(1));Assert.That(Grabs.IsHolding(0)&&Grabs.IsHolding(1),Is.True);Assert.That(Grabs.ThrowCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator DistributedSupportRejectsAPlatformThatDoesNotReachTheFeet()
        {
            _scene=SceneManager.CreateScene("Distributed support",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var torsoObject=new GameObject("Torso");SceneManager.MoveGameObjectToScene(torsoObject,_scene);torsoObject.transform.position=Vector3.up*.57f;var torso=torsoObject.AddComponent<Rigidbody>();torso.useGravity=false;
            var feet=new Collider[2];
            for(var i=0;i<2;i++){var foot=GameObject.CreatePrimitive(PrimitiveType.Sphere);SceneManager.MoveGameObjectToScene(foot,_scene);foot.transform.position=new Vector3(i==0 ? -.1f : .1f,.02f,0);foot.transform.localScale=Vector3.one*.04f;feet[i]=foot.GetComponent<Collider>();}
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.05f;floor.transform.localScale=new Vector3(.05f,.1f,2);
            var probe=new RecoverySupportProbe(torso,feet,.17f,1){ProbeDistance=.65f,CheckDistributedSupport=true};UnityEngine.Physics.SyncTransforms();
            Assert.That(probe.Evaluate(.25f).HasSupport,Is.False);
            floor.transform.localScale=new Vector3(.4f,.1f,2);UnityEngine.Physics.SyncTransforms();Assert.That(probe.Evaluate(.25f).Stable,Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator ReleasedPoseReblendPreservesRootAndClipProgress()
        {
            Create();var pose=Rag.GetUpPose;var root=_victim.Motor.Body;
            pose.Begin(true,new Vector3(-.2f,0,.4f),new Vector3(.2f,0,.4f),root.position,root.rotation);
            var spine=pose.AnimatedBones.Single(b=>b.name=="Spine");spine.localRotation*=Quaternion.Euler(-15,0,0);var before=spine.localRotation;
            var position=root.position;var rotation=root.rotation;var velocity=root.linearVelocity;
            pose.ReblendFromCurrentPose();pose.Apply(1.3f,1f/60);
            Assert.That(Quaternion.Angle(before,spine.localRotation),Is.LessThan(2));
            for(var i=0;i<15;i++)pose.Apply(1.3f,1f/60);
            Assert.That(Quaternion.Angle(before,spine.localRotation),Is.GreaterThan(8));Assert.That(pose.Starts,Is.EqualTo(1));
            Assert.That(root.position,Is.EqualTo(position));Assert.That(root.rotation,Is.EqualTo(rotation));Assert.That(root.linearVelocity,Is.EqualTo(velocity));
            yield return null;
        }
        [Serializable] private sealed class FrameInfo { public int frame,hits,throws;public bool held;public string phase;public float wait; }
        [Serializable] private sealed class CaptureReport { public string status;public int hits,recoveries,throws;public FrameInfo[] frames; }
        [UnityTest] public IEnumerator ActualHoldPunchWaitForSpaceThenReleaseThrowAndRecover()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();Assert.That(Grabs.IsHolding(0),Is.True);
            foreach(var skin in _victim.GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterial.SetColor("_BaseColor",new Color(.92f,.48f,.16f));
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/ragdoll-grab-20260908/Capture"));Directory.CreateDirectory(output);
            var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,_scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.9f;
            var lightObject=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var render=new RenderTexture(640,480,24);render.Create();camera.targetTexture=render;var image=new Texture2D(640,480,TextureFormat.RGB24,false);var frames=new System.Collections.Generic.List<FrameInfo>();
            var releaseAt=-1;var thrownAt=-1;var observedBlocked=false;
            for(var step=0;step<1200;step++)
            {
                var toward=_victim.Motor.Body.position-_holder.Motor.Body.position;
                _holder.Motor.MoveInput=Target.HitCount<5 && Vector3.Dot(_holder.transform.forward,toward.normalized)<.98f ? new Vector2(toward.x,toward.z).normalized : Vector2.zero;
                if(Target.HitCount<5 && Rag.Down.State.DownCount==0 && step<700 && step%70==0)_holder.Hands.SubmitButtons(true,true);
                if(Target.HitCount<5 && Rag.Down.State.DownCount==0 && step<700 && step%70==3)_holder.Hands.SubmitButtons(true,false);
                if(Rag.Recoveries>0 && releaseAt<0)releaseAt=step+30;
                if(Rag.Down.State.DownCount==1 && Rag.Down.State.RemainingSeconds==0 && Rag.Down.State.Phase==RecoveryPresentationPhase.PhysicsTumble && !Rag.LastSupportSample.Clear && releaseAt<0)
                { observedBlocked=true;releaseAt=step+30; }
                if(step==releaseAt){_holder.Hands.SubmitButtons(false,false);thrownAt=step;}
                Tick();
                if(thrownAt<0)Assert.That(Grabs.IsHolding(0),Is.True,$"{step} {Grabs.LastReleaseReason}");
                if(step%3==0 && step<1000)
                {
                    var center=(_holder.transform.position+_victim.transform.position)*.5f+Vector3.up*.45f;camera.transform.position=center+new Vector3(2.5f,1,2.5f);camera.transform.LookAt(center);
                    yield return null;camera.Render();var previous=RenderTexture.active;RenderTexture.active=render;image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();RenderTexture.active=previous;
                    var frame=frames.Count;File.WriteAllBytes(Path.Combine(output,$"Frame_{frame:D3}.png"),image.EncodeToPNG());frames.Add(new FrameInfo{frame=frame,hits=Target.HitCount,throws=Grabs.ThrowCount,held=Grabs.IsHolding(0),phase=Rag.Down.State.Phase.ToString(),wait=(float)Rag.Down.State.RemainingSeconds});
                }
                if(thrownAt>=0 && Rag.Recoveries>0 && step>thrownAt+60)break;
            }
            File.WriteAllText(Path.Combine(output,"Report.json"),JsonUtility.ToJson(new CaptureReport{status="OBSERVED",hits=Target.HitCount,recoveries=Rag.Recoveries,throws=Grabs.ThrowCount,frames=frames.ToArray()},true));
            Assert.That(observedBlocked,Is.True,"Occupied standing space must wait while held");Assert.That(Target.HitCount,Is.EqualTo(5));Assert.That(Rag.Recoveries,Is.EqualTo(1),$"{Rag.Down.State.Phase} {Rag.ReadyAngle} {Rag.ReadyDistance} support={Rag.LastSupportSample.HasSupport}/{Rag.LastSupportSample.Stable}/{Rag.LastSupportSample.Clear} root={_victim.Motor.Body.position} holder={_holder.Motor.Body.position}");Assert.That(Grabs.ThrowCount,Is.EqualTo(1));Assert.That(_receiver.Accumulation.Hits,Is.Zero);Assert.That(Rag.Down.State.DownCount,Is.EqualTo(1));
            File.WriteAllText(Path.Combine(output,"Report.json"),JsonUtility.ToJson(new CaptureReport{status="PASS",hits=Target.HitCount,recoveries=Rag.Recoveries,throws=Grabs.ThrowCount,frames=frames.ToArray()},true));
            camera.targetTexture=null;render.Release();UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator TargetResetCancelsGripWithoutThrow()
        {
            Create();for(var i=0;i<90;i++)Tick();_holder.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();Assert.That(Grabs.IsHolding(0),Is.True);
            Rag.BeginDown(Vector3.zero,Vector3.zero);Tick();Target.ResetReview();Tick();
            Assert.That(Grabs.IsHolding(0),Is.False);Assert.That(Grabs.ThrowCount,Is.Zero);Assert.That(Target.HasIncomingGrips,Is.False);Assert.That(Target.InteractionMass,Is.EqualTo(20));
            yield return null;
        }
    }
}
