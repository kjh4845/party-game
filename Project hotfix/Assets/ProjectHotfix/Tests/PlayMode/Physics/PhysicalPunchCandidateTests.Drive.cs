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
        [UnityTest] public IEnumerator RemovedOpponentDoesNotBreakPunchReference()
        {
            Create(.7f);Tap(0,0);Tick();UnityEngine.Object.Destroy(_pair.Players[1].Rig.gameObject);yield return null;
            for(var i=0;i<40;i++)Tick();
            Assert.That(_pair.PhysicalPunches.Hits.Count,Is.Zero);Assert.That(_pair.PhysicalPunches.IsPunching(0,0),Is.False);
        }
        [UnityTest] public IEnumerator ContactUsesSolvedPoseAndCancelClearsTrunkDrive()
        {
            Create(.38f);Tap(0,0);for(var i=0;i<20&&_pair.PhysicalPunches.Hits.Count==0;i++)Tick();
            Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(1));
            var contactRig=_pair.Players[0].Rig;
            foreach(var name in new[]{"UpperArm_L","Forearm_L"})
            {
                var link=contactRig.Find(name);var observed=Quaternion.Inverse(link.ParentBody.rotation)*link.Body.rotation;
                var actual=Quaternion.Inverse(link.JointFrame)*Quaternion.Inverse(observed)*link.StartRelative*link.JointFrame;
                Assert.That(Quaternion.Angle(link.Joint.targetRotation,actual),Is.LessThan(.06f),"Contact target is the solved joint pose");
            }
            var held=contactRig.Find("Forearm_L").Joint.targetRotation;Tick();
            Assert.That(Quaternion.Angle(contactRig.Find("Forearm_L").Joint.targetRotation,held),Is.LessThan(.06f));
            Assert.That(contactRig.Find("Forearm_L").Joint.targetAngularVelocity.sqrMagnitude,Is.LessThan(1e-8f));
            var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            Create(.7f);Tap(0,0);for(var i=0;i<9;i++)Tick();var rig=_pair.Players[0].Rig;
            Assert.That(Quaternion.Angle(rig.Find("Spine").Joint.targetRotation,Quaternion.identity),Is.GreaterThan(1));
            _pair.enabled=false;
            foreach(var name in new[]{"Spine","Chest"}){var link=rig.Find(name);Assert.That(Quaternion.Angle(link.Joint.targetRotation,Quaternion.identity),Is.LessThan(.01f));Assert.That(link.Joint.slerpDrive.positionSpring,Is.EqualTo(link.Drive.positionSpring));}
            foreach(var name in new[]{"UpperArm_L","Forearm_L"})Assert.That(rig.Find(name).Body.maxAngularVelocity,Is.EqualTo(18));
        }
        [Serializable] private sealed class DriveFrame
        {
            public int tick,hits;public float time,elbow,goalElbow,reach,forwardSpeed,upperError,forearmError;
            public Vector3 tip,goal,root,targetRoot;
        }
        [Serializable] private sealed class DriveRun{public string mode;public float distance;public int side;public DriveFrame[] frames;public PhysicalPunchReview.Hit[] hits;}
        [Serializable] private sealed class DriveReport{public DriveRun[] runs;}
        [UnityTest,Timeout(600000)] public IEnumerator TracePhysicalStrikeTravel()
        {
            var runs=new List<DriveRun>();
            foreach(var mode in new[]{"Far","Near","Mid","MidRight","Head"})
            {
                var distance=mode=="Far"?.7f:mode=="Near"?.38f:.48f;var side=mode=="MidRight"?1:0;
                Create(distance,mode=="Head"?.22f:0);var rig=_pair.Players[0].Rig;var upper=rig.Find("UpperArm_"+(side==0?"L":"R"));var forearm=rig.Find("Forearm_"+(side==0?"L":"R"));
                var length=Vector3.Distance(upper.Goal.position,forearm.Goal.position)+Vector3.Distance(forearm.Goal.position,rig.GoalHand(side));var run=new DriveRun{mode=mode,distance=distance,side=side,frames=new DriveFrame[60]};
                Tap(0,side);
                for(var tick=0;tick<60;tick++)
                {
                    Tick();var relative=Quaternion.Inverse(forearm.ParentBody.rotation)*forearm.Body.rotation;
                    var q=Quaternion.Inverse(forearm.JointFrame)*Quaternion.Inverse(relative)*forearm.StartRelative*forearm.JointFrame;
                    run.frames[tick]=new DriveFrame{tick=tick,hits=_pair.PhysicalPunches.Hits.Count,time=(float)_pair.PhysicalPunches.State(0).PunchAge(side),elbow=Mathf.DeltaAngle(0,2*Mathf.Atan2(q.x,q.w)*Mathf.Rad2Deg),goalElbow=Quaternion.Angle(forearm.Joint.targetRotation,Quaternion.identity),reach=Vector3.Distance(upper.Body.position,rig.HandPosition(side))/length,forwardSpeed=Vector3.Dot(forearm.Body.GetPointVelocity(rig.HandPosition(side)),rig.Actor.Motor.Body.rotation*Vector3.forward),upperError=rig.TrackingError(upper.Name),forearmError=rig.TrackingError(forearm.Name),tip=rig.HandPosition(side),goal=rig.GoalHand(side),root=rig.Actor.Motor.Body.position,targetRoot=_pair.Players[1].Rig.Actor.Motor.Body.position};
                }
                run.hits=_pair.PhysicalPunches.Hits.ToArray();runs.Add(run);var revision=Environment.GetEnvironmentVariable("PUNCH_REVIEW_REVISION")??"Candidate";
                File.WriteAllText(Path.Combine(Output,"Travel_"+revision+".json"),JsonUtility.ToJson(new DriveReport{runs=runs.ToArray()},true));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
