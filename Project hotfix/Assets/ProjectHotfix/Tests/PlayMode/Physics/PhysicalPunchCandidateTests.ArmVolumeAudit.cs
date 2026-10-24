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
        [Serializable] private sealed class ArmAuditPose
        {
            public string name;
            public float requestedLift, requestedElbow;
            public float[] elbowAngle, shoulderForward, shoulderUp;
            public string[] boneNames;
            public Vector3[] bonePositions;
        }
        [Serializable] private sealed class ArmAuditReport { public ArmAuditPose[] poses; }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureArmVolumeAudit()
        {
            // Measurement capture only. Completion is not a visual acceptance
            // or a claim that joint volume is preserved.
            Create(.9f);
            var rig = _pair.Players[0].Rig;
            var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s => s.name.EndsWith("SkinnedBody"));
            var records = new List<ArmAuditPose>();
            var mesh = new Mesh();
            foreach (var renderer in _pair.Players[1].Rig.Actor.Pose.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.gameObject.scene == _scene && light.enabled) light.transform.rotation = Quaternion.Euler(25, 150, 0);

            IEnumerator Capture(string label, float lift, float flex)
            {
                _pair.Present(1);
                skin.BakeMesh(mesh);
                var positions = mesh.vertices;
                using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, label + "_Vertices.bin"))))
                {
                    writer.Write(positions.Length);
                    foreach (var p in positions) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                }
                ExportSurfacePose(skin, label);
                var record = new ArmAuditPose { name = label, requestedLift = lift, requestedElbow = flex,
                    elbowAngle = new float[2], shoulderForward = new float[2], shoulderUp = new float[2],
                    boneNames = skin.bones.Select(b => b.name).ToArray(),
                    bonePositions = skin.bones.Select(b => skin.transform.InverseTransformPoint(b.position)).ToArray() };
                for (var side = 0; side < 2; side++)
                {
                    var forearm = rig.Find(side == 0 ? "Forearm_L" : "Forearm_R");
                    var observed = Quaternion.Inverse(forearm.ParentBody.rotation) * forearm.Body.rotation;
                    var joint = Quaternion.Inverse(forearm.JointFrame) * Quaternion.Inverse(observed) * forearm.StartRelative * forearm.JointFrame;
                    record.elbowAngle[side] = Mathf.Abs(Mathf.DeltaAngle(0, 2 * Mathf.Atan2(joint.x, joint.w) * Mathf.Rad2Deg));
                    record.shoulderForward[side] = rig.ShoulderProtraction(side);
                    record.shoulderUp[side] = rig.ShoulderElevation(side);
                }
                records.Add(record);
                File.WriteAllText(Path.Combine(Output, "ArmAuditPoses.json"), JsonUtility.ToJson(new ArmAuditReport { poses = records.ToArray() }, true));
                foreach (var view in new[] { "Front", "ThreeQuarter", "Side" })
                {
                    var offset = view == "Front" ? new Vector3(0, .03f, 3) : view == "Side" ? new Vector3(-3, .03f, 0) : new Vector3(-2, .1f, 3);
                    var center = rig.Actor.Motor.Body.position + Vector3.up * .55f;
                    _pair.View.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                    var rt = new RenderTexture(1100, 900, 24); rt.Create();
                    var photo = new Texture2D(1100, 900, TextureFormat.RGB24, false);
                    _pair.View.targetTexture = rt; _pair.View.aspect = 1100f / 900f; _pair.View.orthographicSize = .45f;
                    yield return null;
                    _pair.View.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
                    photo.ReadPixels(new Rect(0, 0, 1100, 900), 0, 0); photo.Apply(); RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(Output, label + "_" + view + ".png"), photo.EncodeToPNG());
                    _pair.View.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(photo);
                }
            }

            yield return Capture("Idle", 0, 0);
            foreach (var target in new[] { new Vector2(45, 0), new Vector2(90, 0), new Vector2(120, 0),
                                          new Vector2(90, 45), new Vector2(90, 90), new Vector2(90, 120) })
            {
                _pair.ResetReview();
                rig.IndependentHands = false;
                rig.TargetExercise = ActiveUpperBodyReview.Exercise.Reach;
                for (var tick = 0; tick < 100; tick++)
                {
                    _pair.Step(Dt);
                    foreach (var side in new[] { "L", "R" }) foreach (var part in new[] { "UpperArm_", "Forearm_" })
                    {
                        var link = rig.Find(part + side);
                        link.Joint.targetRotation = Quaternion.AngleAxis(part == "UpperArm_" ? target.x : target.y, Vector3.right);
                        var drive = link.Drive; drive.positionSpring *= 4; drive.positionDamper *= 2; drive.maximumForce *= 3;
                        link.Joint.slerpDrive = drive;
                    }
                    _scene.GetPhysicsScene().Simulate(Dt); _pair.CapturePhysics();
                }
                yield return Capture($"Lift{target.x}_Flex{target.y}", target.x, target.y);
            }
            _pair.ResetReview(); rig.IndependentHands = true; rig.TargetExercise = ActiveUpperBodyReview.Exercise.Rest;
            for (var tick = 0; tick < 60; tick++) Tick();
            Tap(0, 0);
            for (var tick = 0; tick < 25; tick++)
            {
                Tick();
                if (tick == 2 || tick == 8 || tick == 18) yield return Capture("Punch" + tick, -1, -1);
            }
            UnityEngine.Object.Destroy(mesh);
        }
    }
}
