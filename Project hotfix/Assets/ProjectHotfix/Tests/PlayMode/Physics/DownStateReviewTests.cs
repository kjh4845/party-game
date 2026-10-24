using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class DownStateReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Case { public string name; public uint count; public int throws, jumps, punches, grabs; public float blockedTravel; public bool rearmed, menuPreserved; }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }
        [UnityTest] public IEnumerator DownLocksActionsReleasesWithoutThrowAndRequiresFreshInputAfterRecovery()
        {
            var results = new List<Case>();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/down-state-20260906")); Directory.CreateDirectory(output);
            foreach (var name in new[] { "Match", "Lobby", "QueuedRelease", "Reset", "Menu", "AuthorityDisabled" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableGrabReview();
                var down = actor.gameObject.AddComponent<DownStateReview>(); down.Bind(actor, new DownTimingProfile(1.5, .5, 3), name != "Lobby");
                var targetActor = PhysicsJumpReview.Create(new Vector3(0, .02f, .46f)); SceneManager.MoveGameObjectToScene(targetActor.gameObject, _scene);
                targetActor.ReadKeyboard = false; targetActor.enabled = false; targetActor.Motor.ManualStep = true; targetActor.Motor.AuthorityEnabled = false;
                targetActor.Motor.Body.interpolation = RigidbodyInterpolation.None; var target = targetActor.gameObject.AddComponent<StrikeReviewTarget>(); target.GrabEligible = true;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                var result = new Case { name = name }; var blockedPosition = Vector3.zero;
                for (var step = 0; step < 300; step++)
                {
                    var hold = step >= 20 && step < 250 || step >= 260;
                    if (name == "QueuedRelease" && step == 100) hold = false;
                    actor.Hands.SubmitButtons(hold, false);
                    if (step == 100)
                    {
                        Assert.That(actor.Hands.Grabs.IsHolding(0), Is.True, name + " starts holding");
                        actor.Motor.RequestJump();
                        Assert.That(down.TryBeginDown(down.State.RoundGeneration, 1), Is.True);
                        Assert.That(actor.Hands.Grabs.IsHolding(0), Is.False);
                        blockedPosition = actor.Motor.Body.position;
                    }
                    if (step > 100 && step < 200 && !(name == "Reset" && step >= 130)) { actor.Motor.MoveInput = Vector2.up; actor.Motor.SprintRequested = true; actor.Motor.RequestJump(); }
                    if (step == 120 && name == "Menu") actor.Hands.SetInputSuppressed(true);
                    if (step == 120 && name == "AuthorityDisabled") actor.Motor.AuthorityEnabled = false;
                    if (step == 130 && name == "Reset")
                    {
                        var generation = down.State.RoundGeneration; actor.Motor.ResetAt(new Vector3(0, .02f, 0));
                        Assert.That(down.TryBeginDown(generation, 2), Is.False, "Reset invalidates the old generation before admission");
                    }
                    if (step == 180 && name == "AuthorityDisabled") actor.Motor.AuthorityEnabled = true;
                    down.Step(1f / 60f); actor.Motor.Step(1f / 60f); actor.Hands.Step(1f / 60f); physics.Simulate(1f / 60f);
                    if (step >= 100 && step < 130) result.blockedTravel = Mathf.Max(result.blockedTravel, Vector3.Distance(blockedPosition, actor.Motor.Body.position));
                    if (step == 199 && name != "Reset")
                    {
                        Assert.That(down.State.BlocksActions, Is.True, "Timer alone must not unlock");
                        Assert.That(actor.Motor.ActionsBlocked, Is.True);
                        Assert.That(actor.Hands.State.Armed, Is.False);
                        if (name == "AuthorityDisabled") Assert.That(down.State.RemainingSeconds, Is.GreaterThan(.7), "No local authority, no clock advance");
                    }
                    if (step == 245 && name != "Reset")
                    {
                        if (name == "AuthorityDisabled") down.Step(1); // Explicit fixture clock; no production tuning implied.
                        Assert.That(down.State.TryBeginGetUp(down.State.RoundGeneration, down.State.EpisodeId, Quaternion.Euler(-90, 0, 0), Vector3.up, true, true), Is.True);
                        Assert.That(down.State.TryCompleteGetUp(down.State.RoundGeneration, down.State.EpisodeId), Is.True); down.RefreshLock();
                        Assert.That(actor.Motor.ActionsBlocked, Is.False); Assert.That(actor.Hands.State.Armed, Is.False, "Held click cannot auto-rearm");
                    }
                    if (step == 252)
                    {
                        result.rearmed = actor.Hands.State.Armed; result.menuPreserved = actor.Hands.InputSuppressed;
                        Assert.That(result.rearmed, Is.EqualTo(name != "Menu"), name + " all-up rearm respects menu");
                    }
                }
                result.count = down.State.DownCount; result.throws = actor.Hands.Grabs.ThrowCount; result.punches = actor.Hands.State.PunchCount;
                result.jumps = actor.Motor.JumpCount; result.grabs = actor.Hands.Grabs.Acquisitions;
                Assert.That(result.throws, Is.Zero); Assert.That(result.punches, Is.Zero); Assert.That(result.jumps, Is.Zero);
                Assert.That(result.blockedTravel, Is.LessThan(.001f)); Assert.That(result.count, Is.EqualTo(name == "Lobby" || name == "Reset" ? 0 : 1));
                Assert.That(result.grabs, Is.EqualTo(name == "Menu" ? 1 : 2), "New press can grab again only after all-up");
                if (name == "Menu") Assert.That(result.menuPreserved, Is.True);
                results.Add(result);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(output, "DownStateReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }
    }
}
