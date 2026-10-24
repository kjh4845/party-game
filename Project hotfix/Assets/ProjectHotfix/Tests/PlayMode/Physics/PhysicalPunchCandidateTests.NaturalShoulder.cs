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
    public sealed partial class PhysicalPunchCandidateTests
    {
        [UnityTest]
        public IEnumerator ShoulderSocketsFollowReachAndResetTogetherWithSkin()
        {
            Create(.9f);
            var rig = _pair.Players[0].Rig;
            rig.IndependentHands = false;
            rig.TargetExercise = ActiveUpperBodyReview.Exercise.Reach;
            for (var tick = 0; tick < 100; tick++) Tick();
            for (var side = 0; side < 2; side++)
            {
                var upper = rig.Find(side == 0 ? "UpperArm_L" : "UpperArm_R");
                Assert.That(rig.ShoulderProtraction(side), Is.GreaterThan(5));
                Assert.That(rig.ShoulderElevation(side), Is.GreaterThan(1));
                Assert.That(Vector3.Distance(upper.Joint.connectedAnchor, upper.StartConnectedAnchor), Is.InRange(.008f, .035f));
            }
            Assert.That(rig.MaximumJointGap, Is.LessThan(.025f));
            Assert.That(rig.MaximumSkinBodyOffset, Is.LessThan(.025f));
            rig.ResetReview();
            for (var side = 0; side < 2; side++)
            {
                var upper = rig.Find(side == 0 ? "UpperArm_L" : "UpperArm_R");
                Assert.That(rig.ShoulderProtraction(side) + rig.ShoulderElevation(side), Is.Zero);
                Assert.That(Vector3.Distance(upper.Joint.connectedAnchor, upper.StartConnectedAnchor), Is.LessThan(.000001f));
            }
            yield return null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator CaptureNaturalShoulderMotionComparison()
        {
            var report = new List<string>();
            var baselineGap = 0f; var baselineSkinOffset = 0f;
            foreach (var mode in new[] { "Reach", "Punch" }) foreach (var enabled in new[] { false, true })
            {
                Create(.9f);
                var rig = _pair.Players[0].Rig;
                rig.NaturalShoulderMotion = enabled;
                var label = mode + (enabled ? "_After" : "_Before");
                var folder = Path.Combine(Output, label); Directory.CreateDirectory(folder);
                if (mode == "Reach") _pair.AimHeightOffset = .10f;
                foreach (var renderer in _pair.Players[1].Rig.Actor.Pose.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
                var mesh = new Mesh(); var triangles = skin.sharedMesh.triangles; var pairs = CameraGaitReviewTests.Pairs(triangles);
                var surface = 0f; var reach = 0f; var back = 0f; var up = 0f;
                var rt = new RenderTexture(1000, 800, 24); rt.Create();
                var photo = new Texture2D(1000, 800, TextureFormat.RGB24, false);
                _pair.View.targetTexture = rt; _pair.View.aspect = 1.25f; _pair.View.orthographicSize = .45f;
                for (var tick = 0; tick < 100; tick++)
                {
                    if (mode == "Reach")
                    {
                        if (tick == 10) Buttons(0, 3);
                        if (tick == 65) Buttons(0, 0);
                    }
                    else
                    {
                        if (tick == 10) Buttons(0, 1); if (tick == 12) Buttons(0, 0);
                        if (tick == 55) Buttons(0, 2); if (tick == 57) Buttons(0, 0);
                    }
                    Tick(); _pair.Present(1);
                    for (var side = 0; side < 2; side++)
                    {
                        reach = Mathf.Max(reach, rig.ShoulderProtraction(side));
                        back = Mathf.Min(back, rig.ShoulderProtraction(side));
                        up = Mathf.Max(up, rig.ShoulderElevation(side));
                    }
                    skin.BakeMesh(mesh); surface = Mathf.Max(surface, CameraGaitReviewTests.Surface(mesh.vertices, triangles, pairs, out _));
                    var center = rig.Actor.Motor.Body.position + Vector3.up * .56f;
                    var offset = new Vector3(-2.6f, .18f, 1.9f);
                    _pair.View.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                    yield return null;
                    _pair.View.Render(); var old = RenderTexture.active; RenderTexture.active = rt;
                    photo.ReadPixels(new Rect(0, 0, 1000, 800), 0, 0); photo.Apply(); RenderTexture.active = old;
                    File.WriteAllBytes(Path.Combine(folder, $"Frame_{tick:D4}.png"), photo.EncodeToPNG());
                }
                report.Add($"{label}: protraction={reach:R}, retraction={back:R}, elevation={up:R}, surface={surface:R}, jointGap={rig.MaximumJointGap:R}, skinBodyOffset={rig.MaximumSkinBodyOffset:R}");
                File.WriteAllLines(Path.Combine(Output, "ShoulderMotion.txt"), report);
                Assert.That(surface, Is.LessThan(60), label);
                if (enabled)
                {
                    Assert.That(reach, Is.GreaterThan(5));
                    if (mode == "Punch") Assert.That(back, Is.LessThan(-.5f), "The punching shoulder must first load back, then follow through.");
                }
                if (!enabled)
                {
                    baselineGap = rig.MaximumJointGap;
                    baselineSkinOffset = rig.MaximumSkinBodyOffset;
                }
                else
                {
                    Assert.That(rig.MaximumJointGap, Is.LessThan(baselineGap + .005f), "Shoulder motion must not worsen the existing high-speed punch solver gap.");
                    Assert.That(rig.MaximumSkinBodyOffset, Is.LessThan(baselineSkinOffset + .005f));
                }
                if (mode == "Reach") Assert.That(rig.MaximumJointGap, Is.LessThan(.025f));
                _pair.View.targetTexture = null; rt.Release(); Object.Destroy(rt); Object.Destroy(photo); Object.Destroy(mesh);
                var operation = SceneManager.UnloadSceneAsync(_scene); while (!operation.isDone) yield return null;
            }
        }
    }
}
