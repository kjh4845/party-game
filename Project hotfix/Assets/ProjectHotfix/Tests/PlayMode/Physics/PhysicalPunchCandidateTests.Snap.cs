using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        [UnityTest] public IEnumerator ProbeSnapHeadContacts()
        {
            var rows=new List<string>();
            foreach(var distance in new[]{.34f,.38f,.42f})foreach(var drop in new[]{.18f,.22f,.26f})foreach(var side in new[]{0,1})
            {
                Create(distance,drop);Tap(0,side);for(var i=0;i<40;i++)Tick();var punch=_pair.PhysicalPunches;
                rows.Add($"distance={distance} drop={drop} side={side} hits={punch.Hits.Count} points={punch.Score(1).Points} contact={punch.PhysicalContactsInWindow} radius={punch.RejectedOutsideTip} motion={punch.RejectedMotion}");
                foreach(var h in punch.Hits)rows.Add(JsonUtility.ToJson(h));File.WriteAllLines(Path.Combine(Output,"HeadGrid.txt"),rows);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator SnapTargetsStayInsideElbowLimitsAndSettle()
        {
            var rows=new List<string>();
            foreach(var side in new[]{0,1})
            {
                Create(.7f);var rig=_pair.Players[0].Rig;var upper=rig.Find("UpperArm_"+(side==0?"L":"R"));var forearm=rig.Find("Forearm_"+(side==0?"L":"R"));
                var positions=new List<Vector3>();var maxSwing=0f;var last=0f;var preparedFlex=0f;var strikeSpeed=0f;Tap(0,side);
                // The combat guard deliberately remains after the 0.4s action.
                // Observe its timeout and the physical return to idle as well.
                for(var i=0;i<120;i++)
                {
                    Tick();positions.Add(rig.HandPosition(side));var q=forearm.Joint.targetRotation;
                    if(_pair.PhysicalPunches.IsPunching(0,side))
                    {
                        var swing=2*Mathf.Acos(Mathf.Clamp01(Mathf.Sqrt(q.x*q.x+q.w*q.w)))*Mathf.Rad2Deg;maxSwing=Mathf.Max(maxSwing,swing);
                        Assert.That(swing,Is.LessThan(.15f),"No unreachable sideways elbow target");
                        Assert.That(Quaternion.Angle(q,Quaternion.identity),Is.LessThanOrEqualTo(130));
                        var age=_pair.PhysicalPunches.State(0).PunchAge(side);
                        var actual=Quaternion.Inverse(forearm.JointFrame)*Quaternion.Inverse(Quaternion.Inverse(forearm.ParentBody.rotation)*forearm.Body.rotation)*forearm.StartRelative*forearm.JointFrame;
                        if(age<=.101)preparedFlex=Mathf.Max(preparedFlex,Mathf.DeltaAngle(0,2*Mathf.Atan2(actual.x,actual.w)*Mathf.Rad2Deg));
                        if(age>.10&&age<.18)strikeSpeed=Mathf.Max(strikeSpeed,Vector3.Dot(forearm.Body.GetPointVelocity(rig.HandPosition(side)),rig.Actor.Motor.Body.rotation*Vector3.forward));
                        if(i==11)Assert.That(rig.TrackingError(upper.Name),Is.LessThan(10),"The larger withdrawal arc stays within ten degrees of the reachable shoulder goal");
                    }
                    Assert.That(forearm.Joint.angularYLimit.limit,Is.EqualTo(4));Assert.That(forearm.Joint.angularZLimit.limit,Is.EqualTo(4));
                    Assert.That(forearm.Joint.angularXMotion,Is.EqualTo(ConfigurableJointMotion.Limited));
                    if(i==23)Assert.That(_pair.PhysicalPunches.IsPunching(0,side),Is.False,"0.4 seconds at fixed 60 Hz");
                    if(i>110)last=Mathf.Max(last,Vector3.Distance(positions[i],positions[i-1]));
                }
                var maxAcceleration=0f;for(var i=1;i<positions.Count-1;i++)maxAcceleration=Mathf.Max(maxAcceleration,(positions[i-1]-2*positions[i]+positions[i+1]).magnitude/(Dt*Dt));
                Assert.That(preparedFlex,Is.GreaterThan(105));Assert.That(strikeSpeed,Is.GreaterThan(4.5));
                for(var i=1;i<positions.Count;i++)Assert.That(Vector3.Distance(positions[i],positions[i-1]),Is.LessThan(.19f),"No single-step pose jump across half an arm length");
                Assert.That(last,Is.LessThan(.001));Assert.That(_pair.PhysicalPunches.Hits.Count+_pair.PhysicalPunches.ConfirmedCues,Is.Zero);
                Assert.That(upper.Joint.angularYLimit.limit,Is.EqualTo(15));Assert.That(forearm.Body.maxAngularVelocity,Is.EqualTo(18));rows.Add($"side={side} maxElbowSwing={maxSwing:R} peakTipAcceleration={maxAcceleration:R} lastStepMotion={last:R} preparedFlex={preparedFlex:R} strikeSpeed={strikeSpeed:R}");
                File.WriteAllLines(Path.Combine(Output,"Stability.txt"),rows);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [Test] public void ExportConfirmedHitAudio()
        {
            Directory.CreateDirectory(Output);
            foreach(var head in new[]{false,true})
            {
                var pcm=ProjectHotfix.PhysicsJump.PhysicalPunchFeedbackReview.Samples(head);Assert.That(pcm.All(float.IsFinite),Is.True);Assert.That(pcm.Max(Mathf.Abs),Is.LessThan(1));
                using(var w=new BinaryWriter(File.Create(Path.Combine(Output,head?"Head.wav":"Body.wav"))))
                {
                    var bytes=pcm.Length*2;w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+bytes);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(48000);w.Write(96000);w.Write((short)2);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(bytes);foreach(var x in pcm)w.Write((short)(x*32767));
                }
            }
        }
        [UnityTest] public IEnumerator ProbeJointSpacePunch()
        {
            var rows=new List<string>();
            foreach(var shoulder in new[]{45f,75f,90f})foreach(var elbow in new[]{-30f,0f,45f,90f})
            {
                Create(.7f);var rig=_pair.Players[0].Rig;var upper=rig.Find("UpperArm_L");var lower=rig.Find("Forearm_L");
                for(var i=0;i<60;i++)
                {
                    _pair.Step(Dt);upper.Joint.targetRotation=Quaternion.AngleAxis(shoulder,Vector3.right);lower.Joint.targetRotation=Quaternion.AngleAxis(elbow,Vector3.right);
                    foreach(var link in new[]{upper,lower}){var d=link.Drive;d.positionSpring*=4;d.positionDamper*=2;d.maximumForce*=3;link.Joint.slerpDrive=d;}
                    _scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();
                }
                rows.Add($"U={shoulder} E={elbow} tipLocal={rig.Actor.Motor.Body.transform.InverseTransformPoint(rig.HandPosition(0)):R} elbowLocal={rig.Actor.Motor.Body.transform.InverseTransformPoint(lower.Body.position):R} angular={lower.Body.angularVelocity:R} jointAxis={lower.Joint.axis:R}");
                File.WriteAllLines(Path.Combine(Output,"JointSpace.txt"),rows);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
