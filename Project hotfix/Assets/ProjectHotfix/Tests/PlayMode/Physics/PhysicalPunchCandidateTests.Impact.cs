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
        [Serializable] private sealed class ReactionFrame
        {
            public int tick,hits;public bool reacting;public float chest,head,control;public Vector3 root;
        }
        [Serializable] private sealed class ReactionRun
        {
            public string mode;public ReactionFrame[] frames;public float maximumGap;public PhysicalPunchReview.Hit[] hits;
        }
        [Serializable] private sealed class ReactionReport {public ReactionRun[] runs;}
        [UnityTest,Timeout(300000)] public IEnumerator BodyStrikeReactionsFollowContactAndRecover()
        {
            var results=new List<ReactionRun>();
            foreach(var mode in new[]{"Left","Right","Head","Miss","Wall"})
            {
                Create(mode=="Miss"?.8f:mode=="Head"?.42f:.38f,mode=="Head"?.22f:0,mode=="Wall");
                _pair.UseStrikeStyle(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);
                var target=_pair.Players[1].Rig;var root=target.Actor.Motor.Body;
                var chest=target.Find("Chest");var head=target.Find("Head");
                var chestRest=Quaternion.Inverse(root.rotation)*chest.Body.rotation;
                var headRest=Quaternion.Inverse(root.rotation)*head.Body.rotation;
                var run=new ReactionRun{mode=mode,frames=new ReactionFrame[100]};
                Tap(0,mode=="Right"?1:0);
                for(var tick=0;tick<100;tick++)
                {
                    Tick();var hits=_pair.PhysicalPunches.Hits.Count;
                    run.frames[tick]=new ReactionFrame{tick=tick,hits=hits,reacting=target.ImpactReactionActive,control=target.ImpactMovementControl,root=root.position,chest=Quaternion.Angle(chestRest,Quaternion.Inverse(root.rotation)*chest.Body.rotation),head=Quaternion.Angle(headRest,Quaternion.Inverse(root.rotation)*head.Body.rotation)};
                    if(hits==0)Assert.That(target.ImpactReactionActive,Is.False,"No victim response before contact");
                    if(_pair.PhysicalPunches.Hits.Any(hit=>hit.tick==_pair.Tick))Assert.That(target.ImpactReactionActive,Is.True,"Confirmed contact starts the victim response on that physics tick");
                }
                run.hits=_pair.PhysicalPunches.Hits.ToArray();run.maximumGap=target.MaximumJointGap;results.Add(run);
                File.WriteAllText(Path.Combine(Output,"ImpactReactions.json"),JsonUtility.ToJson(new ReactionReport{runs=results.ToArray()},true));
                if(mode=="Miss"||mode=="Wall")Assert.That(run.hits.Length,Is.Zero);
                else
                {
                    Assert.That(run.hits.Length,Is.EqualTo(1));
                    Assert.That(run.frames.Max(f=>f.chest),Is.GreaterThan(8),"A landed blow visibly changes the torso pose");
                    Assert.That(run.frames.Min(f=>f.control),Is.LessThan(.3f),"The movement motor must allow impact momentum");
                    Assert.That(run.frames[99].chest,Is.LessThan(8),"Return to balance after the bounded reaction");
                }
                Assert.That(target.ImpactReactionActive,Is.False);
                Assert.That(target.Find("Chest").Joint.angularYLimit.limit,Is.EqualTo(5));
                Assert.That(run.maximumGap,Is.LessThan(.025f));
                Assert.That(_pair.PhysicalPunches.ConfirmedCues,Is.EqualTo(run.hits.Length));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator BodyStrikeSuppressionClearsReactionAndPelvisOffset()
        {
            Create(.38f);_pair.UseStrikeStyle(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);
            Tap(0,0);for(var tick=0;tick<14;tick++)Tick();
            Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(1));
            Assert.That(_pair.Players[1].Rig.ImpactReactionActive,Is.True);
            _pair.SetSuppressed(true);
            foreach(var player in _pair.Players)
            {
                Assert.That(player.Rig.ImpactReactionActive,Is.False);
                Assert.That(player.Rig.Actor.Pose.AdditionalPelvisYaw,Is.Zero);
                Assert.That(player.Rig.Actor.Pose.AdditionalPelvisDrop,Is.Zero);
                Assert.That(player.Rig.Find("Chest").Joint.angularYLimit.limit,Is.EqualTo(5));
                foreach(var link in player.Rig.Links)Assert.That(Quaternion.Angle(link.Joint.targetRotation,Quaternion.identity),Is.LessThan(.01f));
            }
            yield return null;
        }
        [UnityTest] public IEnumerator CaptureRibcageRaisedArmEnvelope()
        {
            Create(.8f);
            var rig=_pair.Players[0].Rig;
            var skin=rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
            var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);
            var rows=new List<string>();
            yield return ShoulderPhoto("Idle_Back",new Vector3(0,0,-3));
            foreach(var angle in new[]{45f,90f})
            {
                for(var tick=0;tick<90;tick++)
                {
                    _pair.Step(Dt);
                    foreach(var side in new[]{"L","R"})foreach(var name in new[]{"UpperArm_","Forearm_"})
                    {
                        var link=rig.Find(name+side);link.Joint.targetRotation=Quaternion.AngleAxis(name=="UpperArm_"?angle:5,Vector3.right);
                        var drive=link.Drive;drive.positionSpring*=4;drive.positionDamper*=2;drive.maximumForce*=3;link.Joint.slerpDrive=drive;
                    }
                    _scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();
                }
                skin.BakeMesh(mesh);var surface=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _);
                ExportSurfacePose(skin,"Raised_"+angle);
                rows.Add($"armRaise={angle:R}, maximumAdjacentFaceAngle={surface:R}");
                yield return ShoulderPhoto("Raised_"+angle+"_Side",new Vector3(-3,0,0));
                yield return ShoulderPhoto("Raised_"+angle+"_ThreeQuarter",new Vector3(-2,.25f,3));
                Assert.That(surface,Is.LessThan(60));
            }
            File.WriteAllLines(Path.Combine(Output,"RaisedArms.txt"),rows);UnityEngine.Object.Destroy(mesh);
        }
    }
}
