using System;
using System.Collections.Generic;
using ProjectHotfix.Contracts;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Constraint authority; never reads or moves rendered hand bones.
    [DefaultExecutionOrder(200)]
    public sealed class GrabPhysicsReview : MonoBehaviour
    {
        private static readonly HashSet<GrabPhysicsReview> Live=new HashSet<GrabPhysicsReview>();
        private void OnEnable()=>Live.Add(this);
        public bool AutoLiftOnHold;
        // Opt-in visual review. This never changes grab admission, joints or input timing.
        public bool BoundedContactPose;
        private readonly GrabArmObstacle[][] _poseObstacles = new GrabArmObstacle[2][];
        private ulong _geometryTick = ulong.MaxValue;
        private uint _geometryReset = uint.MaxValue;
        public int MutualLiftBlocks {get;private set;}
        private ulong _lastAutoTick=ulong.MaxValue;
        private uint _lastAutoReset=uint.MaxValue;
        private readonly bool[] _mutualBlocked=new bool[2];
        public bool MutualLiftBlocked(int side)=>_mutualBlocked[side];
        public bool HoldsTarget(StrikeReviewTarget target)
        {
            if(target==null||!isActiveAndEnabled||Hands==null||!Hands.isActiveAndEnabled)return false;
            for(var side=0;side<2;side++)if(_targets[side]==target&&_joints[side]!=null&&Hands.State.Phase(side)==HandIntentPhase.Grabbing)return true;
            return false;
        }
        public void EnableRaisedGrab()
        { AutoLiftOnHold=true;LiftHeight=.30f;LiftPullback=.10f;LiftSeconds=.65f; }
        private void FixedUpdate()
        {if(AutoLiftOnHold&&Hands!=null&&!Hands.Actor.Motor.ManualStep)StepAutomaticLifts(gameObject.scene,Time.fixedDeltaTime);}
        // Run after ALL hands have consumed this tick's input and created/released
        // contacts, before physics. Manual review harnesses call this same boundary.
        public static void StepAutomaticLifts(Scene scene,float seconds)
        {
            // Snapshot all opposing arms before ANY actor evaluates its next pose.
            foreach(var grabs in Live)
                if(grabs!=null&&grabs.isActiveAndEnabled&&grabs.gameObject.scene==scene&&grabs.Hands!=null)
                    grabs.PrepareContactGeometry();
            foreach(var grabs in Live)
            {
                if(grabs==null||!grabs.AutoLiftOnHold||!grabs.isActiveAndEnabled||grabs.gameObject.scene!=scene||grabs.Hands==null)continue;
                var motor=grabs.Hands.Actor.Motor;var reset=motor.StepPoseState.ResetVersion;
                if(grabs._lastAutoTick==motor.SimulationTick&&grabs._lastAutoReset==reset)continue;
                grabs._lastAutoTick=motor.SimulationTick;grabs._lastAutoReset=reset;
                if(!motor.AuthorityEnabled||motor.ActionsBlocked||motor.Body.isKinematic||grabs.Hands.InputSuppressed||!grabs.Hands.State.Armed)continue;
                grabs.UpdateLift(seconds,true);
            }
        }
        public HandStrikeReview Hands;
        public LedgeGrabReview Ledges;
        // START: partially assist held bodies, retaining a mass-dependent jump penalty.
        public float HeldJumpAssistance = .75f;
        public float MaximumAssistedMassRatio = 2f;
        public int LastJumpAssistedBodies { get; private set; }
        public float LastJumpAssistImpulse { get; private set; }
        private int _assistedJump = -1;
        private uint _assistedReset;
        private readonly StrikeReviewTarget[] _jumpTargets = new StrikeReviewTarget[2];
        private readonly float[] _jumpChanges = new float[2];
        public float GrabHeight = .54f;
        // Default player gesture: hold to grab, release the last own hand to throw.
        public bool ThrowOnButtonRelease = true;
        // Explicit lift API remains available for isolated physics reviews only.
        public bool LiftRequested { get; private set; }
        public float LiftHeight = .10f, LiftPullback = .06f, LiftSeconds = .50f;
        public float ThrowImpulseNewtonSeconds = 30f, MaximumThrowDeltaSpeed = 3f;
        public int ThrowCount { get; private set; }
        public int LiftBlockedCount { get; private set; }
        public float LastThrowImpulse { get; private set; }
        private readonly float[] _liftAmounts = new float[2];
        private readonly Vector3[] _baseAnchors = new Vector3[2];
        private readonly RaycastHit[] _liftHits = new RaycastHit[64];
        public float LiftAmount(int side) => _liftAmounts[side];
        public void SetLiftRequested(bool requested) { LiftRequested = requested && Hands.State.Armed && !Hands.InputSuppressed; }
        public float SeekRadius = .05f, ReachSeconds = .12f, BreakForce = 2000f, StressReferenceForce = 250f, MaximumAnchorGap = .08f;
        private readonly ConfigurableJoint[] _joints = new ConfigurableJoint[2];
        private readonly Collider[] _contactColliders = new Collider[2];
        private readonly StrikeReviewTarget[] _targets = new StrikeReviewTarget[2];
        private readonly Rigidbody[] _connectedBodies = new Rigidbody[2];
        private readonly Vector3[] _targetAnchors = new Vector3[2], _goals = new Vector3[2];
        private readonly uint[] _targetResets = new uint[2], _ownerResets = new uint[2], _targetTopologies = new uint[2];
        private readonly GripStressState[] _stress = { new GripStressState(), new GripStressState() };
        private readonly Collider[] _contacts = new Collider[64];
        private readonly RaycastHit[] _walls = new RaycastHit[64];
        public int Acquisitions { get; private set; }
        public int Releases { get; private set; }
        public int Breaks { get; private set; }
        public string LastReleaseReason { get; private set; }
        public float MaximumObservedForce { get; private set; }
        public float MaximumObservedStress { get; private set; }
        public bool IsHolding(int side) => (_targets[side] != null && _joints[side] != null) || (Ledges!=null&&Ledges.IsHolding(side));
        public StrikeReviewTarget Target(int side) => _targets[side];
        public float Stress(int side) => Ledges!=null&&Ledges.IsHolding(side)?Ledges.Stress(side):_stress[side].Value;
        public ConfigurableJoint Joint(int side) => _joints[side];
        private Vector3 Origin(int side) => transform.TransformPoint(new Vector3(side == 0 ? -.18f : .18f, .60f, 0));
        private float SeekHeight=>GrabHeight;
        private Vector3 SeekPoint(int side) => transform.TransformPoint(new Vector3(side == 0 ? -.12f : .12f, SeekHeight, .32f));
        public Vector3 ContactPoint(int side) => Ledges!=null&&Ledges.IsHolding(side)?Ledges.ContactPoint(side):_connectedBodies[side] == null ? _goals[side] : _connectedBodies[side].position+_connectedBodies[side].rotation*Vector3.Scale(_connectedBodies[side].transform.lossyScale,_targetAnchors[side]);

        public void Step(float deltaTime)
        {
            Ledges?.BeginStep(deltaTime);
            for (var side = 0; side < 2; side++)
            {
                if(Ledges!=null&&Ledges.OwnsHand(side)){Ledges.StepHolding(side,deltaTime);continue;}
                var phase = Hands.State.Phase(side);
                if (_targets[side] != null || _joints[side] != null || phase == HandIntentPhase.Grabbing)
                {
                    if (phase != HandIntentPhase.Grabbing) { Release(side, "Release", false); continue; }
                    var target = _targets[side];
                    if(target!=null && target.ResetVersion==_targetResets[side] && target.InteractionTopologyVersion!=_targetTopologies[side])
                        RefreshContactBinding(side);
                    if (_contactColliders[side] == null || !_contactColliders[side].enabled || _contactColliders[side].isTrigger || !_contactColliders[side].gameObject.activeInHierarchy
                        || target == null || !target.enabled || !target.Eligible || !target.GrabEligible || !target.gameObject.activeInHierarchy
                        || _connectedBodies[side] == null || _connectedBodies[side].isKinematic || target.ResetVersion != _targetResets[side])
                    { Release(side, "TargetLost", true); continue; }
                    if (_joints[side] == null) { Release(side, "JointBreak", true); continue; }
                    var contactBody=_connectedBodies[side];
                    var point = contactBody.position + contactBody.rotation * Vector3.Scale(contactBody.transform.lossyScale, _targetAnchors[side]);
                    var actorBody = Hands.Actor.Motor.Body;
                    var gap = Vector3.Distance(actorBody.position + actorBody.rotation * Vector3.Scale(transform.lossyScale, _joints[side].anchor), point);
                    if (gap > MaximumAnchorGap || Vector3.Distance(Origin(side), point) > .375f)
                    { Release(side, "ReachBreak", true); continue; }
                    var force = _joints[side].currentForce.magnitude;
                    if (float.IsNaN(force) || float.IsInfinity(force)) { Release(side, "InvalidForce", true); continue; }
                    MaximumObservedForce = Mathf.Max(MaximumObservedForce, force);
                    var broken = _stress[side].Step(deltaTime, force / Mathf.Max(1f, StressReferenceForce));
                    MaximumObservedStress = Mathf.Max(MaximumObservedStress, _stress[side].Value);
                    if (broken) Release(side, "GripStress", true);
                    continue;
                }
                _goals[side] = AutoLiftOnHold&&!Hands.Actor.Motor.Grounded
                    ? transform.TransformPoint(new Vector3(side==0?-.12f:.12f,.82f,.26f)) : SeekPoint(side);
                if (phase != HandIntentPhase.GrabSeek) continue;
                var targetCandidate = FindContact(side, out var contact, out var contactCollider);
                if(targetCandidate==null&&Ledges!=null&&Ledges.TryAcquire(side))continue;
                if (targetCandidate != null) _goals[side] = contact;
                if (targetCandidate == null || Hands.State.SeekAge(side) < ReachSeconds) continue;
                if (!Hands.State.TryBeginGrab(side, targetCandidate.GetInstanceID())) continue;
                var joint = gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = contactCollider.attachedRigidbody;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = transform.InverseTransformPoint(contact);
                joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(contact);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.projectionMode = JointProjectionMode.None; joint.enableCollision = true;
                joint.breakForce = BreakForce; joint.breakTorque = BreakForce;
                _joints[side] = joint; _connectedBodies[side]=joint.connectedBody; _targets[side] = targetCandidate; _contactColliders[side] = contactCollider; _targetAnchors[side] = joint.connectedAnchor;
                _targetResets[side] = targetCandidate.ResetVersion; _targetTopologies[side]=targetCandidate.InteractionTopologyVersion; targetCandidate.RegisterGrip(joint); _ownerResets[side] = Hands.Actor.Motor.PresentationState.ResetVersion; _stress[side].Reset(); Acquisitions++;
                var other = 1 - side;
                _liftAmounts[side] = IsHolding(other) && _targets[other] == targetCandidate ? _liftAmounts[other] : 0f;
                _baseAnchors[side] = joint.anchor - LiftOffset(_liftAmounts[side]);
            }
            if(!AutoLiftOnHold)UpdateLift(deltaTime,false);
            UpdateMovementLoad();
            AssistHeldJump();
            Ledges?.EndStep();
        }

        private void RefreshContactBinding(int side)
        {
            var target=_targets[side];var joint=_joints[side];var body=_connectedBodies[side];
            if(joint==null || body==null)return;
            var point=ContactPoint(side);
            var collider=_contactColliders[side];
            if(collider==null || !collider.enabled || !collider.gameObject.activeInHierarchy || body.isKinematic)
            {
                if(!target.TryFindInteractionCollider(point,MaximumAnchorGap,out collider))return;
                body=collider.attachedRigidbody;
            }
            // Preserve the current world attachment through a representation
            // change; never snap the hand to a new collider centre.
            joint.connectedBody=body;
            joint.connectedAnchor=body.transform.InverseTransformPoint(point);
            _connectedBodies[side]=body;_contactColliders[side]=collider;_targetAnchors[side]=joint.connectedAnchor;
            _targetTopologies[side]=target.InteractionTopologyVersion;
        }

        private Vector3 LiftOffset(float amount)
        {var weight=AutoLiftOnHold?Mathf.SmoothStep(0,1,amount):amount;return new Vector3(0f,LiftHeight*weight,-LiftPullback*weight);}
        public static float RaisedShoulderWeight(float contactHeight)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.56f,.70f,contactHeight));

        private void UpdateLift(float deltaTime,bool automatic)
        {
            for (var side = 0; side < 2; side++)
            {
                if (!IsHolding(side) || _targets[side]==null || (side == 1 && _targets[1] == _targets[0])) continue;
                var target=_targets[side];var owner=GetComponent<StrikeReviewTarget>();
                var reciprocal=target.GetComponent<GrabPhysicsReview>();
                var mutual=reciprocal!=null&&reciprocal.HoldsTarget(owner);
                for(var hand=0;hand<2;hand++)if(_targets[hand]==target)_mutualBlocked[hand]=automatic&&mutual;
                // Freeze the existing anchor on mutual contact. Moving either
                // anchor against the reverse locked grip injects conflicting lift.
                if(automatic&&mutual){MutualLiftBlocks++;continue;}
                var next = Mathf.MoveTowards(_liftAmounts[side], (automatic||LiftRequested) ? 1f : 0f, deltaTime / Mathf.Max(.1f, LiftSeconds));
                var delta = transform.TransformVector(LiftOffset(next) - LiftOffset(_liftAmounts[side]));
                var blocked=false;
                if(next>_liftAmounts[side]&&delta.sqrMagnitude>1e-12f)
                {
                    var shapes=automatic?target.InteractionColliders():new[]{_contactColliders[side]};
                    foreach(var shape in shapes)
                    {
                        if(shape==null||!shape.enabled||shape.isTrigger||!shape.gameObject.activeInHierarchy)continue;
                        var bounds=shape.bounds;var extents=Vector3.Max(bounds.extents-Vector3.one*.003f,Vector3.one*.001f);
                        var count=gameObject.scene.GetPhysicsScene().BoxCast(bounds.center,extents,delta.normalized,_liftHits,Quaternion.identity,delta.magnitude+.003f,~0,QueryTriggerInteraction.Ignore);
                        if(count==_liftHits.Length)blocked=true;
                        for(var i=0;i<count;i++)
                        {
                            var collider=_liftHits[i].collider;
                            if(StrikeReviewHurtbox.Resolve(collider)==target)continue;
                            // The capsule pair already resolves ordinary body
                            // contact. Do not classify the holder as a ceiling.
                            if(automatic&&collider.transform.IsChildOf(transform))continue;
                            blocked=true;
                        }
                        if(blocked)break;
                    }
                }
                if (blocked) { LiftBlockedCount++; continue; }
                for (var hand = 0; hand < 2; hand++) if (IsHolding(hand) && _targets[hand] == _targets[side])
                {
                    _liftAmounts[hand] = next;
                    _joints[hand].anchor = _baseAnchors[hand] + LiftOffset(next);
                }
            }
        }

        // A fresh player up-edge is required. Forced cleanup never calls this path.
        public bool TryReleaseThrow(int side, ulong sequence)
        {
            if(Ledges!=null&&side>=0&&side<2&&Ledges.IsHolding(side)&&Hands.State.CanReleaseGrab(sequence,side)){Ledges.Release(side,"SurfaceRelease",false);return false;}
            if (!ThrowOnButtonRelease || !Hands.State.CanReleaseGrab(sequence, side) || !CanThrow(side)) return false;
            var other = 1 - side;
            if (IsHolding(other) && _targets[other] == _targets[side])
            {
                Release(side, "HandRelease", false);
                UpdateMovementLoad();
                return false;
            }
            return ApplyThrow(side);
        }

        // Legacy explicit lift review: this API still requires preparation.
        public bool TryThrow(int side)
        {
            return side >= 0 && side <= 1 && LiftRequested && _liftAmounts[side] >= .25f
                && CanThrow(side) && ApplyThrow(side);
        }

        private bool CanThrow(int side)
        {
            if (side < 0 || side > 1 || !isActiveAndEnabled || !Hands.isActiveAndEnabled || Hands.InputSuppressed || !Hands.State.Armed
                || !Hands.Actor.Motor.AuthorityEnabled || Hands.Actor.Motor.ActionsBlocked || !IsHolding(side) || Hands.State.Phase(side) != HandIntentPhase.Grabbing
                || _targets[side]==null || _ownerResets[side] != Hands.Actor.Motor.PresentationState.ResetVersion
                || !(side == 0 ? Hands.Actor.Arms.LeftAvailable : Hands.Actor.Arms.RightAvailable)) return false;
            var target = _targets[side]; var body = _connectedBodies[side];
            // Up-edges are consumed before the ordinary grip Step. Rebind here
            // too when a Down/get-up transition happened at this same boundary.
            if (target.ResetVersion == _targetResets[side] && target.InteractionTopologyVersion != _targetTopologies[side])
            { RefreshContactBinding(side); body = _connectedBodies[side]; }
            if (!target.Eligible || !target.GrabEligible || !target.enabled || !target.gameObject.activeInHierarchy
                || body == null || body.isKinematic || target.ResetVersion != _targetResets[side]
                || _contactColliders[side] == null || !_contactColliders[side].enabled || _contactColliders[side].isTrigger
                || !_contactColliders[side].gameObject.activeInHierarchy) return false;
            var point = body.position + body.rotation * Vector3.Scale(body.transform.lossyScale, _targetAnchors[side]);
            var actorBody = Hands.Actor.Motor.Body;
            var anchor = actorBody.position + actorBody.rotation * Vector3.Scale(transform.lossyScale, _joints[side].anchor);
            if (Vector3.Distance(Origin(side), point) > .375f || Vector3.Distance(anchor, point) > MaximumAnchorGap) return false;
            return true;
        }

        private bool ApplyThrow(int side)
        {
            var target = _targets[side]; var body = _connectedBodies[side];
            var impulse = Mathf.Min(Mathf.Max(0f, ThrowImpulseNewtonSeconds), target.InteractionMass * Mathf.Max(0f, MaximumThrowDeltaSpeed));
            var direction = (transform.forward + Vector3.up * .35f).normalized;
            // All of this actor's hands on this body release together. Other
            // actors' constraints are left for physics/normal break handling.
            for (var hand = 0; hand < 2; hand++) if (_targets[hand] == target) Release(hand, "Throw", false);
            target.ApplyInteractionImpulse(direction * impulse);
            ThrowCount++; LastThrowImpulse = impulse; UpdateMovementLoad();
            return true;
        }

        private void AssistHeldJump()
        {
            var motor = Hands.Actor.Motor;
            var reset = motor.PresentationState.ResetVersion;
            if (!motor.JumpCommittedThisStep || (_assistedJump == motor.JumpCount && _assistedReset == reset)) return;
            _assistedJump = motor.JumpCount; _assistedReset = reset;
            LastJumpAssistedBodies = 0; LastJumpAssistImpulse = 0f;
            if (!motor.AuthorityEnabled || motor.ActionsBlocked || Hands.InputSuppressed || !Hands.State.Armed
                || float.IsNaN(HeldJumpAssistance) || float.IsInfinity(HeldJumpAssistance)
                || float.IsNaN(MaximumAssistedMassRatio) || float.IsInfinity(MaximumAssistedMassRatio)) return;
            var speed = motor.LastJumpLaunchSpeed;
            var budget = motor.Body.mass * speed * Mathf.Clamp(MaximumAssistedMassRatio, 0, 2) * Mathf.Clamp01(HeldJumpAssistance);
            var count = 0; var needed = 0f;
            for (var side = 0; side < 2; side++)
            {
                if (!IsHolding(side) || _targets[side]==null || Hands.State.Phase(side) != HandIntentPhase.Grabbing) continue;
                var target = _targets[side]; var body = _connectedBodies[side];
                if (body == null || body.isKinematic || target.Body == motor.Body || (count > 0 && target == _jumpTargets[0])) continue;
                // Bound each target's added speed, including a falling target.
                var change = Mathf.Clamp(speed - target.InteractionVerticalVelocity, 0f, speed);
                _jumpTargets[count] = target; _jumpChanges[count] = change;
                needed += target.InteractionMass * change; count++;
            }
            var fraction = needed > 0f ? Mathf.Min(Mathf.Clamp01(HeldJumpAssistance), budget / needed) : 0f;
            for (var i = 0; i < count; i++)
            {
                var target = _jumpTargets[i]; var change = _jumpChanges[i] * fraction;
                if (change <= 0f) continue;
                LastJumpAssistImpulse += target.AssistInteractionUp(change); LastJumpAssistedBodies++;
            }
        }

        private void UpdateMovementLoad()
        {
            var mass = 0f;
            if (IsHolding(0)&&_targets[0]!=null) mass += _targets[0].InteractionMass;
            if (IsHolding(1) && _targets[1]!=null && _targets[1] != _targets[0]) mass += _targets[1].InteractionMass;
            Hands.Actor.Motor.ExternalLoadAccelerationScale = Mathf.Clamp(1f + mass / Hands.Actor.Motor.Body.mass, 1f, 2f);
        }

        private StrikeReviewTarget FindContact(int side, out Vector3 point, out Collider contactCollider)
        {
            point = SeekPoint(side); contactCollider = null; var scene = gameObject.scene.GetPhysicsScene();
            var count = scene.OverlapSphere(point, SeekRadius, _contacts, ~0, QueryTriggerInteraction.Ignore);
            if (count == _contacts.Length) return null;
            StrikeReviewTarget best = null; var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var collider = _contacts[i]; if (collider.transform.IsChildOf(transform)) continue;
                var target = StrikeReviewHurtbox.Resolve(collider);
                if (target == null || !target.enabled || !target.Eligible || !target.GrabEligible || !target.gameObject.activeInHierarchy
                    || collider.attachedRigidbody == null || collider.attachedRigidbody.isKinematic || target.Body == Hands.Actor.Motor.Body) continue;
                // Keep each hand in its own forward lane instead of selecting the
                // point nearest the shoulder, which pulls thick arms across the chest.
                var lane = transform.TransformPoint(new Vector3(side == 0 ? -.12f : .12f, SeekHeight, 0));
                var contact = collider.Raycast(new Ray(lane, transform.forward), out var laneHit, .4f)
                    ? laneHit.point : collider.ClosestPoint(SeekPoint(side));
                var delta = contact - Origin(side); var distance = delta.magnitude;
                if (distance < .18f || distance > .37f) continue;
                var blocked = false;
                var wallCount = scene.Raycast(Origin(side), delta.normalized, _walls, distance + .0001f, ~0, QueryTriggerInteraction.Ignore);
                if (wallCount == _walls.Length) continue;
                for (var w = 0; w < wallCount; w++)
                    if (!_walls[w].collider.transform.IsChildOf(transform) && StrikeReviewHurtbox.Resolve(_walls[w].collider) != target) blocked = true;
                if (blocked || distance > bestDistance + .000001f) continue;
                if (Mathf.Abs(distance - bestDistance) < .000001f && best != null && target.GetInstanceID() > best.GetInstanceID()) continue;
                best = target; point = contact; contactCollider = collider; bestDistance = distance;
            }
            return best;
        }

        private void Release(int side, string reason, bool broken)
        {
            if(Ledges!=null&&Ledges.OwnsHand(side))Ledges.Release(side,reason,broken);
            var existed = _targets[side] != null || _joints[side] != null || Hands.State.Phase(side) == HandIntentPhase.Grabbing;
            var joint = _joints[side];
            if(_targets[side]!=null)_targets[side].UnregisterGrip(joint);
            if (joint != null)
            {
                // Destroy is deferred. Free every axis now so there is no remaining constraint impulse.
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                Destroy(joint);
            }
            _joints[side] = null; _connectedBodies[side]=null; _targets[side] = null; _contactColliders[side] = null; _liftAmounts[side] = 0f; _mutualBlocked[side]=false; _stress[side].Reset();
            if (Hands.State.Phase(side) == HandIntentPhase.Grabbing) Hands.State.CancelHand(side);
            if (existed) { Releases++; if (broken) Breaks++; LastReleaseReason = reason; }
        }
        public void ReleaseTarget(StrikeReviewTarget target,string reason)
        {
            if(target==null)return;
            for(var side=0;side<2;side++)if(_targets[side]==target)Release(side,reason,false);
            UpdateMovementLoad();
        }
        public void ReleaseAll(string reason) { Ledges?.CancelPendingRequest(); LiftRequested = false; for (var side = 0; side < 2; side++) Release(side, reason, false); UpdateMovementLoad(); }
        private void OnDisable() { Live.Remove(this);if (Hands != null) ReleaseAll("Disabled"); }
        private GrabHandPresentationState CaptureHand(int side)
        {
            if(Ledges!=null&&Ledges.TryPresentation(side,out var surfacePose))return surfacePose;
            var phase = Hands.State.Phase(side); var active = phase == HandIntentPhase.GrabSeek || (phase == HandIntentPhase.Grabbing && IsHolding(side));
            var point = ContactPoint(side);
            var normal = Vector3.zero;
            if(BoundedContactPose && IsHolding(side) && _contactColliders[side]!=null)
            {
                var outward=(Origin(side)-point).normalized;
                if(_contactColliders[side].Raycast(new Ray(point+outward*.1f,-outward),out var hit,.2f))normal=hit.normal;
            }
            return new GrabHandPresentationState(active, IsHolding(side), point.x, point.y, point.z,
                Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)Hands.State.SeekAge(side) / ReachSeconds)),
                AutoLiftOnHold?RaisedShoulderWeight(transform.InverseTransformPoint(point).y):_liftAmounts[side],
                normal.x,normal.y,normal.z,BoundedContactPose?_poseObstacles[side]:null);
        }
        private void PrepareContactGeometry()
        {
            var motor=Hands.Actor.Motor;var reset=motor.StepPoseState.ResetVersion;
            if(_geometryTick==motor.SimulationTick&&_geometryReset==reset)return;
            _geometryTick=motor.SimulationTick;_geometryReset=reset;
            for(var side=0;side<2;side++)
            {
                _poseObstacles[side]=null;
                if(!BoundedContactPose||!IsHolding(side)||_targets[side]==null)continue;
                var actor=_targets[side].GetComponent<PhysicsJumpReview>();
                if(actor==null||actor.FixedPose==null||actor.ExternalPhysicsPose)continue;
                var obstacles=new GrabArmObstacle[4];
                for(var arm=0;arm<2;arm++)
                {
                    var suffix=arm==0?"L":"R";var pose=actor.FixedPose;
                    var upper=pose.WorldPosition("UpperArm_"+suffix);var elbow=pose.WorldPosition("Forearm_"+suffix);
                    var frame=actor.PunchLibrary.clips[arm].frames[0];
                    var tip=pose.WorldPosition("HandLogical_"+suffix)+pose.WorldRotation("HandLogical_"+suffix)*Vector3.back*Vector3.Distance(frame.heads[3],frame.tails[3]);
                    obstacles[arm*2]=new GrabArmObstacle(upper.x,upper.y,upper.z,elbow.x,elbow.y,elbow.z,.045f);
                    obstacles[arm*2+1]=new GrabArmObstacle(elbow.x,elbow.y,elbow.z,tip.x,tip.y,tip.z,.035f);
                }
                _poseObstacles[side]=obstacles;
            }
        }
        public GrabPresentationState Capture() => new GrabPresentationState(CaptureHand(0), CaptureHand(1), Hands.State.ResetVersion);
    }
}
