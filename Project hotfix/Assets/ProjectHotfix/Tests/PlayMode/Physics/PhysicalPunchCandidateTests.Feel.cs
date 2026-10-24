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
        [Serializable] private sealed class FeelFrame
        {
            public int frame,hits; public float time,speed,elbow,upper,spring,damper; public Vector3 tip,root;
        }
        [Serializable] private sealed class FeelRun
        {
            public string mode; public int blocked; public FeelFrame[] frames; public ProjectHotfix.PhysicsJump.PhysicalPunchReview.Hit[] hits;
        }
        [UnityTest] public IEnumerator SolvedContactYieldsThenRetractsAndClears()
        {
            foreach(var mode in new[]{"Near","Mid","Wall"})foreach(var side in new[]{0,1})
            {
                Create(mode=="Mid"?.48f:.38f,0,mode=="Wall");var rig=_pair.Players[0].Rig;Tap(0,side);
                for(var i=0;i<20&&!rig.PunchHasContact(side);i++)Tick();
                Assert.That(rig.PunchHasContact(side),Is.True,mode+" side="+side);
                Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(mode=="Wall"?0:1),mode);
                if(mode=="Wall")Assert.That(_pair.PhysicalPunches.BlockedContacts,Is.EqualTo(1));
                var forward=rig.Actor.Motor.Body.rotation*Vector3.forward;
                // A wall can push the whole character backwards. Measuring the
                // fist relative to that moving root falsely reports penetration.
                var contactFrame=mode=="Wall"?Vector3.zero:rig.Actor.Motor.Body.position;
                var origin=Vector3.Dot(rig.HandPosition(side)-contactFrame,forward);var furthest=origin;
                for(var i=0;i<9;i++)
                {
                    Tick();contactFrame=mode=="Wall"?Vector3.zero:rig.Actor.Motor.Body.position;
                    furthest=Mathf.Max(furthest,Vector3.Dot(rig.HandPosition(side)-contactFrame,forward));
                }
                var returned=Vector3.Dot(rig.HandPosition(side)-contactFrame,forward);
                Assert.That(furthest-origin,Is.LessThan(.04f),mode+" contact must not keep driving through");
                Assert.That(origin-returned,Is.GreaterThan(.07f),mode+" physical hand must retract within 150ms");
                _pair.SetSuppressed(true);
                Assert.That(rig.PunchHasContact(side),Is.False);
                foreach(var part in new[]{"UpperArm_","Forearm_"})
                {
                    var link=rig.Find(part+(side==0?"L":"R"));
                    Assert.That(link.Joint.targetAngularVelocity.sqrMagnitude,Is.LessThan(1e-8f));
                    Assert.That(link.Joint.slerpDrive.positionSpring,Is.EqualTo(link.Drive.positionSpring));
                }
                _pair.ResetReview();Assert.That(_pair.PhysicalPunches.BlockedContacts,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator ContactTransitionNeverWritesRigidbodyMotion()
        {
            Create(.7f);Tap(0,0);for(var i=0;i<8;i++)Tick();var rig=_pair.Players[0].Rig;
            var bodies=rig.Links.Select(l=>l.Body).ToArray();var positions=bodies.Select(b=>b.position).ToArray();var rotations=bodies.Select(b=>b.rotation).ToArray();var velocities=bodies.Select(b=>b.linearVelocity).ToArray();var angular=bodies.Select(b=>b.angularVelocity).ToArray();
            rig.ReceivePunchImpact(0);
            for(var i=0;i<bodies.Length;i++)
            {Assert.That(bodies[i].position,Is.EqualTo(positions[i]));Assert.That(bodies[i].rotation,Is.EqualTo(rotations[i]));Assert.That(bodies[i].linearVelocity,Is.EqualTo(velocities[i]));Assert.That(bodies[i].angularVelocity,Is.EqualTo(angular[i]));}
            yield return null;
        }
        [UnityTest] public IEnumerator ProbeNonTipContactGeometry()
        {
            var rows=new List<string>();
            foreach(var drop in new[]{.22f,.25f,.27f,.29f,.31f,.33f,.35f})
            {
                Create(.38f,drop);Tap(0,0);for(var tick=0;tick<40;tick++)Tick();var p=_pair.PhysicalPunches;
                rows.Add("drop="+drop+" contacts="+p.PhysicalContactsInWindow+" radiusReject="+p.RejectedOutsideTip+" motionReject="+p.RejectedMotion+" glancingReject="+p.RejectedGlancing+" hits="+p.Hits.Count);
                foreach(var hit in p.Hits)rows.Add(JsonUtility.ToJson(hit));
                File.WriteAllLines(Path.Combine(Output,"NonTipGeometry.txt"),rows);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator CapturePunchFeelComparison()
        {
            foreach(var mode in new[]{"Air","Near","Mid","Wall"})
            {
                Create(mode=="Air"?.7f:mode=="Mid"?.48f:.38f,0,mode=="Wall");
                Directory.CreateDirectory(Path.Combine(Output,mode));var rows=new List<FeelFrame>();var rig=_pair.Players[0].Rig;
                for(var frame=0;frame<90;frame++)
                {
                    if(frame==10)Buttons(0,1);if(frame==12)Buttons(0,0);
                    if(frame==50)Buttons(0,2);if(frame==52)Buttons(0,0);
                    Tick();var side=frame<50?0:1;var lower=rig.Find(side==0?"Forearm_L":"Forearm_R");var upper=rig.Find(side==0?"UpperArm_L":"UpperArm_R");
                    rows.Add(new FeelFrame{frame=frame,hits=_pair.PhysicalPunches.Hits.Count,time=(float)_pair.PhysicalPunches.State(0).PunchAge(side),tip=rig.HandPosition(side),root=rig.Actor.Motor.Body.position,speed=Vector3.Dot(lower.Body.GetPointVelocity(rig.HandPosition(side)),rig.Actor.Motor.Body.rotation*Vector3.forward),elbow=Quaternion.Angle(lower.Joint.targetRotation,Quaternion.identity),upper=Mathf.DeltaAngle(0,upper.Joint.targetRotation.eulerAngles.x),spring=lower.Joint.slerpDrive.positionSpring,damper=lower.Joint.slerpDrive.positionDamper});
                    yield return SidePhoto(mode+"/Frame_"+frame.ToString("D3"));
                }
                File.WriteAllText(Path.Combine(Output,mode+".json"),JsonUtility.ToJson(new FeelRun{mode=mode,blocked=_pair.PhysicalPunches.BlockedContacts,frames=rows.ToArray(),hits=_pair.PhysicalPunches.Hits.ToArray()},true));
                if(mode=="Air"||mode=="Wall")Assert.That(_pair.PhysicalPunches.Hits.Count,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
