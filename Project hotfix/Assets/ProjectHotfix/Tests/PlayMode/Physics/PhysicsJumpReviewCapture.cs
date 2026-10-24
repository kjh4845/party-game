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
    public sealed class PhysicsJumpReviewCapture
    {
        private Scene _scene;

        [UnityTearDown]
        public IEnumerator CleanUpScene()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone) yield return null;
        }
        [Serializable] private sealed class Sample
        {
            public int step, jumps, landings;
            public Vector3 position, velocity;
            public bool grounded;
            public float footTargetError, surfaceMinimumY;
        }
        [Serializable] private sealed class Report
        {
            public string result;
            public int physicsSteps, renderCount, bones, animationClips, meshColliders;
            public float stepSeconds, maximumContactSurfaceError;
            public Sample[] samples;
        }

        [UnityTest]
        public IEnumerator ApprovedModel_PhysicsJumpWithoutClips_CapturesReview()
        {
            if (!File.Exists(PhysicsJumpReview.TransportPath)) Assert.Ignore("Local Blender review transport has not been generated.");
            var scene = SceneManager.CreateScene("PhysicsJumpCapture", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _scene = scene;
            var physics = scene.GetPhysicsScene();
            var root = PhysicsJumpReview.Create(new Vector3(0, .02f, 0));
            Assert.That(root.Pose.WorldFootPlant, Is.True, "Approved prototype preset must be the default");
            Assert.That(root.Motor.MoveSpeed, Is.EqualTo(.70f));
            Assert.That(root.Pose.WalkCadence, Is.EqualTo(3f));
            Assert.That(root.Pose.SprintCadence, Is.EqualTo(4.5f));
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            root.Motor.ManualStep = true; root.ReadKeyboard = false;
            root.Motor.Body.interpolation = RigidbodyInterpolation.None;
            var cameraObject = new GameObject("ReviewCaptureCamera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(2.5f, 1.8f, 3.5f); camera.transform.LookAt(new Vector3(.25f, .65f, 0));
            camera.orthographic = true; camera.orthographicSize = .95f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.09f,.13f);
            var lightObject = new GameObject("ReviewCaptureLight"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.5f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(10,.2f,10);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(.12f,.20f,.29f)); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            gridMaterial.SetColor("_BaseColor", new Color(.09f,.14f,.21f));
            for (var index = -4; index <= 4; index++)
            {
                foreach (var alongX in new[] { true, false })
                {
                    var line = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(line, scene);
                    line.GetComponent<Collider>().enabled = false;
                    line.transform.position = alongX ? new Vector3(0, .002f, index*.5f) : new Vector3(index*.5f, .002f, 0);
                    line.transform.localScale = alongX ? new Vector3(4,.002f,.005f) : new Vector3(.005f,.002f,4);
                    line.GetComponent<Renderer>().sharedMaterial = gridMaterial;
                }
            }
            var outDir = Environment.GetEnvironmentVariable("PHX_JUMP_CAPTURE_OUTPUT")
                ?? Path.GetDirectoryName(PhysicsJumpReview.TransportPath);
            var images = Path.Combine(outDir, "Capture-r02"); Directory.CreateDirectory(images);
            var render = new RenderTexture(640, 640, 24); render.Create(); camera.targetTexture = render;
            var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
            var baked = new Mesh(); var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
            var arms = root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("UpperArm_") || t.name.StartsWith("Forearm_")).ToArray();
            var originalRotations = arms.Select(a => a.localRotation).ToArray();
            var rows = new List<Sample>(); var contactError = 0f;
            for (var step = 0; step < 180; step++)
            {
                if (step == 20 || step == 110) root.Motor.RequestJump();
                root.Motor.MoveInput = step >= 70 && step < 150 ? new Vector2(.35f, 0) : Vector2.zero;
                root.Motor.Step(1f/60f); physics.Simulate(1f/60f);
                root.Pose.Apply(root.Motor.PresentationState, 1f/60f);
                var poseFrame = Time.frameCount;
                yield return null; // Let rendered skin consume the updated bone matrices.
                skin.BakeMesh(baked); var minY = baked.vertices.Min(v => skin.transform.TransformPoint(v).y);
                if (root.Motor.Grounded) contactError = Mathf.Max(contactError, Mathf.Abs(minY));
                for (var i = 0; i < arms.Length; i++) Assert.That(arms[i].localRotation, Is.EqualTo(originalRotations[i]), "Jump changed an arm pose");
                rows.Add(new Sample { step = step, jumps = root.Motor.JumpCount, landings = root.Motor.LandingCount,
                    position = root.Motor.Body.position, velocity = root.Motor.Body.linearVelocity,
                    grounded = root.Motor.Grounded, footTargetError = root.Pose.MaximumFootTargetError, surfaceMinimumY = minY });
                if (step % 3 == 0)
                {
                    Assert.That(Time.frameCount, Is.GreaterThan(poseFrame), "Rendered skin must consume this pose before capture");
                    camera.Render(); var previous = RenderTexture.active; RenderTexture.active = render;
                    image.ReadPixels(new Rect(0,0,640,640), 0,0); image.Apply(); RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(images, $"PhysicsJump_{step/3:D3}.png"), image.EncodeToPNG());
                }
            }
            var report = new Report { result = "OBSERVED", physicsSteps = 180, renderCount = 60, bones = skin.bones.Length,
                animationClips = root.GetComponentsInChildren<Animator>().Length,
                meshColliders = root.GetComponentsInChildren<MeshCollider>().Length,
                stepSeconds = 1f/60f, maximumContactSurfaceError = contactError, samples = rows.ToArray() };
            File.WriteAllText(Path.Combine(outDir, "UnityPhysicsReview.json"), JsonUtility.ToJson(report, true));
            Assert.That(root.Motor.JumpCount, Is.EqualTo(2));
            Assert.That(root.Motor.Grounded, Is.True);
            Assert.That(rows.Max(s => s.footTargetError), Is.LessThan(.00001f));
            Assert.That(contactError, Is.LessThan(.008f), "Review surface grounding requires adjustment");
            Assert.That(report.bones, Is.EqualTo(22)); Assert.That(report.animationClips, Is.Zero); Assert.That(report.meshColliders, Is.Zero);
            report.result = "PASS";
            File.WriteAllText(Path.Combine(outDir, "UnityPhysicsReview.json"), JsonUtility.ToJson(report, true));
            UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(baked);
            camera.targetTexture = null; render.Release(); UnityEngine.Object.Destroy(render); UnityEngine.Object.Destroy(floorMaterial);
            UnityEngine.Object.Destroy(gridMaterial);
            var unload = SceneManager.UnloadSceneAsync(scene); while (unload != null && !unload.isDone) yield return null;
        }
    }
}
