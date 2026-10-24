using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        private bool Grab(int player,int mask,int count)
        {
            Buttons(player,mask);for(var i=0;i<180;i++){if(i>30)_pair.Players[player].Move=(player==0?Vector2.up:Vector2.down)*.20f;Tick();if(_pair.LogicalGrips>=count){_pair.Players[player].Move=Vector2.zero;return true;}}
            _pair.Players[player].Move=Vector2.zero;return false;
        }
        [UnityTest] public IEnumerator ActualTipContactScoresBodyAndHeadOnceForEachHand()
        {
            foreach(var drop in new[]{0f,.22f})foreach(var side in new[]{0,1})
            {
                Create(drop>0?.42f:.38f,drop);Tap(0,side);for(var i=0;i<60;i++)Tick();var punches=_pair.PhysicalPunches;
                Assert.That(punches.Hits.Count,Is.EqualTo(1));var hit=punches.Hits[0];Assert.That(hit.side,Is.EqualTo(side));Assert.That(hit.points,Is.EqualTo(drop>0?2:1));Assert.That(punches.Score(1).Points,Is.EqualTo(hit.points));
                Assert.That(hit.tipDistance,Is.LessThanOrEqualTo(.032001f));Assert.That(hit.sourceAge,Is.InRange((float)PhysicalPunchReview.SourceHitStart-.000001f,(float)PhysicalPunchReview.SourceHitEnd+.000001f));Assert.That(hit.closingSpeed,Is.GreaterThanOrEqualTo(.15f));
                Assert.That(punches.ConfirmedCues,Is.EqualTo(punches.Hits.Count));
                Assert.That(hit.impulse,Is.LessThanOrEqualTo(3));Assert.That(_pair.LogicalGrips,Is.Zero);for(var i=0;i<60;i++)Tick();Assert.That(punches.Hits.Count,Is.EqualTo(1));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator OutOfRangeWallAndNonTipContactsCannotScore()
        {
            foreach(var mode in new[]{"Far","Wall","NonTip"})
            {
                // With the fist drive the hand follows the reference path, so the tip
                // now lands at 0.25-0.35m drops (ProbeNonTipContactGeometry, 2026-10-15).
                // 0.22m is the measured arm-only contact (13 radius rejections, 0 hits).
                Create(mode=="Far"?.70f:.38f,mode=="NonTip"?.22f:0,mode=="Wall");Tap(0,0);for(var i=0;i<60;i++)Tick();
                Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.Hits.Count,Is.Zero,mode);
                if(mode=="NonTip")Assert.That(_pair.PhysicalPunches.RejectedOutsideTip,Is.GreaterThan(0));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator MirroredPlayerCanHitWithEitherHand()
        {
            foreach(var side in new[]{0,1})
            {
                Create(.38f);Tap(1,side);for(var i=0;i<60;i++)Tick();Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.Score(0).Points,Is.EqualTo(1));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator HoldAndThrowNeverBecomePunchAndFreeHandCanPunch()
        {
            Create(.5f);Assert.That(Grab(0,1,1),Is.True);Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.Zero);var left=_pair.Players[0].Hands[0].Connection;
            Buttons(0,3);Tick();Tick();Buttons(0,1);Tick();Assert.That(_pair.PhysicalPunches.State(0).Phase(1),Is.EqualTo(HandIntentPhase.Punch));Assert.That(_pair.Players[0].Hands[0].Connection,Is.SameAs(left));
            for(var i=0;i<40;i++)Tick();var count=_pair.PhysicalPunches.State(0).PunchCount;Buttons(0,0);Tick();Assert.That(_pair.Throws,Is.Zero,"a standing opponent is only held, never thrown");Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(count));
            Assert.That(_pair.PhysicalPunches.State(0).Phase(0),Is.Not.EqualTo(HandIntentPhase.Punch));yield return null;
        }
        [UnityTest] public IEnumerator SameTickTimedTapsAndRearmDoNotLoseOrInventActions()
        {
            Create(.7f);_pair.SubmitPhysicalHandEdge(0,0,true);_pair.SubmitPhysicalHandEdge(0,0,false,.04);_pair.SubmitPhysicalHandEdge(1,1,true);_pair.SubmitPhysicalHandEdge(1,1,false,.04);Tick();
            Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.State(1).PunchCount,Is.EqualTo(1));
            _pair.SetSuppressed(true);Tick();_pair.SetSuppressed(false);_pair.SubmitPhysicalHandEdge(0,0,true);_pair.SubmitPhysicalHandEdge(0,0,false,.03);Tick();Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1),"The all-up rearm sample must consume stale edges");
            _pair.SubmitPhysicalHandEdge(0,0,true);_pair.SubmitPhysicalHandEdge(0,0,false,.03);Tick();Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(2));yield return null;
        }
        [UnityTest] public IEnumerator CancellationStopsPunchAndRestoresGrabLimits()
        {
            foreach(var mode in new[]{"Menu","Reset","HandLost","Disabled"})
            {
                Create(.38f);Tap(0,0);for(var i=0;i<3;i++)Tick();Assert.That(_pair.PhysicalPunches.IsPunching(0,0),Is.True);
                if(mode=="Menu")_pair.SetSuppressed(true);if(mode=="Reset")_pair.ResetReview();if(mode=="HandLost")_pair.Players[0].Rig.Find("Forearm_L").Shape.enabled=false;if(mode=="Disabled")_pair.enabled=false;
                if(mode=="Disabled"||mode=="Menu")Assert.That(_pair.Players[0].Rig.Find("UpperArm_L").Joint.angularYLimit.limit,Is.EqualTo(15),"Cancel without waiting for another controller tick");
                Tick();Assert.That(_pair.PhysicalPunches.IsPunching(0,0),Is.False);for(var i=0;i<30;i++)Tick();Assert.That(_pair.PhysicalPunches.Hits.Count,Is.Zero);
                var joint=_pair.Players[0].Rig.Find("UpperArm_L").Joint;Assert.That(joint.angularYLimit.limit,Is.EqualTo(15));Assert.That(joint.angularZLimit.limit,Is.EqualTo(15));Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        private void ReplayInput(int tick)
        {
            if(tick==0)Buttons(0,1);if(tick==2)Buttons(0,0);
            if(tick==45)Buttons(1,2);if(tick==47)Buttons(1,0);
            if(tick==90)Buttons(0,3);if(tick==120)_pair.Players[0].Move=Vector2.up*.2f;
            if(tick==145)_pair.Players[0].Move=Vector2.zero;if(tick==180)Buttons(0,0);
        }
        [UnityTest] public IEnumerator PhysicalPunchReplayMatches15_60_144Fps()
        {
            var poses=new List<Vector3[]>();var states=new List<string>();var rows=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create(.38f);var tick=0;var time=0d;var difference=0f;
                while(tick<240)
                {
                    time=Math.Min(4,time+1d/fps);var due=Math.Min(240,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        ReplayInput(tick);Tick();var points=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body.position).Append(p.Rig.Actor.Motor.Body.position)).ToArray();var punch=_pair.PhysicalPunches;var state=$"{punch.State(0).PunchCount}/{punch.State(1).PunchCount}/{punch.Hits.Count}/{punch.Score(0).Points}/{punch.Score(1).Points}/{_pair.LogicalGrips}/{_pair.Throws}";
                        if(fps==60){poses.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)difference=Mathf.Max(difference,Vector3.Distance(points[i],poses[tick][i]));}tick++;
                    }
                    _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                }
                Assert.That(_pair.PhysicalPunches.Hits.Count,Is.GreaterThan(0));Assert.That(difference,Is.LessThan(.0001f));rows.Add($"fps={fps}, maxPhysicalDifference={difference:R}, hits={_pair.PhysicalPunches.Hits.Count}");File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NativePunchLoopRunsOneAuthorityStep()
        {
            var capture=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;_scene=SceneManager.CreateScene("Native physical punch",new CreateSceneParameters(LocalPhysicsMode.None));_pair=ActiveGrabPairReview.Create(_scene,.38f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Model,matchHandEnvelope:true);
                _pair.ReadKeyboard=false;_pair.View.enabled=false;_pair.EnableStandingSkinContacts=_pair.EnableLiftThrow=_pair.EnablePhysicalPunch=true;
                for(var i=0;i<90;i++)yield return null;Buttons(0,1);for(var i=0;i<2;i++)yield return null;Buttons(0,0);for(var i=0;i<50;i++)yield return null;
                Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(1));foreach(var p in _pair.Players)Assert.That(p.Rig.Steps,Is.EqualTo(_pair.Tick));
            }
            finally{Time.captureDeltaTime=capture;}
        }
        [UnityTest] public IEnumerator RealRepeatedContactsKnockDownOnceWithoutTimeDecay()
        {
            Create(.38f);var punch=_pair.PhysicalPunches;
            // PRD 4.5 (2026-10-17 knockdown): the 10th point knocks the target down and the Down consumes
            // the accumulation; the 11th hit (after the review reset stood it up) starts again at 1.
            // Points never decay with time.
            for(var action=0;action<11;action++)
            {
                if(action>0){foreach(var p in _pair.Players)p.Rig.ResetReview();UnityEngine.Physics.SyncTransforms();for(var i=0;i<90;i++)Tick();}
                Tap(0,action%2);for(var i=0;i<50;i++)Tick();
                Assert.That(punch.Score(1).Points,Is.EqualTo(action<9?action+1:action-9));Assert.That(punch.Hits.Count,Is.EqualTo(action+1));
                if(action==9)Assert.That(_pair.Players[1].Rig.KnockedDown,Is.True);
            }
            Assert.That(punch.ThresholdRequests,Is.EqualTo(1));Assert.That(punch.ThresholdPending(1),Is.False);Assert.That(_pair.Players[1].Rig.Knockdowns,Is.EqualTo(1));
            for(var i=0;i<180;i++)Tick();Assert.That(punch.Score(1).Points,Is.EqualTo(1));
            _pair.ResetReview();Assert.That(punch.Score(1).Points+punch.Hits.Count+punch.ThresholdRequests,Is.Zero);Assert.That(punch.ThresholdPending(1),Is.False);yield return null;
        }
    }
}
