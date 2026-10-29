using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Wall climbing (2026-10-17 review; reworked 2026-10-25). A seeking hand that meets static level geometry above the
    // waist, on its top edge or anywhere on its face, grips it (a world-anchored palm joint); in front of a wall the hands
    // reach straight ahead to it, as for a standing player. The climber stays the active rig, muscles on, never a limp
    // ragdoll. Climbing is the player's own repetition (2026-10-25 review): jump (Space, straight up; the holds stay until
    // the player lets go of the grab buttons), let go, grab again higher at the top of the jump. Holding is a downed
    // player's lift turned round (2026-10-25 review: reuse the grab): the wall does not move, so the lift's carry assist
    // moves the climber, the hold brought to the chest. Getting over the top is the player's too: no assist (2026-10-25
    // review, after a set path, a step up, a bend over the edge and a belly roll all read wrong). Holding the top itself,
    // the wall jump goes higher and in over it (WallJump); let go as it leaves and move in, and it lands on the top.
    public sealed partial class ActiveGrabPairReview
    {
        public float JumpSpeed=2.6f,ClimbMinimumHeight=.3f,ClimbReach=.8f;
        private readonly Collider[] _climbProbe=new Collider[64];
        private readonly RaycastHit[] _groundHits=new RaycastHit[8];

        private bool Climbable(Player owner,Collider collider)=>collider!=null&&collider.enabled&&!collider.isTrigger&&collider.attachedRigidbody==null
            &&collider.bounds.max.y>owner.Rig.Actor.Motor.Body.position.y+ClimbMinimumHeight;

        // Feet on anything solid (floor, ledge top), not a fixed floor height, and never the rig's own parts: its spine
        // link lagging into the cast as it jumped read as ground and ended a wall climb in mid-air (2026-10-25).
        private bool Grounded(ActiveUpperBodyReview rig)
        {
            var body=rig.Actor.Motor.Body;
            var count=gameObject.scene.GetPhysicsScene().SphereCast(body.position+Vector3.up*.15f,.1f,Vector3.down,_groundHits,.08f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for(var i=0;i<count;i++){var hit=_groundHits[i].rigidbody;if(hit!=body&&(hit==null||!rig.Links.Any(l=>l.Body==hit)))return true;}
            return false;
        }

        private void RecordWorldContact(int player,Rigidbody ownBody,Collision collision)
        {
            var owner=Players[player];if(!Climbable(owner,collision.collider))return;
            // Rising from a jump the hands wait for the top of it: caught on the way up, the body went on up past the
            // holds and the arms folded down (2026-10-25 review).
            if(owner.ClimbSession&&!owner.Rig.Supported&&owner.Rig.Actor.Motor.Body.linearVelocity.y>ClimbCatchRise)return;
            for(var side=0;side<2;side++)
            {
                var hand=owner.Hands[side];
                if(hand.Phase!=HandPhase.Seeking||ownBody!=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                for(var c=0;c<collision.contactCount;c++)
                {
                    var contact=collision.GetContact(c);
                    // Holds: anywhere above the waist, the face or the top edge.
                    if(contact.point.y<owner.Rig.Actor.Motor.Body.position.y+ClimbMinimumHeight)continue;
                    var distance=Vector3.Distance(contact.point,owner.Rig.HandPosition(side));
                    if(distance>PalmContactRadius)continue;
                    var previous=_touches[player,side];
                    if(previous.Tick==Tick&&distance>previous.Distance)continue;
                    _touches[player,side]=new Touch{Tick=Tick,Collider=collision.collider,World=true,LocalPoint=contact.point,Distance=distance};
                }
            }
        }
        public float ClimbGripSpring=4000f,ClimbGripDamper=120f,ClimbHandDrive=1f;
        private PhysicsMaterial _climbSlide;
        private PhysicsMaterial ClimbSlide=>_climbSlide!=null?_climbSlide:_climbSlide=new PhysicsMaterial("Climbing hand"){staticFriction=0,dynamicFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum};

        private void AcquireWorld(int player,int side,Touch touch)
        {
            var owner=Players[player];var hand=owner.Hands[side];
            if(Vector3.Distance(owner.Rig.HandPosition(side),touch.LocalPoint)>PalmContactRadius+.005f)return;
            var body=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body;var palm=owner.Rig.PalmCenter(side);
            var ownRadius=((CapsuleCollider)owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Shape).radius;
            var span=Mathf.Max(ownRadius+.004f,Vector3.Distance(palm,touch.LocalPoint)+.002f);
            // No connected body: the connected anchor is the world point on the wall.
            var joint=body.gameObject.AddComponent<ConfigurableJoint>();joint.autoConfigureConnectedAnchor=false;
            joint.anchor=Quaternion.Inverse(body.rotation)*(palm-body.position);joint.connectedAnchor=touch.LocalPoint;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Limited;
            joint.linearLimit=new SoftJointLimit{limit=span,contactDistance=.001f};
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            joint.projectionMode=JointProjectionMode.None;joint.breakForce=GripBreakForce;joint.breakTorque=GripBreakForce;
            // A springy hold, and the holding forearm slides on the wall without friction: a hard limit at the palm against
            // the forearm's own contact with the face and the arm drive fought every tick, and dragged over the top edge
            // the forearm caught and slipped (2026-10-25 review: the arms shook through the climb, forearm
            // jitter median 12 mm/tick², grip force up to 1.6 kN). Letting the forearm pass through the wall instead
            // sank the arm into it (same review).
            joint.linearLimitSpring=new SoftJointLimitSpring{spring=ClimbGripSpring,damper=ClimbGripDamper};
            var shape=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Shape;
            var connection=new PhysicalGrip{A=body,LocalA=joint.anchor,LocalB=touch.LocalPoint,Joint=joint,Span=span,World=true,Owners=1,SlidingHand=shape,SlidingHandMaterial=shape!=null?shape.sharedMaterial:null};
            if(shape!=null)shape.sharedMaterial=ClimbSlide;
            _connections.Add(connection);SharedConnectionsCreated++;
            hand.Connection=connection;hand.TargetBody=null;hand.TargetCollider=touch.Collider;hand.TargetRig=null;
            hand.Phase=HandPhase.Holding;hand.Stress.Reset();owner.Acquisitions++;
            PhysicalPunches?.Acquired(player,side);
            BeginClimb(owner,touch.Collider,touch.LocalPoint);
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
            // A wall too tall to reach the top of: straight ahead to its face at ClimbChest, where the hands meet a standing
            // player's chest (2026-10-25 review: the hands went up over the head). Rising from a jump the hand waits
            // ClimbReachClear off the face until the top of it.
            if(edge.y>shoulder.y+.40f)
            {
                var ahead=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,Vector3.up).normalized;
                point=ledge.ClosestPoint(new Vector3(shoulder.x,body.position.y+ClimbChest,shoulder.z)+ahead*.3f+away)-away*.02f;
                if(owner.ClimbSession&&!rig.Supported&&body.linearVelocity.y>ClimbCatchRise)point+=away*(.02f+ClimbReachClear);
                return true;
            }
            point=rig.HandPosition(side).y<top+.02f?edge+Vector3.up*.10f+away*.07f:edge-Vector3.up*.01f-away*.05f;
            return true;
        }

        private Vector3[] WorldGripPoints(Player player)=>player.Hands
            .Where(h=>h.Phase==HandPhase.Holding&&h.Connection!=null&&h.Connection.Valid&&h.Connection.World).Select(h=>h.Connection.PointB).ToArray();

        // Before locomotion. Jump hops when standing. On a wall (holding it, or just let go of it) it is a wall jump
        // straight up after a short crouch (ClimbWindupSeconds).
        private void StepJumpAndClimb(int index,float seconds)
        {
            var player=Players[index];var rig=player.Rig;var body=rig.Actor.Motor.Body;
            var pressed=!Suppressed&&player.Jump&&!player.JumpPrevious;player.JumpPrevious=player.Jump;
            if(!pressed)return;
            if(player.ClimbSession&&(WallGrip(player,0)!=null||WallGrip(player,1)!=null||player.ClimbFreeTime<WallJumpWindow))
            {
                // Whether it goes over the top is settled by the press, not the launch after the crouch (2026-10-26 timing
                // sweep: let go with Space or a tick before the launch, the top hold was gone and it was a plain jump).
                if(player.ClimbWindup<0&&player.ClimbLaunchAge<0){player.ClimbWindup=0;player.ClimbTopJump=HoldingTop(player)||player.ClimbTopHeld&&player.ClimbFreeTime<WallJumpWindow;}
                return;
            }
            if(!rig.Supported)return;
            body.AddForce(Vector3.up*JumpSpeed,ForceMode.VelocityChange);
            foreach(var link in rig.Links)link.Body.AddForce(Vector3.up*JumpSpeed,ForceMode.VelocityChange);
            player.Jumps++;player.RunJump=Running(index);player.RunJumpAge=0;
        }

        // ---- Powered climb ----
        // While a wall is held the climber is a downed player's lift turned round (ApplyCarryAssist, LiftMotionTarget):
        // the holds stay put, so the lift's one shared acceleration steers the climber instead of the held body, its
        // weight carried. It brings the holds to ClimbChest above the root at the lift's rate, the body ClimbStandoff out
        // from them, so the arms keep the grab's reach in front of the chest. Not a spring under the holds (2026-10-25
        // review: hanging ClimbHang .8 below them the catch fell back down most of the jump, the arms went from the chest
        // to over the head and back). With the feet on the ground it stands holding, kept off the face.
        // Let go of every hold, the wall is remembered while airborne (re-grabbing reaches for it; a jump within
        // WallJumpWindow is still a wall jump) until landing or ClimbAirWindow.
        public float ClimbChest=.47f,ClimbStandoff=.36f,ClimbCatchRise=.5f,ClimbSteer=20f,ClimbSteerDamping=7f,WallJumpSpeed=3.2f,WallJumpWindow=.2f,ClimbAirWindow=2f;
        private float ClimbLiftRate=>Mathf.Max(0,LiftHeight)/Mathf.Max(.1f,LiftDuration);
        private void BeginClimb(Player player,Collider wall,Vector3 point)
        {
            // The face's own normal, from the wall's surface nearest the body: from the hold point to the body it leant
            // up to ~27 deg off when a hand held the face to one side, and the step up put its foot out over the edge.
            var body=player.Rig.Actor.Motor.Body;var normal=Vector3.ProjectOnPlane(body.position-wall.ClosestPoint(body.position),Vector3.up);
            if(normal.sqrMagnitude<1e-6f)normal=Vector3.ProjectOnPlane(body.position-point,Vector3.up);
            if(!player.ClimbSession)player.Climbs++;
            player.ClimbSession=true;player.ClimbWall=wall;player.ClimbWallPoint=point;player.ClimbNormal=normal.sqrMagnitude>1e-6f?normal.normalized:-(body.rotation*Vector3.forward);
            player.ClimbFreeTime=0;player.ClimbPullReady=false;
        }
        private void EndClimbHolds(Player player)
        {
            if(player.Rig!=null&&player.Rig.Actor!=null)player.Rig.Actor.Motor.Body.useGravity=true;
            player.ClimbSession=false;player.ClimbWall=null;player.ClimbWindup=player.ClimbLaunchAge=player.ClimbLegClock=-1;player.ClimbTopHeld=player.ClimbTopJump=false;player.ClimbOverAge=-1;player.ClimbHangValid=false;player.ClimbFreeTime=0;player.ClimbPullReady=false;
        }
        private PhysicalGrip WallGrip(Player player,int side)
        {var hand=player.Hands[side];return hand.Phase==HandPhase.Holding&&hand.Connection!=null&&hand.Connection.Valid&&hand.Connection.World?hand.Connection:null;}
        // Straight up: the vertical speed raised to WallJumpSpeed (never added up), the holds left as they are. Holding the
        // wall's top when Space is pressed (a hold within ClimbTopHold of it: the hands reaching for a ledge's top catch its
        // face ~.11 below the edge; or let go of one within WallJumpWindow) it is a jump over the edge: up fast enough to
        // take the feet ClimbTopClear over the top from where they are (2026-10-26 sweep: a fixed 3.6 m/s cleared a
        // .55 ledge, started standing, by 1.4 cm and a 1.6 m wall, started hanging, by 15 cm, so letting go late failed
        // on the ledge only), never slower than WallJumpSpeed, then only gravity and the landing (no set path: 2026-10-25 review, the player gets over by
        // themselves; straight up at 3.2 m/s the hips reached the top only just, .36 m out from the face, and the 30% air
        // drive could not take them in: from a hang at the top no climber got over). Going in over the edge is the
        // player's forward input (2026-10-26 timing sweep: a fixed ClimbTopJumpIn took every climber over with no input):
        // for ClimbTopSteerSeconds from the launch the speed toward the wall is raised to ClimbTopJumpIn times the stick
        // toward it (full from ClimbTopStickFull). Held on, the arms stop it at their length: the player has to let go
        // as it leaves.
        public float ClimbTopHold=.15f,ClimbTopClear=.17f,ClimbTopJumpIn=1.4f,ClimbTopSteerSeconds=.2f,ClimbTopStickFull=.5f;
        private bool HoldingTop(Player player)=>player.ClimbWall!=null&&new[]{WallGrip(player,0),WallGrip(player,1)}.Any(g=>g!=null&&g.PointB.y>player.ClimbWall.bounds.max.y-ClimbTopHold);
        private void WallJump(Player player)
        {
            var rig=player.Rig;var top=player.ClimbTopJump;player.ClimbTopJump=false;
            var rise=top&&player.ClimbWall!=null?player.ClimbWall.bounds.max.y+ClimbTopClear-rig.Actor.Motor.Body.position.y:0;
            var up=Mathf.Max(WallJumpSpeed,Mathf.Sqrt(2*Physics.gravity.magnitude*Mathf.Max(0,rise)));
            foreach(var part in rig.Links.Select(l=>l.Body).Append(rig.Actor.Motor.Body))
            {var v=part.linearVelocity;if(v.y<up)v.y=up;part.linearVelocity=v;}
            player.WallJumps++;player.ClimbJumpAge=0;player.ClimbOverAge=top?0:-1;if(top)player.TopJumps++;
        }
        private void SteerOverTop(Player player,float seconds)
        {
            if(player.ClimbOverAge<0)return;
            if((player.ClimbOverAge+=seconds)>ClimbTopSteerSeconds){player.ClimbOverAge=-1;return;}
            var inward=-player.ClimbNormal;var input=Suppressed?Vector2.zero:Vector2.ClampMagnitude(player.Move,1);
            var goal=ClimbTopJumpIn*Mathf.Clamp01(Vector3.Dot(new Vector3(input.x,0,input.y),inward)/ClimbTopStickFull);
            if(goal<=0)return;
            var rig=player.Rig;
            foreach(var part in rig.Links.Select(l=>l.Body).Append(rig.Actor.Motor.Body))
            {var v=part.linearVelocity;var along=Vector3.Dot(v,inward);if(along<goal)part.linearVelocity=v+inward*(goal-along);}
        }
        // Before the hands are solved: the wall jump's crouch and push, and the wall tracked while airborne.
        private void StepClimb(int index,float seconds)
        {
            var player=Players[index];if(!player.ClimbSession)return;
            if(player.Rig.RagdollActive){EndClimbHolds(player);return;}
            // The crouch before a wall jump, then the legs pushing straight for ClimbExtendSeconds.
            if(player.ClimbWindup>=0)
            {
                player.ClimbWindup+=seconds;
                if(player.ClimbWindup>=ClimbWindupSeconds){player.ClimbWindup=-1;player.ClimbLaunchAge=0;player.ClimbLegClock=0;player.ClimbLaunchGrounded=player.Rig.Supported;WallJump(player);}
            }
            else if(player.ClimbLaunchAge>=0&&(player.ClimbLaunchAge+=seconds)>ClimbExtendSeconds)player.ClimbLaunchAge=-1;
            SteerOverTop(player,seconds);
            if(WallGrip(player,0)!=null||WallGrip(player,1)!=null){player.ClimbFreeTime=0;player.ClimbTopHeld=HoldingTop(player);return;}
            player.ClimbFreeTime+=seconds;player.ClimbPullReady=false;player.Rig.Actor.Motor.Body.useGravity=true;
            if(player.ClimbFreeTime>ClimbAirWindow||player.ClimbFreeTime>.1f&&Grounded(player.Rig))EndClimbHolds(player);
        }
        // After locomotion: the lift's pull while holding.
        private void CarryClimber(int index)
        {
            var player=Players[index];if(!player.ClimbSession)return;
            var rig=player.Rig;var body=rig.Actor.Motor.Body;var normal=player.ClimbNormal;var seconds=Time.fixedDeltaTime;
            body.useGravity=true;
            // Rising from a wall jump the climber flies free (the steer's damping ate the jump within a few ticks): held
            // on, the arms' reach stops it; let go, it flies on.
            if(player.ClimbJumpAge>=0)
            {
                player.ClimbJumpAge+=seconds;
                if(player.ClimbJumpAge<WallJumpFree&&body.linearVelocity.y>0)return;
                player.ClimbJumpAge=-1;player.ClimbPullReady=false;
            }
            var points=new[]{WallGrip(player,0),WallGrip(player,1)}.Where(g=>g!=null).Select(g=>g.PointB).ToArray();
            if(points.Length==0)
            {
                // Let go during the crouch: it goes on crouching where it hung.
                if(player.ClimbWindup>=0&&player.ClimbHangValid)SteerClimber(rig,player.ClimbHangGoal-Vector3.up*ClimbWindupDip*WindupWeight(player),true);
                return;
            }
            var hold=points.Aggregate(Vector3.zero,(s,p)=>s+p)/points.Length;
            // The pull: the holds' height above the root brought to ClimbChest at the lift's rate.
            var offset=hold.y-body.position.y;
            if(!player.ClimbPullReady){player.ClimbPull=offset;player.ClimbPullReady=true;}
            player.ClimbPull=Mathf.MoveTowards(player.ClimbPull,ClimbChest,seconds*ClimbLiftRate);
            var hang=new Vector3(hold.x,hold.y-player.ClimbPull,hold.z)+normal*ClimbStandoff;
            // Feet on the ground below that: stand holding, its own weight on the feet, only kept off the face (pressed in
            // by the floor's friction the elbows folded and the arms sank into the wall, 2026-10-25).
            var standing=hang.y<=body.position.y+.01f&&Grounded(rig);
            player.ClimbHangGoal=hang;player.ClimbHangValid=!standing;
            // The crouch before a wall jump dips the hanging body ClimbWindupDip.
            if(player.ClimbWindup>=0&&!standing)hang.y-=ClimbWindupDip*WindupWeight(player);
            SteerClimber(rig,hang,!standing);
        }
        public float WallJumpFree=.35f;
        // ---- Legs (presentation, ProceduralJumpPose) ----
        // A wall jump (2026-10-25 review: the knees stayed folded in the air and folded more on the jump, nothing showed
        // the push): hanging, the knees a little bent (ClimbHangTuck) and the feet toward the wall; the press of Space
        // crouches for ClimbWindupSeconds (the knees fold to ClimbCrouchTuck, the body dips ClimbWindupDip; standing, the
        // pelvis drops instead), the jump goes and the legs push straight within half of ClimbExtendSeconds, then bend
        // back into the hang over ClimbRefoldSeconds, ready for the next crouch.
        public float ClimbWindupSeconds=.08f,ClimbExtendSeconds=.12f,ClimbRefoldSeconds=.2f,ClimbWindupDip=.04f,ClimbHangTuck=.015f,ClimbCrouchTuck=.045f,ClimbFeetForward=.03f;
        // The shown legs follow those goals on a spring, never set outright (2026-10-25 review: straightened, the legs
        // were handed to the pose's airborne tuck and folded at once, then snapped into a fixed hang on the catch and
        // stayed there): stiff through the crouch and push (ClimbLegSnap, rad/s), soft otherwise (ClimbLegSoft, damping
        // ratio ClimbLegDamping, a small bounce), and moved by the body: a fall stopped by the catch gives at the knees
        // (ClimbLegGive per m/s), a pull toward the wall leaves the feet behind (ClimbLegSway per m/s).
        public float ClimbLegSnap=45f,ClimbLegSoft=22f,ClimbLegDamping=.55f,ClimbLegGive=.25f,ClimbLegSway=.15f;
        private float WindupWeight(Player player)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(player.ClimbWindup/Mathf.Max(.01f,ClimbWindupSeconds)));
        private void PoseClimbLegs(int index)
        {
            var player=Players[index];var rig=player.Rig;if(rig==null||rig.Actor==null)return;var pose=rig.Actor.Pose;var dt=Time.fixedDeltaTime;
            if(!player.ClimbSession||rig.RagdollActive)
            {
                if(player.ClimbLegsPosed){pose.TuckOverride=-1;pose.FootForwardOffset=0;pose.AdditionalPelvisDrop=0;player.ClimbLegsPosed=false;}
                player.ClimbLegsLive=false;return;
            }
            player.ClimbLegsPosed=true;
            var body=rig.Actor.Motor.Body;var grounded=rig.Supported;
            var velocity=body.linearVelocity;var change=player.ClimbLegsLive?velocity-player.ClimbLastVelocity:Vector3.zero;player.ClimbLastVelocity=velocity;
            if(!player.ClimbLegsLive){player.ClimbLegTuck=player.ClimbLegTuckSpeed=player.ClimbLegForward=player.ClimbLegForwardSpeed=0;player.ClimbLegsLive=true;}
            if(player.ClimbLegClock>=0&&(player.ClimbLegClock+=dt)>ClimbExtendSeconds+ClimbRefoldSeconds)player.ClimbLegClock=-1;
            float drop=0,tuck=ClimbHangTuck,forward=ClimbFeetForward;var snap=false;
            if(player.ClimbWindup>=0)
            {
                var w=WindupWeight(player);snap=true;
                if(grounded)drop=.025f*w;else tuck=Mathf.Lerp(ClimbHangTuck,ClimbCrouchTuck,w);
            }
            else if(player.ClimbLegClock>=0)
            {
                var clock=player.ClimbLegClock;
                if(player.ClimbLaunchGrounded&&clock<ClimbExtendSeconds*.5f)drop=.025f*(1-Mathf.SmoothStep(0,1,clock/(ClimbExtendSeconds*.5f)));
                if(clock<ClimbExtendSeconds){snap=true;var w=Mathf.SmoothStep(0,1,Mathf.Clamp01(clock/(ClimbExtendSeconds*.5f)));tuck=player.ClimbLaunchGrounded?0:Mathf.Lerp(ClimbCrouchTuck,0,w);forward=ClimbFeetForward*(1-w);}
                else{var w=Mathf.SmoothStep(0,1,(clock-ClimbExtendSeconds)/ClimbRefoldSeconds);tuck=ClimbHangTuck*w;forward=ClimbFeetForward*w;}
            }
            if(grounded&&player.ClimbWindup<0)
            {
                // Standing (holding the wall, or back on the floor): the pose's own legs.
                player.ClimbLegTuck=player.ClimbLegTuckSpeed=player.ClimbLegForward=player.ClimbLegForwardSpeed=0;
                pose.TuckOverride=-1;pose.FootForwardOffset=0;pose.AdditionalPelvisDrop=drop;return;
            }
            var w0=snap?ClimbLegSnap:ClimbLegSoft;var z=snap?1f:ClimbLegDamping;
            if(!snap&&player.ClimbLegClock<0)
            {
                player.ClimbLegTuckSpeed+=ClimbLegGive*Mathf.Clamp(change.y,0,1);
                var facing=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,Vector3.up).normalized;
                player.ClimbLegForwardSpeed-=ClimbLegSway*Mathf.Clamp(Vector3.Dot(change,facing),-1,1);
            }
            player.ClimbLegTuckSpeed+=(w0*w0*(tuck-player.ClimbLegTuck)-2*z*w0*player.ClimbLegTuckSpeed)*dt;player.ClimbLegTuck=Mathf.Clamp(player.ClimbLegTuck+player.ClimbLegTuckSpeed*dt,0,.055f);
            player.ClimbLegForwardSpeed+=(w0*w0*(forward-player.ClimbLegForward)-2*z*w0*player.ClimbLegForwardSpeed)*dt;player.ClimbLegForward=Mathf.Clamp(player.ClimbLegForward+player.ClimbLegForwardSpeed*dt,-.04f,.06f);
            pose.TuckOverride=grounded?-1:player.ClimbLegTuck;pose.FootForwardOffset=grounded?0:player.ClimbLegForward;pose.AdditionalPelvisDrop=drop;
        }
        // One acceleration for every part of the climber, as the carry assist gives a carried body (ApplyCarryAssist):
        // the root steered to the goal, bounded like a carried body, its weight carried when off the ground. No part is
        // pulled against another and no speed is set.
        private void SteerClimber(ActiveUpperBodyReview rig,Vector3 goal,bool carried,Vector3 goalVelocity=default)
        {
            var body=rig.Actor.Motor.Body;var momentum=body.mass*body.linearVelocity;
            foreach(var link in rig.Links)momentum+=link.Body.mass*link.Body.linearVelocity;
            var error=goal-body.position;if(!carried)error.y=0;
            var accel=Vector3.ClampMagnitude(error*ClimbSteer+(goalVelocity-momentum/rig.TotalMass)*ClimbSteerDamping,CarryAssistAcceleration);
            if(carried)accel-=Physics.gravity;else accel.y=0;
            body.AddForce(accel*body.mass,ForceMode.Force);
            foreach(var link in rig.Links)link.Body.AddForce(accel*link.Body.mass,ForceMode.Force);
        }
        // Airborne on the wall, a hand that is not holding keeps the grab's reach in front of the chest (a standing
        // player's, MotionTarget), ClimbReachClear off the face: let go, the arms neither drop to the sides nor sweep back
        // up the wall to grab again (2026-10-25 review).
        public float ClimbReachClear=.04f;
        private bool ClimbReachTarget(int index,int side,out Vector3 target)
        {
            target=default;var player=Players[index];var rig=player.Rig;var body=rig.Actor.Motor.Body;
            if(!player.ClimbSession||rig.Supported||player.Hands[side].Phase==HandPhase.Holding)return false;
            target=body.position+body.rotation*new Vector3(side==0?-ReachHalfWidth:ReachHalfWidth,ClimbChest,.40f);
            // Above the wall's top there is no face to keep off (kept off it going over the top, the hands folded back to
            // the chest).
            var off=Vector3.Dot(target-player.ClimbWallPoint,player.ClimbNormal);
            if(off<ClimbReachClear&&(player.ClimbWall==null||target.y<player.ClimbWall.bounds.max.y+.05f))target+=player.ClimbNormal*(ClimbReachClear-off);
            return true;
        }
    }
}
