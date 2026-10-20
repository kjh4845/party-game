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
    public sealed partial class StandingSkinContactTests
    {
        private bool Acquire(int expected,bool reverse=false,int mover=0)
        {
            for(var tick=0;tick<180;tick++)
            {
                if(tick>=30)_pair.Players[mover].Move=(mover==0?Vector2.up:Vector2.down)*.25f;
                Tick(reverse);if(_pair.LogicalGrips>=expected){_pair.Players[mover].Move=Vector2.zero;return true;}
            }
            _pair.Players[mover].Move=Vector2.zero;return false;
        }
        [UnityTest] public IEnumerator SharedOwnershipSurvivesEitherInputOrderAndPartialRelease()
        {
            foreach(var reverse in new[]{false,true})foreach(var mask in new[]{1,3})
            {
                Create();Buttons(0,mask);Buttons(1,mask==1?2:3);Assert.That(Acquire(mask==1?2:4,reverse),Is.True);
                var physical=mask==1?1:2;Assert.That(_pair.PhysicalConnections,Is.EqualTo(physical));
                for(var i=0;i<120;i++)Tick(reverse);
                Assert.That(_pair.LogicalGrips,Is.EqualTo(physical*2));Assert.That(_pair.Players.All(p=>p.Mutual&&p.Breaks==0),Is.True);
                foreach(var h in _pair.Players.SelectMany(p=>p.Hands).Where(h=>h.Connection!=null)){Assert.That(h.Connection.Owners,Is.EqualTo(2));Assert.That(h.Connection.RollingHands,Is.True);}
                Buttons(0,0);Tick(reverse);Assert.That(_pair.PhysicalConnections,Is.EqualTo(physical));Assert.That(_pair.Players[1].Hands.Count(h=>h.Connection!=null),Is.EqualTo(physical));
                for(var i=0;i<30;i++)Tick(reverse);Assert.That(_pair.PhysicalConnections,Is.EqualTo(physical));
                Buttons(1,0);Tick(reverse);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator BothPlayersCanGrabWithEitherHandAndBothHands()
        {
            foreach(var player in new[]{0,1})foreach(var mask in new[]{1,2,3})
            {
                Create();Buttons(player,mask);Assert.That(Acquire(mask==3?2:1,mover:player),Is.True,$"actor={player}, mask={mask}");
                if(mask==3){Buttons(player,2);Tick();Assert.That(_pair.LogicalGrips,Is.EqualTo(1));}
                Buttons(player,0);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator MutualLiftBlockedAndReleaseThrowKeepsOtherOwner()
        {
            Create(lift:true);Buttons(0,3);Buttons(1,3);Assert.That(Acquire(4),Is.True);
            var rise=_pair.Players.SelectMany(p=>p.Hands).Select(h=>h.LiftRise).ToArray();
            for(var i=0;i<120;i++)
            {
                Tick();Assert.That(_pair.Players.All(p=>p.Mutual),Is.True);Assert.That(_pair.LiftSupportForce,Is.Zero);
                var hands=_pair.Players.SelectMany(p=>p.Hands).ToArray();for(var h=0;h<hands.Length;h++)Assert.That(hands[h].LiftRise,Is.EqualTo(rise[h]));
            }
            Buttons(0,0);Tick();Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.PhysicalConnections,Is.EqualTo(2));Assert.That(_pair.Players[1].Hands.All(h=>h.Connection!=null),Is.True);
            Buttons(1,0);Tick();Assert.That(_pair.Throws,Is.EqualTo(2));Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator StandingContactReplayMatches15_60_144Fps()
        {
            var rows=new List<string>();
            foreach(var mode in new[]{"TwoHands","MutualBoth"})
            {
                var baseline=new List<Vector3[]>();var states=new List<string>();
                foreach(var fps in new[]{60,15,144})
                {
                    Create();var tick=0;var time=0d;var difference=0f;
                    while(tick<300)
                    {
                        time=Math.Min(5,time+1d/fps);var due=Math.Min(300,(int)Math.Floor(time*60+1e-8));
                        while(tick<due)
                        {
                            Input(mode,tick);Tick();var points=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body.position).Append(p.Rig.Actor.Motor.Body.position)).ToArray();var state=$"{_pair.LogicalGrips}/{_pair.PhysicalConnections}/{_pair.Players[0].Mutual}";
                            if(fps==60){baseline.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)difference=Mathf.Max(difference,Vector3.Distance(points[i],baseline[tick][i]));}tick++;
                        }
                        _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                    }
                    Assert.That(difference,Is.LessThan(.0001f));rows.Add($"{mode}, fps={fps}, maxPhysicalDifference={difference:R}");File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);
                    var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
                }
            }
        }
        [UnityTest] public IEnumerator CancellationClearsBothOwnersWithoutThrowOrAutomaticRegrab()
        {
            foreach(var mode in new[]{"Menu","Reset","OwnerLost","ColliderLost","ModeOff","Disabled"})
            {
                Create(lift:true);Buttons(0,3);Buttons(1,3);Assert.That(Acquire(4),Is.True);
                switch(mode)
                {
                    case "Menu":_pair.SetSuppressed(true);break;
                    case "Reset":_pair.ResetReview();break;
                    case "OwnerLost":_pair.Players[0].Rig.gameObject.SetActive(false);break;
                    case "ColliderLost":foreach(var h in _pair.Players.SelectMany(p=>p.Hands))h.TargetCollider.enabled=false;break;
                    case "ModeOff":_pair.EnableStandingSkinContacts=false;break;
                    case "Disabled":_pair.enabled=false;break;
                }
                Tick();Assert.That(_pair.Throws+_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero,mode);
                for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero,mode);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NoContactCannotAcquireAndResetAllowsFreshInput()
        {
            Create(distance:1.3f);Buttons(0,3);Buttons(1,3);for(var i=0;i<180;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
            var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            Create();Buttons(0,3);Buttons(1,3);Assert.That(Acquire(4),Is.True);_pair.ResetReview();for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
            Buttons(0,0);Buttons(1,0);Tick();Buttons(0,1);Assert.That(Acquire(1),Is.True);yield return null;
        }
        [UnityTest] public IEnumerator NativeStandingLoopKeepsSingleStepAndOwnership()
        {
            var saved=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;_scene=SceneManager.CreateScene("Native standing contact",new CreateSceneParameters(LocalPhysicsMode.None));
                _pair=ActiveGrabPairReview.Create(_scene,.5f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Model,matchHandEnvelope:true);_pair.EnableStandingSkinContacts=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;
                for(var i=0;i<60;i++)yield return null;Buttons(0,3);Buttons(1,3);
                for(var i=0;i<180;i++){if(i==30)_pair.Players[0].Move=Vector2.up*.25f;if(i==60)_pair.Players[0].Move=Vector2.zero;yield return null;}
                Assert.That(_pair.LogicalGrips,Is.EqualTo(4));Assert.That(_pair.PhysicalConnections,Is.EqualTo(2));Assert.That(_pair.StandingContacts.FixedSteps,Is.EqualTo(_pair.Tick));
                foreach(var p in _pair.Players)Assert.That(p.Rig.Steps,Is.EqualTo(_pair.Tick));
                Buttons(0,0);yield return new WaitForFixedUpdate();yield return null;Assert.That(_pair.LogicalGrips,Is.EqualTo(2));
                Buttons(1,0);yield return new WaitForFixedUpdate();yield return null;Assert.That(_pair.PhysicalConnections,Is.Zero);
            }
            finally{Time.captureDeltaTime=saved;}
        }
    }
}
