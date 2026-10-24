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
    public sealed class ReleaseThrowReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample { public int step, holding, throws; public float height; }
        [Serializable] private sealed class Case
        {
            public string name;
            public int throws, acquisitions, punches;
            public float impulse, maximumLift, maximumSpeed, maximumHandError, distance;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator ButtonRelease_ThrowsWithoutLift_AndForcedCancellationNeverThrows()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/release-throw-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "Left", "Right", "BothSimultaneous", "BothStaggered", "SplitTargets", "Light", "Air", "Moving", "Menu", "QueuedMenu", "Reset", "TargetReset", "TargetDisabled", "HandLost", "AuthorityLost", "ReachBreak", "JointBreak", "ForcedGripCleanup", "HoldMiss", "Tap", "NoPresentation" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableGrabReview();
                Assert.That(actor.Hands.Grabs.ThrowOnButtonRelease, Is.True, "Default player gesture");
                var targets = new List<StrikeReviewTarget>();
                foreach (var x in name == "SplitTargets" ? new[] { -.24f, .24f } : new[] { 0f })
                {
                    var targetActor = PhysicsJumpReview.Create(new Vector3(x, .02f, name == "SplitTargets" ? .42f : .46f)); SceneManager.MoveGameObjectToScene(targetActor.gameObject, _scene);
                    targetActor.ReadKeyboard = false; targetActor.enabled = false; targetActor.Motor.ManualStep = true; targetActor.Motor.AuthorityEnabled = false;
                    targetActor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                    var target = targetActor.gameObject.AddComponent<StrikeReviewTarget>(); target.GrabEligible = name != "HoldMiss";
                    if (name == "Light") target.Body.mass = 2f;
                    targets.Add(target);
                    foreach (var skin in targetActor.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
                }
                actor.Hands.ReviewTargets = targets.ToArray();
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                var camObj = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(camObj, _scene); var camera = camObj.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .95f;
                var center = new Vector3(0, .6f, .65f); camera.transform.position = center + new Vector3(3, .4f, 1); camera.transform.LookAt(center);
                var lightObj = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObj, _scene); var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
                light.transform.rotation = Quaternion.Euler(40, -35, 0);
                var render = new RenderTexture(640, 480, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(640, 480, TextureFormat.RGB24, false);
                var result = new Case { name = name }; var samples = new List<Sample>();
                for (var step = 0; step < 200; step++)
                {
                    var hold = step >= 20 && step < (name == "Tap" ? 23 : 100);
                    var left = hold && name != "Right" && !(name == "BothStaggered" && step >= 80);
                    var right = hold && (name == "Right" || name.StartsWith("Both") || name == "SplitTargets");
                    actor.Hands.SubmitButtons(left, right);
                    actor.Motor.MoveInput = name == "Moving" && step >= 70 && step < 110 ? Vector2.up : Vector2.zero;
                    if (name == "Air" && step == 90) actor.Motor.RequestJump();
                    if (step == 100)
                    {
                        // The up edge is already queued: invalidation must win in this same authority tick.
                        if (name == "Menu" || name == "QueuedMenu") actor.Hands.SetInputSuppressed(true);
                        if (name == "Reset") actor.Motor.ResetAt(new Vector3(0, .02f, 0));
                        if (name == "TargetReset") targets[0].ResetReview();
                        if (name == "TargetDisabled") targets[0].enabled = false;
                        if (name == "HandLost") actor.Arms.LeftAvailable = false;
                        if (name == "AuthorityLost") actor.Motor.AuthorityEnabled = false;
                        if (name == "ReachBreak") targets[0].Body.position += Vector3.forward * .09f;
                    }
                    if (name == "Menu" && step == 80) actor.Hands.SetInputSuppressed(true);
                    if (name == "JointBreak" && step == 80) { UnityEngine.Object.Destroy(actor.Hands.Grabs.Joint(0)); yield return null; }
                    if (name == "ForcedGripCleanup" && step == 80) actor.Hands.Grabs.ReleaseAll("GripStress");
                    actor.Motor.Step(1f / 60f); actor.Hands.Step(1f / 60f); physics.Simulate(1f / 60f);
                    if (name != "NoPresentation") actor.ApplyPresentation(1f / 60f);
                    var grabs = actor.Hands.Grabs;
                    if (step == 60 && name != "HoldMiss" && name != "Tap") Assert.That(grabs.Acquisitions, Is.EqualTo(name.StartsWith("Both") || name == "SplitTargets" ? 2 : 1), name + " acquired before release");
                    if (name == "BothStaggered" && step >= 80 && step < 100) { Assert.That(grabs.IsHolding(0), Is.False); Assert.That(grabs.IsHolding(1), Is.True); Assert.That(grabs.ThrowCount, Is.Zero, "One hand remains held"); }
                    result.maximumLift = Mathf.Max(result.maximumLift, grabs.LiftAmount(0), grabs.LiftAmount(1));
                    result.maximumSpeed = Mathf.Max(result.maximumSpeed, actor.Motor.Body.linearVelocity.magnitude, targets.Max(t => t.Body.linearVelocity.magnitude));
                    result.maximumHandError = Mathf.Max(result.maximumHandError, actor.GrabPose.MaximumHeldTargetError);
                    samples.Add(new Sample { step = step, holding = (grabs.IsHolding(0) ? 1 : 0) + (grabs.IsHolding(1) ? 1 : 0), throws = grabs.ThrowCount, height = targets[0].Body.position.y });
                    if ((name == "Left" || name == "BothStaggered" || name == "Air" || name == "Moving") && step >= 50 && step < 170)
                    {
                        yield return null; camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 50:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.throws = actor.Hands.Grabs.ThrowCount; result.acquisitions = actor.Hands.Grabs.Acquisitions; result.punches = actor.Hands.State.PunchCount;
                result.impulse = actor.Hands.Grabs.LastThrowImpulse; result.distance = targets[0].Body.position.z - .46f; result.samples = samples.ToArray(); results.Add(result);
                File.WriteAllText(Path.Combine(output, "ReleaseThrowReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                var expected = name == "SplitTargets" ? 2 : new[] { "Left", "Right", "BothSimultaneous", "BothStaggered", "Light", "Air", "Moving", "NoPresentation" }.Contains(name) ? 1 : 0;
                Assert.That(result.throws, Is.EqualTo(expected), name + " throw count");
                Assert.That(result.punches, Is.EqualTo(name == "Tap" ? 1 : 0), name + " punch count");
                Assert.That(result.maximumLift, Is.Zero, name + " no lift prerequisite or automatic lift");
                Assert.That(result.impulse, Is.EqualTo(expected == 0 ? 0f : name == "Light" ? 6f : 30f).Within(.0001f), name + " mass bounded impulse");
                Assert.That(result.maximumSpeed, Is.LessThan(8f), name + " bounded physics");
                if (expected > 0)
                {
                    Assert.That(result.maximumHandError, Is.LessThan(.015f), name + " contact pose");
                    Assert.That(result.distance, Is.GreaterThan(.1f), name + " impulse must move the target in physics");
                }
                Assert.That(actor.Hands.Grabs.IsHolding(0) || actor.Hands.Grabs.IsHolding(1), Is.False, name + " no stale hold");
                camera.targetTexture = null; render.Release(); UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(render);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            Assert.That(Mathf.Abs(results.Single(c => c.name == "Left").distance - results.Single(c => c.name == "NoPresentation").distance), Is.LessThan(.0001f), "Presentation must not change physics");
            File.WriteAllText(Path.Combine(output, "ReleaseThrowReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
        }
    }
}
