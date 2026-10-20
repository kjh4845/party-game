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
    public sealed class GrabJumpAssistanceTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample { public int step; public float actorHeight, targetHeight; public bool holding; }
        [Serializable] private sealed class Case
        {
            public string name;
            public float actorHeight, targetHeight, peakSpeed, maxAssistImpulse, maxHandError, maxReachCorrection;
            public int jumps, assistedBodies, breaks, assistEvents;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator HeldJump_RetainsWeightPenaltyWithoutStackingOrStaleTargets()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/grab-jump-weight-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var names = new List<string> { "Solo", "Before", "FullAssist", "Assisted", "BothHands", "SplitTargets", "Heavy", "ReleaseBeforeJump", "TargetDisabled", "MidairRepeat", "Rejump", "AfterReset", "HolderFirst", "TargetFirst", "MutualHolderFirst", "MutualTargetFirst" };
            for (var i = 0; i < 8; i++) names.Add("Direction" + i * 45);
            var results = new List<Case>();
            foreach (var name in names)
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableGrabReview();
                actor.Hands.Grabs.ThrowOnButtonRelease = false; // Isolate the historical constraint/jump/explicit-lift baseline.
                var mutual = name.StartsWith("Mutual");
                if (mutual) { var marker = actor.gameObject.AddComponent<StrikeReviewTarget>(); marker.GrabEligible = true; }
                if (name == "Before") actor.Hands.Grabs.HeldJumpAssistance = 0;
                if (name == "FullAssist") actor.Hands.Grabs.HeldJumpAssistance = 1;
                var targets = new List<PhysicsJumpReview>();
                foreach (var x in name == "SplitTargets" ? new[] { -.24f, .24f } : new[] { 0f })
                {
                    var target = PhysicsJumpReview.Create(new Vector3(x, .02f, name == "SplitTargets" ? .42f : .46f)); SceneManager.MoveGameObjectToScene(target.gameObject, _scene);
                    target.ReadKeyboard = false; target.enabled = false; target.Motor.ManualStep = true;
                    target.Motor.AuthorityEnabled = name.EndsWith("HolderFirst") || name.EndsWith("TargetFirst");
                    if (mutual) { target.Motor.Body.rotation = Quaternion.Euler(0, 180, 0); target.EnableGrabReview(); target.Hands.Grabs.ThrowOnButtonRelease = false; }
                    target.Motor.Body.interpolation = RigidbodyInterpolation.None;
                    if (name == "Heavy") target.Motor.Body.mass = 100f;
                    var marker = target.gameObject.AddComponent<StrikeReviewTarget>(); marker.GrabEligible = true;
                    foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>())
                    { skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f)); if (name == "Solo") skin.enabled = false; }
                    targets.Add(target);
                }
                actor.Hands.ReviewTargets = targets.Select(t => t.GetComponent<StrikeReviewTarget>()).ToArray();
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                var cameraObject = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene); var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .9f;
                var center = new Vector3(0, .58f, .3f); camera.transform.position = center + new Vector3(3, .4f, 1); camera.transform.LookAt(center);
                var lightObject = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObject, _scene); var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 2f; light.transform.rotation = Quaternion.Euler(40, -35, 0);
                var render = new RenderTexture(448, 512, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(448, 512, TextureFormat.RGB24, false);
                var result = new Case { name = name }; var samples = new List<Sample>();
                var actorBase = 0f; var targetBase = 0f;
                for (var step = 0; step < 240; step++)
                {
                    var hold = name != "Solo" && step >= 20 && step < 210;
                    if (name == "ReleaseBeforeJump" && step >= 55) hold = false;
                    if (name == "AfterReset" && step >= 130 && step < 140) hold = false;
                    actor.Hands.SubmitButtons(hold, hold && (name == "BothHands" || name == "SplitTargets"));
                    if (mutual) targets[0].Hands.SubmitButtons(hold, false);
                    if (name.StartsWith("Direction") && step >= 45 && step < 140)
                    {
                        var angle = int.Parse(name.Substring(9)) * Mathf.Deg2Rad;
                        actor.Motor.MoveInput = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                    }
                    else actor.Motor.MoveInput = Vector2.zero;
                    if (step == 60 || (name == "MidairRepeat" && step == 66) || (name == "Rejump" && step == 140) || (name == "AfterReset" && step == 175)) actor.Motor.RequestJump();
                    if (step == 60 && (name.EndsWith("HolderFirst") || name.EndsWith("TargetFirst"))) targets[0].Motor.RequestJump();
                    if (step == 60 && name == "TargetDisabled") targets[0].GetComponent<StrikeReviewTarget>().Eligible = false;
                    if (step == 130 && name == "AfterReset") { actor.Motor.ResetAt(new Vector3(0, .02f, 0)); actor.Hands.ResetTargets(); }
                    if (name.EndsWith("TargetFirst")) { targets[0].Motor.Step(1f / 60f); if (mutual) targets[0].Hands.Step(1f / 60f); }
                    actor.Motor.Step(1f / 60f); actor.Hands.Step(1f / 60f);
                    if (name.EndsWith("HolderFirst")) { targets[0].Motor.Step(1f / 60f); if (mutual) targets[0].Hands.Step(1f / 60f); }
                    if (mutual && step == 59) Assert.That(targets[0].Hands.Grabs.IsHolding(0), Is.True, "Both characters must actually hold each other");
                    if (actor.Motor.JumpCommittedThisStep)
                    {
                        result.maxAssistImpulse = Mathf.Max(result.maxAssistImpulse, actor.Hands.Grabs.LastJumpAssistImpulse);
                        result.assistedBodies = Mathf.Max(result.assistedBodies, actor.Hands.Grabs.LastJumpAssistedBodies);
                        if (actor.Hands.Grabs.LastJumpAssistedBodies > 0) result.assistEvents++;
                        Assert.That(actor.Hands.Grabs.LastJumpAssistImpulse, Is.LessThanOrEqualTo(actor.Motor.Body.mass * actor.Motor.LastJumpLaunchSpeed * 2f * actor.Hands.Grabs.HeldJumpAssistance + .0001f));
                    }
                    physics.Simulate(1f / 60f); actor.ApplyPresentation(1f / 60f);
                    if (step == 40) { actorBase = actor.Motor.Body.position.y; targetBase = targets[0].Motor.Body.position.y; }
                    if (step >= 60)
                    {
                        result.actorHeight = Mathf.Max(result.actorHeight, actor.Motor.Body.position.y - actorBase);
                        result.targetHeight = Mathf.Max(result.targetHeight, targets[0].Motor.Body.position.y - targetBase);
                        result.peakSpeed = Mathf.Max(result.peakSpeed, actor.Motor.Body.linearVelocity.magnitude, targets[0].Motor.Body.linearVelocity.magnitude);
                        result.maxHandError = Mathf.Max(result.maxHandError, actor.GrabPose.MaximumHeldTargetError);
                        result.maxReachCorrection = Mathf.Max(result.maxReachCorrection, actor.GrabPose.MaximumReachCorrection);
                    }
                    samples.Add(new Sample { step = step, actorHeight = actor.Motor.Body.position.y - actorBase, targetHeight = targets[0].Motor.Body.position.y - targetBase, holding = actor.Hands.Grabs.IsHolding(0) });
                    if ((name == "Solo" || name == "FullAssist" || name == "Assisted") && step >= 45 && step < 165)
                    {
                        var frame = Time.frameCount; yield return null; Assert.That(Time.frameCount, Is.GreaterThan(frame));
                        camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 448, 512), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 45:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.jumps = actor.Motor.JumpCount; result.breaks = actor.Hands.Grabs.Breaks; result.samples = samples.ToArray(); results.Add(result);
                foreach (var t in targets) Assert.That(t.GetComponent<StrikeReviewTarget>().HitCount, Is.Zero, "Jump assistance is not an attack hit");
                actor.Hands.SetInputSuppressed(true);
                if (mutual) { Assert.That(targets[0].Hands.Grabs.Breaks, Is.Zero); targets[0].Hands.SetInputSuppressed(true); }
                File.WriteAllText(Path.Combine(output, "GrabJumpReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; render.Release(); UnityEngine.Object.Destroy(render); UnityEngine.Object.Destroy(image);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
            var solo = results.Single(c => c.name == "Solo"); var before = results.Single(c => c.name == "Before"); var assisted = results.Single(c => c.name == "Assisted");
            Assert.That(solo.actorHeight, Is.InRange(.20f, .29f));
            Assert.That(assisted.actorHeight, Is.GreaterThan(before.actorHeight * 2.5f));
            Assert.That(assisted.actorHeight / solo.actorHeight, Is.InRange(.65f, .85f), "Same-mass carried target must remain perceptibly heavier than solo");
            Assert.That(results.Single(c => c.name == "SplitTargets").actorHeight, Is.LessThan(assisted.actorHeight - .01f));
            Assert.That(results.Single(c => c.name == "Heavy").actorHeight, Is.LessThan(results.Single(c => c.name == "SplitTargets").actorHeight));
            foreach (var c in results)
            {
                Assert.That(c.actorHeight, Is.LessThan(.30f), c.name + " no stacking height");
                Assert.That(c.peakSpeed, Is.LessThan(4f), c.name + " speed cap");
                Assert.That(c.maxHandError, Is.LessThan(.015f), c.name + " hand contact");
                Assert.That(c.maxReachCorrection, Is.LessThan(.003f), c.name + " reach");
                Assert.That(c.jumps, Is.EqualTo(c.name == "Rejump" ? 2 : 1), c.name + " jump count");
                if (c.name == "Assisted" || c.name == "BothHands" || c.name == "Rejump" || c.name == "AfterReset") Assert.That(c.assistedBodies, Is.EqualTo(1), c.name);
                if (c.name == "SplitTargets") Assert.That(c.assistedBodies, Is.EqualTo(2));
                if (c.name == "Rejump" || c.name == "AfterReset") Assert.That(c.assistEvents, Is.EqualTo(2), c.name + " fresh jump grant");
                if (c.name == "Solo" || c.name == "Before" || c.name == "ReleaseBeforeJump" || c.name == "TargetDisabled") Assert.That(c.maxAssistImpulse, Is.Zero, c.name);
                if (c.name == "Assisted" || c.name == "BothHands" || c.name == "SplitTargets" || c.name.StartsWith("Direction") || c.name.StartsWith("Mutual")) Assert.That(c.breaks, Is.Zero, c.name + " hold continuity");
            }
            Assert.That(Mathf.Abs(results.Single(c => c.name == "BothHands").actorHeight - assisted.actorHeight), Is.LessThan(.01f));
            Assert.That(Mathf.Abs(results.Single(c => c.name == "HolderFirst").actorHeight - results.Single(c => c.name == "TargetFirst").actorHeight), Is.LessThan(.005f));
            Assert.That(Mathf.Abs(results.Single(c => c.name == "MutualHolderFirst").actorHeight - results.Single(c => c.name == "MutualTargetFirst").actorHeight), Is.LessThan(.005f));
            File.WriteAllText(Path.Combine(output, "GrabJumpReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }
    }
}
