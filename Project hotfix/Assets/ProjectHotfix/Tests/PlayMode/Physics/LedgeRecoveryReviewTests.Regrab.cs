using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class LedgeRecoveryReviewTests
    {
        private string RegrabOutput => Environment.GetEnvironmentVariable("CODEX_REGRAB_OUTPUT") ??
            Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/climb-no-repeat-20260913"));

        [UnityTest] public IEnumerator ContinuousPlayerHoldDoesNotRestartGrabAfterClimbHandoff()
        {
            var rows=new List<string>();
            foreach(var candidate in new[]{false,true})foreach(var hands in new[]{1,2,3})
            {
                Create(true,true);Actor.Hands.Grabs.BoundedContactPose=candidate;if(candidate)Ledge.EnableContactReview();
                ReadyHold(hands);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);HoldStill();for(var i=0;i<20;i++)Tick();
                var acquisitions=Ledge.Acquisitions;var handoff=false;var repeated=0;
                Actor.Motor.MoveInput=Vector2.up;Actor.Motor.SprintRequested=true;Actor.Motor.RequestJump();
                for(var tick=0;tick<150;tick++)
                {
                    // No synthetic up/down edges. This is the player's original hold.
                    Tick();handoff|=Ledge.LastReleaseReason=="ClimbHandoff";
                    if(handoff)
                    {
                        var pose=Actor.Hands.Grabs.Capture();if(pose.Left.Active||pose.Right.Active)repeated++;
                        Assert.That(Actor.Hands.State.Phase(0),Is.Not.EqualTo(HandIntentPhase.GrabSeek));
                        Assert.That(Actor.Hands.State.Phase(1),Is.Not.EqualTo(HandIntentPhase.GrabSeek));
                    }
                    if(Recovered())Actor.Motor.MoveInput=Vector2.zero;
                }
                rows.Add($"candidate={candidate}, hands={hands}, handoff={handoff}, repeatedReachTicks={repeated}, acquisitions={Ledge.Acquisitions}, initialAcquisitions={acquisitions}, assists={Ledge.TotalAssists}, recovered={Recovered()}");
                Directory.CreateDirectory(RegrabOutput);File.WriteAllLines(Path.Combine(RegrabOutput,"ContinuousHold.txt"),rows);
                Assert.That(handoff,Is.True);Assert.That(repeated,Is.Zero);Assert.That(Ledge.Acquisitions,Is.EqualTo(acquisitions));
                Assert.That(Ledge.TotalAssists,Is.EqualTo(1));Assert.That(Recovered(),Is.True);Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }

        [UnityTest] public IEnumerator ReviewDriverRegripsOnlyWhenAnotherAssistIsNeeded()
        {
            var rows=new List<string>();
            foreach(var candidate in new[]{false,true})foreach(var depth in new[]{-.65f,-2.18f})
            {
                Create(true,true);Actor.Hands.Grabs.BoundedContactPose=candidate;if(candidate)Ledge.EnableContactReview();
                ReadyHold(3);SeedFall(2,depth);var input=new RecoveryInput();var extraSeek=0;
                for(var tick=0;tick<300&&!input.finished&&!_boundary.Out[0];tick++)
                {
                    RecoveryStep(input,3);
                    if(Ledge.TotalAssists>0&&!NeedsAnotherRecoveryAssist()&&
                        (Actor.Hands.State.Phase(0)==HandIntentPhase.GrabSeek||Actor.Hands.State.Phase(1)==HandIntentPhase.GrabSeek))extraSeek++;
                }
                rows.Add($"candidate={candidate}, depth={depth:R}, regripPresses={input.regripPresses}, extraSeekTicks={extraSeek}, assists={Ledge.TotalAssists}, recovered={input.finished}");
                Directory.CreateDirectory(RegrabOutput);File.WriteAllLines(Path.Combine(RegrabOutput,"ReviewInput.txt"),rows);
                Assert.That(input.finished,Is.True);Assert.That(input.regripPresses,Is.EqualTo(depth< -2?1:0));
                Assert.That(Ledge.TotalAssists,Is.EqualTo(depth< -2?2:1));Assert.That(extraSeek,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
    }
}
