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
    // Measures a fixed world target against the actual rig and physics path.
    // This is a feasibility audit, not an IK implementation or visual approval.
    public sealed class WorldFootPlantFeasibilityTests
    {
        private Scene _scene;
        [Serializable] private sealed class Contact
        {
            public int side, samples, reachableSamples;
            public float duration, rootTravel, footTravel, maximumRequiredExtension;
            public float firstUnreachableAge = -1f;
        }
        [Serializable] private sealed class Case
        {
            public string mode;
            public int directionDegrees;
            public float legLength, actualSpeed, cycleHz, meanStanceDuration;
            public float meanWorldFootSpeed, meanReachableFraction, maximumRequiredExtension;
            public Contact[] contacts;
        }
        [Serializable] private sealed class Report
        {
            public string status, limitation;
            public Case[] cases;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest] public IEnumerator ReviewedGait_WorldAnchorsExposeReachLimit_InEightDirections() => Audit(false);

        [UnityTest] public IEnumerator Candidate_WorldAnchorsRemainReachable_InEightDirections() => Audit(true);

        private IEnumerator Audit(bool candidate)
        {
            Assert.That(File.Exists(PhysicsJumpReview.TransportPath), Is.True, "Approved review transport required.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath,
                (candidate ? "../../.codex_tmp/foot-plant-candidate-20260906/AnchorAudit-" : "../../.codex_tmp/world-foot-lock-20260906/Review-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(output);
            var results = new List<Case>();
            const float dt = 1f / 60f;
            foreach (var sprint in new[] { false, true }) for (var angle = 0; angle < 360; angle += 45)
            {
                _scene = SceneManager.CreateScene("WorldPlantAudit", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .02f, 0), false);
                SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                if (candidate) actor.UseFootPlantPreset();
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true;
                actor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                actor.Motor.SprintRequested = sprint;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(floor, _scene);
                floor.transform.position = new Vector3(0, -.1f, 0);
                floor.transform.localScale = new Vector3(60, .2f, 60);
                var nodes = actor.GetComponentsInChildren<Transform>();
                var feet = new[] { nodes.Single(t => t.name == "Foot_L"), nodes.Single(t => t.name == "Foot_R") };
                var hips = new[] { nodes.Single(t => t.name == "Thigh_L"), nodes.Single(t => t.name == "Thigh_R") };
                var knees = new[] { nodes.Single(t => t.name == "Calf_L"), nodes.Single(t => t.name == "Calf_R") };
                var length = Vector3.Distance(hips[0].position, knees[0].position) + Vector3.Distance(knees[0].position, feet[0].position);
                var anchors = new Vector3[2]; var previousFeet = new Vector3[2];
                var active = new Contact[2]; var priorStance = new bool[2]; var complete = new List<Contact>();
                var previousRoot = actor.transform.position;
                var speedSum = 0f; var phaseSum = 0f; var measureCount = 0;
                var input = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
                for (var step = 0; step < 480; step++)
                {
                    actor.Motor.MoveInput = step >= 20 ? input : Vector2.zero;
                    var oldPhase = actor.Pose.GaitPhase;
                    actor.Motor.Step(dt); physics.Simulate(dt); actor.ApplyPresentation(dt);
                    if (step >= 180)
                    {
                        Assert.That(actor.Motor.Grounded, Is.True);
                        speedSum += Vector3.ProjectOnPlane(actor.Motor.Body.linearVelocity, Vector3.up).magnitude;
                        phaseSum += Mathf.Repeat(actor.Pose.GaitPhase - oldPhase, 1f); measureCount++;
                    }
                    for (var side = 0; side < 2; side++)
                    {
                        var phase = Mathf.Repeat(actor.Pose.GaitPhase + side * .5f - .25f, 1f);
                        var stance = actor.Motor.Grounded && phase < .5f;
                        if (step >= 180 && stance && !priorStance[side])
                        {
                            active[side] = new Contact { side = side };
                            anchors[side] = feet[side].position;
                        }
                        var contact = active[side];
                        if (contact != null && stance)
                        {
                            // Fixed target is never moved or clamped to conceal unreachable poses.
                            var extension = Mathf.Max(0f, Vector3.Distance(hips[side].position, anchors[side]) - length);
                            contact.maximumRequiredExtension = Mathf.Max(contact.maximumRequiredExtension, extension);
                            if (extension <= .000001f) contact.reachableSamples++;
                            else if (contact.firstUnreachableAge < 0f) contact.firstUnreachableAge = contact.samples * dt;
                            if (contact.samples > 0)
                            {
                                contact.duration += dt;
                                contact.rootTravel += Vector3.ProjectOnPlane(actor.transform.position - previousRoot, Vector3.up).magnitude;
                                contact.footTravel += Vector3.ProjectOnPlane(feet[side].position - previousFeet[side], Vector3.up).magnitude;
                            }
                            contact.samples++;
                        }
                        if (!stance && contact != null) { complete.Add(contact); active[side] = null; }
                        priorStance[side] = stance; previousFeet[side] = feet[side].position;
                    }
                    previousRoot = actor.transform.position;
                }
                Assert.That(complete.Count, Is.GreaterThanOrEqualTo(10));
                var result = new Case
                {
                    mode = sprint ? "Sprint" : "Walk", directionDegrees = angle, legLength = length,
                    actualSpeed = speedSum / measureCount, cycleHz = phaseSum / (measureCount * dt),
                    meanStanceDuration = complete.Average(c => c.duration),
                    meanWorldFootSpeed = complete.Sum(c => c.footTravel) / complete.Sum(c => c.duration),
                    meanReachableFraction = (float)complete.Sum(c => c.reachableSamples) / complete.Sum(c => c.samples),
                    maximumRequiredExtension = complete.Max(c => c.maximumRequiredExtension), contacts = complete.ToArray()
                };
                results.Add(result);
                // A regression guard against calling accurate root-relative IK "zero slip".
                if (candidate)
                {
                    Assert.That(result.meanWorldFootSpeed, Is.LessThan(.001f));
                    Assert.That(result.maximumRequiredExtension, Is.LessThan(.00001f));
                    Assert.That(result.meanReachableFraction, Is.EqualTo(1f));
                }
                else
                {
                    Assert.That(result.meanWorldFootSpeed, Is.GreaterThan(.5f));
                    Assert.That(result.maximumRequiredExtension, Is.GreaterThan(.1f));
                    Assert.That(result.meanReachableFraction, Is.LessThan(.5f));
                }
                var unload = SceneManager.UnloadSceneAsync(_scene);
                while (unload != null && !unload.isDone) yield return null;
            }
            File.WriteAllText(Path.Combine(output, "WorldFootPlantFeasibility.json"), JsonUtility.ToJson(new Report
            {
                status = candidate ? "CANDIDATE_STEADY_ANCHORS_PASS" : "AUDIT_PASS_STYLE_DECISION_REQUIRED",
                limitation = "Static flat floor; 60 Hz, steady eight-direction motion. Targets anchored at first sampled stance frame. Reach is an optimistic geometric bound, not deformation QA. Runtime locking and tuning applied only when candidate mode is selected.",
                cases = results.ToArray()
            }, true));
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
                candidate ? "../../.codex_tmp/foot-plant-candidate-20260906/LatestAnchorAuditPath.txt" : "../../.codex_tmp/world-foot-lock-20260906/LatestReviewPath.txt")), output);
        }
    }
}
