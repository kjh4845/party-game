using System;
using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // While running (Shift held at speed) the hands do not punch or grab: a fresh press of either button is
    // a dropkick instead (2026-10-20 review). Only the flying legs can hit, once per dropkick.
    public sealed partial class ActiveGrabPairReview
    {
        public float RunningMinimumSpeed=1.2f;
        // Leg surface within this of the opponent's colliders, closing at least this fast, is a hit.
        public float DropkickContactDistance=.03f,DropkickMinimumClosing=1f,DropkickHitWindow=.7f;
        private readonly Rigidbody[] _kickTargetBodies=new Rigidbody[16];

        public bool Running(int index)
        {
            var player=Players[index];if(!Active(player)||player.Rig.RagdollActive||!player.Sprint||!player.Rig.Supported)return false;
            var velocity=player.Rig.Actor.Motor.Body.linearVelocity;velocity.y=0;
            return velocity.magnitude>=RunningMinimumSpeed;
        }

        // Before the hand intents are read: a running player's buttons are taken away from punch and grab.
        private void GateRunningHands()
        {
            for(var p=0;p<2;p++)
            {
                var player=Players[p];
                if(Suppressed||!Running(p)||!player.Hands.Any(h=>h.Button))continue;
                var pressed=Array.FindIndex(player.Hands,h=>h.Button&&!h.Previous&&!h.MustRelease);
                player.InputArmed=false;
                for(var side=0;side<2;side++){var hand=player.Hands[side];if(hand.Connection!=null)Release(p,side,"Running",false);hand.MustRelease=hand.Button;}
                if(pressed>=0&&player.Rig.BeginDropkick(pressed))player.DropkickHit=false;
            }
        }

        private void StepDropkick(int index,float seconds)
        {
            var player=Players[index];var rig=player.Rig;
            if(!rig.Dropkicking)return;
            rig.StepDropkick(seconds);
            var other=Players[1-index];
            if(player.DropkickHit||rig.DropkickAge>DropkickHitWindow||!Active(other))return;
            var target=other.Rig;
            var targetBodies=target.RagdollActive?KnockdownBodies(target):target.Links.Select(l=>l.Body).Append(target.Actor.Motor.Body).ToArray();
            var count=0;foreach(var body in targetBodies)if(count<_kickTargetBodies.Length)_kickTargetBodies[count++]=body;
            foreach(var leg in KnockdownBodies(rig).Where(b=>b.name.StartsWith("Calf")||b.name.StartsWith("Thigh")))
            foreach(var shape in leg.GetComponentsInChildren<CapsuleCollider>())
            {
                if(!shape.enabled)continue;
                var axis=shape.direction==0?Vector3.right:shape.direction==1?Vector3.up:Vector3.forward;var half=Mathf.Max(0,shape.height*.5f-shape.radius);
                for(var s=0;s<=4;s++)
                {
                    var sample=shape.transform.TransformPoint(shape.center+axis*Mathf.Lerp(-half,half,s/4f));
                    for(var b=0;b<count;b++)foreach(var collider in _kickTargetBodies[b].GetComponents<Collider>())
                    {
                        if(!collider.enabled||collider.isTrigger)continue;
                        var point=collider.ClosestPoint(sample);var gap=Vector3.Distance(point,sample)-shape.radius;
                        if(gap>DropkickContactDistance)continue;
                        var toward=point-sample;if(toward.sqrMagnitude<1e-8f)toward=Vector3.ProjectOnPlane(_kickTargetBodies[b].worldCenterOfMass-sample,Vector3.up);
                        var closing=Vector3.Dot(leg.GetPointVelocity(sample)-_kickTargetBodies[b].GetPointVelocity(point),toward.normalized);
                        if(closing<DropkickMinimumClosing)continue;
                        player.DropkickHit=true;player.DropkickHits++;
                        PhysicalPunches?.ConfirmDropkick(index,_kickTargetBodies[b],point,leg.GetPointVelocity(sample),closing);
                        return;
                    }
                }
            }
        }
    }
}
