using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    public sealed partial class ActiveGrabPairReview
    {
        // Opt-in candidate. The old grab/drag comparisons keep their exact policy.
        public bool EnableLiftThrow;
        public float LiftHeight=.65f,LiftDuration=.85f;
        [Range(0,.9f)] public float LimpWeightCompensation=.80f;
        public float ThrowImpulse=30f,MaximumThrowAddedSpeed=3f;
        // Fight review (physical punch candidate): throws strong enough to carry a body off a ledge,
        // ~6 m/s added with more loft. The lift/drag comparisons keep the 30 N·s / 1.5 m/s baseline.
        public float ThrowLoft=.35f;
        // A knocked-down body is carried overhead (hand goal in the lifter's frame: half width, height
        // above the feet, forward). Standing opponents are only held, never lifted or thrown (2026-10-17).
        public float OverheadHalfWidth=.12f,OverheadHeight=.95f,OverheadForward=.12f;
        public void UseFightThrows(){ThrowImpulse=120f;MaximumThrowAddedSpeed=4.5f;ThrowLoft=.5f;}
        public float LiftSupportForce {get;private set;}
        public int Throws {get;private set;}
        public Vector3 LastThrowAddedVelocity {get;private set;}
        private float _liftSupportWeight;

        private bool LiftAllowed(int player)
        {
            var p=Players[player];
            if(!EnableLiftThrow||Suppressed||!Active(p)||p.Mutual)return false;
            // No airborne weight cancellation: held jumps still carry the load.
            return p.Rig.Supported&&p.Rig.Actor.Motor.Body.linearVelocity.y<.5f;
        }

        private void ApplyLimpLiftSupport(float seconds)
        {
            LiftSupportForce=0;
            var held=ExternalTarget!=null&&ExternalTarget.Available&&Enumerable.Range(0,Players.Length).Any(p=>LiftAllowed(p)&&Players[p].Hands.Any(h=>
                h.Phase==HandPhase.Holding&&h.Connection!=null&&h.Connection.Valid&&ExternalTarget.Owns(h.TargetBody)));
            // A knocked-down opponent held by a supported player gets the same partial weight support.
            var downed=Enumerable.Range(0,Players.Length).Select(p=>Players[p]).Where(target=>target.Rig!=null&&target.Rig.KnockedDown
                &&Enumerable.Range(0,Players.Length).Any(p=>LiftAllowed(p)&&Players[p].Hands.Any(h=>h.Phase==HandPhase.Holding&&h.Connection!=null&&h.Connection.Valid&&h.TargetRig==target.Rig))).ToArray();
            held|=downed.Length>0;
            // Only acquisition ramps in. Release, target loss and airborne loss
            // stop support in this fixed tick, without touching velocities/mass.
            _liftSupportWeight=held?Mathf.MoveTowards(_liftSupportWeight,1,seconds/.18f):0;
            if(!held)return;
            var forcePerKg=-Physics.gravity*(Mathf.Clamp(LimpWeightCompensation,0,.9f)*_liftSupportWeight);
            ApplyCarryAssist();
            // Once per target, not per hand/player. All parts keep positive weight
            // and receive no pose torque, so the held body remains a limp doll.
            var bodies=downed.Length>0?downed.SelectMany(target=>KnockdownBodies(target.Rig)):ExternalTarget.Bodies;
            foreach(var body in bodies)
            {
                if(body==null||body.isKinematic||!body.useGravity)continue;
                var force=forcePerKg*body.mass;body.AddForce(force,ForceMode.Force);LiftSupportForce+=force.magnitude;
            }
        }

        private Vector3 LiftMotionTarget(int player,int side,Vector3 actual,float seconds)
        {
            var hand=Players[player].Hands[side];
            var downed=hand.TargetRig!=null&&hand.TargetRig.KnockedDown;
            if(!EnableLiftThrow||hand.Phase!=HandPhase.Holding||hand.Connection==null||!hand.Connection.Valid||!downed&&!(ExternalTarget!=null&&ExternalTarget.Owns(hand.TargetBody)))
            {hand.LiftReady=false;hand.LiftRise=0;hand.LiftWait=0;return actual;}
            if(!hand.LiftReady){hand.LiftStart=actual;hand.LiftRise=0;hand.LiftReady=true;}
            // A downed body is lifted once every pressed hand holds it (or after .4 s), so the second
            // hand is not left behind when the first one starts to raise it.
            var owner=Players[player];hand.LiftWait+=seconds;
            var waiting=downed&&hand.LiftWait<.4f&&owner.Hands.Count(h=>h.Button&&!h.MustRelease)>owner.Hands.Count(h=>h.Phase==HandPhase.Holding);
            if(LiftAllowed(player)&&!waiting)hand.LiftRise=Mathf.MoveTowards(hand.LiftRise,Mathf.Max(0,LiftHeight),seconds*Mathf.Max(0,LiftHeight)/Mathf.Max(.1f,LiftDuration));
            if(downed)
            {
                var overhead=new Vector3(side==0?-OverheadHalfWidth:OverheadHalfWidth,OverheadHeight,OverheadForward);
                var t=Mathf.Clamp01(hand.LiftRise/Mathf.Max(.01f,LiftHeight));
                var goal=Vector3.Lerp(actual,overhead,t*t*(3-2*t));
                var root=owner.Rig.Actor.Motor.Body;hand.LiftGoal=root.position+root.rotation*goal;
                return goal;
            }
            // Follow the real contact horizontally while raising the original
            // contact height. Never add height to last tick's moving skin pose.
            actual.y=Mathf.Max(actual.y,hand.LiftStart.y+hand.LiftRise);
            return actual;
        }

        // The carried body's held points follow the carrier's raising hands. Two small arms cannot raise
        // a 20 kg ragdoll overhead against its own leverage; this is the carry assist (Party Animals style
        // lift). It is one acceleration shared by every part, so it adds no internal force: the body keeps
        // whatever limp shape it has and only its grip points are steered to the hands' lift goals.
        public float CarryAssistAcceleration=25f;
        private void ApplyCarryAssist()
        {
            for(var p=0;p<Players.Length;p++)
            {
                if(!LiftAllowed(p))continue;var player=Players[p];
                var holding=player.Hands.Where(h=>h.Phase==HandPhase.Holding&&h.LiftReady&&h.LiftRise>0&&h.Connection!=null&&h.Connection.Valid&&!h.Connection.World&&h.TargetRig!=null&&h.TargetRig.KnockedDown).ToArray();
                if(holding.Length==0)continue;
                var target=holding[0].TargetRig;var bodies=KnockdownBodies(target);
                Vector3 error=Vector3.zero,velocity=Vector3.zero;
                foreach(var hand in holding)
                {
                    var forearm=player.Rig.Find("Forearm_"+(hand==player.Hands[0]?"L":"R")).Body;
                    var remote=hand.Connection.A==forearm?hand.Connection.B:hand.Connection.A;
                    var point=hand.Connection.A==forearm?hand.Connection.PointB:hand.Connection.PointA;
                    // Steer the held point to the hands' lift goal itself: the hands, jointed to it and
                    // driven to the same goal, come up with the body. (Following the palm instead left
                    // both waiting on each other at chest height.)
                    error+=hand.LiftGoal-point;velocity+=remote.GetPointVelocity(point);
                }
                error/=holding.Length;velocity/=holding.Length;
                // Plus the weight the limp support leaves (it cancels 80%): a carried body is weightless.
                var acceleration=(Vector3.ClampMagnitude(error*20f-velocity*7f,CarryAssistAcceleration)-Physics.gravity*(1-Mathf.Clamp(LimpWeightCompensation,0,.9f)))*_liftSupportWeight;
                foreach(var body in bodies)if(body!=null&&!body.isKinematic)body.AddForce(acceleration*body.mass,ForceMode.Force);
            }
        }

        private Rigidbody[] ReleaseThrowBodies(int player,int side,string reason,bool broken)
        {
            var owner=Players[player];var hand=owner.Hands[side];
            if(!EnableLiftThrow||Suppressed||broken||reason!="Release"||!HandAvailable(owner,side)||hand.Connection==null||!hand.Connection.Valid||hand.TargetCollider==null||!hand.TargetCollider.enabled||hand.TargetCollider.isTrigger||!hand.TargetCollider.gameObject.activeInHierarchy)return null;
            var low=ExternalTarget!=null&&ExternalTarget.Available&&ExternalTarget.Owns(hand.TargetBody);
            if(!low&&(hand.TargetRig==null||!hand.TargetRig.isActiveAndEnabled||!hand.TargetRig.KnockedDown))return null;
            if(owner.Hands.Where((h,s)=>s!=side).Any(h=>h.Connection!=null&&h.Connection.Valid&&
                (low?ExternalTarget.Owns(h.TargetBody):h.TargetRig==hand.TargetRig)))return null;
            return low?ExternalTarget.Bodies:KnockdownBodies(hand.TargetRig);
        }

        private void ThrowReleasedTarget(int player,Rigidbody[] bodies)
        {
            // Only a knocked-down (limp) rig is thrown. Its upper body leaves faster than its legs, so it
            // turns over in the air instead of flying and sliding as one block (2026-10-17 review).
            var target=Players[1-player].Rig;
            var tumble=target!=null&&target.KnockedDown&&bodies.Contains(target.Actor.Motor.Body);
            var mass=bodies.Where(b=>b!=null&&!b.isKinematic).Sum(b=>b.mass);if(mass<=0)return;
            var forward=Players[player].Rig.Actor.Motor.Body.rotation*Vector3.forward;forward.y=0;
            var velocity=(forward.normalized+Vector3.up*ThrowLoft).normalized*Mathf.Min(Mathf.Max(0,MaximumThrowAddedSpeed),Mathf.Max(0,ThrowImpulse)/mass);
            var low=tumble?bodies.Min(b=>b.worldCenterOfMass.y):0;var high=tumble?bodies.Max(b=>b.worldCenterOfMass.y):1;
            // A bounded impulse shared by mass preserves existing momentum and
            // angular motion. The second hand never doubles it.
            foreach(var body in bodies)if(body!=null&&!body.isKinematic)
            {
                var spin=tumble?Mathf.Lerp(.6f,1.3f,Mathf.InverseLerp(low,high,body.worldCenterOfMass.y)):1;
                body.AddForce(velocity*spin*body.mass,ForceMode.Impulse);
            }
            Throws++;LastThrowAddedVelocity=velocity;LiftSupportForce=0;_liftSupportWeight=0;
        }
    }
}
