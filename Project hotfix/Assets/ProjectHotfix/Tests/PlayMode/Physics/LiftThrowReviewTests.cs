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
    public sealed class LiftThrowReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample { public int step, holding, throws; public float lift, height; }
        [Serializable] private sealed class Case
        {
            public string name;
            public int worstSurfaceStep;
            public Vector3 worstSurfaceLocation;
            public int throws, blocks, surfaceSamples, hardEdges, foldedEdges;
            public float maximumLift, maximumHeight, maximumSpeed, maximumHandError, maximumReachCorrection, maximumSurfaceAngle, lastImpulse, finalDistance;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator ExplicitLiftThrow_PhysicsBoundsCancellationAndPose()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/lift-throw-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "LiftLower", "ThrowLeft", "ThrowRight", "BothThrow", "AddSecondHand", "NormalDrop", "TooEarly", "Menu", "Reset", "TargetLost", "OwnerLost", "Ceiling", "LightProp", "Heavy", "NoPresentation", "MovingThrow" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableGrabReview();
                actor.Hands.Grabs.ThrowOnButtonRelease = false; // Isolate the historical constraint/jump/explicit-lift baseline.
                var targetActor = PhysicsJumpReview.Create(new Vector3(0, .02f, .46f)); SceneManager.MoveGameObjectToScene(targetActor.gameObject, _scene);
                targetActor.ReadKeyboard = false; targetActor.enabled = false; targetActor.Motor.ManualStep = true; targetActor.Motor.AuthorityEnabled = false;
                targetActor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                var target = targetActor.gameObject.AddComponent<StrikeReviewTarget>(); target.GrabEligible = true;
                if (name == "LightProp") target.Body.mass = 2f;
                if (name == "Heavy") target.Body.mass = 100f;
                actor.Hands.ReviewTargets = new[] { target };
                foreach (var skin in targetActor.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                if (name == "Ceiling")
                {
                    var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ceiling, _scene);
                    ceiling.transform.position = new Vector3(0, 1.13f, .46f); ceiling.transform.localScale = new Vector3(.6f, .1f, .5f);
                }
                var camObj = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(camObj, _scene); var camera = camObj.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .95f;
                var center = new Vector3(0, .6f, .5f); camera.transform.position = center + new Vector3(3, .4f, 1); camera.transform.LookAt(center);
                var lightObj = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObj, _scene); var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
                light.transform.rotation = Quaternion.Euler(40, -35, 0);
                var render = new RenderTexture(640, 480, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(640, 480, TextureFormat.RGB24, false);
                var skinBody = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody")); var mesh = new Mesh();
                var triangles = skinBody.sharedMesh.triangles; var pairs = EdgePairs(triangles);
                var result = new Case { name = name }; var samples = new List<Sample>(); var baseline = 0f;
                for (var step = 0; step < 240; step++)
                {
                    var hold = step >= 20 && step < 130;
                    actor.Hands.SubmitButtons(hold && name != "ThrowRight", hold && (name == "ThrowRight" || name == "BothThrow" || (name == "AddSecondHand" && step >= 85)));
                    actor.Motor.MoveInput = name == "MovingThrow" && step >= 70 && step < 110 ? Vector2.up : Vector2.zero;
                    if (step == 40) baseline = target.Body.position.y;
                    if (step == 50) actor.Hands.Grabs.SetLiftRequested(true);
                    if ((name == "LiftLower" || name == "NormalDrop") && step == 95) actor.Hands.Grabs.SetLiftRequested(false);
                    if (name == "TooEarly" && step == 51) Assert.That(actor.Hands.Grabs.TryThrow(0), Is.False);
                    if (step == 85)
                    {
                        if (name == "Menu") actor.Hands.SetInputSuppressed(true);
                        if (name == "Reset") { actor.Motor.ResetAt(new Vector3(0, .02f, 0)); Assert.That(actor.Hands.Grabs.TryThrow(0), Is.False); actor.Hands.ResetTargets(); }
                        if (name == "TargetLost") target.Eligible = false;
                        if (name == "OwnerLost") actor.Arms.LeftAvailable = false;
                        if (name == "Menu" || name == "Reset" || name == "TargetLost" || name == "OwnerLost") Assert.That(actor.Hands.Grabs.TryThrow(0), Is.False);
                    }
                    if (step == 105 && (name == "ThrowLeft" || name == "ThrowRight" || name == "BothThrow" || name == "LightProp" || name == "Heavy" || name == "NoPresentation" || name == "MovingThrow"))
                    {
                        var accepted = actor.Hands.Grabs.TryThrow(name == "ThrowRight" ? 1 : 0);
                        if (name != "Heavy") Assert.That(accepted, Is.True, name + " explicit throw");
                        Assert.That(actor.Hands.Grabs.TryThrow(0) || actor.Hands.Grabs.TryThrow(1), Is.False, "No duplicate or second-hand impulse");
                    }
                    actor.Motor.Step(1f / 60f); actor.Hands.Step(1f / 60f); physics.Simulate(1f / 60f);
                    if (name != "NoPresentation") actor.ApplyPresentation(1f / 60f);
                    result.maximumLift = Mathf.Max(result.maximumLift, actor.Hands.Grabs.LiftAmount(0), actor.Hands.Grabs.LiftAmount(1));
                    if (step >= 50) result.maximumHeight = Mathf.Max(result.maximumHeight, target.Body.position.y - baseline);
                    result.maximumSpeed = Mathf.Max(result.maximumSpeed, target.Body.linearVelocity.magnitude, actor.Motor.Body.linearVelocity.magnitude);
                    result.maximumHandError = Mathf.Max(result.maximumHandError, actor.GrabPose.MaximumHeldTargetError);
                    result.maximumReachCorrection = Mathf.Max(result.maximumReachCorrection, actor.GrabPose.MaximumReachCorrection);
                    if ((name == "LiftLower" || name == "ThrowLeft" || name == "BothThrow" || name == "MovingThrow") && step >= 50 && step <= 140 && step % 2 == 0)
                    { skinBody.BakeMesh(mesh); var previousAngle = result.maximumSurfaceAngle; Surface(mesh.vertices, triangles, pairs, result); if (result.maximumSurfaceAngle > previousAngle) result.worstSurfaceStep = step; result.surfaceSamples++; }
                    samples.Add(new Sample { step = step, holding = (actor.Hands.Grabs.IsHolding(0) ? 1 : 0) + (actor.Hands.Grabs.IsHolding(1) ? 1 : 0),
                        throws = actor.Hands.Grabs.ThrowCount, lift = actor.Hands.Grabs.LiftAmount(0), height = target.Body.position.y - baseline });
                    if ((name == "LiftLower" || name == "ThrowLeft" || name == "BothThrow" || name == "Ceiling") && step >= 40 && step < 160)
                    {
                        var frame = Time.frameCount; yield return null; Assert.That(Time.frameCount, Is.GreaterThan(frame));
                        camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 40:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.throws = actor.Hands.Grabs.ThrowCount; result.blocks = actor.Hands.Grabs.LiftBlockedCount;
                result.lastImpulse = actor.Hands.Grabs.LastThrowImpulse; result.finalDistance = target.Body.position.z - .46f; result.samples = samples.ToArray(); results.Add(result);
                Assert.That(target.HitCount, Is.Zero); actor.Hands.SetInputSuppressed(true); Assert.That(actor.Hands.Grabs.IsHolding(0) || actor.Hands.Grabs.IsHolding(1), Is.False);
                File.WriteAllText(Path.Combine(output, "LiftThrowReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; render.Release(); foreach (var obj in new UnityEngine.Object[] { image, render, mesh }) UnityEngine.Object.Destroy(obj);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
            foreach (var c in results)
            {
                var expected = c.name == "ThrowLeft" || c.name == "ThrowRight" || c.name == "BothThrow" || c.name == "LightProp" || c.name == "NoPresentation" || c.name == "MovingThrow";
                if (c.name != "Heavy") Assert.That(c.throws, Is.EqualTo(expected ? 1 : 0), c.name);
                Assert.That(c.maximumHandError, Is.LessThan(.015f), c.name + " contact"); Assert.That(c.maximumReachCorrection, Is.LessThan(.003f), c.name + " reach");
                Assert.That(c.maximumSpeed, Is.LessThan(8f), c.name + " stable physics"); Assert.That(c.lastImpulse, Is.LessThanOrEqualTo(30.0001f));
                Assert.That(c.hardEdges, Is.Zero, c.name + " sharp surface"); Assert.That(c.foldedEdges, Is.Zero, c.name + " folded surface");
                if (c.name == "LiftLower") Assert.That(c.maximumHeight, Is.GreaterThan(.08f));
                if (c.name == "Ceiling") { Assert.That(c.blocks, Is.GreaterThan(0)); Assert.That(c.maximumHeight, Is.LessThan(.09f)); }
            }
            Assert.That(Mathf.Abs(results.Single(c => c.name == "ThrowLeft").finalDistance - results.Single(c => c.name == "NoPresentation").finalDistance), Is.LessThan(.0001f));
            Assert.That(results.Single(c => c.name == "MovingThrow").finalDistance, Is.GreaterThan(results.Single(c => c.name == "ThrowLeft").finalDistance + .1f), "Moving momentum must remain in the throw");
            Assert.That(results.Single(c => c.name == "LightProp").lastImpulse, Is.LessThanOrEqualTo(6.0001f));
            File.WriteAllText(Path.Combine(output, "LiftThrowReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }

        private static List<Vector2Int> EdgePairs(int[] triangles)
        {
            var pending = new Dictionary<ulong, int>(); var pairs = new List<Vector2Int>();
            for (var f = 0; f < triangles.Length / 3; f++) for (var e = 0; e < 3; e++)
            { var a = triangles[f * 3 + e]; var b = triangles[f * 3 + (e + 1) % 3]; var key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b); if (pending.TryGetValue(key, out var other)) { pairs.Add(new Vector2Int(other, f)); pending.Remove(key); } else pending.Add(key, f); }
            Assert.That(pending.Count, Is.Zero); return pairs;
        }
        private static void Surface(Vector3[] vertices, int[] triangles, List<Vector2Int> pairs, Case result)
        {
            var normals = new Vector3[triangles.Length / 3];
            for (var f = 0; f < normals.Length; f++) { var a = vertices[triangles[f * 3]]; var cross = Vector3.Cross(vertices[triangles[f * 3 + 1]] - a, vertices[triangles[f * 3 + 2]] - a); var length = cross.magnitude; if (length <= 1e-20f) Assert.Fail("Degenerate triangle"); normals[f] = cross / length; }
            var min = 1f; var worst = 0; foreach (var pair in pairs) { var dot = Vector3.Dot(normals[pair.x], normals[pair.y]); if (dot < min) { min = dot; worst = pair.x; } if (dot < .7071067f) result.hardEdges++; if (dot < 0) result.foldedEdges++; }
            var angle = Mathf.Acos(Mathf.Clamp(min, -1, 1)) * Mathf.Rad2Deg;
            if (angle > result.maximumSurfaceAngle) { result.maximumSurfaceAngle = angle; result.worstSurfaceLocation = (vertices[triangles[worst * 3]] + vertices[triangles[worst * 3 + 1]] + vertices[triangles[worst * 3 + 2]]) / 3f; }
        }
    }
}
