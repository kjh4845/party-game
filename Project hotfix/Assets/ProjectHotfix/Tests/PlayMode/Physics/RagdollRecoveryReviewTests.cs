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
    public sealed class RagdollRecoveryReviewTests
    {
        private Scene _scene;
        [Serializable] private sealed class Case { public string name, finalPhase, worstJoint; public int recoveries, aligns, interruptions; public float gap, speed, surfaceAngle, finalHeight; public uint downCount; public float handoff; public float readyAngle, readyDistance, readySpeed, readyAngular; public int worstStep, surfaceSamples; public Vector3 worstLocation; }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup() { if (!_scene.IsValid() || !_scene.isLoaded) yield break; var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null; }
        [UnityTest] public IEnumerator CandidateFourDirections_PhysicsAndSkinReview()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/ragdoll-recovery-20260906"));
            var output = Path.Combine(root, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "FaceUp", "FaceDown", "LeftSide", "RightSide", "Ceiling", "CeilingRemoved", "Reset", "SideGateLeft", "SideGateRight" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .65f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                var pose = (name == "FaceUp" || name == "Ceiling" || name == "CeilingRemoved" || name == "Reset") ? Quaternion.Euler(-90, 0, 0) : name == "FaceDown" ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(0, 0, name == "LeftSide" || name == "SideGateLeft" ? 90 : -90);
                actor.transform.rotation = Quaternion.Euler(0, 25, 0) * pose; actor.Motor.Body.rotation = actor.transform.rotation;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene); floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(20, .2f, 20);
                GameObject ceiling = null;
                if (name.StartsWith("Ceiling")) { ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ceiling, _scene); ceiling.transform.position = new Vector3(0, 1f, 0); ceiling.transform.localScale = new Vector3(4, .2f, 4); }
                UnityEngine.Physics.SyncTransforms();
                var rag = actor.gameObject.AddComponent<RagdollRecoveryReview>(); rag.RelaxedRagdoll = false; rag.UseGetUpAnimation = false; rag.Bind(actor); Assert.That(rag.BeginDown(Vector3.zero, Vector3.zero), Is.True); Assert.That(rag.TotalMass, Is.EqualTo(20).Within(.0001));
                Rigidbody[] seededBodies = null;
                if (name.StartsWith("SideGate"))
                {
                    seededBodies = _scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Rigidbody>()).ToArray();
                    var owned = seededBodies.SelectMany(b => b.GetComponents<Collider>()).Where(c => c.enabled).ToArray();
                    var shift = Vector3.up * (.005f - owned.Min(c => c.bounds.min.y));
                    foreach (var b in seededBodies) { b.transform.position += shift; b.position = b.transform.position; b.useGravity = false; }
                    UnityEngine.Physics.SyncTransforms(); rag.Down.State.Advance(1.5);
                }
                var cameraObj = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(cameraObj, _scene); var camera = cameraObj.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 1.0f; var center = new Vector3(0, .5f, 0); camera.transform.position = center + new Vector3(2.4f, 1.1f, 2.4f); camera.transform.LookAt(center);
                var lightObj = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObj, _scene); var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(45, -35, 0);
                var render = new RenderTexture(512, 512, 24); render.Create(); camera.targetTexture = render; var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
                var skin = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody")); var mesh = new Mesh(); var triangles = skin.sharedMesh.triangles; var pairs = Pairs(triangles);
                var result = new Case { name = name };
                for (var step = 0; step < 900; step++)
                {
                    if (name == "Reset" && step == 120) actor.Motor.ResetAt(new Vector3(0, .02f, 0));
                    if (name == "CeilingRemoved" && step == 300) { ceiling.SetActive(false); UnityEngine.Physics.SyncTransforms(); }
                    rag.Down.Step(1f / 60); rag.Step(1f / 60);
                    if (seededBodies != null && rag.AlignmentStarts > 0) foreach (var b in seededBodies) b.useGravity = true;
                    actor.Motor.Step(1f / 60); actor.Hands.Step(1f / 60); physics.Simulate(1f / 60);
                    if (rag.Active) rag.ApplyPose(); else actor.ApplyPresentation(1f / 60);
                    if ((rag.Active && step % 2 == 0) || step % 15 == 0)
                    {
                        skin.BakeMesh(mesh); var angle = Surface(mesh.vertices, triangles, pairs, out var location); result.surfaceSamples++; if (angle > result.surfaceAngle) { result.worstStep = step; result.worstLocation = location; } result.surfaceAngle = Mathf.Max(result.surfaceAngle, angle);
                        }
                    if ((name == "FaceUp" || name == "FaceDown" || name == "LeftSide" || name == "RightSide" || name.StartsWith("SideGate")) && step % 3 == 0 && step < 480)
                    {
                        yield return null; camera.Render(); var old = RenderTexture.active; RenderTexture.active = render; image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step / 3:D3}.png"), image.EncodeToPNG());
                    }
                }
                result.worstJoint = rag.WorstJoint; result.handoff = rag.MaximumHandoffBoneShift; result.readyAngle = rag.ReadyAngle; result.readyDistance = rag.ReadyDistance; result.readyAngular = rag.ReadyAngular; result.readySpeed = rag.ReadySpeed; result.finalPhase = rag.Down.State.Phase.ToString(); result.downCount = rag.Down.State.DownCount; result.recoveries = rag.Recoveries; result.aligns = rag.AlignmentStarts; result.interruptions = rag.Interruptions;
                result.gap = rag.MaximumJointGap; result.speed = rag.MaximumSpeed; result.finalHeight = actor.Motor.Body.position.y; results.Add(result);
                File.WriteAllText(Path.Combine(output, "RagdollReview.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; render.Release(); foreach (var obj in new UnityEngine.Object[] { render, image, mesh }) UnityEngine.Object.Destroy(obj);
                var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(root, "LatestReviewPath.txt"), output);
            foreach (var c in results) { Assert.That(c.recoveries, Is.EqualTo(c.name == "Ceiling" || c.name == "Reset" ? 0 : 1), c.name); Assert.That(c.handoff, Is.LessThan(.015f), c.name + " handoff"); Assert.That(c.downCount, Is.EqualTo(c.name == "Reset" ? 0 : 1)); if (c.name.StartsWith("SideGate")) Assert.That(c.aligns, Is.GreaterThan(0), c.name + " actual side alignment"); Assert.That(c.gap, Is.LessThan(.015f), c.name); Assert.That(c.speed, Is.LessThan(8), c.name); Assert.That(c.surfaceAngle, Is.LessThan(45), c.name); }
            File.WriteAllText(Path.Combine(output, "RagdollReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }
        [UnityTest] public IEnumerator RuntimeStartAndRepeatedStandingFallRestoreMovementAndReset()
        {
            _scene = SceneManager.CreateScene("Standing repeated recovery", new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
            var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
            actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene); floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(20, .2f, 20);
            var solver = actor.Motor.Body.solverIterations; var collision = actor.Motor.Body.collisionDetectionMode;
            var rag = actor.gameObject.AddComponent<RagdollRecoveryReview>(); rag.RelaxedRagdoll = false; rag.UseGetUpAnimation = false; yield return null;
            Assert.That(rag.Actor, Is.EqualTo(actor), "Runtime Start binds after scene creation");
            for (var episode = 0; episode < 2; episode++)
            {
                for (var i = 0; i < 60; i++)
                {
                    actor.Hands.SubmitButtons(false, false); actor.Motor.MoveInput = episode == 0 ? Vector2.zero : Vector2.up;
                    rag.Down.Step(1f / 60); rag.Step(1f / 60); actor.Motor.Step(1f / 60); actor.Hands.Step(1f / 60); physics.Simulate(1f / 60); actor.ApplyPresentation(1f / 60);
                }
                Assert.That(rag.BeginDown(Vector3.zero, actor.transform.right * .8f), Is.True);
                for (var i = 0; i < 900; i++)
                {
                    rag.Down.Step(1f / 60); rag.Step(1f / 60); actor.Motor.Step(1f / 60); actor.Hands.Step(1f / 60); physics.Simulate(1f / 60);
                    if (rag.Active) rag.ApplyPose(); else actor.ApplyPresentation(1f / 60);
                }
                Assert.That(rag.Recoveries, Is.EqualTo(episode + 1)); Assert.That(rag.Active, Is.False);
                Assert.That(actor.Motor.Body.mass, Is.EqualTo(20)); Assert.That(rag.TotalMass, Is.EqualTo(20));
                Assert.That(actor.Motor.Body.solverIterations, Is.EqualTo(solver)); Assert.That(actor.Motor.Body.collisionDetectionMode, Is.EqualTo(collision));
                Assert.That(actor.Motor.ActionsBlocked, Is.False); Assert.That(actor.Hands.Grabs.ThrowCount, Is.Zero);
            }
            actor.Motor.ResetAt(new Vector3(0, .02f, 0)); rag.Down.Step(1f / 60); rag.Step(1f / 60);
            Assert.That(rag.Down.State.DownCount, Is.Zero); Assert.That(actor.Motor.ActionsBlocked, Is.False);
        }
        private static List<Vector2Int> Pairs(int[] triangles)
        {
            var edges = new Dictionary<ulong, int>(); var pairs = new List<Vector2Int>();
            for (var face = 0; face < triangles.Length / 3; face++) for (var e = 0; e < 3; e++)
            { var a = triangles[face * 3 + e]; var b = triangles[face * 3 + (e + 1) % 3]; var key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b); if (edges.TryGetValue(key, out var other)) { pairs.Add(new Vector2Int(face, other)); edges.Remove(key); } else edges.Add(key, face); }
            return pairs;
        }
        private static float Surface(Vector3[] vertices, int[] triangles, List<Vector2Int> pairs, out Vector3 location)
        {
            var normals = new Vector3[triangles.Length / 3];
            for (var f = 0; f < normals.Length; f++) { var a = vertices[triangles[f * 3]]; var cross = Vector3.Cross(vertices[triangles[f * 3 + 1]] - a, vertices[triangles[f * 3 + 2]] - a); if (cross.sqrMagnitude <= 1e-30f) Assert.Fail("Degenerate surface triangle"); normals[f] = cross / cross.magnitude; }
            var min = 1f; var worst = 0; foreach (var pair in pairs) { var dot = Vector3.Dot(normals[pair.x], normals[pair.y]); if (dot < min) { min = dot; worst = pair.x; } } location = (vertices[triangles[worst * 3]] + vertices[triangles[worst * 3 + 1]] + vertices[triangles[worst * 3 + 2]]) / 3; return Mathf.Acos(Mathf.Clamp(min, -1, 1)) * Mathf.Rad2Deg;
        }
    }
}
