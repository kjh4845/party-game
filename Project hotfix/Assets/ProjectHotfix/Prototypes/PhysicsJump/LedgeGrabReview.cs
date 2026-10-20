using System;
using System.Collections.Generic;
using ProjectHotfix.Contracts;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Static authored-surface contact and bounded launch; presentation only follows the contact.
    public sealed class LedgeGrabReview : MonoBehaviour
    {
        internal static readonly HashSet<LedgeGrabReview> Live=new HashSet<LedgeGrabReview>();
        private void OnEnable()=>Live.Add(this);
        public GrabPhysicsReview Grabs;
        public bool RaisedReach;
        // Review-only physical contact spacing. Zero preserves the approved baseline.
        public float ContactStandOff;
        public bool PreserveClimbExitPose;
        private readonly double[] _exitUntil = new double[2];
        private readonly Vector3[] _exitPoints = new Vector3[2];
        private readonly float[] _exitTop = new float[2];
        public void EnableContactReview()
        { Grabs.BoundedContactPose=true;ContactStandOff=.14f;PreserveClimbExitPose=true; }
        private float ReachHeight=>RaisedReach?.82f:Grabs.GrabHeight;
        public int MaximumAssists=2;
        public float CooldownSeconds=.35f,MaximumRiseCU=1.25f,RoofClearanceCU=.15f;
        public float ContactSpring=12000f,ContactDamper=600f,MaximumTetherGap=.30f;
        public int AssistsUsed {get;private set;}
        public int TotalAssists {get;private set;}
        public int Acquisitions {get;private set;}
        public int Breaks {get;private set;}
        public int RejectedRequests {get;private set;}
        public float LastLaunchSpeed {get;private set;}
        public float MaximumObservedForce {get;private set;}
        public float MaximumObservedStress {get;private set;}
        public string LastReleaseReason {get;private set;}
        private readonly Vector3[] _supportForces=new Vector3[2];
        public LedgeHangingState HangingState {get;private set;}
        public void PreparePresentationState()
        {
            var active=!Motor.Grounded&&!Motor.ActionsBlocked&&!Motor.Body.isKinematic;
            var weight=Mathf.Max(1,Motor.Body.mass*UnityEngine.Physics.gravity.magnitude);
            HangingState=new LedgeHangingState { Left=active&&IsHolding(0),Right=active&&IsHolding(1),
                LeftLoad=Mathf.Max(0,_supportForces[0].y)/weight,RightLoad=Mathf.Max(0,_supportForces[1].y)/weight,
                LocalVelocity=Quaternion.Inverse(Motor.Body.rotation)*Motor.Body.linearVelocity,ResetVersion=Motor.StepPoseState.ResetVersion };
        }
        private readonly Vector3[] _ownerAnchors=new Vector3[2];
        private readonly float[] _standOffAmounts=new float[2];
        private readonly ReviewGrabSurface[] _surfaces=new ReviewGrabSurface[2];
        private readonly Vector3[] _localContacts=new Vector3[2],_candidatePoint=new Vector3[2];
        private readonly Vector3[] _contactNormals=new Vector3[2];
        private readonly bool[] _candidate=new bool[2],_owns=new bool[2];
        private readonly GripStressState[] _stress={new GripStressState(),new GripStressState()};
        private readonly Collider[] _contacts=new Collider[32];
        private readonly RaycastHit[] _blockers=new RaycastHit[32];
        private bool _pending;
        private double _time,_nextAllowed,_verticalFreedomUntil;
        private float _supported,_stepSeconds;
        private uint _reset;
        private CapsuleCollider _capsule;private PhysicsMaterial _groundMaterial,_airMaterial;private bool _frictionBound;
        private HandStrikeReview Hands=>Grabs.Hands;
        private PhysicsJumpMotor Motor=>Hands.Actor.Motor;
        public bool OwnsHand(int side)=>_owns[side];
        public bool IsHolding(int side)=>_owns[side]&&_surfaces[side]!=null;
        public ReviewGrabSurface Surface(int side)=>_surfaces[side];
        public float Stress(int side)=>_stress[side].Value;
        public Vector3 ContactPoint(int side)=>_surfaces[side]!=null?_surfaces[side].transform.TransformPoint(_localContacts[side]):_candidatePoint[side];
        private Vector3 Origin(int side)=>Motor.Body.position+Motor.Body.rotation*new Vector3(side==0?-.18f:.18f,.60f,0);
        private Vector3 Seek(int side)=>Motor.Body.position+Motor.Body.rotation*new Vector3(side==0?-.12f:.12f,ReachHeight,RaisedReach?.26f:.32f);
        private bool Reachable(int side,Vector3 point,float reserve)
        {
            var pose=Hands.Actor.FixedPose;var suffix=side==0?"L":"R";
            var upper=pose.WorldReachPosition("UpperArm_"+suffix);var forearm=pose.WorldReachPosition("Forearm_"+suffix);var hand=pose.WorldReachPosition("HandLogical_"+suffix);
            var clip=Hands.Actor.PunchLibrary.clips[side].frames[0];
            var length=Vector3.Distance(upper,forearm)+Vector3.Distance(forearm,hand)+Vector3.Distance(clip.heads[3],clip.tails[3]);
            return Vector3.Distance(upper,point)<=length-reserve;
        }
        public void BeginStep(float seconds)
        {
            _time=Hands.SimulationTime;_stepSeconds=seconds;_candidate[0]=_candidate[1]=false;
            if(!_frictionBound)
            {
                _capsule=Motor.GetComponent<CapsuleCollider>();_groundMaterial=_capsule.sharedMaterial;
                _airMaterial=new PhysicsMaterial("Air capsule no wall adhesion"){staticFriction=0,dynamicFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum};_frictionBound=true;
            }
            var material=Motor.Grounded?_groundMaterial:_airMaterial;if(_capsule.sharedMaterial!=material)_capsule.sharedMaterial=material;
            var reset=Motor.StepPoseState.ResetVersion;
            if(reset!=_reset){_reset=reset;for(var i=0;i<2;i++)Release(i,"Reset",false);_pending=false;AssistsUsed=TotalAssists=0;_nextAllowed=_verticalFreedomUntil=0;_supported=0;}
            _supported=Motor.Grounded&&!IsHolding(0)&&!IsHolding(1)?_supported+seconds:0;
            if(_supported>=.10f)AssistsUsed=0;
        }
        public bool QueueClimbRequest()
        {
            if(!(IsHolding(0)&&_surfaces[0].ClimbAllowed)&&!(IsHolding(1)&&_surfaces[1].ClimbAllowed))return false;
            _pending=true;return true;
        }
        public void CancelPendingRequest(){_pending=false;_exitUntil[0]=_exitUntil[1]=0;}
        private bool CanAssistNow()=>!Motor.Body.isKinematic&&Motor.AuthorityEnabled&&!Motor.ActionsBlocked&&!Hands.InputSuppressed&&Hands.State.Armed&&AssistsUsed<MaximumAssists&&_time+1e-8>=_nextAllowed
            &&((IsHolding(0)&&_surfaces[0].ClimbAllowed)||(IsHolding(1)&&_surfaces[1].ClimbAllowed));
        public void AfterPhysics()
        {
            for(var side=0;side<2;side++)if(OwnsHand(side))
            {
                if(_surfaces[side]==null||!_surfaces[side].isActiveAndEnabled||!_surfaces[side].Shape.enabled){Release(side,"SurfaceLost",true);continue;}
                // During the committed upward launch, hand off before the
                // shoulder crowds an overhead contact into a folded elbow.
                var height=(Quaternion.Inverse(Motor.Body.rotation)*(ContactPoint(side)-Motor.Body.position)).y;
                if(RaisedReach&&_time<_verticalFreedomUntil&&height<.74f){Release(side,"ClimbHandoff",false);continue;}
                if(!Reachable(side,ContactPoint(side),-.003f))Release(side,"ReachBreak",true);
            }
        }
        public void StepHolding(int side,float seconds)
        {
            var surface=_surfaces[side];
            if(Motor.Body.isKinematic){Release(side,"OwnerKinematic",true);return;}
            if(Hands.State.Phase(side)!=HandIntentPhase.Grabbing){Release(side,"Release",false);return;}
            if(surface==null||!surface.isActiveAndEnabled||!surface.Shape.enabled||surface.Shape.isTrigger||surface.Shape.attachedRigidbody!=null)
            {Release(side,"SurfaceLost",true);return;}
            // Catch the fall before spending force on cosmetic spacing. A large
            // immediate horizontal error would consume the bounded support force.
            if(Mathf.Abs(Motor.Body.linearVelocity.y)<.5f&&!_pending&&_time>=_verticalFreedomUntil)
            {
                var next=Mathf.MoveTowards(_standOffAmounts[side],Mathf.Clamp(ContactStandOff,0,.16f),.6f*seconds);
                _ownerAnchors[side]+=Vector3.forward*(next-_standOffAmounts[side]);_standOffAmounts[side]=next;
            }
            var point=ContactPoint(side);var anchor=Motor.Body.position+Motor.Body.rotation*_ownerAnchors[side];
            var free=_time<_verticalFreedomUntil||(_pending&&CanAssistNow());var gap=anchor-point;
            if((free?Vector3.ProjectOnPlane(gap,Vector3.up).magnitude:gap.magnitude)>MaximumTetherGap||Vector3.Distance(Origin(side),point)>(RaisedReach?.415f:.375f)||!Reachable(side,point,-.005f))
            {Release(side,"ReachBreak",true);return;}
            var applied=-gap*ContactSpring-Motor.Body.GetPointVelocity(anchor)*ContactDamper;
            if(free)applied=Vector3.ProjectOnPlane(applied,Vector3.up);
            applied=Vector3.ClampMagnitude(applied,Grabs.BreakForce);var force=applied.magnitude;
            if(float.IsNaN(force)||float.IsInfinity(force)){Release(side,"InvalidForce",true);return;}
            // The upright capsule controller owns yaw; surface support translates its body.
            Motor.Body.AddForce(applied,ForceMode.Force);_supportForces[side]=applied;
            MaximumObservedForce=Mathf.Max(MaximumObservedForce,force);var broken=_stress[side].Step(seconds,force/Mathf.Max(1,Grabs.StressReferenceForce));
            MaximumObservedStress=Mathf.Max(MaximumObservedStress,_stress[side].Value);if(broken)Release(side,"GripStress",true);
        }
        public bool TryAcquire(int side)
        {
            if(Motor.Body.isKinematic||Hands.State.Phase(side)!=HandIntentPhase.GrabSeek)return false;
            var scene=gameObject.scene.GetPhysicsScene();var count=scene.OverlapSphere(Seek(side),Grabs.SeekRadius,_contacts,~0,QueryTriggerInteraction.Ignore);
            if(count==_contacts.Length)return false;
            ReviewGrabSurface best=null;var point=Vector3.zero;var bestDistance=float.PositiveInfinity;
            for(var i=0;i<count;i++)
            {
                var collider=_contacts[i];var surface=collider.GetComponent<ReviewGrabSurface>();
                if(surface==null||!surface.isActiveAndEnabled||collider.attachedRigidbody!=null||collider.isTrigger)continue;
                var lane=Motor.Body.position+Motor.Body.rotation*new Vector3(side==0?-.12f:.12f,ReachHeight,0);
                if(!collider.Raycast(new Ray(lane,Motor.Body.rotation*Vector3.forward),out var hit,.4f)||Mathf.Abs(hit.normal.y)>.8f)continue;
                var delta=hit.point-Origin(side);var distance=delta.magnitude;if(distance<.18f||distance>.37f||!Reachable(side,hit.point,.005f))continue;
                var n=scene.Raycast(Origin(side),delta.normalized,_blockers,distance+.0001f,~0,QueryTriggerInteraction.Ignore);var blocked=n==_blockers.Length;
                for(var j=0;j<n;j++)if(_blockers[j].collider!=collider&&!_blockers[j].collider.transform.IsChildOf(transform))blocked=true;
                if(blocked||distance>bestDistance+.000001f)continue;
                if(Mathf.Abs(distance-bestDistance)<.000001f&&best!=null&&surface.GetInstanceID()>best.GetInstanceID())continue;
                best=surface;point=hit.point;bestDistance=distance;_contactNormals[side]=hit.normal;
            }
            if(best==null)return false;_candidate[side]=true;_candidatePoint[side]=point;
            if(Hands.State.SeekAge(side)<Grabs.ReachSeconds||!Hands.State.TryBeginGrab(side,best.GetInstanceID()))return false;
            _verticalFreedomUntil=0; // A new contact catches the body; it never inherits the old launch release window.
            _ownerAnchors[side]=Quaternion.Inverse(Motor.Body.rotation)*(point-Motor.Body.position);
            _standOffAmounts[side]=0;
            _owns[side]=true;_surfaces[side]=best;_localContacts[side]=best.transform.InverseTransformPoint(point);_stress[side].Reset();Acquisitions++;
            // The high contact already exists in this tick. Catch a fast fall
            // now, before physics can move a full step past the remaining reach.
            if(RaisedReach)StepHolding(side,_stepSeconds);
            return IsHolding(side);
        }
        public void EndStep()
        {
            if(!_pending)return;_pending=false;
            ReviewGrabSurface surface=null;for(var i=0;i<2;i++)if(IsHolding(i)&&_surfaces[i].ClimbAllowed)surface=_surfaces[i];
            if(surface==null||Motor.Body.isKinematic||!Motor.AuthorityEnabled||Motor.ActionsBlocked||Hands.InputSuppressed||!Hands.State.Armed||AssistsUsed>=MaximumAssists||_time+1e-8<_nextAllowed)
            {RejectedRequests++;return;}
            var unit=Motor.GetComponent<CapsuleCollider>().height;var height=Mathf.Clamp(surface.TopY+RoofClearanceCU*unit-Motor.Body.position.y,.10f*unit,MaximumRiseCU*unit);
            LastLaunchSpeed=Mathf.Sqrt(2*Mathf.Abs(UnityEngine.Physics.gravity.y)*height);_verticalFreedomUntil=_time+.6;
            Motor.ApplyClimbLaunch(LastLaunchSpeed);AssistsUsed++;TotalAssists++;_nextAllowed=_time+CooldownSeconds;
        }
        public void Release(int side,string reason,bool broken)
        {
            _exitUntil[side]=0;
            if(PreserveClimbExitPose&&reason=="ClimbHandoff"&&_surfaces[side]!=null)
            {_exitUntil[side]=_time+.20;_exitPoints[side]=ContactPoint(side);_exitTop[side]=_surfaces[side].TopY;}
            var existed=_owns[side];_owns[side]=false;_supportForces[side]=Vector3.zero;
            _surfaces[side]=null;_candidate[side]=false;_stress[side].Reset();
            if(existed){Hands.State.CancelHand(side);LastReleaseReason=reason;if(broken)Breaks++;}
        }
        public bool TryPresentation(int side,out GrabHandPresentationState state)
        {
            if(PreserveClimbExitPose&&_time<_exitUntil[side]&&Hands.State.Phase(side)!=HandIntentPhase.GrabSeek)
            {
                var point=_exitPoints[side];var n=_contactNormals[side];
                state=new GrabHandPresentationState(false,false,point.x,point.y,point.z,0,0,n.x,n.y,n.z,null,_exitTop[side]);return true;
            }
            state=default;var holding=IsHolding(side);if(!holding&&!(_candidate[side]&&Hands.State.Phase(side)==HandIntentPhase.GrabSeek))return false;
            var p=holding?ContactPoint(side):_candidatePoint[side];var normal=Grabs.BoundedContactPose?_contactNormals[side]:Vector3.zero;
            state=new GrabHandPresentationState(true,holding,p.x,p.y,p.z,Mathf.SmoothStep(0,1,Mathf.Clamp01((float)Hands.State.SeekAge(side)/Grabs.ReachSeconds)),RaisedReach?GrabPhysicsReview.RaisedShoulderWeight((Quaternion.Inverse(Motor.Body.rotation)*(p-Motor.Body.position)).y):0,normal.x,normal.y,normal.z);return true;
        }
        private void OnDestroy(){if(_airMaterial!=null)Destroy(_airMaterial);}
        private void OnDisable(){Live.Remove(this);if(_capsule!=null)_capsule.sharedMaterial=_groundMaterial;if(Grabs==null)return;_pending=false;for(var i=0;i<2;i++)Release(i,"Disabled",false);}
    }
}
