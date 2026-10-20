using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Wall climbing (2026-10-17 review): a seeking hand that meets the top edge of static level geometry
    // above the waist grips it (a world-anchored palm joint). Jump (Space) while holding climbs on the
    // body's own physics (a hanging ragdoll); without a wall grip, jump is a small hop.
    public sealed partial class ActiveGrabPairReview
    {
        public float JumpSpeed=2.6f,ClimbMinimumHeight=.3f,ClimbReach=.8f,LedgeHoldDepth=.06f;
        private readonly Collider[] _climbProbe=new Collider[16];
        private readonly RaycastHit[] _groundHits=new RaycastHit[8];

        private bool Climbable(Player owner,Collider collider)=>collider!=null&&collider.enabled&&!collider.isTrigger&&collider.attachedRigidbody==null
            &&collider.bounds.max.y>owner.Rig.Actor.Motor.Body.position.y+ClimbMinimumHeight;

        // Feet on anything solid (floor, ledge top), not a fixed floor height. Rig colliders sit on
        // the Ignore Raycast layer, so a body is never its own ground.
        private bool Grounded(ActiveUpperBodyReview rig)
        {
            var body=rig.Actor.Motor.Body;
            return gameObject.scene.GetPhysicsScene().SphereCast(body.position+Vector3.up*.15f,.1f,Vector3.down,_groundHits,.08f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)>0;
        }

        private void RecordWorldContact(int player,Rigidbody ownBody,Collision collision)
        {
            var owner=Players[player];if(!Climbable(owner,collision.collider))return;
            for(var side=0;side<2;side++)
            {
                var hand=owner.Hands[side];
                if(hand.Phase!=HandPhase.Seeking||ownBody!=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                for(var c=0;c<collision.contactCount;c++)
                {
                    var contact=collision.GetContact(c);
                    // Holds: the top edge, or the face above the shoulder (an overhead hold to hang from). A palm
                    // brushing the face lower down keeps reaching.
                    var shoulderY=owner.Rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position.y;
                    if(contact.point.y<owner.Rig.Actor.Motor.Body.position.y+ClimbMinimumHeight
                        ||contact.point.y<collision.collider.bounds.max.y-LedgeHoldDepth&&contact.point.y<shoulderY+FaceHoldHeight)continue;
                    var distance=Vector3.Distance(contact.point,owner.Rig.HandPosition(side));
                    if(distance>PalmContactRadius)continue;
                    var previous=_touches[player,side];
                    if(previous.Tick==Tick&&distance>previous.Distance)continue;
                    _touches[player,side]=new Touch{Tick=Tick,Collider=collision.collider,World=true,LocalPoint=contact.point,Distance=distance};
                }
            }
        }

        private void AcquireWorld(int player,int side,Touch touch)
        {
            var owner=Players[player];var hand=owner.Hands[side];
            if(Vector3.Distance(owner.Rig.HandPosition(side),touch.LocalPoint)>PalmContactRadius+.005f)return;
            var body=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body;var palm=owner.Rig.PalmCenter(side);
            var ownRadius=((CapsuleCollider)owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Shape).radius;
            var span=Mathf.Max(ownRadius+.004f,Vector3.Distance(palm,touch.LocalPoint)+.002f);
            // No connected body: the connected anchor is the world point on the ledge.
            var joint=body.gameObject.AddComponent<ConfigurableJoint>();joint.autoConfigureConnectedAnchor=false;
            joint.anchor=Quaternion.Inverse(body.rotation)*(palm-body.position);joint.connectedAnchor=touch.LocalPoint;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Limited;
            joint.linearLimit=new SoftJointLimit{limit=span,contactDistance=.001f};
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            joint.projectionMode=JointProjectionMode.None;joint.breakForce=GripBreakForce;joint.breakTorque=GripBreakForce;
            var connection=new PhysicalGrip{A=body,LocalA=joint.anchor,LocalB=touch.LocalPoint,Joint=joint,Span=span,World=true,Owners=1};
            _connections.Add(connection);SharedConnectionsCreated++;
            hand.Connection=connection;hand.TargetBody=null;hand.TargetCollider=touch.Collider;hand.TargetRig=null;
            hand.Phase=HandPhase.Holding;hand.Stress.Reset();owner.Acquisitions++;
            PhysicalPunches?.Acquired(player,side);
            // A hold above the shoulder takes the body's weight at once: it hangs from it (2026-10-17 review:
            // holding a wall must feel gravity).
            if(touch.LocalPoint.y>owner.Rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position.y+.05f)StartHang(player,false);
        }
        // A face hold must be this far above the shoulder (arms up, hanging); lower, the hand keeps reaching.
        public float FaceHoldHeight=.2f;
        private bool FeetDown(ActiveUpperBodyReview rig)
        {
            var pelvis=rig.Actor.Motor.Body.worldCenterOfMass;
            return gameObject.scene.GetPhysicsScene().SphereCast(pelvis,.08f,Vector3.down,_groundHits,.40f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)>0;
        }

        // A seeking hand goes for the top edge of a ledge in reach: the nearest static surface above the
        // waist, queried from above and in front of the shoulder. Below the edge the hand rises a little off
        // the face (pressing into it only dragged the palm up the wall); above it, it comes down on the top.
        private bool TryClimbSurface(int player,int side,out Vector3 point)
        {
            point=default;var owner=Players[player];var rig=owner.Rig;var body=rig.Actor.Motor.Body;
            var shoulder=rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position;
            var query=shoulder+body.rotation*new Vector3(0,.45f,.25f);
            var count=gameObject.scene.GetPhysicsScene().OverlapSphere(shoulder,ClimbReach,_climbProbe,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            var best=float.PositiveInfinity;var top=0f;Collider ledge=null;
            for(var i=0;i<count;i++)
            {
                var collider=_climbProbe[i];if(!Climbable(owner,collider))continue;
                var candidate=collider.ClosestPoint(query);if(candidate.y<body.position.y+ClimbMinimumHeight)continue;
                var distance=(candidate-shoulder).sqrMagnitude;if(distance<best){best=distance;top=collider.bounds.max.y;ledge=collider;}
            }
            if(ledge==null||best>ClimbReach*ClimbReach)return false;
            // The edge nearest the shoulder at the top's height (on the climber's side of the ledge).
            var edge=ledge.ClosestPoint(new Vector3(shoulder.x,top,shoulder.z));
            var away=shoulder-edge;away.y=0;away=away.sqrMagnitude>.0001f?away.normalized:-(body.rotation*Vector3.forward);
            // A wall too tall to reach the top of: reach high up its face instead.
            if(edge.y>shoulder.y+.40f){point=ledge.ClosestPoint(shoulder+Vector3.up*.40f)-away*.02f;return true;}
            point=rig.HandPosition(side).y<top+.02f?edge+Vector3.up*.10f+away*.07f:edge-Vector3.up*.01f-away*.05f;
            return true;
        }

        // Top of the gripped ledge: a hand may hold its front face below the edge.
        private float LedgeTop(Player player)=>player.Hands.Where(h=>h.Phase==HandPhase.Holding&&h.Connection!=null&&h.Connection.World&&h.TargetCollider!=null)
            .Select(h=>h.TargetCollider.bounds.max.y).DefaultIfEmpty(float.NegativeInfinity).Max();
        private Vector3[] WorldGripPoints(Player player)=>player.Hands
            .Where(h=>h.Phase==HandPhase.Holding&&h.Connection!=null&&h.Connection.Valid&&h.Connection.World).Select(h=>h.Connection.PointB).ToArray();

        // Before locomotion. Jump hops when standing; jump while holding a low ledge hops off it on the limp
        // body. Overhead holds hang the body at once (AcquireWorld).
        private void StepJumpAndClimb(int index,float seconds)
        {
            var player=Players[index];var rig=player.Rig;var body=rig.Actor.Motor.Body;
            var pressed=!Suppressed&&player.Jump&&!player.JumpPrevious;player.JumpPrevious=player.Jump;
            if(!pressed)return;
            if(WorldGripPoints(player).Length>0){StartHang(index,true);return;}
            if(!rig.Supported)return;
            body.AddForce(Vector3.up*JumpSpeed,ForceMode.VelocityChange);
            foreach(var link in rig.Links)link.Body.AddForce(Vector3.up*JumpSpeed,ForceMode.VelocityChange);
            player.Jumps++;
        }

        // The standing grips become holds of the limp body (Gang Beasts style): it hangs under gravity from
        // its forearms. hop: jump off the holds at once (jump pressed while holding a low ledge).
        private void StartHang(int index,bool hop)
        {
            var player=Players[index];var rig=player.Rig;var body=rig.Actor.Motor.Body;
            var holds=new Vector3?[2];var palms=new Vector3[2];var surfaces=new Collider[2];Collider wall=null;
            for(var side=0;side<2;side++)
            {
                var hand=player.Hands[side];var grip=hand.Connection;
                if(hand.Phase!=HandPhase.Holding||grip==null||!grip.Valid||!grip.World)continue;
                holds[side]=grip.PointB;palms[side]=rig.PalmCenter(side);surfaces[side]=hand.TargetCollider;wall=hand.TargetCollider;
            }
            if(!holds.Any(h=>h.HasValue))return;
            var edge=holds.Where(h=>h.HasValue).Aggregate(Vector3.zero,(sum,h)=>sum+h.Value)/holds.Count(h=>h.HasValue);
            var forward=edge-body.position;forward.y=0;forward=forward.sqrMagnitude>.0001f?forward.normalized:body.rotation*Vector3.forward;
            // The standing grips end here; the ragdoll's forearms take the same holds below.
            for(var side=0;side<2;side++)if(holds[side].HasValue)
            {Release(index,side,"Climb",false);var hand=player.Hands[side];hand.MustRelease=hand.Button;if(hand.MustRelease)hand.Phase=HandPhase.AwaitRelease;}
            if(!rig.BeginClimbRagdoll())return;
            rig.Knockdown.HoldTumble=true;rig.Knockdown.SetOverheadShoulders(true);rig.Knockdown.SetHangingChain(true);
            var bodies=KnockdownBodies(rig);
            for(var side=0;side<2;side++)if(holds[side].HasValue)AddHold(player,side,bodies,palms[side],holds[side].Value,surfaces[side]);
            player.ClimbAge=0;player.ClimbForward=forward;player.ClimbFree=0;player.ClimbGrabWait=0;player.ClimbSession=true;player.ClimbWall=wall;
            player.JumpPrevious=player.Jump;player.Climbs++;
            if(hop)Hop(player,bodies);
        }

        // Push the whole limp body off upward (a little in to the wall) and let go; with the hands on the top
        // of the wall the push also carries it over.
        private void Hop(Player player,Rigidbody[] bodies,bool over=false)
        {
            var atTop=over||Enumerable.Range(0,2).Any(s=>player.ClimbHolds[s]!=null&&player.ClimbHolds[s].connectedAnchor.y>player.ClimbHoldTop[s]-.1f);
            for(var side=0;side<2;side++)ReleaseHold(player,side);
            // In to the wall: straight at its nearest face from the chest (a fixed direction made every hop
            // drift sideways off the wall).
            if(player.ClimbWall!=null)
            {
                var chest=RagdollPart(bodies,"Chest").position;var into=player.ClimbWall.ClosestPoint(chest)-chest;into.y=0;
                if(into.sqrMagnitude>.0004f)player.ClimbForward=into.normalized;
            }
            // Feet on the ground add the legs' jump to the arms' push.
            var push=Vector3.up*(FeetDown(player.Rig)?HopFromFeetSpeed:HopSpeed)+player.ClimbForward*(atTop?HopOverSpeed:.3f);
            foreach(var part in bodies)part.AddForce(push,ForceMode.VelocityChange);
            player.ClimbGrabWait=HopCatchDelay;player.Hops++;
        }

        private static Rigidbody RagdollPart(Rigidbody[] bodies,string name)=>bodies.First(b=>b.name.StartsWith(name));
        private void AddHold(Player player,int side,Rigidbody[] bodies,Vector3 palm,Vector3 point,Collider surface)
        {
            var forearm=RagdollPart(bodies,"Forearm_"+(side==0?"L":"R"));
            player.ClimbHoldSurface[side]=surface;
            var joint=forearm.gameObject.AddComponent<ConfigurableJoint>();joint.autoConfigureConnectedAnchor=false;
            joint.anchor=forearm.transform.InverseTransformPoint(palm);joint.connectedAnchor=point;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Limited;
            joint.linearLimit=new SoftJointLimit{limit=.01f,contactDistance=.001f};
            // A springy grip: the hand's contact with the surface it holds and a rigid hold limit fought each
            // other (thousands of N, alternating each step) until the limp body blew apart.
            joint.linearLimitSpring=new SoftJointLimitSpring{spring=6000f,damper=120f};
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            // A climbing hold lets go only when its grab button does (with a break force a fresh hold snapped).
            joint.projectionMode=JointProjectionMode.None;
            player.ClimbHolds[side]=joint;player.ClimbHoldTop[side]=surface.bounds.max.y;
        }
        private void ReleaseHold(Player player,int side)
        {
            var joint=player.ClimbHolds[side];if(joint==null)return;
            Destroy(joint);player.ClimbHolds[side]=null;player.ClimbHoldSurface[side]=null;
        }
        // Fist end of a ragdoll forearm (its capsule runs along local +z from the elbow).
        private static Vector3 RagdollHand(Rigidbody forearm)
        {
            var capsule=forearm.GetComponent<CapsuleCollider>();
            return forearm.transform.TransformPoint(capsule.center+Vector3.forward*(capsule.height*.5f-capsule.radius*.5f));
        }

        // The hanging climber, per tick (2026-10-17 review: hang under gravity, jump off the holds, catch
        // the wall again higher up, jump again):
        // - a released grab button lets that hand go;
        // - jump while holding (or lying with the feet down) is a hop: the whole body pushes off upward, a
        //   little in to the wall, and lets go; with the hands on the top of the wall the push also goes over
        //   it, so the limp body flops onto the top;
        // - a free hand whose grab button is held reaches up the wall (or, near the top, onto it) and grips
        //   wherever it touches above the hips, on the way up or falling back down;
        // - the climb ends once the hips lie on top, or with no hold and no button, or lying still below;
        //   the limp body then gets up as from any tumble.
        // Reaching is an arm muscle force between the forearm and the chest at the shoulder. Nothing else
        // moves the body but the hops.
        public float HopSpeed=2.8f,HopFromFeetSpeed=3.4f,HopOverSpeed=1.3f,ReachForce=90f,HopCatchDelay=.15f;
        private readonly Collider[] _reachProbe=new Collider[8];
        private void StepClimbRagdoll(int index,float seconds)
        {
            var player=Players[index];var rig=player.Rig;
            if(!rig.ClimbingRagdoll||!player.ClimbSession){if(player.ClimbSession)EndClimbHolds(player);return;}
            var pressed=!Suppressed&&player.Jump&&!player.JumpPrevious;player.JumpPrevious=player.Jump;
            for(var side=0;side<2;side++)
                if(player.ClimbHolds[side]!=null&&(Suppressed||!player.Hands[side].Button))ReleaseHold(player,side);
            var bodies=KnockdownBodies(rig);var chest=RagdollPart(bodies,"Chest");var pelvis=rig.Actor.Motor.Body;
            var held=player.ClimbHolds.Count(h=>h!=null);
            var buttons=!Suppressed&&player.Hands.Any(h=>h.Button);
            player.ClimbAge+=seconds;player.ClimbGrabWait=Mathf.Max(0,player.ClimbGrabWait-seconds);
            var wall=player.ClimbWall;var hips=pelvis.worldCenterOfMass;
            // Done once the hips lie on top, clear of its edges (at the very edge the hanging legs pull the
            // limp body back off).
            if(wall!=null&&hips.y>wall.bounds.max.y+.03f&&Mathf.Min(Mathf.Min(hips.x-wall.bounds.min.x,wall.bounds.max.x-hips.x),Mathf.Min(hips.z-wall.bounds.min.z,wall.bounds.max.z-hips.z))>.25f)
            {EndClimbHolds(player);return;}
            // Chest lying on the top with the legs over the edge: the arms under it push it on over.
            var draped=wall!=null&&chest.position.y>wall.bounds.max.y&&chest.position.x>wall.bounds.min.x&&chest.position.x<wall.bounds.max.x&&chest.position.z>wall.bounds.min.z&&chest.position.z<wall.bounds.max.z;
            if(pressed&&(held>0||draped||FeetDown(rig))){Hop(player,bodies,draped);held=0;}
            player.ClimbFree=held>0?0:player.ClimbFree+seconds;
            if(System.Environment.GetEnvironmentVariable("PARTY_GRAB_DEBUG")=="1")
                Debug.Log($"CLIMBRAG t{Tick} held={held} pressed={pressed} hips={hips:F2} chest={chest.position:F2} holds={string.Join(",",player.ClimbHolds.Select(h=>h==null?"-":$"{h.connectedAnchor.y:F2}/{h.currentForce.magnitude:F0}"))}");
            if(!buttons&&held==0||player.ClimbFree>1.5f&&pelvis.linearVelocity.magnitude<.3f){EndClimbHolds(player);return;}
            for(var side=0;side<2;side++)
            {
                if(player.ClimbHolds[side]!=null||Suppressed||!player.Hands[side].Button)continue;
                var forearm=RagdollPart(bodies,"Forearm_"+(side==0?"L":"R"));var shoulder=RagdollPart(bodies,"UpperArm_"+(side==0?"L":"R"));
                var tip=RagdollHand(forearm);
                // On the wall: high on its face above the shoulder, or, once the shoulder nears its top, on the
                // top ahead.
                var goal=shoulder.position+Vector3.up*.40f+player.ClimbForward*.25f;
                if(wall!=null)
                {
                    var top=wall.bounds.max.y;
                    goal=shoulder.position.y+.15f>top?wall.ClosestPoint(shoulder.position+player.ClimbForward*.3f+Vector3.up*.3f)-Vector3.up*.02f
                        :wall.ClosestPoint(shoulder.position+Vector3.up*.42f)+player.ClimbForward*.02f;
                }
                var reach=Vector3.ClampMagnitude((goal-tip)*400f-forearm.GetPointVelocity(tip)*20f,ReachForce);
                forearm.AddForceAtPosition(reach,tip,ForceMode.Force);chest.AddForceAtPosition(-reach,shoulder.position,ForceMode.Force);
                if(player.ClimbGrabWait>0)continue;
                var count=gameObject.scene.GetPhysicsScene().OverlapSphere(tip,.06f,_reachProbe,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                for(var i=0;i<count;i++)
                {
                    var surface=_reachProbe[i];if(surface.attachedRigidbody!=null||surface.isTrigger||surface.bounds.max.y<pelvis.position.y+ClimbMinimumHeight)continue;
                    var point=surface.ClosestPoint(tip);if(point.y<hips.y+.1f||Vector3.Distance(point,tip)>.05f)continue;
                    AddHold(player,side,bodies,tip,point,surface);player.ClimbWall=surface;break;
                }
            }
        }

        private void EndClimbHolds(Player player)
        {
            for(var side=0;side<2;side++)ReleaseHold(player,side);
            player.ClimbAge=-1;player.ClimbSession=false;
            if(player.Rig!=null&&player.Rig.Knockdown!=null){player.Rig.Knockdown.HoldTumble=false;player.Rig.Knockdown.SetOverheadShoulders(false);player.Rig.Knockdown.SetHangingChain(false);}
        }
    }
}
