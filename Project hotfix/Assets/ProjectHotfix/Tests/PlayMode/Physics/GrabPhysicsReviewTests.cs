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
    public sealed class GrabPhysicsReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample { public int step, holding, hits; public float stress, targetTravel; }
        [Serializable] private sealed class Case
        {
            public string name, releaseReason;
            public int worstSurfaceStep;
            public Vector3 worstSurfaceLocation;
            public int acquisitions, releases, breaks, punches, hits, maximumHeld, surfaceSamples, hardEdges, foldedEdges;
            public float maximumHandError, maximumReachCorrection, maximumSpeed, targetTravel, maximumForce, maximumStress, surfaceAngle, releaseVelocityChange;
            public bool differentTargets, simultaneousGrabPunch, airborneWhileHolding;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator GrabContact_ConstraintsOwnershipStressAndRelease()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/grab-physics-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "HoldLeft", "HoldRight", "Both", "Drag", "GrabPunch", "Jump", "SplitTargets", "Wall", "Miss", "Menu", "Reset", "TargetDisabled", "ColliderDisabled", "TargetRemoved", "TargetReset", "HandOccupied", "ReachBreak", "ForceBreak", "StressBreak", "IdleHold", "NoPresentation", "Prop" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None; actor.EnableGrabReview();
                actor.Hands.Grabs.ThrowOnButtonRelease = false; // Isolate the historical constraint/jump/explicit-lift baseline.
                var targets = new List<StrikeReviewTarget>();
                foreach (var x in name == "SplitTargets" ? new[] { -.24f, .24f } : new[] { 0f })
                {
                    if (name == "Prop")
                    {
                        var prop = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(prop, _scene);
                        prop.transform.position = new Vector3(0, .6f, .4f); prop.transform.localScale = new Vector3(.28f, .2f, .2f);
                        prop.AddComponent<Rigidbody>().mass = 2f; var propTarget = prop.AddComponent<StrikeReviewTarget>(); propTarget.GrabEligible = true; targets.Add(propTarget);
                        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(pedestal, _scene);
                        pedestal.transform.position = new Vector3(0, .25f, .4f); pedestal.transform.localScale = new Vector3(.28f, .5f, .2f);
                        continue;
                    }
                    var a = PhysicsJumpReview.Create(new Vector3(x, .02f, name == "Miss" ? 1.2f : name == "SplitTargets" ? .42f : .46f));
                    SceneManager.MoveGameObjectToScene(a.gameObject, _scene); a.ReadKeyboard = false; a.enabled = false; a.Motor.ManualStep = true; a.Motor.AuthorityEnabled = false;
                    a.Motor.Body.interpolation = RigidbodyInterpolation.None;
                    foreach (var skin in a.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
                    var t = a.gameObject.AddComponent<StrikeReviewTarget>(); t.GrabEligible = true; targets.Add(t);
                }
                actor.Hands.ReviewTargets = targets.ToArray(); var target = targets[0]; var initial = target.Body.position;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                if (name == "Wall")
                {
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, _scene);
                    wall.transform.position = new Vector3(0, .55f, .245f); wall.transform.localScale = new Vector3(.7f, 1.1f, .015f);
                }
                var camObj = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(camObj, _scene); var camera = camObj.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .9f;
                var center = new Vector3(0, .58f, .45f); camera.transform.position = center + new Vector3(3, .4f, 1); camera.transform.LookAt(center);
                var lightObj = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObj, _scene); var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 2f; light.transform.rotation = Quaternion.Euler(40, -35, 0);
                var render = new RenderTexture(640, 480, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(640, 480, TextureFormat.RGB24, false);
                var skinBody = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
                var mesh = new Mesh(); var triangles = skinBody.sharedMesh.triangles; var pairs = EdgePairs(triangles);
                var forearm = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Forearm_L");
                var result = new Case { name = name }; var samples = new List<Sample>();
                var steps = name == "IdleHold" ? 600 : name == "StressBreak" ? 600 : 210;
                for (var step = 0; step < steps; step++)
                {
                    var down = step >= 20 && (step < 130 || name == "StressBreak" || name == "IdleHold");
                    var right = (name == "HoldRight" || name == "Both" || name == "SplitTargets") && down;
                    if (name == "GrabPunch") right = step >= 70 && step < 73;
                    actor.Hands.SubmitButtons(name != "HoldRight" && down, right);
                    actor.Motor.MoveInput = (name == "Drag" || name == "NoPresentation") && step >= 65 && step < 120 ? Vector2.up : Vector2.zero;
                    if (name == "Jump" && step == 65) actor.Motor.RequestJump();
                    if (step == 60)
                    {
                        if (name == "Menu") actor.Hands.SetInputSuppressed(true);
                        if (name == "Reset") actor.Motor.ResetAt(new Vector3(0, .02f, 0));
                        if (name == "TargetDisabled") target.gameObject.SetActive(false);
                        if (name == "ColliderDisabled") { target.Body.useGravity = false; target.GetComponent<Collider>().enabled = false; }
                        if (name == "TargetRemoved") { UnityEngine.Object.Destroy(target.gameObject); yield return null; }
                        if (name == "TargetReset") target.ResetReview();
                        if (name == "ReachBreak") target.Body.position += Vector3.forward * .7f;
                        if (name == "ForceBreak") target.Body.linearVelocity = Vector3.forward * 8f;
                        if (name == "StressBreak") { actor.Motor.Body.constraints = RigidbodyConstraints.FreezeAll; target.Body.constraints = RigidbodyConstraints.FreezeRotation; }
                    }
                    if (name == "StressBreak" && step >= 60 && actor.Hands.Grabs.IsHolding(0)) target.Body.AddForce(Vector3.forward * 200f, ForceMode.Force);
                    var external = forearm.localRotation;
                    if (name == "HandOccupied" && step >= 60)
                    { actor.Arms.LeftAvailable = false; forearm.localRotation = Quaternion.Euler(12, 5, 3); external = forearm.localRotation; }
                    actor.Motor.Step(1f / 60f);
                    var beforeRelease = target == null ? Vector3.zero : target.Body.linearVelocity;
                    actor.Hands.Step(1f / 60f);
                    if (step == 130 && target != null) result.releaseVelocityChange = Vector3.Distance(beforeRelease, target.Body.linearVelocity);
                    physics.Simulate(1f / 60f); if (name != "NoPresentation") actor.ApplyPresentation(1f / 60f);
                    if (name == "HandOccupied" && step >= 60) Assert.That(forearm.localRotation, Is.EqualTo(external));
                    var held = (actor.Hands.Grabs.IsHolding(0) ? 1 : 0) + (actor.Hands.Grabs.IsHolding(1) ? 1 : 0);
                    result.maximumHeld = Mathf.Max(result.maximumHeld, held);
                    result.airborneWhileHolding |= held > 0 && !actor.Motor.Grounded;
                    if (name == "SplitTargets" && held == 2) result.differentTargets |= actor.Hands.Grabs.Target(0) != actor.Hands.Grabs.Target(1);
                    if (name == "GrabPunch") result.simultaneousGrabPunch |= actor.Hands.Grabs.IsHolding(0) && actor.Punches.IsActive(1);
                    result.maximumHandError = Mathf.Max(result.maximumHandError, actor.GrabPose.MaximumHeldTargetError);
                    if (held > 0) result.maximumReachCorrection = Mathf.Max(result.maximumReachCorrection, actor.GrabPose.MaximumReachCorrection);
                    result.maximumSpeed = Mathf.Max(result.maximumSpeed, actor.Motor.Body.linearVelocity.magnitude);
                    if (target != null)
                    {
                        result.maximumSpeed = Mathf.Max(result.maximumSpeed, target.Body.linearVelocity.magnitude);
                        result.targetTravel = Mathf.Max(result.targetTravel, Vector3.ProjectOnPlane(target.Body.position - initial, Vector3.up).magnitude);
                    }
                    if (((name == "HoldLeft" || name == "GrabPunch") && ((step >= 25 && step <= 48) || (step >= 60 && step <= 120 && step % 10 == 0) || (step >= 130 && step <= 150)))
                        || (name == "Drag" && step >= 65 && step <= 150) || (name == "Jump" && step >= 65 && step <= 110)
                        || ((name == "HoldRight" || name == "Both" || name == "SplitTargets" || name == "Prop") && step >= 45 && step <= 125 && step % 20 == 5))
                    { skinBody.BakeMesh(mesh); var oldAngle = result.surfaceAngle; Surface(mesh.vertices, triangles, pairs, result); if (result.surfaceAngle > oldAngle) result.worstSurfaceStep = step; result.surfaceSamples++; }
                    samples.Add(new Sample { step = step, holding = held, hits = target == null ? 0 : target.HitCount,
                        stress = Mathf.Max(actor.Hands.Grabs.Stress(0), actor.Hands.Grabs.Stress(1)), targetTravel = result.targetTravel });
                    if ((name == "Drag" || name == "GrabPunch" || name == "Jump" || name == "Both") && step >= 30 && step < 150)
                    {
                        var frame = Time.frameCount; yield return null; Assert.That(Time.frameCount, Is.GreaterThan(frame));
                        camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 30:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.acquisitions = actor.Hands.Grabs.Acquisitions; result.releases = actor.Hands.Grabs.Releases; result.breaks = actor.Hands.Grabs.Breaks;
                result.maximumForce = actor.Hands.Grabs.MaximumObservedForce; result.maximumStress = actor.Hands.Grabs.MaximumObservedStress;
                result.releaseReason = actor.Hands.Grabs.LastReleaseReason; result.punches = actor.Hands.State.PunchCount; result.hits = target == null ? 0 : target.HitCount;
                result.samples = samples.ToArray(); results.Add(result);
                actor.Hands.SetInputSuppressed(true);
                Assert.That(actor.Hands.Grabs.IsHolding(0) || actor.Hands.Grabs.IsHolding(1), Is.False);
                Assert.That(actor.Motor.ExternalLoadAccelerationScale, Is.EqualTo(1f));
                File.WriteAllText(Path.Combine(output, "GrabPhysicsReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; render.Release(); foreach (var obj in new UnityEngine.Object[] { render, image, mesh }) UnityEngine.Object.Destroy(obj);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
            foreach (var c in results)
            {
                Assert.That(c.acquisitions, Is.EqualTo(c.name == "Wall" || c.name == "Miss" ? 0 : c.name == "Both" || c.name == "SplitTargets" ? 2 : 1), c.name + " relation count");
                Assert.That(c.maximumHandError, Is.LessThan(.015f), c.name + " held terminal contact");
                Assert.That(c.maximumReachCorrection, Is.LessThan(.003f), c.name + " arm stretch");
                Assert.That(c.maximumSpeed, Is.LessThan(12f), c.name + " solver stability");
                Assert.That(c.releaseVelocityChange, Is.LessThan(.00001f), c.name + " no synthesized throw impulse");
                Assert.That(c.hardEdges, Is.Zero, c.name + " sharp surface"); Assert.That(c.foldedEdges, Is.Zero, c.name + " folded surface");
                Assert.That(c.punches, Is.EqualTo(c.name == "GrabPunch" ? 1 : 0), c.name + " no pre-grab punch");
                if (c.name == "GrabPunch") { Assert.That(c.simultaneousGrabPunch, Is.True); Assert.That(c.hits, Is.EqualTo(1)); }
                if (c.name == "SplitTargets") Assert.That(c.differentTargets, Is.True);
                if (c.name == "Jump") { Assert.That(c.airborneWhileHolding, Is.True); Assert.That(c.breaks, Is.Zero); }
                if (c.name == "Drag") Assert.That(c.targetTravel, Is.GreaterThan(.04f));
                if (c.name == "IdleHold") { Assert.That(c.breaks, Is.Zero); Assert.That(c.maximumStress, Is.Zero); }
                if (c.name == "StressBreak") { Assert.That(c.releaseReason, Is.EqualTo("GripStress")); Assert.That(c.maximumStress, Is.EqualTo(1f)); }
                if (c.name == "ForceBreak" || c.name == "ReachBreak") Assert.That(c.breaks, Is.EqualTo(1));
            }
            Assert.That(Mathf.Abs(results.Single(c => c.name == "Drag").targetTravel - results.Single(c => c.name == "NoPresentation").targetTravel), Is.LessThan(.0001f), "Grab physics must not depend on animation");
            File.WriteAllText(Path.Combine(output, "GrabPhysicsReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }

        private static List<Vector2Int> EdgePairs(int[] triangles)
        {
            var pending = new Dictionary<ulong, int>(); var pairs = new List<Vector2Int>();
            for (var f = 0; f < triangles.Length / 3; f++) for (var e = 0; e < 3; e++)
            {
                var a = triangles[f * 3 + e]; var b = triangles[f * 3 + (e + 1) % 3]; var key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (pending.TryGetValue(key, out var other)) { pairs.Add(new Vector2Int(other, f)); pending.Remove(key); } else pending.Add(key, f);
            }
            Assert.That(pending.Count, Is.Zero); return pairs;
        }
        private static void Surface(Vector3[] vertices, int[] triangles, List<Vector2Int> pairs, Case result)
        {
            var normals = new Vector3[triangles.Length / 3];
            for (var f = 0; f < normals.Length; f++)
            {
                var a = vertices[triangles[f * 3]]; var cross = Vector3.Cross(vertices[triangles[f * 3 + 1]] - a, vertices[triangles[f * 3 + 2]] - a);
                var length = cross.magnitude; if (length <= 1e-20f) Assert.Fail("Degenerate triangle"); normals[f] = cross / length;
            }
            var min = 1f; var worst = 0;
            foreach (var pair in pairs) { var dot = Vector3.Dot(normals[pair.x], normals[pair.y]); if (dot < min) { min = dot; worst = pair.x; } if (dot < .7071067f) result.hardEdges++; if (dot < 0) result.foldedEdges++; }
            var angle = Mathf.Acos(Mathf.Clamp(min, -1, 1)) * Mathf.Rad2Deg;
            if (angle > result.surfaceAngle) { result.surfaceAngle = angle; result.worstSurfaceLocation = (vertices[triangles[worst * 3]] + vertices[triangles[worst * 3 + 1]] + vertices[triangles[worst * 3 + 2]]) / 3f; }
        }
    }
}
