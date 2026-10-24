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
    public sealed class HandStrikeReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample { public int step, hits; public string left, right; public float displacement; }
        [Serializable] private sealed class Case
        {
            public string name;
            public int punches, hits;
            public float maximumSpeed, maximumImpulse, maximumDisplacement, maximumPresentationTimeError;
            public bool sawGrabSeek;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator AuthorityStrike_TapHoldOcclusionCancellationAndPresentationIsolation()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/hand-strike-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "LeftTap", "RightTap", "BothTap", "Hold", "SameFrameTap", "StalledFrameTap", "Wall", "Miss", "Occupied", "MenuCancel", "Rearm", "Reset", "NoAuthority", "NoPresentation", "UnavailableTarget", "RemovedTarget", "Reattack", "MultipleColliders" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableHandStrikeReview();
                var targetActor = PhysicsJumpReview.Create(new Vector3(0, .02f, name == "Miss" ? 1.2f : .52f)); SceneManager.MoveGameObjectToScene(targetActor.gameObject, _scene);
                targetActor.ReadKeyboard = false; targetActor.enabled = false; targetActor.Motor.ManualStep = true;
                targetActor.Motor.AuthorityEnabled = false; targetActor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                var target = targetActor.gameObject.AddComponent<StrikeReviewTarget>(); actor.Hands.ReviewTargets = new[] { target };
                foreach (var skin in targetActor.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
                if (name == "UnavailableTarget") target.Eligible = false;
                if (name == "Occupied") actor.Arms.LeftAvailable = false;
                if (name == "MultipleColliders")
                {
                    var child = new GameObject("AdditionalTargetCollider"); child.transform.SetParent(targetActor.transform, false);
                    child.transform.localPosition = new Vector3(0, .66f, 0); child.AddComponent<SphereCollider>().radius = .17f;
                }
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(12, .2f, 12);
                if (name == "Wall")
                {
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, _scene);
                    wall.transform.position = new Vector3(0, .55f, .245f); wall.transform.localScale = new Vector3(.7f, 1.1f, .015f);
                }
                var cameraObject = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .78f;
                var center = new Vector3(0, .48f, .35f); camera.transform.position = center + new Vector3(3f, .5f, .8f); camera.transform.LookAt(center);
                var lightObject = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObject, _scene);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f; light.transform.rotation = Quaternion.Euler(40, -35, 0);
                var render = new RenderTexture(640, 480, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(640, 480, TextureFormat.RGB24, false);
                var result = new Case { name = name }; var samples = new List<Sample>();
                for (var step = 0; step < 180; step++)
                {
                    var down = step >= 20 && step < (name == "Hold" || name == "Rearm" ? 46 : 23);
                    if (name == "Rearm") down |= step >= 55 && step < 58;
                    if (name == "Reattack") down |= step >= 80 && step < 83;
                    if (name == "SameFrameTap" || name == "StalledFrameTap") down = false;
                    actor.Hands.SubmitButtons(name != "RightTap" && down, (name == "RightTap" || name == "BothTap") && down,
                        (name == "SameFrameTap" || name == "StalledFrameTap") && step == 20, false, name == "StalledFrameTap" ? .2 : 1f / 60f);
                    if ((name == "MenuCancel" && step == 27) || (name == "Rearm" && step == 25)) actor.Hands.SetInputSuppressed(true);
                    if ((name == "MenuCancel" && step == 35) || (name == "Rearm" && step == 30)) actor.Hands.SetInputSuppressed(false);
                    if (name == "Reset" && step == 27) actor.Motor.ResetAt(new Vector3(0, .02f, 0));
                    if (name == "NoAuthority" && step == 18) actor.Motor.AuthorityEnabled = false;
                    if (name == "RemovedTarget" && step == 28) targetActor.gameObject.SetActive(false);
                    if (name == "Reattack" && step == 75) { target.Body.position = new Vector3(0, .02f, .52f); target.Body.linearVelocity = Vector3.zero; }
                    actor.Motor.Step(1f / 60f); actor.Hands.Step(1f / 60f); physics.Simulate(1f / 60f);
                    if (name != "NoPresentation")
                    {
                        actor.ApplyPresentation(1f / 60f);
                        for (var side = 0; side < 2; side++) if (actor.Hands.State.Phase(side) == HandIntentPhase.Punch)
                        {
                            Assert.That(actor.Punches.IsActive(side), Is.True);
                            result.maximumPresentationTimeError = Mathf.Max(result.maximumPresentationTimeError, Mathf.Abs(actor.Punches.PlaybackTime(side) - (float)actor.Hands.State.PunchAge(side)));
                        }
                    }
                    result.sawGrabSeek |= actor.Hands.State.Phase(0) == HandIntentPhase.GrabSeek;
                    result.maximumSpeed = Mathf.Max(result.maximumSpeed, Vector3.ProjectOnPlane(target.Body.linearVelocity, Vector3.up).magnitude);
                    result.maximumImpulse = Mathf.Max(result.maximumImpulse, target.LastImpulse);
                    result.maximumDisplacement = Mathf.Max(result.maximumDisplacement, Mathf.Abs(target.Body.position.z - (name == "Miss" ? 1.2f : .52f)));
                    if (step <= 28 && name != "SameFrameTap") Assert.That(target.HitCount, Is.Zero, name + " no hit on press or preparation");
                    samples.Add(new Sample { step = step, hits = target.HitCount, left = actor.Hands.State.Phase(0).ToString(), right = actor.Hands.State.Phase(1).ToString(), displacement = target.Body.position.z - .52f });
                    if ((name == "LeftTap" || name == "BothTap" || name == "Hold" || name == "Wall") && step >= 10 && step < 130)
                    {
                        var frame = Time.frameCount; yield return null; Assert.That(Time.frameCount, Is.GreaterThan(frame));
                        camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 10:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.punches = actor.Hands.State.PunchCount; result.hits = target.HitCount; result.samples = samples.ToArray(); results.Add(result);
                Assert.That(actor.Hands.QueryOverflowCount, Is.Zero);
                actor.Hands.ResetTargets();
                Assert.That(target.HitCount, Is.Zero);
                Assert.That(target.Body.linearVelocity.sqrMagnitude, Is.LessThan(1e-10f));
                Assert.That(Vector3.Distance(target.Body.position, new Vector3(0, .02f, name == "Miss" ? 1.2f : .52f)), Is.LessThan(.00001f));
                File.WriteAllText(Path.Combine(output, "HandStrikeReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; render.Release(); UnityEngine.Object.Destroy(render); UnityEngine.Object.Destroy(image);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
            foreach (var c in results)
            {
                var expected = c.name == "BothTap" || c.name == "Reattack" ? 2
                    : c.name == "LeftTap" || c.name == "RightTap" || c.name == "SameFrameTap" || c.name == "Rearm" || c.name == "NoPresentation" || c.name == "MultipleColliders" ? 1 : 0;
                Assert.That(c.hits, Is.EqualTo(expected), c.name + " hit count");
                Assert.That(c.maximumPresentationTimeError, Is.LessThan(.000001f), c.name + " authority clock");
                Assert.That(c.maximumImpulse, Is.LessThanOrEqualTo(20.0001f), c.name + " impulse bound");
                Assert.That(c.maximumSpeed, Is.LessThanOrEqualTo(2.001f), c.name + " bounded velocity change");
                if (expected > 0) Assert.That(c.maximumDisplacement, Is.GreaterThan(.005f), c.name + " real rigidbody response");
                if (c.name == "Hold") { Assert.That(c.sawGrabSeek, Is.True); Assert.That(c.punches, Is.Zero); }
            }
            File.WriteAllText(Path.Combine(output, "HandStrikeReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }
    }
}
