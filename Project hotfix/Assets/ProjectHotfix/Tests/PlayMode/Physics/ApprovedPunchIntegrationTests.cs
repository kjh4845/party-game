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
    public sealed class ApprovedPunchIntegrationTests
    {
        private Scene _scene;
        [Serializable] private sealed class Case
        {
            public string name;
            public float maximumLegDifference, maximumBodyVelocityDifference, maximumSurfaceAngle;
            public float maximumGroundPenetration, maximumFootReachCorrection, endArmDifference;
            public int surfaceSamples, hardEdges, foldedEdges;
        }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator ApprovedSource_ArmHeadsAndTailsMatchUnitySamples()
        {
            _scene = SceneManager.CreateScene("PunchParity", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actor = PhysicsJumpReview.Create(Vector3.zero);
            SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
            actor.ReadKeyboard = false; actor.Motor.ManualStep = true;
            Assert.That(actor.PunchLibrary, Is.Not.Null, "Run the approved punch exporter.");
            var nodes = actor.GetComponentsInChildren<Transform>();
            var maximum = 0f;
            foreach (var clip in actor.PunchLibrary.clips)
            {
                var bones = clip.bones.Select(name => nodes.Single(t => t.name == name)).ToArray();
                foreach (var frame in clip.frames)
                {
                    for (var bone = 0; bone < 4; bone++) bones[bone].localRotation = clip.Sample(frame.time, bone);
                    for (var bone = 0; bone < 4; bone++)
                    {
                        var head = actor.Pose.transform.InverseTransformPoint(bones[bone].position);
                        var length = Vector3.Distance(frame.heads[bone], frame.tails[bone]);
                        var tail = actor.Pose.transform.InverseTransformPoint(bones[bone].TransformPoint(Vector3.back * length));
                        maximum = Mathf.Max(maximum, Vector3.Distance(head, frame.heads[bone]), Vector3.Distance(tail, frame.tails[bone]));
                    }
                }
            }
            Assert.That(maximum, Is.LessThan(.00001f), "Blender to Unity arm pose parity");
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/punch-unity-20260906/Parity.json"));
            File.WriteAllText(path, "{\"status\":\"PASS\",\"samples\":170,\"maximumLandmarkErrorMeters\":" + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}");
            yield return null;
        }

        [UnityTest] public IEnumerator Punches_ComposeWithLocomotionAirOwnershipAndReset()
        {
            var baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/punch-unity-20260906"));
            var output = Path.Combine(baseDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            var results = new List<Case>();
            foreach (var name in new[] { "IdleLeft", "WalkLeft", "SprintRight", "WalkBoth", "AirContinue", "OccupiedRight", "OwnershipTaken", "Reset" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0));
                var control = PhysicsJumpReview.Create(new Vector3(3, .02f, 0));
                foreach (var a in new[] { actor, control })
                {
                    SceneManager.MoveGameObjectToScene(a.gameObject, _scene); a.ReadKeyboard = false; a.Motor.ManualStep = true;
                    a.Motor.Body.interpolation = RigidbodyInterpolation.None;
                    a.Motor.SprintRequested = name == "SprintRight";
                }
                Assert.That(actor.Punches, Is.Not.Null);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMaterial.SetColor("_BaseColor", new Color(.13f, .20f, .28f));
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var lightObject = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObject, _scene);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.5f; light.transform.rotation = Quaternion.Euler(45, -35, 0);
                var cameraObject = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .72f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f);
                var target = new RenderTexture(512, 576, 24); target.Create(); camera.targetTexture = target;
                var image = new Texture2D(512, 576, TextureFormat.RGB24, false);
                var nodes = actor.GetComponentsInChildren<Transform>(); var reference = control.GetComponentsInChildren<Transform>();
                var legs = nodes.Where(t => t.name.StartsWith("Thigh_") || t.name.StartsWith("Calf_") || t.name.StartsWith("Foot_")).ToArray();
                var otherLegs = legs.Select(b => reference.Single(t => t.name == b.name)).ToArray();
                var arms = nodes.Where(t => t.name.StartsWith("Clavicle_") || t.name.StartsWith("UpperArm_") || t.name.StartsWith("Forearm_") || t.name.StartsWith("HandLogical_")).ToArray();
                var idle = arms.Select(t => t.localRotation).ToArray();
                var right = arms.Where(t => t.name.EndsWith("_R")).ToArray(); var rightIdle = right.Select(t => t.localRotation).ToArray();
                var leftForearm = nodes.Single(t => t.name == "Forearm_L"); var externalPose = leftForearm.localRotation * Quaternion.Euler(15, 3, 0);
                if (name == "OccupiedRight") actor.Arms.RightAvailable = control.Arms.RightAvailable = false;
                var skin = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
                var mesh = new Mesh(); var triangles = skin.sharedMesh.triangles; var pairs = EdgePairs(triangles);
                var result = new Case { name = name };
                for (var step = 0; step < 210; step++)
                {
                    foreach (var a in new[] { actor, control })
                    {
                        a.Motor.MoveInput = name != "IdleLeft" && step >= 15 && step < 135 ? Vector2.up : Vector2.zero;
                        if (name == "AirContinue" && step == 40) a.Motor.RequestJump();
                        if (name == "Reset" && step == 45) a.Motor.ResetAt(new Vector3(a == actor ? 0 : 3, .02f, 0));
                        a.Motor.Step(1f / 60f);
                    }
                    physics.Simulate(1f / 60f);
                    if (step == 30)
                    {
                        Assert.That(actor.TryPreviewPunch(name == "SprintRight" ? 1 : 0), Is.True, name);
                        Assert.That(actor.TryPreviewPunch(name == "SprintRight" ? 1 : 0), Is.False, "No restart during active punch");
                        if (name == "WalkBoth") Assert.That(actor.TryPreviewPunch(1), Is.True);
                        if (name == "OccupiedRight") Assert.That(actor.TryPreviewPunch(1), Is.False);
                    }
                    if (name == "OwnershipTaken" && step >= 45) { actor.Arms.LeftAvailable = false; leftForearm.localRotation = externalPose; externalPose = leftForearm.localRotation; }
                    actor.ApplyPresentation(1f / 60f); control.ApplyPresentation(1f / 60f);
                    if (step == 35 || step == 43)
                    {
                        var hand = name == "SprintRight" ? "R" : "L";
                        var shoulder = nodes.Single(t => t.name == "UpperArm_" + hand);
                        var elbow = nodes.Single(t => t.name == "Forearm_" + hand);
                        var wrist = nodes.Single(t => t.name == "HandLogical_" + hand);
                        var bend = Vector3.Angle(elbow.position - shoulder.position, wrist.position - elbow.position);
                        if (step == 35) Assert.That(bend, Is.GreaterThan(100f), name + " approved 0.10 s chamber");
                        else Assert.That(bend, Is.LessThan(25f), name + " approved extension");
                    }
                    if (name == "AirContinue" && !actor.Motor.Grounded) Assert.That(actor.TryPreviewPunch(1), Is.False, "No new ground punch in air");
                    if (name == "OwnershipTaken" && step >= 45)
                    { Assert.That(actor.Punches.IsActive(0), Is.False); Assert.That(leftForearm.localRotation, Is.EqualTo(externalPose)); }
                    if (name == "Reset" && step == 45) Assert.That(actor.Punches.IsActive(0), Is.False);
                    if (name == "OccupiedRight") for (var i = 0; i < right.Length; i++) Assert.That(right[i].localRotation, Is.EqualTo(rightIdle[i]));
                    for (var i = 0; i < legs.Length; i++) result.maximumLegDifference = Mathf.Max(result.maximumLegDifference, Quaternion.Angle(legs[i].localRotation, otherLegs[i].localRotation));
                    result.maximumBodyVelocityDifference = Mathf.Max(result.maximumBodyVelocityDifference, Vector3.Distance(actor.Motor.Body.linearVelocity, control.Motor.Body.linearVelocity));
                    result.maximumFootReachCorrection = Mathf.Max(result.maximumFootReachCorrection, actor.Pose.MaximumReachCorrection);
                    if (step % 3 == 0 || ((name == "WalkLeft" || name == "SprintRight") && step >= 30 && step <= 72))
                    {
                        skin.BakeMesh(mesh); var vertices = mesh.vertices;
                        if (actor.Motor.Grounded) result.maximumGroundPenetration = Mathf.Max(result.maximumGroundPenetration, -vertices.Min(v => skin.transform.TransformPoint(v).y));
                        if (step >= 30 && step <= 72 && name != "OwnershipTaken") { Surface(vertices, triangles, pairs, result); result.surfaceSamples++; }
                    }
                    var poseFrame = Time.frameCount; yield return null;
                    if (step >= 15 && step < 135)
                    {
                        Assert.That(Time.frameCount, Is.GreaterThan(poseFrame));
                        var center = actor.transform.position + new Vector3(0, .5f, .06f);
                        camera.transform.position = center + new Vector3(2.8f, .35f, 2f); camera.transform.LookAt(center);
                        camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, 512, 576), 0, 0); image.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(output, $"{name}_{step - 15:D3}.png"), image.EncodeToPNG());
                    }
                }
                if (name != "OwnershipTaken") for (var i = 0; i < arms.Length; i++) result.endArmDifference = Mathf.Max(result.endArmDifference, Quaternion.Angle(arms[i].localRotation, idle[i]));
                Assert.That(actor.Punches.IsActive(0) || actor.Punches.IsActive(1), Is.False);
                results.Add(result);
                File.WriteAllText(Path.Combine(output, "PunchIntegration.json"), JsonUtility.ToJson(new Report { status = "OBSERVED", cases = results.ToArray() }, true));
                camera.targetTexture = null; target.Release();
                foreach (var obj in new UnityEngine.Object[] { image, target, mesh, floorMaterial }) UnityEngine.Object.Destroy(obj);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir, "LatestReviewPath.txt"), output);
            foreach (var c in results)
            {
                Assert.That(c.maximumLegDifference, Is.LessThan(.06f), c.name + " leg ownership");
                Assert.That(c.maximumBodyVelocityDifference, Is.LessThan(.0001f), c.name + " no physical impulse");
                Assert.That(c.maximumGroundPenetration, Is.LessThan(.008f), c.name + " ground");
                Assert.That(c.maximumFootReachCorrection, Is.LessThan(.003f), c.name + " reach");
                Assert.That(c.endArmDifference, Is.LessThan(.25f), c.name + " idle return");
                Assert.That(c.hardEdges, Is.Zero, c.name + " sharp surface"); Assert.That(c.foldedEdges, Is.Zero, c.name + " folded surface");
            }
            File.WriteAllText(Path.Combine(output, "PunchIntegration.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = results.ToArray() }, true));
        }

        private static List<Vector2Int> EdgePairs(int[] triangles)
        {
            var pending = new Dictionary<ulong, int>(); var pairs = new List<Vector2Int>();
            for (var face = 0; face < triangles.Length / 3; face++) for (var edge = 0; edge < 3; edge++)
            {
                var a = triangles[face * 3 + edge]; var b = triangles[face * 3 + (edge + 1) % 3]; var key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (pending.TryGetValue(key, out var other)) { pairs.Add(new Vector2Int(other, face)); pending.Remove(key); } else pending.Add(key, face);
            }
            Assert.That(pending.Count, Is.Zero); return pairs;
        }

        private static void Surface(Vector3[] vertices, int[] triangles, List<Vector2Int> pairs, Case result)
        {
            var normals = new Vector3[triangles.Length / 3];
            for (var face = 0; face < normals.Length; face++)
            {
                var a = vertices[triangles[face * 3]]; var cross = Vector3.Cross(vertices[triangles[face * 3 + 1]] - a, vertices[triangles[face * 3 + 2]] - a);
                var length = cross.magnitude; if (length <= 1e-20f) Assert.Fail("Degenerate triangle"); normals[face] = cross / length;
            }
            var minimum = 1f;
            foreach (var pair in pairs)
            {
                var dot = Vector3.Dot(normals[pair.x], normals[pair.y]); minimum = Mathf.Min(minimum, dot);
                if (dot < .7071067f) result.hardEdges++; if (dot < 0) result.foldedEdges++;
            }
            result.maximumSurfaceAngle = Mathf.Max(result.maximumSurfaceAngle, Mathf.Acos(Mathf.Clamp(minimum, -1, 1)) * Mathf.Rad2Deg);
        }
    }
}
