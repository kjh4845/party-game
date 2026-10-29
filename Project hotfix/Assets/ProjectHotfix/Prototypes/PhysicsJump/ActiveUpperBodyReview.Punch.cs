using UnityEngine;
using System.Collections.Generic;

namespace ProjectHotfix.PhysicsJump
{
    public sealed partial class ActiveUpperBodyReview
    {
        private readonly float[] _punchImpactAge={-1,-1};
        private readonly bool[] _punchBlocked=new bool[2];
        private readonly Quaternion[,] _impactTarget=new Quaternion[2,2];
        private readonly Dictionary<Link,Quaternion> _punchPreviousTarget=new Dictionary<Link,Quaternion>();
        private readonly float[] _punchCompact=new float[2];
        // How much of the close-range hook a punch uses (0 = the normal stroke), fixed when it starts (PhysicalPunchReview).
        private readonly float[] _punchHook=new float[2];
        public float PunchHook(int side)=>_punchReference[side]?_punchHook[side]:0;
        private readonly float[] _guardRemaining=new float[2],_guardWeight=new float[2];
        private readonly Dictionary<Rigidbody,float> _hitReaction=new Dictionary<Rigidbody,float>();
        public bool PunchHasContact(int side)=>_punchReference[side]&&_punchImpactAge[side]>=0;
        public bool IsStriking=>_punchReference[0]||_punchReference[1];
        public bool IsStrikingArm(Link link)=>(link.Name.StartsWith("UpperArm")||link.Name.StartsWith("Forearm"))&&_punchReference[link.Name.EndsWith("_L")?0:1];
        public void ReceivePunchImpact(int side,bool blocked=false)
        {
            if(!_punchReference[side]||_punchImpactAge[side]>=0)return;
            _punchImpactAge[side]=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;
            _punchBlocked[side]=blocked;
            for(var part=0;part<2;part++)
            {
                var link=Find((part==0?"UpperArm_":"Forearm_")+(side==0?"L":"R"));
                // Capture the solved pose, not an extension target the wall/body
                // prevented us reaching. Keep all Rigidbody motion with PhysX.
                var observed=Quaternion.Inverse(link.ParentBody.rotation)*link.Body.rotation;
                var target=Quaternion.Inverse(link.JointFrame)*Quaternion.Inverse(observed)*link.StartRelative*link.JointFrame;
                _impactTarget[side,part]=target;
                link.Joint.targetRotation=target;
                _punchPreviousTarget[link]=target;
                link.Joint.targetAngularVelocity=Vector3.zero;
                var drive=link.Joint.slerpDrive;ApplyPunchContactDrive(side,ref drive);link.Joint.slerpDrive=drive;
            }
        }
        public void ReceivePunchReaction(Rigidbody body){if(_links.Exists(l=>l.Body==body))_hitReaction[body]=0;}
        private float HitReactionScale(Link link,float seconds)
        {
            if(!_hitReaction.TryGetValue(link.Body,out var age))return 1;
            age+=seconds;if(age>=.09f){_hitReaction.Remove(link.Body);return 1;}
            _hitReaction[link.Body]=age;return Mathf.Lerp(.72f,1,Smooth(age/.09f));
        }
        private void ApplyPunchBodyReference()
        {
            if(StrikeStyle==ReviewStrikeStyle.BodyStrike){ApplyBodyStrikeTorso();return;}
            if(StrikeStyle!=ReviewStrikeStyle.Straight&&(_gripPosture[0]||_gripPosture[1]||_handActive[0]||_handActive[1]))
            {RestoreStrikeTorsoLimit();return;}
            var arc=StrikeStyle!=ReviewStrikeStyle.Straight;
            if(arc&&(_punchReference[0]||_punchReference[1]))
            {
                var chest=Find("Chest");
                if(!_strikeTorsoLimit){_strikeOriginalChestY=chest.Joint.angularYLimit.limit;_strikeTorsoLimit=true;}
                chest.Joint.angularYLimit=new SoftJointLimit{limit=15};
            }
            else RestoreStrikeTorsoLimit();
            var lean=0f;var twist=0f;
            for(var side=0;side<2;side++)if(_punchReference[side])
            {
                var t=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;
                var weight=Smooth((t-.065f)/.075f)*(1-Smooth((t-.17f)/.12f));
                lean=Mathf.Max(lean,(arc?7:4.5f)*weight);twist+=(side==0?-6:6)*weight;
            }
            if(lean<=0&&Mathf.Abs(twist)<.0001f)return;
            // Small physical trunk contribution; no root movement or skin warp.
            SetJointGoal(Find("Spine"),Quaternion.AngleAxis(-lean,Vector3.right));
            SetJointGoal(Find("Chest"),Quaternion.AngleAxis(Mathf.Clamp(twist,-6,6),Vector3.up));
        }
        private void RestoreStrikeTorsoLimit()
        {
            if(!_strikeTorsoLimit)return;
            Find("Chest").Joint.angularYLimit=new SoftJointLimit{limit=_strikeOriginalChestY};_strikeTorsoLimit=false;
        }
        private void ApplyCombatGuard(float seconds)
        {
            if(LoweredCarriage&&StrikeStyle==ReviewStrikeStyle.BodyStrike){ApplyCounterSwing();return;}
            var attacking=_punchReference[0]||_punchReference[1];
            for(var side=0;side<2;side++)
            {
                if(_gripPosture[side]||_handActive[side]){_guardRemaining[side]=_guardWeight[side]=0;continue;}
                _guardRemaining[side]=attacking&&!_punchBlocked[side]?.7f:Mathf.Max(0,_guardRemaining[side]-seconds);
                _guardWeight[side]=Mathf.MoveTowards(_guardWeight[side],Mathf.Clamp01(_guardRemaining[side]/.2f),seconds/.09f);
                if(_punchReference[side]||_guardWeight[side]<=0)continue;
                var sign=side==0?-1:1;var weight=_guardWeight[side];
                SetJointGoal(Find("UpperArm_"+(side==0?"L":"R")),Quaternion.AngleAxis(32*weight,Vector3.right)*Quaternion.AngleAxis(sign*6*weight,Vector3.up));
                SetJointGoal(Find("Forearm_"+(side==0?"L":"R")),Quaternion.AngleAxis((StrikeStyle==ReviewStrikeStyle.BodyStrike?GuardFlex:100)*weight,Vector3.right));
            }
        }
        // The reference is authored in the actual joint axes. In particular,
        // elbow flexion has no swing target fighting its 4-degree side limit.
        private void ApplyJointSpacePunch(int side)
        {
            var suffix=side==0?"L":"R";var upper=Find("UpperArm_"+suffix);var forearm=Find("Forearm_"+suffix);
            if(_punchEntryPending[side])
            {
                _punchEntry[side][0]=Quaternion.Inverse(upper.Goal.parent.rotation)*upper.Body.rotation;
                _punchEntry[side][1]=Quaternion.Inverse(upper.Body.rotation)*forearm.Body.rotation;
                _punchImpactAge[side]=-1;
                _punchBlocked[side]=false;
                _punchEntryPending[side]=false;
            }
            var t=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;
            if(StrikeStyle!=ReviewStrikeStyle.Straight)
            {
                if(StrikeStyle==ReviewStrikeStyle.BodyStrike)ApplyBodyStrikeArm(side,upper,forearm,t);
                else ApplyArcStrike(side,upper,forearm,t);
                var blend=Smooth(t/.065f);
                upper.Goal.localRotation=Quaternion.Slerp(_punchEntry[side][0],upper.Goal.localRotation,blend);
                forearm.Goal.localRotation=Quaternion.Slerp(_punchEntry[side][1],forearm.Goal.localRotation,blend);
                return;
            }
            // Bring the fist to a compact guard in front of the chest. Moving
            // the elbow forward with the fold avoids the old low scooping arc.
            var compact=_punchCompact[side];var prepareLift=32f;
            var extensionLift=Mathf.Lerp(86,55,compact);var extensionFlex=Mathf.Lerp(8,30,compact);
            var strikeEnd=Mathf.Lerp(.142f,.132f,compact);var throughEnd=strikeEnd+.008f;
            float lift,flex,aim;
            if(t<throughEnd)
            {
                var shoulder=Smooth((t-.08f)/(strikeEnd-.08f));
                var extension=StrikeCurve((t-.08f)/(strikeEnd-.08f));
                lift=Mathf.Lerp(prepareLift,extensionLift,shoulder);
                flex=Mathf.Lerp(115,extensionFlex,extension);
                aim=Mathf.Lerp(.2f,1,shoulder);
            }
            else if(t<.26f){var k=Smooth((t-throughEnd)/(.26f-throughEnd));lift=Mathf.Lerp(extensionLift,prepareLift,k);flex=Mathf.Lerp(extensionFlex,105,k);aim=Mathf.Lerp(1,.3f,k);}
            else {lift=prepareLift;flex=105;aim=.3f;}
            var yaw=(side==0?-18:18)*aim;
            SetJointGoal(upper,Quaternion.AngleAxis(lift,Vector3.right)*Quaternion.AngleAxis(yaw,Vector3.up));
            SetJointGoal(forearm,Quaternion.AngleAxis(flex,Vector3.right));
            if(_punchImpactAge[side]>=0)
            {
                var age=t-_punchImpactAge[side];
                // Brief yielding contact, then retract from that physical pose.
                // The target moves away through the joint drives; no body lock.
                var recoil=_punchBlocked[side]?Smooth(age/.055f):Smooth((age-.025f)/.080f);
                var settle=_punchBlocked[side]?1-Smooth((age-.105f)/Mathf.Max(.07f,.395f-_punchImpactAge[side]-.105f)):1;
                var yawSign=side==0?-1:1;
                var returnFlex=(_punchBlocked[side]?110:100)*settle;
                var upperReturn=Quaternion.AngleAxis((_punchBlocked[side]?-25:prepareLift)*settle,Vector3.right)*Quaternion.AngleAxis(yawSign*6*settle,Vector3.up);
                var lowerReturn=Quaternion.AngleAxis(returnFlex,Vector3.right);
                SetJointGoal(upper,Quaternion.Slerp(_impactTarget[side,0],upperReturn,recoil));
                SetJointGoal(forearm,Quaternion.Slerp(_impactTarget[side,1],lowerReturn,recoil));
            }
            var entry=Smooth(t/.06f);
            upper.Goal.localRotation=Quaternion.Slerp(_punchEntry[side][0],upper.Goal.localRotation,entry);
            forearm.Goal.localRotation=Quaternion.Slerp(_punchEntry[side][1],forearm.Goal.localRotation,entry);
        }
        private void ApplyArcStrike(int side,Link upper,Link forearm,float t)
        {
            var sign=side==0?-1:1;
            var holding=_gripPosture[1-side]||_handActive[1-side];
            var slam=StrikeStyle==ReviewStrikeStyle.DiagonalSlam&&!holding;
            var strike=StrikeCurve((t-.075f)/.09f);
            var recover=Smooth((t-.175f)/.18f);
            var lift=Mathf.Lerp(slam?(holding?85:115):60,slam?(holding?78:62):78,strike);
            var yaw=slam?Mathf.Lerp(-sign*(holding?15:20),sign*(holding?15:20),strike):Mathf.Lerp(-sign*15,sign*15,strike);
            var roll=slam?0:Mathf.Lerp(sign*(holding?25:35),-sign*(holding?25:35),strike);
            var flex=Mathf.Lerp(slam?(holding?65:45):65,slam?(holding?50:28):50,strike);
            lift=Mathf.Lerp(lift,32,recover);yaw=Mathf.Lerp(yaw,sign*6,recover);roll*=1-recover;flex=Mathf.Lerp(flex,100,recover);
            var upperTarget=Quaternion.AngleAxis(lift,Vector3.right)*Quaternion.AngleAxis(yaw,Vector3.up)*Quaternion.AngleAxis(roll,Vector3.forward);
            var lowerTarget=Quaternion.AngleAxis(flex,Vector3.right);
            if(_punchImpactAge[side]>=0)
            {
                var age=t-_punchImpactAge[side];
                var rebound=Smooth((age-.02f)/.1f);
                upperTarget=Quaternion.Slerp(_impactTarget[side,0],Quaternion.AngleAxis(32,Vector3.right)*Quaternion.AngleAxis(sign*6,Vector3.up),rebound);
                lowerTarget=Quaternion.Slerp(_impactTarget[side,1],Quaternion.AngleAxis(100,Vector3.right),rebound);
            }
            SetJointGoal(upper,upperTarget);SetJointGoal(forearm,lowerTarget);
        }
        private void ApplyPunchVelocity(Link link,float seconds)
        {
            var arm=link.Name.StartsWith("UpperArm")||link.Name.StartsWith("Forearm");
            var side=link.Name.EndsWith("_L")?0:1;
            if(!arm||!_punchReference[side]||_gripPosture[side])
            {link.Joint.targetAngularVelocity=Vector3.zero;_punchPreviousTarget.Remove(link);return;}
            var target=link.Joint.targetRotation;
            if(_punchPreviousTarget.TryGetValue(link,out var previous))
            {
                var delta=target*Quaternion.Inverse(previous);
                if(delta.w<0)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
                delta.ToAngleAxis(out var angle,out var axis);
                // targetRotation is already the inverse relative rotation in joint
                // axes. Unity's velocity drive uses the same convention (rad/s).
                var velocity=angle>.0001f?axis*(Mathf.DeltaAngle(0,angle)*Mathf.Deg2Rad/seconds):Vector3.zero;
                var impactAge=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate-_punchImpactAge[side];
                if(_punchImpactAge[side]>=0&&impactAge<=.025f+.0001f)velocity=Vector3.zero;
                link.Joint.targetAngularVelocity=Vector3.ClampMagnitude(velocity,link.Name.StartsWith("UpperArm")?34:50);
            }
            else link.Joint.targetAngularVelocity=Vector3.zero;
            _punchPreviousTarget[link]=target;
        }
        private void ApplyPunchContactDrive(int side,ref JointDrive drive)
        {
            if(!PunchHasContact(side))return;
            var age=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate-_punchImpactAge[side];
            var restore=Smooth((age-.025f)/.075f);
            // A lowered-carriage body strike stays firm through contact (the .25 yield read as the
            // attacker being knocked back); a blocked stroke still yields.
            var firm=LoweredCarriage&&StrikeStyle==ReviewStrikeStyle.BodyStrike&&!_punchBlocked[side];
            drive.positionSpring*=Mathf.Lerp(firm?.7f:.25f,1,restore);
            drive.positionDamper*=Mathf.Lerp(firm?1.3f:2,1,restore);
            drive.maximumForce*=Mathf.Lerp(firm?.8f:.4f,1,restore);
        }
        private static float StrikeCurve(float value)
        {
            var t=Mathf.Clamp01(value);return t*t*t*(4-3*t);
        }
        private static float Smooth(float value)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(value));
        private void SetJointGoal(Link link,Quaternion jointTarget)
        {
            var parent=link.ParentName==null?_goalRoot:_goals[link.ParentName];
            var desired=link.StartRelative*link.JointFrame*Quaternion.Inverse(jointTarget)*Quaternion.Inverse(link.JointFrame);
            link.Goal.rotation=parent.rotation*desired;
        }
    }
}
