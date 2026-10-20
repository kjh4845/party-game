using System.Collections.Generic;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    public sealed partial class ActiveUpperBodyReview
    {
        private const float ImpactDuration=.46f;
        private float _impactAge=-1,_impactStrength,_impactSide;
        private Vector3 _impactLocalDirection;
        private string _impactPart;
        private bool _impactHeld,_bodyStrikePose;
        private struct CombatLimits {public SoftJointLimit Low,High,Y,Z;}
        private readonly Dictionary<Link,CombatLimits> _combatLimits=new Dictionary<Link,CombatLimits>();
        public bool ImpactReactionActive=>_impactAge>=0;
        public float ImpactReactionAge=>_impactAge;
        // The shove reads as a stagger only if the feet do not brake it at once (control returns
        // over .12-.36 s; was .06-.22 s before the 2026-10-16 stronger knockback).
        public float ImpactMovementControl=>!ImpactReactionActive?1:Mathf.Lerp(.1f,1,Smooth((_impactAge-.12f)/.24f));
        public Vector3 BodyStrikeAcceleration
        {
            get
            {
                if(StrikeStyle!=ReviewStrikeStyle.BodyStrike||ImpactReactionActive||_handActive[0]||_handActive[1]||_gripPosture[0]||_gripPosture[1])return Vector3.zero;
                var amount=0f;
                for(var side=0;side<2;side++)if(_punchReference[side]&&!PunchHasContact(side))
                {
                    var t=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;
                    amount=Mathf.Max(amount,3.8f*Smooth((t-.04f)/.035f)*(1-Smooth((t-.13f)/.055f)));
                }
                return Actor.Motor.Body.rotation*Vector3.forward*amount*LungeScale;
            }
        }

        // Only the confirmed contact path calls this. A miss or wall cannot
        // start a victim response, and a second contact replaces bounded state.
        public void ReceivePunchReaction(Rigidbody body,Vector3 direction,Vector3 point,float closingSpeed,bool held)
        {
            if(StrikeStyle!=ReviewStrikeStyle.BodyStrike){ReceivePunchReaction(body);return;}
            var link=_links.Find(item=>item.Body==body);
            if(link==null||direction.sqrMagnitude<.0001f)return;
            var root=Actor.Motor.Body;
            _impactLocalDirection=Quaternion.Inverse(root.rotation)*direction.normalized;
            _impactSide=Mathf.Clamp((Quaternion.Inverse(root.rotation)*(point-root.position)).x/.13f,-1,1);
            _impactPart=link.Name;
            _impactStrength=Mathf.Lerp(.55f,1.15f,Mathf.InverseLerp(.35f,3.5f,closingSpeed));
            _impactHeld=held;_impactAge=0;
            EnsureCombatLimits();
            var axis=Vector3.Cross(Vector3.up,direction).normalized;
            var kick=_impactStrength*(held?.65f:1);
            Find("Chest").Body.AddTorque(axis*(1.4f*kick),ForceMode.VelocityChange);
            Find("Spine").Body.AddTorque(axis*(.55f*kick),ForceMode.VelocityChange);
            Find("Head").Body.AddTorque(axis*((link.Name=="Head"?2.5f:1.1f)*kick),ForceMode.VelocityChange);
        }
        public void ClearImpactReaction()
        {
            ResetShoulderMotion();
            _impactAge=-1;_impactStrength=0;_impactPart=null;_hitReaction.Clear();
            foreach(var link in _links)
            {link.Joint.slerpDrive=link.Drive;link.Joint.targetRotation=Quaternion.identity;link.Joint.targetAngularVelocity=Vector3.zero;}
            RestoreBodyStrikePose();
            RestoreCombatLimits();
        }
        private void EnsureCombatLimits()
        {
            if(_combatLimits.Count>0)return;
            foreach(var name in new[]{"Spine","Chest","Head"})
            {
                var link=Find(name);var joint=link.Joint;
                _combatLimits[link]=new CombatLimits{Low=joint.lowAngularXLimit,High=joint.highAngularXLimit,Y=joint.angularYLimit,Z=joint.angularZLimit};
                var bend=name=="Head"?38:name=="Chest"?26:24;
                joint.lowAngularXLimit=new SoftJointLimit{limit=-bend};joint.highAngularXLimit=new SoftJointLimit{limit=bend};
                joint.angularYLimit=new SoftJointLimit{limit=name=="Spine"?16:32};
                joint.angularZLimit=new SoftJointLimit{limit=name=="Spine"?18:28};
            }
        }
        private void RestoreCombatLimits()
        {
            foreach(var pair in _combatLimits)
            {
                var joint=pair.Key.Joint;var limits=pair.Value;
                joint.lowAngularXLimit=limits.Low;joint.highAngularXLimit=limits.High;joint.angularYLimit=limits.Y;joint.angularZLimit=limits.Z;
            }
            _combatLimits.Clear();
        }
        // Back brace after the extension (deg) and the forward body thrust scale of a body strike.
        // 3/.3 keep the forward travel per punch near the earlier ~3 cm (7 cm walked a stationary
        // attacker into reach after two punches). .3 since the locomotion brake no longer counts
        // the punching arm, which used to cancel part of the thrust.
        public float BraceAngle=3,LungeScale=.3f;
        private float _settleLean;private const float SettleRate=20;
        private void ApplyBodyStrikeTorso()
        {
            var attacking=_punchReference[0]||_punchReference[1];
            if(!attacking&&!ImpactReactionActive)
            {
                RestoreBodyStrikePose();
                // The forward lean carries past the end of the stroke and straightens at SettleRate
                // instead of snapping upright, which read as the attacker reeling back (2026-10-16).
                if(LoweredCarriage&&_settleLean>.05f)
                {
                    _settleLean=Mathf.MoveTowards(_settleLean,0,SettleRate*_stepSeconds);
                    SetJointGoal(Find("Spine"),Quaternion.AngleAxis(-_settleLean,Vector3.right));
                    return;
                }
                _settleLean=0;RestoreCombatLimits();return;
            }
            EnsureCombatLimits();_bodyStrikePose=true;
            var lean=0f;var twist=0f;var coil=0f;var brace=0f;
            for(var side=0;side<2;side++)if(_punchReference[side])
            {
                var t=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;var sign=side==0?-1:1;
                var entry=Smooth(t/.055f);var strike=StrikeCurve((t-.06f)/.085f);var recovery=1-Smooth((t-.21f)/.18f);
                var held=_handActive[1-side]||_gripPosture[1-side];var scale=held?.45f:1;
                var weight=entry*recovery*scale;
                twist+=sign*Mathf.Lerp(-9,17,strike)*weight;
                if(LoweredCarriage)
                {
                    // Lean peaks with the extension and straightens straight away; a short back
                    // brace meets the forward pitch left when the body thrust stops.
                    // On a hit the attacker follows through: lean held past the impact, no brace, no recoil.
                    var contact=PunchHasContact(side);
                    var follow=contact?1-Smooth((t-_punchImpactAge[side]-.1f)/.3f):0;
                    lean=Mathf.Max(lean,Mathf.Lerp(2,10,strike)*entry*Mathf.Max(1-Smooth((t-.16f)/.12f),follow)*scale);
                    if(!contact)brace=Mathf.Max(brace,Smooth((t-.15f)/.06f)*(1-Smooth((t-.32f)/.12f))*scale);
                }
                else lean=Mathf.Max(lean,Mathf.Lerp(2,11,strike)*weight);
                coil=Mathf.Max(coil,entry*(1-strike)*scale);
                if(PunchHasContact(side)&&!LoweredCarriage)
                {
                    var since=t-_punchImpactAge[side];var recoil=Smooth(since/.035f)*(1-Smooth((since-.065f)/.12f));
                    lean-=5*recoil*scale;twist-=sign*7*recoil*scale;
                }
            }
            // Leaving a stroke, never drop the lean faster than SettleRate.
            if(LoweredCarriage&&!ImpactReactionActive)lean=Mathf.Max(lean,Mathf.MoveTowards(_settleLean,0,SettleRate*_stepSeconds));
            _settleLean=ImpactReactionActive?0:lean;
            SetJointGoal(Find("Spine"),Quaternion.AngleAxis(-lean+BraceAngle*brace,Vector3.right));
            SetJointGoal(Find("Chest"),Quaternion.AngleAxis(Mathf.Clamp(twist,-22,22),Vector3.up));
            var yaw=-twist*.32f;var drop=.012f*coil;
            var pelvis=_goals["Pelvis"];
            pelvis.localPosition=_referencePelvisPosition+Vector3.down*drop;
            pelvis.rotation=Quaternion.AngleAxis(yaw,_goalRoot.up)*pelvis.rotation;
            Find("Spine").Joint.connectedAnchor=Quaternion.Inverse(Actor.Motor.Body.rotation)*(Find("Spine").Goal.position-Actor.Motor.Body.position);
            Actor.Pose.AdditionalPelvisDrop=drop;Actor.Pose.AdditionalPelvisYaw=yaw;
        }
        private void RestoreBodyStrikePose()
        {
            if(!_bodyStrikePose)return;
            _goals["Pelvis"].localPosition=_referencePelvisPosition;
            Find("Spine").Joint.connectedAnchor=Find("Spine").StartConnectedAnchor;
            Actor.Pose.AdditionalPelvisDrop=Actor.Pose.AdditionalPelvisYaw=0;
            _bodyStrikePose=false;
        }
        // Elbow targets stay near 95 degrees: past ~105 the forearm skin meets the
        // upper arm (r40 cage body), and physical overshoot adds roughly 10 degrees.
        private const float ChamberFlex=95,GuardFlex=92;
        // Arm path of a body strike, and whether the arms return to a lowered carriage instead of
        // the raised two-hand guard (which read as a W). Defaults chosen in review 2026-10-16:
        // Merged path, lowered carriage, two-stage return, lean peaking with the extension.
        public enum BodyStrikeArmPath { Current, Straight, Swing, Merged }
        public BodyStrikeArmPath BodyArmPath=BodyStrikeArmPath.Merged;
        public bool LoweredCarriage=true;
        private const float LoweredLift=8,LoweredFlex=18,RetractLift=45,RetractYaw=10;
        private void ApplyBodyStrikeArm(int side,Link upper,Link forearm,float t)
        {
            var sign=side==0?-1:1;var held=_handActive[1-side]||_gripPosture[1-side];
            var path=held?BodyStrikeArmPath.Current:BodyArmPath;
            float lift,yaw,roll,flex,recover;
            if(path==BodyStrikeArmPath.Straight)
            {
                // Chamber at the chest, extend along the line of the shoulder, hold the reach briefly.
                var reach=StrikeCurve((t-.06f)/.075f);recover=Smooth((t-.25f)/.16f);
                lift=Mathf.Lerp(40,84,reach);yaw=Mathf.Lerp(0,sign*8,reach);roll=0;flex=Mathf.Lerp(ChamberFlex,8,reach);
            }
            else if(path==BodyStrikeArmPath.Swing)
            {
                // A loose, partly bent arm swept in from the outside at shoulder height.
                var strike=StrikeCurve((t-.07f)/.10f);recover=Smooth((t-.23f)/.18f);
                lift=Mathf.Lerp(52,80,strike);yaw=Mathf.Lerp(-sign*38,sign*18,strike);roll=Mathf.Lerp(sign*20,-sign*10,strike);flex=Mathf.Lerp(55,32,strike);
            }
            else if(path==BodyStrikeArmPath.Merged)
            {
                // The current chamber-and-extend, started a little wider so it reads as swinging in.
                var strike=StrikeCurve((t-.065f)/.085f);recover=Smooth((t-.215f)/.175f);
                lift=Mathf.Lerp(50,Mathf.Lerp(84,76,_punchCompact[side]),strike);
                yaw=Mathf.Lerp(-sign*32,sign*22,strike);
                roll=Mathf.Lerp(sign*24,-sign*18,strike);
                flex=Mathf.Lerp(80,Mathf.Lerp(28,45,_punchCompact[side]),strike);
            }
            else
            {
                var strike=StrikeCurve((t-.065f)/.085f);recover=Smooth((t-.215f)/.175f);
                lift=Mathf.Lerp(48,held?79:Mathf.Lerp(86,76,_punchCompact[side]),strike);
                yaw=Mathf.Lerp(-sign*22,sign*(held?12:24),strike);
                roll=Mathf.Lerp(sign*(held?18:28),-sign*22,strike);
                flex=Mathf.Lerp(ChamberFlex,held?62:Mathf.Lerp(25,45,_punchCompact[side]),strike);
            }
            var restLift=LoweredCarriage?LoweredLift:35;var restYaw=LoweredCarriage?0:sign*6;var restFlex=LoweredCarriage?LoweredFlex:GuardFlex;
            Quaternion upperTarget,lowerTarget;
            if(LoweredCarriage&&!held)
            {
                // Two-stage return: fold the fist back along the line to the chest, then lower the
                // folded arm to the side and let it open. A straight arm falling read as a dropped punch.
                var fold=Smooth((t-.19f)/.09f);var lower=Smooth((t-.27f)/.13f);
                var upperLift=Mathf.Lerp(Mathf.Lerp(lift,RetractLift,fold),restLift,lower);
                var upperYaw=Mathf.Lerp(Mathf.Lerp(yaw,sign*RetractYaw,fold),restYaw,lower);
                upperTarget=Quaternion.AngleAxis(upperLift,Vector3.right)*Quaternion.AngleAxis(upperYaw,Vector3.up)*Quaternion.AngleAxis(roll*(1-fold),Vector3.forward);
                lowerTarget=Quaternion.AngleAxis(Mathf.Lerp(Mathf.Lerp(flex,GuardFlex,fold),restFlex,lower),Vector3.right);
            }
            else
            {
                upperTarget=Quaternion.AngleAxis(Mathf.Lerp(lift,restLift,recover),Vector3.right)*Quaternion.AngleAxis(Mathf.Lerp(yaw,restYaw,recover),Vector3.up)*Quaternion.AngleAxis(roll*(1-recover),Vector3.forward);
                lowerTarget=Quaternion.AngleAxis(Mathf.Lerp(flex,restFlex,recover),Vector3.right);
            }
            if(PunchHasContact(side)&&LoweredCarriage&&!held&&!_punchBlocked[side])
            {
                // Follow-through: hold the pose the hit was made in briefly, then the normal folded return.
                var age=t-_punchImpactAge[side];var fold=Smooth((age-.06f)/.09f);var lower=Smooth((age-.14f)/.22f);
                var upperReturn=Quaternion.Slerp(Quaternion.AngleAxis(RetractLift,Vector3.right)*Quaternion.AngleAxis(sign*RetractYaw,Vector3.up),Quaternion.AngleAxis(restLift,Vector3.right)*Quaternion.AngleAxis(restYaw,Vector3.up),lower);
                var lowerReturn=Quaternion.AngleAxis(Mathf.Lerp(GuardFlex,restFlex,lower),Vector3.right);
                upperTarget=Quaternion.Slerp(_impactTarget[side,0],upperReturn,fold);
                lowerTarget=Quaternion.Slerp(_impactTarget[side,1],lowerReturn,fold);
            }
            else if(PunchHasContact(side))
            {
                var age=t-_punchImpactAge[side];var bounce=Smooth((age-.016f)/.055f);
                var settle=Smooth((age-.11f)/.16f);
                var recoilUpper=Quaternion.AngleAxis(_punchBlocked[side]?22:52,Vector3.right)*Quaternion.AngleAxis(-sign*12,Vector3.up);
                var guardUpper=Quaternion.AngleAxis(restLift,Vector3.right)*Quaternion.AngleAxis(restYaw,Vector3.up);
                upperTarget=Quaternion.Slerp(_impactTarget[side,0],Quaternion.Slerp(recoilUpper,guardUpper,settle),bounce);
                lowerTarget=Quaternion.Slerp(_impactTarget[side,1],Quaternion.AngleAxis(Mathf.Lerp(ChamberFlex+5,restFlex,settle),Vector3.right),bounce);
            }
            SetJointGoal(upper,upperTarget);SetJointGoal(forearm,lowerTarget);
        }
        // The attacker keeps its feet: for PlantDuration after its own hit the trunk (root, Spine,
        // Chest, Head) may not move backwards along its facing nor pitch back past upright. The contact reaction otherwise
        // shoved the attacker ~2 cm back and pitched it 9 deg (2026-10-16 review). Being hit
        // yourself in that time releases the plant.
        public float PlantDuration=.5f;
        private readonly float[] _strikeTouchAge={-1,-1};
        // Called for every contact of a punching forearm with the opponent, confirmed or not.
        public void NoteStrikeTouch(int side)
        {if(_punchReference[side]&&_strikeTouchAge[side]<0)_strikeTouchAge[side]=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;}
        // The stroke's own reaction moves the root ~1.3 cm back inside a single solver step, before
        // any velocity brake can act. While extending, that backward step is undone by moving the
        // whole rig (root and every link, so no joint changes) forward by the same amount.
        private Vector3 _plantRoot;private bool _plantValid;
        private void ApplyStrokePlant()
        {
            var root=Actor.Motor.Body;var extending=false;
            if(LoweredCarriage&&StrikeStyle==ReviewStrikeStyle.BodyStrike&&!ImpactReactionActive)
                for(var side=0;side<2;side++)
                {if(!_punchReference[side])continue;var t=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;extending|=t>=.04f&&t<.25f;}
            if(extending&&_plantValid)
            {
                var forward=Vector3.ProjectOnPlane(root.rotation*Vector3.forward,Vector3.up).normalized;
                var back=Vector3.Dot(root.position-_plantRoot,forward);
                if(back<0)
                {
                    var shift=-forward*back;
                    root.position+=shift;root.transform.position+=shift;
                    foreach(var link in _links){link.Body.position+=shift;link.Body.transform.position+=shift;}
                }
            }
            _plantRoot=root.position;_plantValid=true;
        }
        private void ApplyStrikePlant()
        {
            ApplyStrokePlant();
            if(!LoweredCarriage||StrikeStyle!=ReviewStrikeStyle.BodyStrike||ImpactReactionActive){_plantTimer=0;return;}
            // Starts at the striking forearm's first touch of the opponent (which can come a step
            // before the hit is confirmed) and runs PlantDuration, past the end of the stroke.
            for(var side=0;side<2;side++)
                if(_punchReference[side]&&(_strikeTouchAge[side]>=0||PunchHasContact(side))&&!_plantStarted[side]){_plantStarted[side]=true;_plantTimer=PlantDuration;}
            if(_plantTimer<=0)return;
            _plantTimer-=_stepSeconds;
            var root=Actor.Motor.Body;
            var forward=Vector3.ProjectOnPlane(root.rotation*Vector3.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            foreach(var name in new[]{"Spine","Chest","Head"})
            {
                var link=Find(name);var body=link.Body;
                var velocity=body.linearVelocity;var along=Vector3.Dot(velocity,forward);
                if(along<0)body.linearVelocity=velocity-forward*along;
                // No reeling back: the trunk may straighten from its forward lean but not pitch past
                // upright. Positive rotation about `right` pitches forward.
                var up=body.rotation*(Quaternion.Inverse(link.StartRotation)*Vector3.up);
                var lean=Mathf.Asin(Mathf.Clamp(Vector3.Dot(up,forward),-1,1))*Mathf.Rad2Deg;
                var pitchRate=Vector3.Dot(body.angularVelocity,right);
                if(lean<MinimumFollowLean&&pitchRate<0)body.angularVelocity-=right*pitchRate;
            }
            {
                var velocity=root.linearVelocity;var along=Vector3.Dot(velocity,forward);
                if(along<0)root.linearVelocity=velocity-forward*along;
            }
        }
        private float _plantTimer;private readonly bool[] _plantStarted=new bool[2];
        private const float MinimumFollowLean=1;
        // Lowered carriage: the free arm swings slightly back with the strike instead of rising
        // into a guard, then hangs again; nothing is held after the punch.
        private void ApplyCounterSwing()
        {
            for(var side=0;side<2;side++)
            {
                _guardRemaining[side]=_guardWeight[side]=0;
                if(_punchReference[side]||_gripPosture[side]||_handActive[side]||!_punchReference[1-side])continue;
                var t=_punchSourceAge[1-side]/PhysicalPunchReview.PlaybackRate;
                var weight=Smooth((t-.04f)/.08f)*(1-Smooth((t-.22f)/.16f));
                if(weight<=0)continue;
                var suffix=side==0?"L":"R";
                SetJointGoal(Find("UpperArm_"+suffix),Quaternion.AngleAxis(-14*weight,Vector3.right));
                SetJointGoal(Find("Forearm_"+suffix),Quaternion.AngleAxis(35*weight,Vector3.right));
            }
        }
        private float ImpactEnvelope(float delay=0)
        {
            var age=_impactAge-delay;
            return age<0?0:Smooth(age/.055f)*(1-Smooth((age-.12f)/.29f));
        }
        private void ApplyImpactReaction(float seconds)
        {
            if(!ImpactReactionActive)return;
            _impactAge+=seconds;
            if(_impactAge>=ImpactDuration){_impactAge=-1;return;}
            EnsureCombatLimits();
            var direction=Actor.Motor.Body.rotation*_impactLocalDirection;
            var axis=Vector3.Cross(Vector3.up,direction).normalized;
            if(axis.sqrMagnitude<.001f)axis=Actor.Motor.Body.rotation*Vector3.right;
            var strength=_impactStrength*(_impactHeld?.75f:1);
            var headHit=_impactPart=="Head";
            var limbHit=_impactPart.StartsWith("UpperArm")||_impactPart.StartsWith("Forearm");
            var torsoAmount=(limbHit?.7f:1)*strength;
            // The head leads a head hit; the chest leads a body hit, then the
            // waist follows. Each target is solved by the physical joint drive.
            var spine=Find("Spine");var chest=Find("Chest");var head=Find("Head");
            spine.Goal.rotation=Quaternion.AngleAxis(13*torsoAmount*ImpactEnvelope(.035f),axis)*spine.Goal.rotation;
            chest.Goal.rotation=Quaternion.AngleAxis((headHit?10:19)*torsoAmount*ImpactEnvelope(headHit?.03f:0),axis)*chest.Goal.rotation;
            chest.Goal.rotation=Quaternion.AngleAxis(-_impactSide*13*strength*ImpactEnvelope(),Vector3.up)*chest.Goal.rotation;
            head.Goal.rotation=Quaternion.AngleAxis((headHit?27:12)*strength*ImpactEnvelope(headHit?0:.035f),axis)*head.Goal.rotation;
            for(var side=0;side<2;side++)
            {
                if(_punchReference[side]||_handActive[side]||_gripPosture[side])continue;
                var amount=ImpactEnvelope(.015f)*strength;var sign=side==0?-1:1;
                SetJointGoal(Find("UpperArm_"+(side==0?"L":"R")),Quaternion.AngleAxis(22*amount,Vector3.right)*Quaternion.AngleAxis(sign*12*amount,Vector3.forward));
                SetJointGoal(Find("Forearm_"+(side==0?"L":"R")),Quaternion.AngleAxis(55*amount,Vector3.right));
            }
        }
        private void ApplyImpactReactionDrive(Link link,ref JointDrive drive)
        {
            if(!ImpactReactionActive)return;
            var torso=link.Name=="Spine"||link.Name=="Chest"||link.Name=="Head";
            if(!torso&&link.Name!=_impactPart)return;
            var yielding=1-Smooth(_impactAge/.05f);
            // Yield at impact, then actively carry the directional reaction
            // through the body instead of immediately pulling back to idle.
            var pose=ImpactEnvelope();
            drive.positionSpring*=Mathf.Lerp(1+pose*1.1f,.28f,yielding);
            drive.positionDamper*=Mathf.Lerp(1.3f,.7f,yielding);
            drive.maximumForce*=Mathf.Lerp(1+pose*.8f,.45f,yielding);
        }
    }
}
