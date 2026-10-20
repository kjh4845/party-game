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
    public sealed partial class PhysicalPunchCandidateTests
    {
        [Serializable] private sealed class ArcSample
        {
            public int tick, hits;
            public Vector3 tip, targetRoot;
            public float speed;
        }
        [Serializable] private sealed class ArcProbe
        {
            public string style;
            public float distance, surface;
            public Vector3 worstSurfacePoint;
            public int side;
            public int contacts, outside, glancing, motion;
            public ArcSample[] frames;
            public PhysicalPunchReview.Hit[] hits;
        }
        [Serializable] private sealed class ArcProbes { public ArcProbe[] runs; }

        [UnityTest, Timeout(300000)]
        public IEnumerator ProbeArcStrikeContacts()
        {
            var runs = new List<ArcProbe>();
            foreach (var style in new[] { ActiveUpperBodyReview.ReviewStrikeStyle.SideSwing, ActiveUpperBodyReview.ReviewStrikeStyle.DiagonalSlam })
                foreach (var distance in new[] { .34f, .38f, .42f, .46f, .50f })
                    foreach (var side in new[] { 0, 1 })
                    {
                        Create(distance);
                        foreach (var player in _pair.Players) player.Rig.StrikeStyle = style;
                        var rig = _pair.Players[0].Rig;
                        var body = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.name.EndsWith("SkinnedBody"));
                        var mesh = new Mesh();
                        var triangles = body.sharedMesh.triangles;
                        var pairs = CameraGaitReviewTests.Pairs(triangles);
                        var probe = new ArcProbe { style = style.ToString(), distance = distance, side = side, frames = new ArcSample[40] };
                        Tap(0, side);
                        for (var tick = 0; tick < 40; tick++)
                        {
                            Tick();
                            probe.frames[tick] = new ArcSample { tick = tick, hits = _pair.PhysicalPunches.Hits.Count, tip = rig.HandPosition(side), targetRoot = _pair.Players[1].Rig.Actor.Motor.Body.position, speed = rig.Find("Forearm_" + (side == 0 ? "L" : "R")).Body.GetPointVelocity(rig.HandPosition(side)).magnitude };
                            if (tick % 4 == 0)
                            {
                                body.BakeMesh(mesh);
                                var angle=CameraGaitReviewTests.Surface(mesh.vertices, triangles, pairs, out var location);
                                if(angle>probe.surface)
                                {
                                    probe.surface=angle;probe.worstSurfacePoint=location;
                                    if(Mathf.Abs(distance-.5f)<.001f&&side==0)
                                    {
                                        var prefix=style.ToString()+"_Worst";
                                        using(var writer=new BinaryWriter(File.Create(Path.Combine(Output,prefix+"_Vertices.bin"))))
                                        {writer.Write(mesh.vertexCount);foreach(var point in mesh.vertices){writer.Write(point.x);writer.Write(point.y);writer.Write(point.z);}}
                                        using(var writer=new BinaryWriter(File.Create(Path.Combine(Output,prefix+"_Matrices.bin"))))
                                        {writer.Write(body.bones.Length);for(var b=0;b<body.bones.Length;b++){var m=body.transform.worldToLocalMatrix*body.bones[b].localToWorldMatrix*body.sharedMesh.bindposes[b];for(var row=0;row<4;row++)for(var col=0;col<4;col++)writer.Write(m[row,col]);}}
                                        yield return ShoulderPhoto(prefix,new Vector3(-2,.3f,3));
                                    }
                                }
                            }
                        }
                        probe.hits = _pair.PhysicalPunches.Hits.ToArray();
                        probe.contacts=_pair.PhysicalPunches.PhysicalContactsInWindow;
                        probe.outside=_pair.PhysicalPunches.RejectedOutsideTip;
                        probe.glancing=_pair.PhysicalPunches.RejectedGlancing;
                        probe.motion=_pair.PhysicalPunches.RejectedMotion;
                        runs.Add(probe);
                        File.WriteAllText(Path.Combine(Output, "ArcContacts.json"), JsonUtility.ToJson(new ArcProbes { runs = runs.ToArray() }, true));
                        Assert.That(_pair.PhysicalPunches.State(0).PunchCount, Is.EqualTo(1));
                        Assert.That(_pair.PhysicalPunches.ConfirmedCues, Is.EqualTo(probe.hits.Length));
                        Assert.That(probe.hits.All(hit => hit.tipDistance <= .08201f), Is.True);
                        UnityEngine.Object.Destroy(mesh);
                        var operation = SceneManager.UnloadSceneAsync(_scene);
                        while (!operation.isDone) yield return null;
                    }
        }

        [UnityTest]
        public IEnumerator ArcCancelAndResetRestoreJointLimits()
        {
            foreach (var style in new[] { ActiveUpperBodyReview.ReviewStrikeStyle.SideSwing, ActiveUpperBodyReview.ReviewStrikeStyle.DiagonalSlam })
                foreach (var reset in new[] { false, true })
                {
                    Create(.7f);
                    foreach (var player in _pair.Players) player.Rig.StrikeStyle = style;
                    var rig = _pair.Players[0].Rig;
                    var shoulder = rig.Find("UpperArm_L").Joint;
                    var originalX = shoulder.highAngularXLimit.limit;
                    var originalChestY = rig.Find("Chest").Joint.angularYLimit.limit;
                    Tap(0, 0);
                    for (var i = 0; i < 8; i++) Tick();
                    Assert.That(shoulder.angularYLimit.limit, Is.GreaterThan(35));
                    if (reset) _pair.ResetReview(); else _pair.SetSuppressed(true);
                    Assert.That(shoulder.highAngularXLimit.limit, Is.EqualTo(originalX));
                    Assert.That(shoulder.angularYLimit.limit, Is.EqualTo(15));
                    Assert.That(rig.Find("Chest").Joint.angularYLimit.limit, Is.EqualTo(originalChestY));
                    Assert.That(_pair.PhysicalPunches.Hits.Count, Is.Zero);
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    while (!operation.isDone) yield return null;
                }
        }
    }
}
