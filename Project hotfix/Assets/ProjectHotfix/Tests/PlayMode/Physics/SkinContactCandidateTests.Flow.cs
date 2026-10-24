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
    public sealed partial class SkinContactCandidateTests
    {
        private void Acquire(int mask=3)
        {
            Buttons(mask);for(var i=0;i<210;i++){if(i<150)Approach(mask==3?2:1);else _pair.Players[0].Move=Vector2.zero;Tick();}
            Assert.That(_pair.LogicalGrips,Is.EqualTo(mask==3?2:1));Assert.That(_pair.LiftSupportForce,Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator EitherHandLiftsWithoutMassOrInertiaChanges()
        {
            var rows=new List<string>();
            foreach(var mask in new[]{1,2})
            {
                Create();var target=_pair.ExternalTarget;var start=target.Center.y;var inertia=target.Bodies.Select(b=>b.inertiaTensor).ToArray();var mass=target.Bodies.Select(b=>b.mass).ToArray();
                Acquire(mask);var endRise=target.Center.y-start;var heights=new List<float>();for(var i=0;i<60;i++){Tick();heights.Add(target.Center.y-start);}var rise=heights.Max();rows.Add($"hand={mask} riseAt210={endRise:R} peakNextSecond={rise:R} meanNextSecond={heights.Average():R} support={_pair.LiftSupportForce:R}");File.WriteAllLines(Path.Combine(Output,"OneHand.txt"),rows);
                // The material grip point changes the hanging body's centre of
                // mass. Verify sustained visible lift, not one proxy-hull frame.
                // Raw height differences remain in the review report/video.
                Assert.That(heights.Average(),Is.GreaterThan(.10f));AssertLimp();
                for(var i=0;i<target.Bodies.Length;i++){Assert.That(target.Bodies[i].mass,Is.EqualTo(mass[i]));Assert.That(target.Bodies[i].inertiaTensor,Is.EqualTo(inertia[i]));}
                Buttons(0);Tick();Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.LiftSupportForce,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator LastHandThrowsOnceAndSupportDoesNotStack()
        {
            Create();Assert.That(_pair.LiftSupportForce,Is.Zero);Acquire();
            var budget=_pair.ExternalTarget.Bodies.Sum(b=>b.mass)*UnityEngine.Physics.gravity.magnitude*_pair.LimpWeightCompensation;
            Assert.That(_pair.LiftSupportForce,Is.EqualTo(budget).Within(.001));
            Buttons(2);Tick();Assert.That(_pair.LogicalGrips,Is.EqualTo(1));Assert.That(_pair.Throws,Is.Zero);Assert.That(_pair.LiftSupportForce,Is.EqualTo(budget).Within(.001));
            Buttons(0);Tick();Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);Assert.That(_pair.LiftSupportForce,Is.Zero);
            Assert.That(_pair.LastThrowAddedVelocity.magnitude,Is.EqualTo(1.5f).Within(.0001));
            for(var i=0;i<60;i++)Tick();Assert.That(_pair.Throws,Is.EqualTo(1));AssertLimp();yield return null;
        }
        [UnityTest] public IEnumerator CancellationAndTargetLossNeverThrowOrKeepSupport()
        {
            foreach(var mode in new[]{"Menu","Reset","OwnerLost","TargetDisabled","ColliderLost","ColliderTrigger","GripBroken","Disabled"})
            {
                Create();Acquire();
                switch(mode)
                {
                    case "Menu":_pair.SetSuppressed(true);break;
                    case "Reset":_pair.ResetReview();break;
                    case "OwnerLost":_pair.Players[0].Rig.gameObject.SetActive(false);break;
                    case "TargetDisabled":_pair.ExternalTarget.enabled=false;Buttons(0);break;
                    case "ColliderLost":foreach(var h in _pair.Players[0].Hands)h.TargetCollider.enabled=false;Buttons(0);break;
                    case "ColliderTrigger":foreach(var h in _pair.Players[0].Hands)h.TargetCollider.isTrigger=true;Buttons(0);break;
                    case "GripBroken":foreach(var h in _pair.Players[0].Hands)UnityEngine.Object.DestroyImmediate(h.Connection.Joint);break;
                    case "Disabled":_pair.enabled=false;break;
                }
                Tick();Assert.That(_pair.Throws,Is.Zero,mode);Assert.That(_pair.LiftSupportForce,Is.Zero,mode);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero,mode);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator EarlyReleaseDoesNotWaitForLiftAndFreeFallRestoresFullGravity()
        {
            Create();Buttons(3);
            for(var i=0;i<180&&_pair.LogicalGrips==0;i++){Approach();Tick();}
            Assert.That(_pair.LogicalGrips,Is.GreaterThan(0));Assert.That(_pair.Players[0].Hands.Max(h=>h.LiftRise),Is.LessThan(_pair.LiftHeight*.2f));
            Buttons(0);Tick();Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.LiftSupportForce,Is.Zero);
            foreach(var body in _pair.ExternalTarget.Bodies)
            {
                var point=body.position+Vector3.up*3;body.transform.position=point;body.position=point;body.linearVelocity=body.angularVelocity=Vector3.zero;
            }
            UnityEngine.Physics.SyncTransforms();Tick();
            var velocity=_pair.ExternalTarget.Bodies.Sum(b=>b.linearVelocity.y*b.mass)/20;
            Assert.That(velocity,Is.EqualTo(UnityEngine.Physics.gravity.y*Dt).Within(.01f));AssertLimp();yield return null;
        }
        [UnityTest] public IEnumerator AirborneHolderGetsNoGravityCancellation()
        {
            Create();Acquire();var root=_pair.Players[0].Rig.Actor.Motor.Body;
            // The same launch state regardless of horizontal jump direction.
            foreach(var horizontal in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
            {
                root.linearVelocity=horizontal+Vector3.up*2;_pair.Step(Dt);Assert.That(_pair.LiftSupportForce,Is.Zero);
                _scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();
            }
            AssertLimp();yield return null;
        }
        [UnityTest] public IEnumerator MutualHoldBlocksAdditionalLift()
        {
            Create(low:false,contacts:false);_pair.EnableLiftThrow=false;_pair.Buttons(0,true,true);_pair.Buttons(1,true,true);
            for(var i=0;i<180;i++)Tick();Assert.That(_pair.Players.All(p=>p.Mutual),Is.True);
            _pair.EnableLiftThrow=true;
            for(var i=0;i<90;i++)
            {
                Tick();Assert.That(_pair.Players.All(p=>p.Mutual),Is.True);
                foreach(var h in _pair.Players.SelectMany(p=>p.Hands).Where(h=>h.Connection!=null))Assert.That(h.LiftRise,Is.Zero);
                Assert.That(_pair.LiftSupportForce,Is.Zero);
            }
            _pair.SetSuppressed(true);Tick();Assert.That(_pair.Throws,Is.Zero);yield return null;
        }
        private void ReplayInput(int tick)
        {
            if(tick<150)Approach();else _pair.Players[0].Move=tick<240?Vector2.down*.2f:Vector2.zero;
            if(tick==250)Buttons(2);if(tick==280)Buttons(0);
        }
        [UnityTest] public IEnumerator LiftReleaseReplayMatchesAt15_60_144Fps()
        {
            var baseline=new List<Vector3[]>();var states=new List<string>();var rows=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();Buttons(3);var tick=0;var time=0d;var difference=0f;
                while(tick<360)
                {
                    time=Math.Min(6,time+1d/fps);var due=Math.Min(360,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        ReplayInput(tick);Tick();var points=_pair.Players[0].Rig.Links.Select(l=>l.Body.position).Append(_pair.Players[0].Rig.Actor.Motor.Body.position).Concat(_pair.ExternalTarget.Bodies.Select(b=>b.position)).ToArray();
                        var state=$"{_pair.LogicalGrips},{_pair.Throws},{_pair.LiftSupportForce:R}";
                        if(fps==60){baseline.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)difference=Mathf.Max(difference,Vector3.Distance(points[i],baseline[tick][i]));}tick++;
                    }
                    _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                }
                Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(difference,Is.LessThan(.0001));rows.Add($"fps={fps}, maximumPhysicalDifference={difference:R}");File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NativeLiftFixtureRunsOncePerFixedStep()
        {
            var saved=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;_scene=SceneManager.CreateScene("Native limp lift",new CreateSceneParameters(LocalPhysicsMode.None));
                _pair=ActiveGrabPairReview.Create(_scene,.5f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Candidate,true,LimpGrabTargetReview.Orientation.FaceUp,true);
                _pair.EnableLiftThrow=true;_pair.EnableSkinContacts=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;
                for(var i=0;i<150;i++)yield return null;var start=_pair.ExternalTarget.Center.y;Buttons(3);
                for(var i=0;i<240;i++){if(i<150)Approach();else _pair.Players[0].Move=Vector2.zero;yield return null;}
                Assert.That(_pair.Players[0].Rig.Steps,Is.EqualTo(_pair.Tick));Assert.That(_pair.SkinContacts.FixedSteps,Is.EqualTo(_pair.Tick));Assert.That(_pair.ExternalTarget.Center.y-start,Is.GreaterThan(.15f));AssertLimp();
                Buttons(0);yield return new WaitForFixedUpdate();yield return null;Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.LiftSupportForce,Is.Zero);
            }
            finally{Time.captureDeltaTime=saved;}
        }
    }
}
