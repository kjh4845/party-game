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
    public sealed partial class ShoulderReachCandidateTests
    {
        private void LowInput(int tick)
        {
            if(tick<150)_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;
            if(tick==180)_pair.Players[0].Move=Vector2.down*.35f;if(tick==250)_pair.Players[0].Move=Vector2.zero;if(tick==280)Buttons(0,0);
        }
        [UnityTest] public IEnumerator LowGrabIsIndependentOfRenderRate()
        {
            var poses=new List<Vector3[]>();var states=new List<string>();var rows=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                CreateLow(LimpGrabTargetReview.Orientation.FaceUp);Buttons(0,3);var tick=0;var time=0d;var error=0f;
                while(tick<360)
                {
                    time=Math.Min(6,time+1d/fps);var due=Math.Min(360,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        LowInput(tick);Tick();var points=_pair.Players[0].Rig.Links.Select(l=>l.Body.position).Append(_pair.Players[0].Rig.Actor.Motor.Body.position).Concat(_pair.ExternalTarget.Bodies.Select(b=>b.position)).ToArray();
                        var state=string.Join(",",_pair.Players[0].Hands.Select(h=>h.Phase.ToString()));
                        if(fps==60){poses.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)error=Mathf.Max(error,Vector3.Distance(points[i],poses[tick][i]));}tick++;
                    }
                    _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                }
                rows.Add($"fps={fps}, maximumPhysicalDifference={error:R}");File.WriteAllLines(Path.Combine(Output,"LowFrameRates.txt"),rows);Assert.That(error,Is.LessThan(.0001f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator LowResetRestoresPelvisAndRequiresFreshInput()
        {
            CreateLow(LimpGrabTargetReview.Orientation.FaceUp);Buttons(0,3);for(var tick=0;tick<170;tick++){LowInput(tick);Tick();}
            Assert.That(_pair.LogicalGrips,Is.GreaterThan(0));Assert.That(_pair.Players[0].Rig.LowReachWeight,Is.GreaterThan(.1f));
            _pair.ResetReview();var rig=_pair.Players[0].Rig;
            Assert.That(rig.LowReachWeight+rig.Actor.Pose.AdditionalPelvisDrop+rig.Actor.Pose.AdditionalPelvisPitch,Is.Zero);
            Assert.That(Vector3.Distance(rig.Find("Spine").Joint.connectedAnchor,rig.Find("Spine").StartConnectedAnchor),Is.LessThan(.00001f));
            for(var i=0;i<120;i++)Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
            Buttons(0,0);Tick();Buttons(0,3);for(var tick=0;tick<170;tick++){LowInput(tick);Tick();}
            Assert.That(_pair.LogicalGrips,Is.GreaterThan(0));_pair.SetSuppressed(true);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
            _pair.SetSuppressed(false);for(var i=0;i<60;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator NativeLowFixtureUsesOnePhysicsLoop()
        {
            var saved=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;_scene=SceneManager.CreateScene("Native low grab",new CreateSceneParameters(LocalPhysicsMode.None));
                _pair=ActiveGrabPairReview.Create(_scene,.5f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Candidate,true,LimpGrabTargetReview.Orientation.FaceUp,true);_pair.ReadKeyboard=false;_pair.View.enabled=false;
                for(var i=0;i<150;i++)yield return null;Buttons(0,3);
                for(var i=0;i<180;i++){_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;yield return null;}
                Assert.That(_pair.Tick,Is.GreaterThan(250));Assert.That(_pair.LogicalGrips,Is.GreaterThan(0));Assert.That(_pair.Players[0].Rig.Steps,Is.EqualTo(_pair.Tick));
                Assert.That(_pair.ExternalTarget.Ragdoll.Recoveries,Is.Zero);Buttons(0,0);yield return new WaitForFixedUpdate();yield return null;Assert.That(_pair.PhysicalConnections,Is.Zero);
            }
            finally{Time.captureDeltaTime=saved;}
        }
    }
}
