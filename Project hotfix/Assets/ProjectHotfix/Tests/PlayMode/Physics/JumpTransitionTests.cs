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
    public sealed class JumpTransitionTests
    {
        private Scene _scene;
        [Serializable] private sealed class Sample
        {
            public int step;
            public bool grounded;
            public bool surfaceSampled;
            public float gait, yawDelta, jointDelta, footError, minimumY;
            public Vector3 position, velocity;
        }
        [Serializable] private sealed class CaseResult
        {
            public string name;
            public int presentationFps, jumps;
            public float maximumJointDelta, maximumBoundaryDelta, maximumYawDelta, maximumFootError, maximumGroundPenetration;
            public float finalGait, finalSpeed;
            public float maximumPhaseAdvance, finalPoseDelta;
            public float maximumReachCorrection;
            public bool occupiedArmsPreserved;
            public Sample[] samples;
        }
        [Serializable] private sealed class Report
        {
            public string result, outputDirectory;
            public CaseResult[] cases;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(_scene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EightDirectionsAndAirChanges_BlendIntoLocomotion()
        {
            if (!File.Exists(PhysicsJumpReview.TransportPath)) Assert.Ignore("Local review transport missing.");
            var rootDir = Environment.GetEnvironmentVariable("PHX_JUMP_TRANSITION_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/jump-transitions-20260905"));
            var output = Path.Combine(rootDir, "Review-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            var cases = new List<CaseResult>();
            var metricsOnly = Environment.GetEnvironmentVariable("PHX_REVIEW_METRICS_ONLY") == "1";
            var names = new[] { "Forward", "ForwardRight", "Right", "BackRight", "Back", "BackLeft", "Left", "ForwardLeft",
                "AirReverse", "AirTurn90", "AirStop", "SprintLanding", "LandingSprint", "BufferedRejump", "OccupiedArms", "ResetInAir", "Forward30fps", "Forward120fps" };
            for (var caseIndex = 0; caseIndex < names.Length; caseIndex++)
            {
                var name = names[caseIndex]; var fps = name == "Forward30fps" ? 30 : name == "Forward120fps" ? 120 : 60;
                _scene = SceneManager.CreateScene("Transition-" + name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0));
                SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                var angle = caseIndex < 8 ? caseIndex * 45f * Mathf.Deg2Rad : 0f;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0,-.1f,0); floor.transform.localScale = new Vector3(30,.2f,30);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(.13f,.2f,.28f)); floor.GetComponent<Renderer>().sharedMaterial = material;
                var grid = new Material(Shader.Find("Universal Render Pipeline/Unlit")); grid.SetColor("_BaseColor", new Color(.07f,.11f,.17f));
                for (var lineIndex = -10; lineIndex <= 10; lineIndex++)
                {
                    for (var axis = 0; axis < 2; axis++)
                    {
                        var line = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(line, _scene);
                        line.GetComponent<Collider>().enabled = false;
                        line.transform.position = axis == 0 ? new Vector3(0,.002f,lineIndex*.5f) : new Vector3(lineIndex*.5f,.002f,0);
                        line.transform.localScale = axis == 0 ? new Vector3(10,.002f,.004f) : new Vector3(.004f,.002f,10);
                        line.GetComponent<Renderer>().sharedMaterial = grid;
                    }
                }
                var lightObj = new GameObject("Light"); SceneManager.MoveGameObjectToScene(lightObj, _scene);
                var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.5f;
                light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45,-35,0);
                var cameraObj = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(cameraObj, _scene);
                var camera = cameraObj.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .85f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.09f,.13f);
                var render = new RenderTexture(448,448,24); render.Create(); camera.targetTexture = render;
                var image = new Texture2D(448,448,TextureFormat.RGB24,false); var baked = new Mesh();
                var skin = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
                var joints = actor.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Thigh_") || t.name.StartsWith("Calf_") || t.name.StartsWith("Foot_")).ToArray();
                var arms = actor.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("UpperArm_") || t.name.StartsWith("Forearm_")).ToArray();
                if (name == "OccupiedArms")
                {
                    actor.Arms.LeftAvailable = actor.Arms.RightAvailable = false;
                    foreach (var arm in arms) arm.localRotation *= Quaternion.Euler(12f, 9f, 0f);
                }
                var armRotations = arms.Select(a => a.localRotation).ToArray();
                var previous = joints.Select(t => t.localRotation).ToArray();
                var restRotations = previous.ToArray(); var lastYaw = actor.Motor.Body.rotation;
                var wasGrounded = false; var result = new CaseResult { name = name, presentationFps = fps, occupiedArmsPreserved = true };
                var samples = new List<Sample>(); var requestedRejump = false; var pendingBoundary = false;
                for (var step = 0; step < 210; step++)
                {
                    var desired = direction;
                    if (step >= 55 && name == "AirReverse") desired = -direction;
                    if (step >= 55 && name == "AirTurn90") desired = Vector2.right;
                    if (step >= 55 && name == "AirStop") desired = Vector2.zero;
                    actor.Motor.MoveInput = step >= 10 && step < 140 ? desired * .75f : Vector2.zero;
                    actor.Motor.SprintRequested = name == "SprintLanding" || (name == "LandingSprint" && step >= 70);
                    if (step == 45) actor.Motor.RequestJump();
                    if (name == "BufferedRejump" && step > 55 && !requestedRejump
                        && actor.Motor.Body.linearVelocity.y < -1f && actor.Motor.PresentationState.GroundDistance < .09f)
                    {
                        actor.Motor.RequestJump(); requestedRejump = true;
                    }
                    var reset = name == "ResetInAir" && step == 60;
                    if (reset) actor.Motor.ResetAt(new Vector3(0,.02f,0));
                    actor.Motor.Step(1f/60f); physics.Simulate(1f/60f);
                    var delta = 0f;
                    pendingBoundary |= wasGrounded != actor.Motor.Grounded;
                    var presented = fps >= 60 || step % 2 == 0;
                    if (presented)
                    {
                        for (var substep = 0; substep < (fps == 120 ? 2 : 1); substep++)
                        {
                            var previousPhase = actor.Pose.GaitPhase;
                            actor.ApplyPresentation(1f/fps);
                            if (actor.Pose.WorldFootPlant && !actor.Motor.Grounded)
                            {
                                Assert.That(actor.Pose.IsFootPlanted(0), Is.False, name + " airborne left anchor");
                                Assert.That(actor.Pose.IsFootPlanted(1), Is.False, name + " airborne right anchor");
                            }
                            if (!reset) result.maximumPhaseAdvance = Mathf.Max(result.maximumPhaseAdvance,
                                Mathf.Repeat(actor.Pose.GaitPhase - previousPhase, 1f));
                            for (var index = 0; index < joints.Length; index++)
                            {
                                delta = Mathf.Max(delta, Quaternion.Angle(previous[index], joints[index].localRotation));
                                previous[index] = joints[index].localRotation;
                            }
                        }
                    }
                    var poseFrame = Time.frameCount;
                    if (!metricsOnly) yield return null; // Captures require a refreshed skinning frame.
                    var yaw = Quaternion.Angle(lastYaw, actor.Motor.Body.rotation); lastYaw = actor.Motor.Body.rotation;
                    if (step > 15 && !reset)
                    {
                        result.maximumJointDelta = Mathf.Max(result.maximumJointDelta, delta);
                        result.maximumYawDelta = Mathf.Max(result.maximumYawDelta, yaw);
                        if (pendingBoundary && presented) result.maximumBoundaryDelta = Mathf.Max(result.maximumBoundaryDelta, delta);
                    }
                    if (presented) pendingBoundary = false;
                    wasGrounded = actor.Motor.Grounded;
                    result.maximumFootError = Mathf.Max(result.maximumFootError, actor.Pose.MaximumFootTargetError);
                    result.maximumReachCorrection = Mathf.Max(result.maximumReachCorrection, actor.Pose.MaximumReachCorrection);
                    var minimumY = float.PositiveInfinity;
                    if (step % 3 == 0)
                    {
                        skin.BakeMesh(baked); minimumY = baked.vertices.Min(v => skin.transform.TransformPoint(v).y);
                        if (actor.Motor.Grounded) result.maximumGroundPenetration = Mathf.Max(result.maximumGroundPenetration, -minimumY);
                        if (caseIndex < 14 && !metricsOnly)
                        {
                            Assert.That(Time.frameCount, Is.GreaterThan(poseFrame), "Capture skipped the skinning frame");
                            var center = new Vector3(actor.Motor.Body.position.x,.65f,actor.Motor.Body.position.z);
                            camera.transform.position = center + new Vector3(2.4f,.9f,3.4f); camera.transform.LookAt(center);
                            camera.Render(); var old = RenderTexture.active; RenderTexture.active = render;
                            image.ReadPixels(new Rect(0,0,448,448),0,0); image.Apply(); RenderTexture.active = old;
                            File.WriteAllBytes(Path.Combine(output,$"{name}_{step/3:D3}.png"),image.EncodeToPNG());
                        }
                    }
                    if (name == "OccupiedArms")
                        for (var i = 0; i < arms.Length; i++) result.occupiedArmsPreserved &= arms[i].localRotation == armRotations[i];
                    samples.Add(new Sample { step = step, grounded = actor.Motor.Grounded, surfaceSampled = step % 3 == 0, gait = actor.Pose.GaitWeight,
                        yawDelta = yaw, jointDelta = delta, footError = actor.Pose.MaximumFootTargetError,
                        minimumY = float.IsPositiveInfinity(minimumY) ? 0f : minimumY,
                        position = actor.Motor.Body.position, velocity = actor.Motor.Body.linearVelocity });
                }
                result.jumps = actor.Motor.JumpCount; result.finalGait = actor.Pose.GaitWeight;
                result.finalPoseDelta = joints.Select((joint, index) => Quaternion.Angle(joint.localRotation, restRotations[index])).Max();
                result.finalSpeed = actor.Motor.Body.linearVelocity.magnitude; result.samples = samples.ToArray(); cases.Add(result);
                Debug.Log($"TRANSITION_CASE {name} joint={result.maximumJointDelta:F2} boundary={result.maximumBoundaryDelta:F2} yaw={result.maximumYawDelta:F2} foot={result.maximumFootError:G3} ground={result.maximumGroundPenetration:G3}");
                File.WriteAllText(Path.Combine(output,"TransitionReview.json"),JsonUtility.ToJson(new Report { result = "OBSERVED", outputDirectory = output, cases = cases.ToArray() },true));
                UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(baked); camera.targetTexture = null; render.Release(); UnityEngine.Object.Destroy(render);
                UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(grid);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(rootDir,"LatestReviewPath.txt"),output);
            foreach (var c in cases)
            {
                Assert.That(c.maximumBoundaryDelta, Is.LessThan(15f * 60f/c.presentationFps), c.name + " contact transition");
                Assert.That(c.maximumJointDelta, Is.LessThan(20f * 60f/c.presentationFps), c.name + " pose step");
                Assert.That(c.maximumYawDelta, Is.LessThan(7.2f), c.name + " physical heading jump");
                Assert.That(c.maximumFootError, Is.LessThan(.003f), c.name + " reach");
                Assert.That(c.maximumReachCorrection, Is.LessThan(.003f), c.name + " unreachable stride");
                Assert.That(c.maximumGroundPenetration, Is.LessThan(.008f), c.name + " surface ground");
                Assert.That(c.finalGait, Is.LessThan(.01f), c.name + " gait residue");
                Assert.That(c.finalSpeed, Is.LessThan(.02f), c.name + " stop");
                Assert.That(c.finalPoseDelta, Is.LessThan(.25f), c.name + " Idle pose closure");
                Assert.That(c.maximumPhaseAdvance, Is.LessThan(4.5f/c.presentationFps), c.name + " gait phase restart");
                Assert.That(c.occupiedArmsPreserved, Is.True, c.name + " arm ownership");
                Assert.That(c.jumps, Is.EqualTo(c.name == "BufferedRejump" ? 2 : c.name == "ResetInAir" ? 0 : 1), c.name);
            }
            File.WriteAllText(Path.Combine(output,"TransitionReview.json"),JsonUtility.ToJson(new Report { result = "PASS", outputDirectory = output, cases = cases.ToArray() },true));
        }
    }
}
