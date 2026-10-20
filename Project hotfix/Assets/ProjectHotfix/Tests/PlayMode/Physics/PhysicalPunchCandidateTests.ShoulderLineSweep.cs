using System;
using System.Collections;
using System.Collections.Generic;
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
        [Serializable] private sealed class ShoulderSweepFrame
        {
            public int frame;
            public string phase;
            public float requestedLift, requestedFlex, surfaceAngle;
            public Vector3 worstPoint;
        }
        [Serializable] private sealed class ShoulderSweepReport
        {
            public string model;
            public float maximumSurfaceAngle;
            public ShoulderSweepFrame[] frames;
        }

        [UnityTest, Explicit("Local visual diagnostic; select this test explicitly"), Category("VisualDiagnostics"), Timeout(300000)]
        public IEnumerator CaptureShoulderLineSweep()
        {
            // Technical surface check plus real visual evidence. A pass alone
            // does not accept the shoulder silhouette or remove a visible crease.
            Create(.9f);
            var rig = _pair.Players[0].Rig;
            var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Single(s => s.name.EndsWith("SkinnedBody"));
            foreach (var renderer in _pair.Players[1].Rig.Actor.Pose.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            var mesh = new Mesh();
            var triangles = skin.sharedMesh.triangles;
            var pairs = CameraGaitReviewTests.Pairs(triangles);
            var rows = new List<ShoulderSweepFrame>();
            var maximum = 0f;
            var frame = 0;
            var exportAllFrames = Environment.GetEnvironmentVariable("PARTY_SHOULDER_EXPORT_ALL_FRAMES") == "1";
            var folder = Path.Combine(Output, "ShoulderSweep");
            Directory.CreateDirectory(folder);
            var rt = new RenderTexture(900, 800, 24); rt.Create();
            var photo = new Texture2D(900, 800, TextureFormat.RGB24, false);
            _pair.View.targetTexture = rt; _pair.View.aspect = 900f / 800f; _pair.View.orthographicSize = .46f;

            void Export(string name, Vector3[] positions)
            {
                using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, name + "_Vertices.bin"))))
                {
                    writer.Write(positions.Length);
                    foreach (var p in positions) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                }
                ExportSurfacePose(skin, name);
                using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, name + "_BlendWeights.bin"))))
                {
                    writer.Write(skin.sharedMesh.blendShapeCount);
                    for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++) writer.Write(skin.GetBlendShapeWeight(i) / 100f);
                }
            }

            IEnumerator Capture(string phase, float lift, float flex)
            {
                _pair.Present(1); skin.BakeMesh(mesh);
                var positions = mesh.vertices;
                var angle = CameraGaitReviewTests.Surface(positions, triangles, pairs, out var point);
                rows.Add(new ShoulderSweepFrame { frame = frame, phase = phase,
                    requestedLift = lift, requestedFlex = flex, surfaceAngle = angle, worstPoint = point });
                if (angle > maximum) { maximum = angle; Export("SweepWorst", positions); }
                if (exportAllFrames || frame % 30 == 0) Export($"Sweep_{frame:D4}", positions);
                if (frame % 4 == 0)
                {
                    foreach (var view in new[] { "Front", "ThreeQuarter" })
                    {
                        var offset = view == "Front" ? new Vector3(0, .03f, 3) : new Vector3(-2, .1f, 3);
                        var center = rig.Actor.Motor.Body.position + Vector3.up * .55f;
                        _pair.View.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                        yield return null; _pair.View.Render();
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        photo.ReadPixels(new Rect(0, 0, 900, 800), 0, 0); photo.Apply(); RenderTexture.active = previous;
                        File.WriteAllBytes(Path.Combine(folder, $"{view}_{frame / 4:D4}.png"), photo.EncodeToPNG());
                    }
                }
                frame++;
            }

            foreach (var flex in new[] { 0f, 90f })
            {
                _pair.ResetReview(); rig.IndependentHands = false;
                rig.TargetExercise = ActiveUpperBodyReview.Exercise.Reach;
                for (var tick = 0; tick < 180; tick++)
                {
                    var lift = 60f * (1 - Mathf.Cos(2 * Mathf.PI * tick / 179f));
                    var bend = flex * Mathf.SmoothStep(0, 1, Mathf.Min(tick, 179 - tick) / 25f);
                    _pair.Step(Dt);
                    foreach (var side in new[] { "L", "R" })
                    foreach (var part in new[] { "UpperArm_", "Forearm_" })
                    {
                        var link = rig.Find(part + side);
                        link.Joint.targetRotation = Quaternion.AngleAxis(part == "UpperArm_" ? lift : bend, Vector3.right);
                        var drive = link.Drive; drive.positionSpring *= 4; drive.positionDamper *= 2; drive.maximumForce *= 3;
                        link.Joint.slerpDrive = drive;
                    }
                    _scene.GetPhysicsScene().Simulate(Dt); _pair.CapturePhysics();
                    yield return Capture(flex == 0 ? "StraightArmLift" : "BentArmLift", lift, bend);
                }
            }
            _pair.ResetReview(); rig.IndependentHands = true; rig.TargetExercise = ActiveUpperBodyReview.Exercise.Rest;
            for (var tick = 0; tick < 60; tick++) Tick();
            Tap(0, 0);
            for (var tick = 0; tick < 60; tick++) { Tick(); yield return Capture("Punch", -1, -1); }

            File.WriteAllText(Path.Combine(Output, "ShoulderSweep.json"), JsonUtility.ToJson(new ShoulderSweepReport
                { model = Model, maximumSurfaceAngle = maximum, frames = rows.ToArray() }, true));
            _pair.View.targetTexture = null; rt.Release();
            UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(photo); UnityEngine.Object.Destroy(mesh);
            Assert.That(maximum, Is.LessThan(60), "Continuous pose transitions must not introduce a sharp folded band.");
        }
    }
}
