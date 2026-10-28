using System;
using System.Collections.Generic;
using System.Linq;
using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Stage 3 local strike harness. Only actual physical contacts can score.
    // ThresholdReached is a handoff request, not a fabricated Down/GetUp state.
    public sealed class PhysicalPunchReview
    {
        public const float PlaybackRate=1.75f;
        public const double SourceHitStart=.1575,SourceHitEnd=.35;
        // A body strike holds its full reach until ~.21 s and folds after; a target at the edge of
        // reach is met at .20-.25 s. Its window ends at .25 s (source .4375); other styles keep .35.
        public const double BodyStrikeSourceHitEnd=.4375;
        private struct Edge {public int Player,Side;public bool Down;public double? Held;}
        private struct BodyPose {public Vector3 Position,Velocity,AngularVelocity,Center;public Quaternion Rotation;}
        private sealed class Candidate {public int Player,Side,Part;public Rigidbody Body;public Vector3 Point,Normal,Direction;public float Distance,EndDistance,Closing,SweptClosing,IncomingClosing;}
        [Serializable] public sealed class Hit
        {
            public ulong tick,action;public int attacker,target,side,points,total;
            public string region,part;public Vector3 position,normal,strikeDirection;
            public float sourceAge,tipDistance,endTipDistance,closingSpeed,impulse,bodyImpulse,sweptClosingSpeed,incomingClosingSpeed;
            // False for a hit on a body getting up: it lands but adds nothing (RisingHitScale).
            public bool counted=true;
        }
        private readonly ActiveGrabPairReview _pair;
        private readonly PhysicalPunchFeedbackReview _feedback;
        private readonly GroundHandActionStateMachine[] _states={new GroundHandActionStateMachine(.15,PlaybackRate,SourceHitStart,BodyStrikeSourceHitEnd),new GroundHandActionStateMachine(.15,PlaybackRate,SourceHitStart,BodyStrikeSourceHitEnd)};
        private readonly PunchDownAccumulation[] _scores={new PunchDownAccumulation(new PunchDownProfile(10,1,2,1,6),1),new PunchDownAccumulation(new PunchDownProfile(10,1,2,1,6),1)};
        private readonly Queue<Edge> _edges=new Queue<Edge>();
        private readonly bool[,] _buttons=new bool[2,2];
        private readonly bool[] _acceptEdges=new bool[2];
        private readonly Candidate[,] _candidates=new Candidate[2,2];
        private readonly bool[,] _blocked=new bool[2,2];
        public int BlockedContacts {get;private set;}
        private readonly Vector3[,] _tip=new Vector3[2,2];
        private readonly Vector3[,] _tipVelocity=new Vector3[2,2];
        private readonly Vector3[] _forward=new Vector3[2];
        private readonly ulong[] _sequence=new ulong[2];
        private readonly Dictionary<Rigidbody,BodyPose> _before=new Dictionary<Rigidbody,BodyPose>();
        private readonly List<Hit> _hits=new List<Hit>();
        private readonly bool[] _threshold=new bool[2];
        private double _time;private float _seconds;private uint _generation=1;
        public float TipRadius=.03f,ContactTolerance=.002f,MinimumClosingSpeed=.35f;
        public float ImpulseNewtonSeconds=20,MaximumPartDeltaSpeed=1;
        // A bounded whole-body response makes a confirmed strike readable even
        // when the touched arm has little mass. No impulse is added for a miss.
        public float BodyImpactImpulse=4,MaximumBodyDeltaSpeed=.2f;
        // Body strike knockback, as a whole-body velocity change (m/s) scaled by the
        // measured closing speed. The struck part additionally gets PartSpeed so it
        // leads the reaction. A held victim is mostly kept in place by the grip.
        // 2026-10-16 review: hits push further (was .55-.95 m/s). Consecutive hits on the same target
        // inside ComboWindow add ComboStep each (up to ComboMax), and an unsupported target flies further.
        public float KnockbackMin=.9f,KnockbackMax=1.5f,KnockbackLift=.2f,HeadKnockbackScale=1.25f,HeldKnockbackScale=.35f;
        public float ComboWindow=.8f,ComboStep=.25f,ComboMax=1.75f,AirborneKnockbackScale=1.35f,KnockdownShoveScale=.6f;
        private readonly double[] _lastHitTime={double.NegativeInfinity,double.NegativeInfinity};
        private readonly int[] _comboHits=new int[2];
        public float PartSpeedMin=2.2f,PartSpeedMax=3.4f,MaximumPartImpulse=25;
        // 2026-10-21: a body getting up can be hit but nothing counts (no accumulation, no knockdown, no combo);
        // it is only pushed at this share of a normal hit, with a soft cue. 2026-10-24: the same for a body lying knocked
        // out (the attacker bends down to it, ActiveGrabPairReview.AimLowStrike).
        public float RisingHitScale=.5f;
        // A body lying knocked out takes a full-strength punch on the part hit and a bounce off the floor instead of the
        // sideways push: a punch from above pressed it into the floor and it barely moved, and the half push and soft cue
        // of a rising hit had no punch to it (2026-10-24 review). Lifted off the floor the sideways push slid it out of
        // reach after a hit or two. The bounce is up to LyingHitBounce (m/s) where it was hit, falling to LyingHitBounceFar
        // of that over LyingHitSpread (m), so the struck part jumps and the rest follows; then it flinches
        // (RagdollRecoveryReview.Flinch). Still nothing counts.
        public float LyingHitBounce=.8f,LyingHitBounceFar=.25f,LyingHitSpread=.25f;
        // Striking from above, the forearm's lower half meets a lying body as often as the fist does and reads as the
        // same blow: its striking end reaches LyingTipReachScale times as far (2026-10-24: 8 of 16 contacts were refused).
        public float LyingTipReachScale=2f;
        public const float MinimumFacingDot=.25f;
        public int RejectedGlancing {get;private set;}
        public int RejectedOutsideTip {get;private set;}
        public int RejectedMotion {get;private set;}
        public int PhysicalContactsInWindow {get;private set;}
        public int ThresholdRequests {get;private set;}
        public IReadOnlyList<Hit> Hits=>_hits;
        // Review diagnostics: when set, every attacker-forearm contact with the opponent is logged with its outcome.
        public List<string> ContactLog;
        private void Log(int player,int side,string outcome,float distance=float.NaN,float facing=float.NaN,float closing=float.NaN,string part=null)
        {if(ContactLog!=null)ContactLog.Add($"{_pair.Tick}\tP{player}\t{(side==0?"L":"R")}\t{outcome}\tage={_states[player].SourcePunchAge(side)/PlaybackRate:F3}\tphase={_states[player].Phase(side)}\tdist={distance:F3}\tfacing={facing:F2}\tclosing={closing:F2}\tpart={part}");}
        public GroundHandActionStateMachine State(int player)=>_states[player];
        // Press-to-punch (GroundHandActionStateMachine.PunchOnPress): set by the pair every step.
        public bool PunchOnPress {get=>_states[0].PunchOnPress;set{foreach(var state in _states)state.PunchOnPress=value;}}
        public PunchDownAccumulation Score(int player)=>_scores[player];
        public bool ThresholdPending(int player)=>_threshold[player];
        public int ConfirmedCues=>_feedback!=null?_feedback.ConfirmedCues:0;
        public PhysicalPunchReview(ActiveGrabPairReview pair)
        {_pair=pair;_feedback=pair.GetComponent<PhysicalPunchFeedbackReview>();if(_feedback==null)_feedback=pair.gameObject.AddComponent<PhysicalPunchFeedbackReview>();_feedback.Clear();}
        private bool ActorAvailable(int p)=>_pair.Players[p].Rig!=null&&_pair.Players[p].Rig.isActiveAndEnabled&&!_pair.Players[p].Rig.RagdollActive;
        private bool Available(int p,int side)
        {
            if(!ActorAvailable(p))return false;var shape=_pair.Players[p].Rig.Find("Forearm_"+(side==0?"L":"R")).Shape;
            return shape!=null&&shape.enabled&&!shape.isTrigger&&shape.attachedRigidbody!=null;
        }
        public void QueueEdge(int player,int side,bool down,double heldSeconds=double.NaN)
        {
            if(player<0||player>1||side<0||side>1)throw new ArgumentOutOfRangeException();
            _edges.Enqueue(new Edge{Player=player,Side=side,Down=down,Held=!down&&double.IsFinite(heldSeconds)&&heldSeconds>=0?(double?)heldSeconds:null});
        }
        public bool IsPunching(int p,int side)=>_states[p].Phase(side)==HandIntentPhase.Punch;
        public bool CanHit(int p,int side)=>IsPunching(p,side)&&_states[p].Active(side)&&!_pair.Players[p].Rig.PunchHasContact(side)
            &&(_pair.Players[p].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike||_states[p].SourcePunchAge(side)<=SourceHitEnd+1e-9);
        public void BeforeStep(float seconds)
        {
            _seconds=seconds;_before.Clear();
            Array.Clear(_candidates,0,_candidates.Length);Array.Clear(_blocked,0,_blocked.Length);
            for(var p=0;p<2;p++)
            {
                var player=_pair.Players[p];var state=_states[p];
                _acceptEdges[p]=state.Armed&&player.InputArmed&&!_pair.Suppressed&&ActorAvailable(p);
                if(_pair.Suppressed||!ActorAvailable(p)||!player.InputArmed){state.Suppress();}
                state.ObserveButtonsForRearm(player.Hands[0].Button,player.Hands[1].Button,_pair.Suppressed||!ActorAvailable(p));
                if(!_acceptEdges[p])for(var side=0;side<2;side++)_buttons[p,side]=player.Hands[side].Button;
                if(!ActorAvailable(p))continue;
                _forward[p]=player.Rig.Actor.Motor.Body.rotation*Vector3.forward;
                foreach(var body in player.Rig.Links.Select(l=>l.Body).Append(player.Rig.Actor.Motor.Body))_before[body]=new BodyPose{Position=body.position,Rotation=body.rotation,Velocity=body.linearVelocity,AngularVelocity=body.angularVelocity,Center=body.worldCenterOfMass};
                for(var side=0;side<2;side++){_tip[p,side]=player.Rig.HandPosition(side);_tipVelocity[p,side]=player.Rig.Find("Forearm_"+(side==0?"L":"R")).Body.GetPointVelocity(_tip[p,side]);}
            }
            foreach(var e in _edges)if(_acceptEdges[e.Player]&&e.Held.HasValue)_states[e.Player].PrepareTimedRelease(e.Side,_time,e.Held.Value);
            while(_edges.Count>0)
            {
                var e=_edges.Dequeue();if(_acceptEdges[e.Player])Receive(e.Player,e.Side,e.Down,e.Held);
            }
            for(var p=0;p<2;p++)if(_acceptEdges[p])for(var side=0;side<2;side++)Receive(p,side,_pair.Players[p].Hands[side].Button,null);
            _time+=seconds;
            for(var p=0;p<2;p++)
            {
                _states[p].Advance(_time);
                for(var side=0;side<2;side++)if(!Available(p,side)||_pair.Players[p].Hands[side].MustRelease)_states[p].CancelHand(side);
            }
        }
        private void Receive(int player,int side,bool down,double? held)
        {
            if(_buttons[player,side]==down)return;_buttons[player,side]=down;
            var rig=_pair.Players[player].Rig;var grounded=ActorAvailable(player)&&rig.Actor.Motor.Body.position.y<.065f&&rig.Actor.Motor.Body.linearVelocity.y<.5f;
            // Pressed to punch, anywhere the feet are (a ledge top, a jump; the old fixed floor height refused every punch
            // on top of a wall), except hanging on a wall: there a press is the next grab.
            if(PunchOnPress)grounded=ActorAvailable(player)&&!(_pair.Players[player].Climbing&&!rig.Supported);
            _states[player].Receive(++_sequence[player],side,down,_time,grounded,Available(player,side),held);
        }
        public void Acquired(int player,int side)=>_states[player].TryBeginGrab(side,2-player);
        public void Released(int player,int side)=>_states[player].CancelHand(side);
        public void ApplyReference(int player,int side)
        {
            var rig=_pair.Players[player].Rig;var active=IsPunching(player,side)&&Available(player,side);
            var compact=0f;
            if(active&&ActorAvailable(1-player))
            {
                var separation=_pair.Players[1-player].Rig.Actor.Motor.Body.position-rig.Actor.Motor.Body.position;separation.y=0;
                compact=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.46f,.34f,separation.magnitude));
            }
            rig.SetPunchReference(side,active,(float)_states[player].SourcePunchAge(side),compact);
            if(active){rig.SetHandTarget(side,false,Vector3.zero);rig.SetGripPosture(side,false);rig.SetHandDriveScale(side,3);}
        }
        private float SweptTipDistance(Rigidbody own,Vector3 tip,Rigidbody other,Vector3 contact,BodyPose otherBefore)
        {
            if(!_before.TryGetValue(own,out var ownBefore))return Vector3.Distance(tip,contact);
            var localTip=Quaternion.Inverse(own.rotation)*(tip-own.position);
            var localContact=Quaternion.Inverse(other.rotation)*(contact-other.position);
            var angle=Mathf.Max(Quaternion.Angle(ownBefore.Rotation,own.rotation),Quaternion.Angle(otherBefore.Rotation,other.rotation));
            var segments=Mathf.Clamp(Mathf.CeilToInt(angle/5),1,24);
            var previous=ownBefore.Position+ownBefore.Rotation*localTip-otherBefore.Position-otherBefore.Rotation*localContact;
            var distance=previous.magnitude;
            for(var sample=1;sample<=segments;sample++)
            {
                var t=sample/(float)segments;
                var relative=Vector3.Lerp(ownBefore.Position,own.position,t)+Quaternion.Slerp(ownBefore.Rotation,own.rotation,t)*localTip
                    -Vector3.Lerp(otherBefore.Position,other.position,t)-Quaternion.Slerp(otherBefore.Rotation,other.rotation,t)*localContact;
                var step=relative-previous;var along=step.sqrMagnitude>1e-12f?Mathf.Clamp01(-Vector3.Dot(previous,step)/step.sqrMagnitude):0;
                distance=Mathf.Min(distance,(previous+step*along).magnitude);previous=relative;
            }
            return distance;
        }
        public void Contact(int player,Rigidbody own,Collision collision)
        {
            if(_pair.Suppressed||!ActorAvailable(player))return;var other=collision.rigidbody;
            var ownRig=_pair.Players[player].Rig;
            if(other==ownRig.Actor.Motor.Body||ownRig.Links.Any(l=>l.Body==other))return;
            if(other==null||other.isKinematic){CollectBlock(player,own,collision);return;}
            if(!ActorAvailable(1-player)){if(UncountedTarget(1-player))RisingContact(player,own,other,collision);return;}
            var targetRig=_pair.Players[1-player].Rig;
            if(other!=targetRig.Actor.Motor.Body&&!targetRig.Links.Any(l=>l.Body==other))return;
            var target=1-player;
            for(var side=0;side<2;side++)
            {
                var forearmContact=own==_pair.Players[player].Rig.Find("Forearm_"+(side==0?"L":"R")).Body;
                if(forearmContact&&IsPunching(player,side))ownRig.NoteStrikeTouch(side);
                if(ContactLog!=null&&IsPunching(player,side)&&!CanHit(player,side)&&(forearmContact||own==_pair.Players[player].Rig.Find("UpperArm_"+(side==0?"L":"R")).Body))
                    Log(player,side,_pair.Players[player].Rig.PunchHasContact(side)?"after-contact":_states[player].Active(side)&&(_pair.Players[player].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike||_states[player].SourcePunchAge(side)<=SourceHitEnd)?"not-hittable":"outside-window",part:other.name+(forearmContact?"":" (upper arm)"));
                if(ContactLog!=null&&CanHit(player,side)&&!forearmContact&&own==_pair.Players[player].Rig.Find("UpperArm_"+(side==0?"L":"R")).Body)
                    Log(player,side,"upper-arm-only",part:other.name);
                if(!CanHit(player,side)||!Available(player,side)||_pair.Players[player].Hands[side].Connection!=null||own!=_pair.Players[player].Rig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                PhysicalContactsInWindow++;
                if(_threshold[target]||!_before.TryGetValue(other,out var before))continue;
                for(var c=0;c<collision.contactCount;c++)
                {
                    var point=collision.GetContact(c).point;var tip=_pair.Players[player].Rig.HandPosition(side);var endDistance=Vector3.Distance(tip,point);
                    // CCD contact can occur before the final pose of this fixed
                    // step. Sweep the same small terminal through measured body
                    // poses; never enlarge it to include the middle of the arm.
                    var distance=SweptTipDistance(own,tip,other,point,before);
                    var arc=ownRig.StrikeStyle!=ActiveUpperBodyReview.ReviewStrikeStyle.Straight;
                    // Sweeps use the actual rounded end and distal forearm as
                    // their striking surface. This classifies a real collider
                    // contact; it never creates a larger collision/hit volume.
                    var strikeReach=arc?.08f*ownRig.ArmReachScale:TipRadius;
                    if(distance>strikeReach+ContactTolerance){RejectedOutsideTip++;Log(player,side,"outside-tip",distance,part:other.name);continue;}
                    var local=Quaternion.Inverse(other.rotation)*(point-other.position);var motion=point-(before.Position+before.Rotation*local);
                    // The solver may already stop or rebound a real impact before
                    // this callback. Retain incoming physical point velocity too;
                    // a reference pose or overlap alone never establishes a hit.
                    var previousPoint=before.Position+before.Rotation*local;
                    var targetVelocity=before.Velocity+Vector3.Cross(before.AngularVelocity,previousPoint-before.Center);
                    var incoming=_tipVelocity[player,side]-targetVelocity;
                    var ownMotion=tip-_tip[player,side];
                    if(arc&&_before.TryGetValue(own,out var ownBefore))
                    {
                        var ownLocal=Quaternion.Inverse(own.rotation)*(point-own.position);
                        var previousOwnPoint=ownBefore.Position+ownBefore.Rotation*ownLocal;
                        incoming=ownBefore.Velocity+Vector3.Cross(ownBefore.AngularVelocity,previousOwnPoint-ownBefore.Center)-targetVelocity;
                        ownMotion=point-previousOwnPoint;
                    }
                    var intoOther=-collision.GetContact(c).normal;
                    var direction=arc&&incoming.sqrMagnitude>.0001f?incoming.normalized:_forward[player];
                    if(Vector3.Dot(intoOther,direction)<MinimumFacingDot)
                    {RejectedGlancing++;Log(player,side,"glancing",distance,Vector3.Dot(intoOther,direction),part:other.name);continue;}
                    // Arc strikes close along their real contact normal. A
                    // lateral/downward impact need not travel in root-forward.
                    var closingAxis=arc?intoOther:_forward[player];
                    var sweptClosing=Vector3.Dot(ownMotion-motion,closingAxis)/_seconds;
                    var incomingClosing=Vector3.Dot(incoming,closingAxis);
                    var closing=Mathf.Max(sweptClosing,incomingClosing);
                    if(closing<MinimumClosingSpeed){RejectedMotion++;Log(player,side,"slow",distance,Vector3.Dot(intoOther,direction),closing,other.name);continue;}
                    Log(player,side,"candidate",distance,Vector3.Dot(intoOther,direction),closing,other.name);
                    var part=Array.FindIndex(targetRig.Links.ToArray(),l=>l.Body==other);var previous=_candidates[player,side];
                    if(previous==null||distance<previous.Distance-1e-6f||Mathf.Abs(distance-previous.Distance)<=1e-6f&&part<previous.Part)
                        _candidates[player,side]=new Candidate{Player=player,Side=side,Part=part,Body=other,Point=point,Normal=collision.GetContact(c).normal,Direction=direction,Distance=distance,EndDistance=endDistance,Closing=closing,SweptClosing=sweptClosing,IncomingClosing=incomingClosing};
                }
            }
        }
        // Down, lying or getting up: hits land but count for nothing (RisingHit).
        private bool UncountedTarget(int p)=>_pair.Players[p].Rig!=null&&_pair.Players[p].Rig.isActiveAndEnabled&&_pair.Players[p].Rig.KnockedDown;
        // A fist meeting a body that is down (its knockdown ragdoll, not the standing links): the striking end
        // closing on it is an uncounted hit candidate (RisingHit). Simpler than the standing gate: no swept pose history.
        private void RisingContact(int player,Rigidbody own,Rigidbody other,Collision collision)
        {
            var ownRig=_pair.Players[player].Rig;var targetRig=_pair.Players[1-player].Rig;
            if(!_pair.KnockdownBodies(targetRig).Contains(other))return;
            for(var side=0;side<2;side++)
            {
                if(!CanHit(player,side)||!Available(player,side)||_pair.Players[player].Hands[side].Connection!=null||own!=ownRig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                PhysicalContactsInWindow++;
                for(var c=0;c<collision.contactCount;c++)
                {
                    var contact=collision.GetContact(c);var tip=ownRig.HandPosition(side);var distance=Vector3.Distance(tip,contact.point);
                    var reach=(ownRig.StrikeStyle!=ActiveUpperBodyReview.ReviewStrikeStyle.Straight?.08f*ownRig.ArmReachScale:TipRadius)*(targetRig.GettingUp?1:LyingTipReachScale);
                    if(distance>reach+ContactTolerance){RejectedOutsideTip++;continue;}
                    var incoming=_tipVelocity[player,side]-other.GetPointVelocity(contact.point);var closing=Vector3.Dot(incoming,-contact.normal);
                    if(closing<MinimumClosingSpeed){RejectedMotion++;continue;}
                    var previous=_candidates[player,side];
                    if(previous==null||distance<previous.Distance)
                        _candidates[player,side]=new Candidate{Player=player,Side=side,Part=-1,Body=other,Point=contact.point,Normal=contact.normal,Direction=incoming.sqrMagnitude>.0001f?incoming.normalized:_forward[player],Distance=distance,EndDistance=distance,Closing=closing,SweptClosing=closing,IncomingClosing=closing};
                }
            }
        }
        private void CollectBlock(int player,Rigidbody own,Collision collision)
        {
            var rig=_pair.Players[player].Rig;
            for(var side=0;side<2;side++)
            {
                if(!CanHit(player,side)||!Available(player,side)||_pair.Players[player].Hands[side].Connection!=null||own!=rig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                // A wall can obstruct the forearm before the terminal reaches it,
                // including a contact that started during preparation. This is a
                // blocked stroke, never a scored hit: no tip/speed damage gate.
                for(var c=0;c<collision.contactCount;c++)
                    if(Vector3.Dot(-collision.GetContact(c).normal,rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.Straight?_forward[player]:_tipVelocity[player,side].normalized)>=MinimumFacingDot)
                        _blocked[player,side]=true;
            }
        }
        public void AfterPhysics()
        {
            _feedback.AdvanceTick();
            for(var player=0;player<2;player++)for(var side=0;side<2;side++)
            {
                if(_blocked[player,side]&&!_pair.Suppressed&&Available(player,side)&&CanHit(player,side))
                {_pair.Players[player].Rig.ReceivePunchImpact(side,true);BlockedContacts++;}
                _blocked[player,side]=false;
                var hit=_candidates[player,side];_candidates[player,side]=null;var target=1-player;
                if(hit==null||_pair.Suppressed||!Available(player,side)||!ActorAvailable(target)&&!UncountedTarget(target)||hit.Body==null||hit.Body.isKinematic||_threshold[target]||!CanHit(player,side))continue;
                if(!_states[player].TryConfirmHit(side,target+1))continue;
                var action=_states[player].ActionId(side);
                if(UncountedTarget(target)){RisingHit(player,side,hit,hit.Body.name.StartsWith("Head")?PunchHitRegion.Head:PunchHitRegion.Body,action);continue;}
                var region=hit.Body==_pair.Players[target].Rig.Find("Head").Body?PunchHitRegion.Head:PunchHitRegion.Body;
                var targetKnockdown=_pair.Players[target].Rig.Knockdown;
                var downCount=targetKnockdown!=null?targetKnockdown.Down.State.DownCount:0u;
                var result=_scores[target].Receive(_generation,player+1,action,false,region,downCount);if(result==PunchDownResult.Rejected)continue;
                // A rig with a knockdown ragdoll goes down now (PRD 4.5): the accumulation is consumed and
                // the threshold drops with DownCount. Without one, the request is only recorded.
                // Just after standing up the threshold is held, not spent: the next hit after the protection downs.
                var protectedTarget=_pair.Players[target].Rig.DownProtected;
                var knockdown=result==PunchDownResult.ThresholdReached&&targetKnockdown!=null&&!protectedTarget;
                if(result==PunchDownResult.ThresholdReached&&!protectedTarget){ThresholdRequests++;if(!knockdown)_threshold[target]=true;}
                var bodyStrike=_pair.Players[player].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike;
                var power=Mathf.InverseLerp(.35f,3.5f,hit.Closing);
                var strength=bodyStrike?Mathf.Lerp(.65f,1.3f,power):1;
                var targetRig=_pair.Players[target].Rig;
                var held=_pair.Players[player].Hands.Any(hand=>hand.Connection!=null&&hand.TargetRig==targetRig);
                var bodyDirection=Vector3.ProjectOnPlane(hit.Direction,Vector3.up);
                float impulse,bodyImpulse;
                if(bodyStrike)
                {
                    impulse=Mathf.Min(MaximumPartImpulse,hit.Body.mass*Mathf.Lerp(PartSpeedMin,PartSpeedMax,power));
                    hit.Body.AddForceAtPosition(hit.Direction*impulse,hit.Point,ForceMode.Impulse);
                    _comboHits[target]=_time-_lastHitTime[target]<=ComboWindow?_comboHits[target]+1:0;_lastHitTime[target]=_time;
                    var combo=Mathf.Min(ComboMax,1+ComboStep*_comboHits[target]);
                    var speed=Mathf.Lerp(KnockbackMin,KnockbackMax,power)*(region==PunchHitRegion.Head?HeadKnockbackScale:1)*(held?HeldKnockbackScale:combo*(targetRig.Supported?1:AirborneKnockbackScale));
                    var shove=(bodyDirection.sqrMagnitude>.01f?bodyDirection.normalized*speed:Vector3.zero)+Vector3.up*(held?0:KnockbackLift);
                    // The knockdown blow drops the body near where it stands (the tip-over does the rest).
                    if(knockdown)targetRig.BeginKnockdown(shove*KnockdownShoveScale);else targetRig.ApplyKnockback(shove);
                    bodyImpulse=shove.magnitude*targetRig.TotalMass;
                }
                else
                {
                    impulse=Mathf.Min(Mathf.Max(0,ImpulseNewtonSeconds),hit.Body.mass*Mathf.Max(0,MaximumPartDeltaSpeed));hit.Body.AddForceAtPosition(hit.Direction*impulse,hit.Point,ForceMode.Impulse);
                    bodyImpulse=Mathf.Min(Mathf.Max(0,BodyImpactImpulse),targetRig.TotalMass*Mathf.Max(0,MaximumBodyDeltaSpeed));
                    if(held)bodyImpulse=Mathf.Min(bodyImpulse,1);
                    if(bodyDirection.sqrMagnitude>.01f)targetRig.Actor.Motor.Body.AddForce(bodyDirection.normalized*bodyImpulse,ForceMode.Impulse);
                    if(knockdown)targetRig.BeginKnockdown(bodyDirection.sqrMagnitude>.01f?bodyDirection.normalized:Vector3.zero);
                }
                var pointsAtHit=_scores[target].Points;
                if(knockdown)_scores[target].ConsumeDown();
                _pair.Players[player].Rig.ReceivePunchImpact(side);
                if(!targetRig.RagdollActive)targetRig.ReceivePunchReaction(hit.Body,hit.Direction,hit.Point,hit.Closing,held);
                _feedback.Confirm(region==PunchHitRegion.Head,_pair.ReadKeyboard&&!_pair.ManualStep,_pair.View,_pair.Players[player].Rig,targetRig,hit.Point,hit.Direction,strength);
                _hits.Add(new Hit{tick=_pair.Tick,action=action,attacker=player,target=target,side=side,points=region==PunchHitRegion.Head?2:1,total=pointsAtHit,region=region.ToString(),part=hit.Body.name,position=hit.Point,normal=hit.Normal,strikeDirection=hit.Direction,sourceAge=(float)_states[player].SourcePunchAge(side),tipDistance=hit.Distance,endTipDistance=hit.EndDistance,closingSpeed=hit.Closing,impulse=impulse,bodyImpulse=bodyImpulse,sweptClosingSpeed=hit.SweptClosing,incomingClosingSpeed=hit.IncomingClosing});
                if(_pair.ReadKeyboard)Debug.Log($"Physical punch: P{player+1} {(side==0?"L":"R")} → P{target+1} {region}, {pointsAtHit}"+(knockdown?" — KNOCKDOWN":_threshold[target]?" — Down request recorded; transition is the next review stage.":""),_pair);
            }
        }
        private void RisingHit(int player,int side,Candidate hit,PunchHitRegion region,ulong action)
        {
            var target=1-player;var targetRig=_pair.Players[target].Rig;var power=Mathf.InverseLerp(.35f,3.5f,hit.Closing);
            var lying=!targetRig.GettingUp;var scale=lying?1:RisingHitScale;
            var impulse=Mathf.Min(MaximumPartImpulse,hit.Body.mass*Mathf.Lerp(PartSpeedMin,PartSpeedMax,power))*scale;
            hit.Body.AddForceAtPosition(hit.Direction*impulse,hit.Point,ForceMode.Impulse);
            var flat=Vector3.ProjectOnPlane(hit.Direction,Vector3.up);
            var shove=lying?Vector3.zero:flat.sqrMagnitude>.01f?flat.normalized*Mathf.Lerp(KnockbackMin,KnockbackMax,power)*RisingHitScale:Vector3.zero;
            var bodies=_pair.KnockdownBodies(targetRig);
            foreach(var body in bodies)
            {
                if(!lying){body.AddForce(shove,ForceMode.VelocityChange);continue;}
                var near=Mathf.Exp(-Mathf.Pow(Vector3.Distance(body.worldCenterOfMass,hit.Point)/LyingHitSpread,2));
                var bounce=Vector3.up*LyingHitBounce*Mathf.Lerp(.6f,1,power)*Mathf.Lerp(LyingHitBounceFar,1,near);
                body.AddForce(bounce,ForceMode.VelocityChange);shove+=bounce/bodies.Length;
            }
            if(lying)targetRig.Knockdown.Flinch(Mathf.Lerp(.6f,1,power));
            _pair.Players[player].Rig.ReceivePunchImpact(side);
            var strength=_pair.Players[player].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike?Mathf.Lerp(.65f,1.3f,power):1;
            _feedback.Confirm(false,_pair.ReadKeyboard&&!_pair.ManualStep,_pair.View,_pair.Players[player].Rig,targetRig,hit.Point,hit.Direction,lying?strength:RisingHitScale,false,lying);
            _hits.Add(new Hit{tick=_pair.Tick,action=action,attacker=player,target=target,side=side,points=0,total=_scores[target].Points,region=region.ToString(),part=hit.Body.name,position=hit.Point,normal=hit.Normal,strikeDirection=hit.Direction,sourceAge=(float)_states[player].SourcePunchAge(side),tipDistance=hit.Distance,endTipDistance=hit.EndDistance,closingSpeed=hit.Closing,impulse=impulse,bodyImpulse=shove.magnitude*targetRig.TotalMass,sweptClosingSpeed=hit.SweptClosing,incomingClosingSpeed=hit.IncomingClosing,counted=false});
            if(_pair.ReadKeyboard)Debug.Log($"Physical punch: P{player+1} {(side==0?"L":"R")} → P{target+1} {region} while down — not counted",_pair);
        }
        // A running dropkick's feet met the opponent (ActiveGrabPairReview.Dropkick). Stronger than any punch:
        // a standing target is knocked down along the kick, a body already down is shoved further.
        public float DropkickKnockback=2.6f,DropkickLift=.6f;
        public void ConfirmDropkick(int player,Rigidbody part,Vector3 point,Vector3 direction,float closing)
        {
            var target=1-player;var targetRig=_pair.Players[target].Rig;
            if(_pair.Suppressed||targetRig==null||!targetRig.isActiveAndEnabled)return;
            var rising=targetRig.GettingUp;
            var flat=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            var strength=Mathf.Lerp(.85f,1.2f,Mathf.InverseLerp(1,4,closing));
            var shove=flat*DropkickKnockback*strength+Vector3.up*DropkickLift;
            var head=!targetRig.RagdollActive&&part==targetRig.Find("Head").Body;
            if(targetRig.RagdollActive)foreach(var body in _pair.KnockdownBodies(targetRig))body.AddForce(shove*(rising?.6f*RisingHitScale:.6f),ForceMode.VelocityChange);
            else if(targetRig.DownProtected)targetRig.ApplyKnockback(shove);
            else{if(targetRig.BeginKnockdown(shove))_scores[target].ConsumeDown();}
            _feedback.Confirm(head,_pair.ReadKeyboard&&!_pair.ManualStep,_pair.View,_pair.Players[player].Rig,targetRig,point,flat,rising?RisingHitScale:strength*1.3f,!rising);
            _hits.Add(new Hit{tick=_pair.Tick,attacker=player,target=target,side=-1,points=0,total=_scores[target].Points,region="Dropkick",part=part.name,position=point,strikeDirection=flat,closingSpeed=closing,bodyImpulse=shove.magnitude*targetRig.TotalMass*(rising?.6f*RisingHitScale:1),counted=!rising});
            if(_pair.ReadKeyboard)Debug.Log($"Dropkick: P{player+1} → P{target+1} {part.name}",_pair);
        }
        public void Suppress()
        {
            _edges.Clear();Array.Clear(_candidates,0,_candidates.Length);Array.Clear(_blocked,0,_blocked.Length);for(var p=0;p<2;p++){_states[p].Suppress();for(var side=0;side<2;side++)_pair.Players[p].Rig.CancelPunchReference(side);_pair.Players[p].Rig.ClearImpactReaction();}
        }
        public void Reset()
        {
            Suppress();_generation++;_hits.Clear();BlockedContacts=ThresholdRequests=RejectedMotion=RejectedGlancing=RejectedOutsideTip=PhysicalContactsInWindow=0;
            if(_feedback!=null)_feedback.Clear();
            for(var p=0;p<2;p++){_states[p].Reset();_scores[p].Reset(_generation);_threshold[p]=false;_lastHitTime[p]=double.NegativeInfinity;_comboHits[p]=0;}
        }
    }
}
