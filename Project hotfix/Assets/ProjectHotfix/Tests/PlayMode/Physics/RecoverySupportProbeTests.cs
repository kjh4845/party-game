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
    public sealed class RecoverySupportProbeTests
    {
        private Scene _scene;
        [Serializable] private sealed class Case { public string name; public bool support, stable, clear; public float normalY; }
        [Serializable] private sealed class Report { public string status; public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
        }
        private GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; SceneManager.MoveGameObjectToScene(obj, _scene);
            obj.transform.position = position; obj.transform.localScale = scale; return obj;
        }
        [UnityTest] public IEnumerator RealQueriesRequireContactStabilityAndStandingClearance()
        {
            var cases = new List<Case>();
            foreach (var name in new[] { "Flat", "Slope", "Steep", "Air", "Hover", "Ceiling", "OtherActor", "TriggerCeiling", "TriggerFloor", "MovingTorso", "RotatingTorso", "MovingPlatform", "MatchingPlatform", "SupportLost", "SupportChanged" })
            {
                _scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var normal = Quaternion.Euler(0, 0, name == "Slope" ? 25 : name == "Steep" ? 70 : 0) * Vector3.up;
                var floor = Cube("Floor", -normal * .1f, new Vector3(10, .2f, 10)); floor.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                if (name == "TriggerFloor") floor.GetComponent<Collider>().isTrigger = true;
                var torsoObj = GameObject.CreatePrimitive(PrimitiveType.Sphere); SceneManager.MoveGameObjectToScene(torsoObj, _scene);
                torsoObj.layer = 2; torsoObj.transform.localScale = Vector3.one * .2f;
                torsoObj.transform.position = name == "Air" ? Vector3.up * 2 : name == "Hover" ? Vector3.up * .26f : normal * .12f;
                var torso = torsoObj.AddComponent<Rigidbody>(); torso.useGravity = false;
                if (name == "Ceiling" || name == "OtherActor" || name == "TriggerCeiling")
                {
                    var ceiling = Cube("Standing obstruction", new Vector3(0, .75f, 0), new Vector3(1, .1f, 1));
                    if (name == "OtherActor") ceiling.layer = 2;
                    if (name == "TriggerCeiling") ceiling.GetComponent<Collider>().isTrigger = true;
                }
                Rigidbody platform = null;
                if (name == "MovingPlatform" || name == "MatchingPlatform") { platform = floor.AddComponent<Rigidbody>(); platform.useGravity = false; }
                var nextFloor = name == "SupportChanged" ? Cube("New support", new Vector3(0, -.1f, 0), new Vector3(10, .2f, 10)) : null;
                if (nextFloor != null) nextFloor.SetActive(false);
                physics.Simulate(1f / 60f);
                var before = torso.position; var rotation = torso.rotation;
                if (name == "MovingTorso") torso.linearVelocity = Vector3.right;
                if (name == "RotatingTorso") torso.angularVelocity = Vector3.up;
                if (platform != null) { platform.linearVelocity = Vector3.right * 2; if (name == "MatchingPlatform") torso.linearVelocity = Vector3.right * 2; }
                var probe = new RecoverySupportProbe(torso, new[] { torsoObj.GetComponent<Collider>() }, .17f, 1f);
                RecoverySupportSample sample = default;
                for (var i = 0; i < 30; i++)
                {
                    if (i == 20 && (name == "SupportLost" || name == "SupportChanged"))
                    { floor.SetActive(false); if (nextFloor != null) nextFloor.SetActive(true); UnityEngine.Physics.SyncTransforms(); }
                    sample = probe.Evaluate(1f / 60f);
                    if (i < 11) Assert.That(sample.Stable, Is.False, "Dwell is required");
                }
                var supported = name != "Steep" && name != "Air" && name != "Hover" && name != "TriggerFloor" && name != "SupportLost";
                var stable = supported && name != "MovingTorso" && name != "RotatingTorso" && name != "MovingPlatform" && name != "SupportChanged";
                var clear = supported && name != "Ceiling" && name != "OtherActor";
                Assert.That(sample.HasSupport, Is.EqualTo(supported), name + " support"); Assert.That(sample.Stable, Is.EqualTo(stable), name + " stable"); Assert.That(sample.Clear, Is.EqualTo(clear), name + " clearance");
                Assert.That(torso.position, Is.EqualTo(before), "Probe must not move bodies"); Assert.That(torso.rotation, Is.EqualTo(rotation));
                cases.Add(new Case { name = name, support = sample.HasSupport, stable = sample.Stable, clear = sample.Clear, normalY = sample.Normal.y });
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/recovery-support-20260906")); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "RecoverySupportReview.json"), JsonUtility.ToJson(new Report { status = "PASS", cases = cases.ToArray() }, true));
        }
        [UnityTest] public IEnumerator DownTimerUsesRealQueriesButCannotCompleteAlignmentOrGetUp()
        {
            for (var entry = 0; entry < 4; entry++)
            {
                _scene = SceneManager.CreateScene("Recovery gate", new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0, .19f, 0)); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.useGravity = false;
                actor.Motor.Body.rotation = new[] { Quaternion.Euler(-90, 0, 0), Quaternion.Euler(90, 0, 0), Quaternion.Euler(0, 0, 90), Quaternion.Euler(0, 0, -90) }[entry];
                actor.Motor.Body.constraints = RigidbodyConstraints.None;
                Cube("Floor", new Vector3(0, -.1f, 0), new Vector3(10, .2f, 10));
                var ceiling = Cube("Ceiling", new Vector3(0, .75f, 0), new Vector3(3, .1f, 3));
                physics.Simulate(1f / 60f);
                var down = actor.gameObject.AddComponent<DownStateReview>(); down.Bind(actor, new DownTimingProfile(.1, .1, .3), true);
                down.BindRecoveryProbe(new RecoverySupportProbe(actor.Motor.Body, actor.GetComponentsInChildren<Collider>(), .17f, 1f));
                Assert.That(down.TryBeginDown(1, 1), Is.True);
                for (var i = 0; i < 30; i++) down.Step(1f / 60f);
                Assert.That(down.State.RemainingSeconds, Is.Zero); Assert.That(down.State.Phase, Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble));
                Assert.That(down.SupportSample.Stable, Is.True); Assert.That(down.SupportSample.Clear, Is.False);
                ceiling.SetActive(false); UnityEngine.Physics.SyncTransforms(); down.Step(1f / 60f);
                Assert.That(down.State.Phase, Is.EqualTo(entry < 2 ? RecoveryPresentationPhase.SharedGetUp : RecoveryPresentationPhase.AligningToGetUp));
                var episode = down.State.EpisodeId; var count = down.State.DownCount;
                for (var i = 0; i < 60; i++) down.Step(1f / 60f);
                Assert.That(actor.Motor.ActionsBlocked, Is.True, "No automatic get-up completion");
                ceiling.SetActive(true); UnityEngine.Physics.SyncTransforms(); down.Step(1f / 60f);
                Assert.That(down.State.Phase, Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble)); Assert.That(down.State.EpisodeId, Is.EqualTo(episode)); Assert.That(down.State.DownCount, Is.EqualTo(count));
                actor.Motor.ResetAt(new Vector3(0, .19f, 0)); down.Step(1f / 60f); Assert.That(down.State.DownCount, Is.Zero); Assert.That(actor.Motor.ActionsBlocked, Is.False);
                var unload = SceneManager.UnloadSceneAsync(_scene); while (unload != null && !unload.isDone) yield return null;
            }
        }
    }
}
