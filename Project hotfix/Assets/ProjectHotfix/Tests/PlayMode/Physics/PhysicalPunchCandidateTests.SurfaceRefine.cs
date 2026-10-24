using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        [UnityTest]
        public IEnumerator CaptureRefinedSurfaceCloseups()
        {
            Create(.8f);
            var rig = _pair.Players[0].Rig;
            foreach (var renderer in _pair.Players[1].Rig.Actor.Pose.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.gameObject.scene == _scene).ToArray();
            foreach (var light in lights) if (light.enabled) light.transform.rotation = Quaternion.Euler(25, 135, 0);
            var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
            foreach (var angle in new[] { 0f, 90f, 120f })
            {
                if (angle > 0) for (var tick = 0; tick < 90; tick++)
                {
                    _pair.Step(Dt);
                    foreach (var side in new[] { "L", "R" }) foreach (var name in new[] { "UpperArm_", "Forearm_" })
                    {
                        var link = rig.Find(name + side);
                        link.Joint.targetRotation = Quaternion.AngleAxis(name == "UpperArm_" ? angle : 5, Vector3.right);
                        var drive = link.Drive; drive.positionSpring *= 4; drive.positionDamper *= 2; drive.maximumForce *= 3; link.Joint.slerpDrive = drive;
                    }
                    _scene.GetPhysicsScene().Simulate(Dt); _pair.CapturePhysics();
                }
                _pair.Present(1);
                ExportSurfacePose(skin, "DetailRaise_" + angle);
                foreach (var view in new[] { "Front", "ThreeQuarter", "Side", "Back" })
                {
                    var offset = view == "Front" ? new Vector3(0, 0, 3) : view == "Side" ? new Vector3(-3, 0, 0)
                        : view == "Back" ? new Vector3(0, 0, -3) : new Vector3(-2, .15f, 3);
                    var center = rig.Actor.Motor.Body.position + Vector3.up * .465f;
                    _pair.View.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                    var rt = new RenderTexture(1000, 1000, 24); rt.Create();
                    var photo = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
                    _pair.View.targetTexture = rt; _pair.View.aspect = 1; _pair.View.orthographicSize = .33f;
                    yield return null;
                    _pair.View.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
                    photo.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); photo.Apply(); RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(Output, $"Detail_{angle}_{view}.png"), photo.EncodeToPNG());
                    _pair.View.targetTexture = null; rt.Release(); Object.Destroy(rt); Object.Destroy(photo);
                }
            }
        }

        [UnityTest]
        public IEnumerator CorrectedContactSurfaceMatchesSkinAndIgnoresRenderInterpolation()
        {
            Create(.7f);
            var rig = _pair.Players[0].Rig;
            var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Single(s => s.name.EndsWith("SkinnedBody"));
            var driver = skin.GetComponent<SurfacePoseCorrectives>();
            Assert.That(driver, Is.Not.Null, "Run this check with the refined character transport.");
            var surface = new SkinSurfaceReview(rig.Actor, rig.Links.Select(l => l.Body).Append(rig.Actor.Motor.Body).ToArray());
            var mesh = new Mesh();
            var baseMesh = new Mesh();
            var maximumError = 0f;
            var maximumCorrection = 0f;
            var samples = 0;
            Tap(0, 0);
            for (var tick = 0; tick < 25; tick++)
            {
                Tick();
                if (tick != 5 && tick != 8 && tick != 12 && tick != 18) continue;
                _pair.Present(1);
                driver.ApplyCurrentPose();
                skin.BakeMesh(mesh);
                var vertices = mesh.vertices;
                for (var shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++) skin.SetBlendShapeWeight(shape, 0);
                skin.BakeMesh(baseMesh);
                var uncorrected = baseMesh.vertices;
                driver.ApplyCurrentPose();
                surface.BeginFixedFrame();
                var selected = Enumerable.Range(0, vertices.Length)
                    .OrderByDescending(i => (vertices[i] - uncorrected[i]).sqrMagnitude).Take(12).ToArray();
                foreach (var vertex in selected)
                {
                    var point = skin.transform.TransformPoint(vertices[vertex]);
                    maximumCorrection = Mathf.Max(maximumCorrection, Vector3.Distance(vertices[vertex], uncorrected[vertex]));
                    var anchor = surface.Closest(point);
                    maximumError = Mathf.Max(maximumError, Vector3.Distance(point, anchor.Position));
                    var fixedPoint = anchor.Position;
                    _pair.Present(.23f);
                    surface.BeginFixedFrame();
                    Assert.That(Vector3.Distance(anchor.Position, fixedPoint), Is.LessThan(.000001f),
                        "Contact skin must use fixed pose weights even when render bones are interpolated.");
                    _pair.Present(1);
                    samples++;
                }
            }
            Object.Destroy(mesh);
            Object.Destroy(baseMesh);
            File.WriteAllText(Path.Combine(Output, "CorrectedSkinContact.txt"),
                $"samples={samples}\nmaximumCorrection={maximumCorrection:R}\nmaximumContactError={maximumError:R}\n");
            Assert.That(samples, Is.EqualTo(48));
            Assert.That(maximumCorrection, Is.GreaterThan(.003f), "The check must exercise positional, not just normal, correction.");
            Assert.That(maximumError, Is.LessThan(.0001f), "Fixed contact surface must coincide with the corrected rendered mesh.");
            yield return null;
        }
    }
}
