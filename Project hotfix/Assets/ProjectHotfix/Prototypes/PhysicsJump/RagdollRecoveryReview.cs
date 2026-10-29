using System;
using System.Collections.Generic;
using System.Linq;
using ProjectHotfix.Simulation;
using ProjectHotfix.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace ProjectHotfix.PhysicsJump
{
    // Isolated candidate: explicit Down commands only, no damage/impact inference.
    [DefaultExecutionOrder(-50)]
    public sealed class RagdollRecoveryReview : MonoBehaviour
    {
        private sealed class Link
        {
            public Transform Bone; public Rigidbody Body, ParentBody; public ConfigurableJoint Joint;
            public Quaternion RestLocal, BindActorRotation; public Vector3 RestPosition, EntryAnchor, BindActorPosition, BindConnectedAnchor;
        }
        public PhysicsJumpReview Actor { get; private set; }
        public DownStateReview Down { get; private set; }
        public bool Active { get; private set; }
        public uint InteractionTopologyVersion { get; private set; }
        public RecoverySupportSample LastSupportSample { get; private set; }
        private bool HasIncomingGrips => Actor.GetComponent<StrikeReviewTarget>()?.HasIncomingGrips ?? false;
        private bool _heldRecoveryPose;
        public bool UseGetUpAnimation = true;
        public bool RelaxedRagdoll = true;
        public bool ArticulatedTorso;
        // A slumped (sitting/kneeling) body whose torso is more than 60 deg from the floor returns upright
        // directly, as a held body already does, instead of the lying face-up/face-down GetUp. Off by default (Shared Arena unchanged); the
        // r43 interaction rig's punch knockdown slumps rather than lying flat (2026-10-17).
        public bool UprightReturnWithoutGrip;
        // Judge floor support under the pelvis instead of the chest: a slumped body rests on its hips
        // with the chest off the floor and never counted as supported (set before Bind).
        public bool ProbeFromPelvis;
        // Free hips and knees (set before Bind): the r20 limits (thigh -20..25, knee 45 deg, no side
        // swing) kept a limp r43 body standing on its feet in a kneel (2026-10-17 review).
        public bool LooseLegs;
        // Free shoulders (swing 75/70 deg: at 90 a limp arm could balance pointing straight up), elbows (120), neck (40) and trunk (spine/chest 25, side 15), set
        // before Bind: the r20 limits (30, 60, 20, 10-12) laid every limp body out the same way, arms along
        // its sides (2026-10-17 review: falls looked static and alike).
        public bool LooseUpperBody;
        // Elbow bend of a loose upper body (set before Bind). 120 folded the thick r43 arms until they read as broken
        // (2026-10-20 review: "팔이 막 꺾이거나"); the r43 rig uses 90.
        public float LooseElbowBend = 120f;
        // Parts collide with each other except jointed neighbours and pairs already touching in the bind
        // pose (set before Bind). Off = the r20 behaviour where every part ignores every other one, which
        // let loose arms pass through the trunk (2026-10-17).
        public bool SelfCollision;
        // Hanging chain: each joint sees its parent no heavier than its child. A body hanging from one .75 kg
        // forearm otherwise stretched the chain apart (a 27:1 mass ratio, 2026-10-17). Only while hanging:
        // on a falling knockdown the same scaling blew the ragdoll apart.
        public void SetHangingChain(bool hanging)
        {
            foreach (var link in _links) link.Joint.connectedMassScale = hanging ? Mathf.Max(1f, link.ParentBody.mass / link.Body.mass) : 1f;
        }
        // Shoulders that reach overhead (a climber's arms), instead of the 75 deg limp limit.
        public void SetOverheadShoulders(bool overhead)
        {
            foreach (var link in _links)
            {
                if (!link.Bone.name.StartsWith("UpperArm")) continue;
                var limit = overhead ? 165f : LooseUpperBody ? 75f : 30f;
                link.Joint.lowAngularXLimit = new SoftJointLimit { limit = -limit }; link.Joint.highAngularXLimit = new SoftJointLimit { limit = limit };
            }
        }
        private readonly List<(Collider, Collider)> _selfIgnores = new List<(Collider, Collider)>();
        // Pairs already overlapping when a limp episode starts (arms raised over the head, a guard): ignored
        // until they have moved apart, instead of being shot apart by depenetration.
        private readonly List<(Collider, Collider)> _entryOverlaps = new List<(Collider, Collider)>();
        // If the physics body has not followed the GetUp clip .25 s after it ends, complete the GetUp at
        // the clip's standing pose (r43 rig: the recovery forces stall around 30-55 deg).
        public bool SnapCompleteAfterAnimation;
        // Evaluate the authored GetUp live on this rig instead of the r20-baked clips (set before Bind).
        public bool ProceduralGetUp;
        // Stand up over the feet (set before Bind): the standing spot is where the feet lie (prone) or where the
        // drawn-in feet will be (supine), and the get-up keeps them planted there (GetUpAnimationPose.PlantedFeet).
        // A side-lying body rolls to the nearer of face down / face up first, never straight upright (2026-10-20).
        public bool FeetPlantedGetUp;
        // Body proxy radii and Down timing, set before Bind. Defaults are the approved r20 arena values.
        public float SpineRadius = .10f, ChestRadius = .105f, HeadRadius = .12f, UpperArmRadius = .045f, ForearmRadius = .043f,
            ThighRadius = .045f, CalfRadius = .025f, PelvisRadius = .12f;
        // Skin-fitted head length (along the head bone, crown side) and torso (set before Bind; 0 = off, the r20 round proxies). The torso is wide and shallow: the round
        // spine/chest proxies match its depth, a capsule each side (radius, sideways offset) fills out its width, and
        // the pelvis becomes one capsule across the hips (radius, half width) so it no longer pokes out front and back.
        public float HeadLength = .24f, SpineSideRadius, SpineSideOffset, ChestSideRadius, ChestSideOffset, PelvisAcrossRadius, PelvisAcrossHalfWidth;
        public double DownBaseSeconds = 1.5, DownIncrementSeconds = .5, DownMaximumSeconds = 3;
        // Plays the GetUp (and the upright return) this many times faster than authored.
        public float GetUpSpeed = 1;
        // Ragdoll surface friction (r20 arena: .2, Minimum combine).
        public float RagdollFriction = .2f;
        public PhysicsMaterialCombine RagdollFrictionCombine = PhysicsMaterialCombine.Minimum;
        private bool _splitTorso, _standingColliderPrepared;
        private float DownRootMass => _splitTorso ? 3f : 8f;
        private Quaternion _chestToActor;
        private Quaternion ObservedTorsoRotation => _splitTorso
            ? _links.Single(l => l.Bone.name == "Chest").Body.rotation * _chestToActor : Actor.Motor.Body.rotation;
        private Vector3 _pelvisLocal, _getUpEntryPelvis;
        public GetUpAnimationPose GetUpPose { get; private set; }
        // The presentation bone a ragdoll body stands for (its frame matches the body's while the body lies; during the
        // get-up clip the bone shows where the visible limb is). Null for the root.
        // The root has no bone of its own: it rides the pelvis bone, held where it was against it when the clip began.
        // The ragdoll body a skin bone rides on: its own link, else the nearest linked ancestor, else the root.
        public Rigidbody BodyForBone(Transform bone)
        {
            for (var b = bone; b != null && b != Actor.Pose.transform; b = b.parent)
                foreach (var link in _links) if (link.Bone == b) return link.Body;
            return Actor.Motor.Body;
        }
        public bool TryPresentationFrame(Rigidbody body, out Matrix4x4 bodyToPose)
        {
            bodyToPose = Matrix4x4.identity;
            if (body == Actor.Motor.Body) { if (!_bones.TryGetValue("Pelvis", out var pelvis)) return false; bodyToPose = pelvis.localToWorldMatrix * _rootInPelvis * body.transform.worldToLocalMatrix; return true; }
            foreach (var link in _links) if (link.Body == body) { bodyToPose = link.Bone.localToWorldMatrix * link.Body.transform.worldToLocalMatrix; return true; }
            return false;
        }
        private Matrix4x4 _rootInPelvis = Matrix4x4.identity;
        public bool GetUpPosePlaying => GetUpPose != null && GetUpPose.Playing;
        public float GetUpAge { get; private set; }
        private bool _animationStarted;
        private Quaternion _getUpEntryRotation;
        private Vector3 _getUpEntryCom;
        public int Recoveries { get; private set; }
        public float MaximumJointGap { get; private set; }
        public string WorstJoint { get; private set; }
        public RecoveryPresentationPhase WorstGapPhase { get; private set; }
        public float WorstGapGetUpAge { get; private set; }
        public uint WorstGapDownCount { get; private set; }
        public float MaximumSpeed { get; private set; }
        public int AlignmentStarts { get; private set; }
        public float AlignmentToleranceDegrees = 10f;
        // Rolled the wrong way and lying still more than AlignmentRestartDegrees off the target for AlignmentRestartSeconds,
        // the roll starts over from the way the body now lies (2026-10-30: a running-jump dropkicker rolled to 173 deg
        // off its face-down target and lay there for good).
        public float AlignmentRestartDegrees = 90f, AlignmentRestartSeconds = 1f;
        public int AlignmentRestarts { get; private set; }
        public int Interruptions { get; private set; }
        public float ReadyAngle, ReadyDistance, ReadySpeed, ReadyAngular;
        public float MaximumHandoffBoneShift { get; private set; }
        private Quaternion _resetRotation;
        private RigidbodyInterpolation _interpolation;
        private CollisionDetectionMode _collisionMode;
        private int _solverIterations, _solverVelocityIterations;
        private float _angularDamping, _depenetrationVelocity, _maxAngularVelocity;
        public float TotalMass => Actor.Motor.Body.mass + _links.Where(l => !l.Body.isKinematic).Sum(l => l.Body.mass);
        // Review diagnostics: elbow bend per side (deg), positive = flexed toward the arm's front (as at bind), negative =
        // bent the other way.
        public string ElbowDebug()
        {
            var text = "";
            foreach (var side in new[] { "L", "R" })
            {
                var upper = _links.Single(l => l.Bone.name == "UpperArm_" + side); var fore = _links.Single(l => l.Bone.name == "Forearm_" + side);
                var upperDir = upper.Body.rotation * ((CapsuleCollider)upper.Body.GetComponent<Collider>()).center.normalized;
                var foreDir = fore.Body.rotation * ((CapsuleCollider)fore.Body.GetComponent<Collider>()).center.normalized;
                var anterior = upper.Body.rotation * (Quaternion.Inverse(upper.BindActorRotation) * Vector3.forward);
                var angle = Vector3.Angle(upperDir, foreDir);
                text += $" elbow{side}={(Vector3.Dot(foreDir, anterior) >= 0 ? angle : -angle):F0}";
            }
            return text;
        }
        // A punch on the body lying knocked out makes it flinch (2026-10-24 review: the lying body needed more action):
        // for FlinchSeconds the hands, feet and head are pulled in toward the trunk and up at FlinchAcceleration (fading
        // out, mostly up: FlinchLift), the upper arms and thighs at half that; then for FlinchReleaseSeconds they are
        // pushed back out at FlinchRelease of that, lifted a little off the floor's friction, and it is limp again. Pulled
        // mostly inward and released flat, a few hits curled it into a ball that stayed (limp, nothing opens it again)
        // and the next punches missed. Forces, not joint drives: the knocked-out body keeps
        // no drive (MaximumLimpDriveSpring), and the joint limits bend elbows and knees the right way.
        public float FlinchSeconds = .12f, FlinchReleaseSeconds = .18f, FlinchAcceleration = 26f, FlinchLift = 1.5f, FlinchRelease = .8f, FlinchReleaseLift = .35f;
        private float _flinchAge = -1, _flinchStrength;
        public int Flinches { get; private set; }
        public void Flinch(float strength) { if (!Active) return; _flinchAge = 0; _flinchStrength = Mathf.Clamp01(strength); Flinches++; }
        private void StepFlinch(float seconds)
        {
            if (_flinchAge < 0) return;
            if (!Active || Down.State.Phase != RecoveryPresentationPhase.PhysicsTumble || _flinchAge >= FlinchSeconds + FlinchReleaseSeconds) { _flinchAge = -1; return; }
            var release = _flinchAge >= FlinchSeconds;
            var fade = release ? Mathf.Sin(Mathf.PI * (_flinchAge - FlinchSeconds) / FlinchReleaseSeconds) : 1 - _flinchAge / FlinchSeconds;
            var accel = FlinchAcceleration * _flinchStrength * (release ? FlinchRelease * fade : fade * fade);
            var chest = _links.FirstOrDefault(l => l.Bone.name == "Chest"); var center = chest != null ? (chest.Body.worldCenterOfMass + Actor.Motor.Body.worldCenterOfMass) * .5f : Actor.Motor.Body.worldCenterOfMass;
            foreach (var link in _links)
            {
                var name = link.Bone.name;
                var share = name.StartsWith("Forearm") || name.StartsWith("Calf") || name == "Head" ? 1f : name.StartsWith("UpperArm") || name.StartsWith("Thigh") ? .5f : 0f;
                if (share <= 0 || link.Body.isKinematic) continue;
                var inward = Vector3.ProjectOnPlane(center - link.Body.worldCenterOfMass, Vector3.up); inward = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector3.zero;
                var direction = release ? (-inward + Vector3.up * FlinchReleaseLift).normalized : (inward + Vector3.up * FlinchLift).normalized;
                link.Body.AddForce(direction * accel * share, ForceMode.Acceleration);
            }
            _flinchAge += seconds;
        }
        public int CopyDynamicBodies(Rigidbody[] destination)
        {
            if (destination == null || destination.Length < _links.Count + 1) throw new ArgumentException("Body buffer is too small.", nameof(destination));
            var count=0;
            if (!Actor.Motor.Body.isKinematic) destination[count++]=Actor.Motor.Body;
            if (Active) foreach(var link in _links) if(!link.Body.isKinematic) destination[count++]=link.Body;
            return count;
        }
        public Vector3 StandingLocalContact(Rigidbody body,Vector3 localPoint)
        {
            if(body==Actor.Motor.Body)return Vector3.Scale(body.transform.lossyScale,localPoint);
            var link=_links.Single(l=>l.Body==body);
            return link.BindActorPosition+link.BindActorRotation*Vector3.Scale(body.transform.lossyScale,localPoint);
        }
        public bool TryFindInteractionCollider(Vector3 point,float tolerance,out Collider result)
        {
            result=null;var best=tolerance*tolerance;
            foreach(var collider in _colliders.Concat(new[]{_locomotion}))
            {
                if(collider==null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy
                    || collider.attachedRigidbody==null || collider.attachedRigidbody.isKinematic) continue;
                var distance=(collider.ClosestPoint(point)-point).sqrMagnitude;
                if(distance>best)continue;best=distance;result=collider;
            }
            return result!=null;
        }
        private readonly List<Link> _links = new List<Link>();
        private readonly List<Collider> _colliders = new List<Collider>();
        public IReadOnlyList<Collider> Colliders => _colliders;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> _restPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Quaternion> _restRotations = new Dictionary<string, Quaternion>();
        private GameObject _proxyRoot;
        private PhysicsMaterial _ragdollMaterial;
        private CapsuleCollider _locomotion;
        private CapsuleCollider _torso;
        private RecoverySupportProbe _probe;
        private Vector3 _floorPoint, _standingRoot;
        private Quaternion _standingRotation;
        private float _uprightSeconds, _tumbleReadySeconds, _alignSeconds, _alignStuckSeconds, _blockedSeconds, _proppedSeconds, _clearTimer;
        // Shove (m/s) given to a standing character in the way; a downed one gets the smaller BodyPush. Only a static
        // blocker (a wall) makes the downed body shift itself instead.
        // A loose prop (crate) gets RoomPropPush, a downed character RoomBodyPush, every RoomPushInterval. A standing
        // character is nudged every tick it is in the way instead, at RoomNudgeMin plus RoomNudgeGain per metre it is
        // inside, up to RoomNudgeMax (2026-10-21: the old 1.6 m/s shove threw a player standing over the body ~.9 m).
        public float RoomBodyPush = .6f, RoomPropPush = 1.2f, RoomPushInterval = .4f, RoomRadius = .32f;
        public float RoomNudgeMin = .3f, RoomNudgeGain = 4f, RoomNudgeMax = .8f;
        public int RoomPushes { get; private set; }
        // Ticks a standing character was nudged while this body still lay without moving (should stay 0).
        public int LyingNudges { get; private set; }
        // Rolling over, waiting for room or in the get-up itself (any recovery that has started moving).
        public bool Rising => Active && (Down.State.Phase == RecoveryPresentationPhase.AligningToGetUp || Down.State.Phase == RecoveryPresentationPhase.SharedGetUp || Down.State.Phase == RecoveryPresentationPhase.UprightReturn);
        private readonly Collider[] _roomOverlaps = new Collider[16];
        // Characters nudged this tick (by either room check), each once; cleared at the start of every recovery step.
        private readonly HashSet<ActiveUpperBodyReview> _roomPushed = new HashSet<ActiveUpperBodyReview>();
        // Characters within RoomRadius of the standing spot are nudged off; a static blocker only counts (and makes the
        // downed body scoot clear) while the get-up is still waiting for room. Props move and walls count on `interval` ticks.
        // A standing character is only nudged once the body is getting up, never while it still lies there (2026-10-22: a
        // knocked-out body that pushed a puncher off read as the knockout itself shoving).
        private void ClearRoom(Vector3 standingRoot, bool waiting, bool interval)
        {
            var count = gameObject.scene.GetPhysicsScene().OverlapCapsule(standingRoot + Vector3.up * (RoomRadius + .01f), standingRoot + Vector3.up * .83f, RoomRadius, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore);
            var wall = Vector3.zero; var nudged = false;
            for (var i = 0; i < count; i++) if (!(waiting && StandingCharacter(_roomOverlaps[i])) && ShoveFromRoom(_roomOverlaps[i], standingRoot, RoomRadius, waiting, standingRoot.y, true, interval, ref wall) && RoomRig(_roomOverlaps[i]) != null) nudged = true;
            if (interval && !nudged && wall.sqrMagnitude > .0001f) Scoot(wall);
        }
        // One collider in the room the get-up needs (`radius` around `from`), moved away from `from` when `act`: a standing
        // character is nudged (every tick, harder the deeper it stands in), while a downed body (RoomBodyPush) and a loose prop
        // (RoomPropPush) are pushed and a fixed blocker (when `fixedCounts`) adds its push-out to `wall` only on `interval`
        // ticks. Returns whether it was in the way at all (own parts and the floor are not).
        private bool ShoveFromRoom(Collider other, Vector3 from, float radius, bool fixedCounts, float floorY, bool act, bool interval, ref Vector3 wall)
        {
            if (other == _locomotion || _colliders.Contains(other)) return false;
            if (other.attachedRigidbody == null && other.bounds.max.y <= floorY + .05f) return false;
            var away = from - other.ClosestPoint(from); away.y = 0;
            if (away.sqrMagnitude < .0001f) away = Vector3.ProjectOnPlane(from - other.bounds.center, Vector3.up);
            away = -away.normalized;
            var rig = RoomRig(other);
            if (rig != null && rig == Actor.GetComponentInParent<ActiveUpperBodyReview>()) return false;
            if (!act) return true;
            if (rig != null && !rig.RagdollActive)
            {
                if (!_roomPushed.Add(rig)) return true;
                var inside = radius - Vector3.ProjectOnPlane(from - other.ClosestPoint(from), Vector3.up).magnitude;
                if (!rig.Shoved) RoomPushes++;
                if (!_animationStarted && Down.State.Phase != RecoveryPresentationPhase.AligningToGetUp && Down.State.Phase != RecoveryPresentationPhase.UprightReturn) LyingNudges++;
                rig.NudgeFromRoom(away, Mathf.Clamp(RoomNudgeMin + Mathf.Max(0, inside) * RoomNudgeGain, RoomNudgeMin, RoomNudgeMax));
            }
            else if (!interval) return true;
            else if (other.attachedRigidbody != null && !other.attachedRigidbody.isKinematic) { other.attachedRigidbody.AddForce(away * (rig != null ? RoomBodyPush : RoomPropPush), ForceMode.VelocityChange); RoomPushes++; }
            else if (other.attachedRigidbody == null && fixedCounts) wall -= away;
            return true;
        }
        private static bool StandingCharacter(Collider other) { var rig = RoomRig(other); return rig != null && !rig.RagdollActive; }
        private static ActiveUpperBodyReview RoomRig(Collider other)
        {
            var hurtbox = other.GetComponentInParent<StrikeReviewHurtbox>();
            return other.GetComponentInParent<ActiveUpperBodyReview>() ?? (hurtbox != null && hurtbox.ActorRoot != null ? hurtbox.ActorRoot.GetComponentInParent<ActiveUpperBodyReview>() : null);
        }
        // Only characters, who keep being shoved, stand in the standing spot (nothing fixed or loose): once the wait for
        // room runs out the get-up goes ahead anyway (2026-10-21: someone pressing in kept the body down for good).
        private bool OnlyCharactersInRoom(Vector3 standingRoot)
        {
            var count = gameObject.scene.GetPhysicsScene().OverlapCapsule(standingRoot + Vector3.up * (RoomRadius + .01f), standingRoot + Vector3.up * .83f, RoomRadius, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore);
            var wall = Vector3.zero; var any = false;
            for (var i = 0; i < count; i++)
            {
                if (!ShoveFromRoom(_roomOverlaps[i], standingRoot, RoomRadius, true, standingRoot.y, false, false, ref wall)) continue;
                if (RoomRig(_roomOverlaps[i]) == null) return false;
                any = true;
            }
            return any;
        }
        // A fixed blocker (box, wall) moves the downed body instead: it slides along the floor away from it at
        // RoomScootSpeed for a moment (2026-10-21; the old .4 m/s hop flung a body caught under a box).
        public float RoomScootSpeed = .8f, RoomScootSeconds = .3f;
        public int RoomScoots { get; private set; }
        private Vector3 _scoot; private float _scootSeconds;
        private void Scoot(Vector3 direction) { _scoot = Vector3.ProjectOnPlane(direction, Vector3.up).normalized; _scootSeconds = RoomScootSeconds; RoomScoots++; }
        private void StepScoot(float seconds)
        {
            if (_scootSeconds <= 0) return;
            _scootSeconds -= seconds; var goal = _scoot * RoomScootSpeed;
            foreach (var target in _links.Select(l => l.Body).Append(Actor.Motor.Body))
            {
                if (target.isKinematic) continue;
                var velocity = target.linearVelocity; target.AddForce(new Vector3(goal.x - velocity.x, 0, goal.z - velocity.z), ForceMode.VelocityChange);
            }
        }
        // Before the planted get-up starts it needs room for more than its standing spot: both hand plants and the
        // trunk's path from the pelvis up over that spot. While a thing is there (at most RoomHoldSeconds) it waits:
        // loose props are nudged and fixed blockers make the body scoot clear (2026-10-21 review: rising beside someone,
        // the arm and chest went through them). A standing character does not hold it: the knockout ends and the body
        // gets straight up, shoving them off as it rises (ClearRoom, and the walker kept off the rising body by
        // ActiveGrabPairReview.SeparateFromDowned). 2026-10-22 review: lying still while shoving read as the knockout pushing.
        public float RoomHoldSeconds = 1.2f;
        private float _roomHold, _roomPushTimer;
        private readonly HashSet<Collider> _roomFound = new HashSet<Collider>();
        private bool HoldForRoom(float seconds, Vector3 pelvis, Vector3[] anchors, bool[] planted)
        {
            if (!FeetPlantedGetUp || _roomHold >= RoomHoldSeconds) return false;
            var scene = gameObject.scene.GetPhysicsScene(); _roomFound.Clear();
            void Gather(int count) { for (var i = 0; i < count; i++) _roomFound.Add(_roomOverlaps[i]); }
            Gather(scene.OverlapCapsule(_standingRoot + Vector3.up * .31f, _standingRoot + Vector3.up * .85f, .3f, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore));
            // Wide enough for the arms beside the trunk as well, elbows out face down (at .2 m an arm rose into a box by
            // the hip; at .3 a face-down elbow still brushed a wall).
            Gather(scene.OverlapCapsule(pelvis + Vector3.up * .36f, _standingRoot + Vector3.up * .7f, .34f, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore));
            for (var side = 0; side < 2; side++) if (planted[side]) Gather(scene.OverlapSphere(anchors[side] + Vector3.up * .12f, .1f, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore));
            var line = new Vector3(_standingRoot.x, pelvis.y, _standingRoot.z) - pelvis;
            var push = (_roomPushTimer -= seconds) <= 0; var wall = Vector3.zero; var blocked = false;
            foreach (var other in _roomFound)
            {
                if (StandingCharacter(other)) continue;
                var point = other.bounds.center; var along = line.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(point - pelvis, line) / line.sqrMagnitude) : 0;
                var from = pelvis + line * along;
                if (ShoveFromRoom(other, from, .34f, true, _floorPoint.y, true, push, ref wall)) blocked = true;
            }
            if (!blocked) return false;
            if (push) { _roomPushTimer = RoomPushInterval; if (wall.sqrMagnitude > .0001f) Scoot(wall); }
            _roomHold += seconds; return true;
        }
        // Still no room beside the trunk once the wait for room is over (boxed in on both sides): the arm on a cramped
        // side does not plant and push but stays tucked against the body (its elbow flared into the box, 2026-10-21).
        // Only checked when the wait ran out without making room; the probe reaches .4 m out, where a flared elbow goes.
        private bool ArmCramped(Vector3 pelvis, Vector3 outward)
        {
            var chest = _bones["Chest"].position; outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized * .3f;
            var count = gameObject.scene.GetPhysicsScene().OverlapCapsule(pelvis + outward + Vector3.up * .1f, new Vector3(chest.x, pelvis.y, chest.z) + outward + Vector3.up * .1f, .1f, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore);
            var wall = Vector3.zero;
            for (var i = 0; i < count; i++) if (ShoveFromRoom(_roomOverlaps[i], pelvis, .1f, true, _floorPoint.y, false, false, ref wall)) return true;
            return false;
        }
        // A hand plants only on open floor: the first surface under the planned point must be the floor the body lies
        // on (not another character, a box top or a crate) with nothing else around the palm; failing that, points
        // pulled in toward the body and along it are tried, and with none the hand stays free (2026-10-21).
        private static readonly Vector2[] AnchorTries = { Vector2.zero, new Vector2(.06f, 0), new Vector2(.12f, 0), new Vector2(.06f, .08f), new Vector2(.06f, -.08f) };
        private readonly RaycastHit[] _anchorHits = new RaycastHit[16];
        private bool TryFloorAnchor(Vector3 planned, Vector3 inward, Vector3 along, out Vector3 anchor)
        {
            var scene = gameObject.scene.GetPhysicsScene();
            foreach (var offset in AnchorTries)
            {
                var point = planned + inward * offset.x + along * offset.y;
                var count = scene.Raycast(point + Vector3.up * .5f, Vector3.down, _anchorHits, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var best = float.PositiveInfinity; RaycastHit floor = default;
                for (var i = 0; i < count; i++) if (_anchorHits[i].distance < best && _anchorHits[i].collider != _locomotion && !_colliders.Contains(_anchorHits[i].collider)) { floor = _anchorHits[i]; best = floor.distance; }
                if (floor.collider == null || floor.normal.y < .7f || Mathf.Abs(floor.point.y - _floorPoint.y) > .05f) continue;
                var overlaps = scene.OverlapSphere(floor.point + Vector3.up * .06f, .05f, _roomOverlaps, ~0, QueryTriggerInteraction.Ignore); var open = true;
                for (var i = 0; i < overlaps && open; i++) open = _roomOverlaps[i] == floor.collider || _roomOverlaps[i] == _locomotion || _colliders.Contains(_roomOverlaps[i]);
                if (!open) continue;
                anchor = floor.point + floor.normal * .01f; return true;
            }
            anchor = planned; return false;
        }
        private uint _motorReset;
        private ulong _sequence;
        
        private void Start() { if (Actor == null) Bind(GetComponent<PhysicsJumpReview>()); }
        public void Bind(PhysicsJumpReview actor)
        {
            _splitTorso = ArticulatedTorso;
            if (_splitTorso && !RelaxedRagdoll) throw new InvalidOperationException("Articulated candidate requires limp ragdoll mode.");
            Actor = actor; actor.EnableGrabReview();
            _collisionMode = actor.Motor.Body.collisionDetectionMode; _interpolation = actor.Motor.Body.interpolation; _solverIterations = actor.Motor.Body.solverIterations; _solverVelocityIterations = actor.Motor.Body.solverVelocityIterations;
            _maxAngularVelocity = actor.Motor.Body.maxAngularVelocity; _angularDamping = actor.Motor.Body.angularDamping; _depenetrationVelocity = actor.Motor.Body.maxDepenetrationVelocity;
            var resetForward = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up);
            if (resetForward.sqrMagnitude < .01f) resetForward = Vector3.ProjectOnPlane(actor.transform.up, Vector3.up) * (actor.transform.forward.y > 0 ? -1 : 1);
            _resetRotation = Quaternion.LookRotation(resetForward, Vector3.up);
            foreach (var bone in actor.Pose.GetComponentsInChildren<Transform>())
            { _bones[bone.name] = bone; _restPositions[bone.name] = bone.localPosition; _restRotations[bone.name] = bone.localRotation; }
            _pelvisLocal = transform.InverseTransformPoint(_bones["Pelvis"].position);
            _locomotion = actor.GetComponent<CapsuleCollider>();
            _torso = actor.gameObject.AddComponent<CapsuleCollider>(); _torso.radius = PelvisRadius; _torso.height = _splitTorso ? .26f : .55f; _torso.center = new Vector3(0, _splitTorso ? .20f : .415f, 0); _torso.enabled = false; _torso.contactOffset = .002f;
            if (_splitTorso && PelvisAcrossRadius > 0) { _torso.direction = 0; _torso.radius = PelvisAcrossRadius; _torso.height = 2 * Mathf.Max(PelvisAcrossRadius, PelvisAcrossHalfWidth); }
            _colliders.Add(_torso);
            _proxyRoot = new GameObject("RagdollCandidatePhysics"); SceneManager.MoveGameObjectToScene(_proxyRoot, actor.gameObject.scene);
            _proxyRoot.AddComponent<StrikeReviewHurtbox>().ActorRoot = actor.transform;
            if (_splitTorso)
            {
                AddLink("Spine", "Chest", null, 2.5f, SpineRadius, 0, 10f); AddSides("Spine", SpineSideRadius, SpineSideOffset);
                AddLink("Chest", "Neck", "Spine", 2.5f, ChestRadius, 0, 12f); AddSides("Chest", ChestSideRadius, ChestSideOffset);
                _chestToActor = Quaternion.Inverse(_bones["Chest"].rotation) * actor.transform.rotation;
            }
            AddLink("Head", null, _splitTorso ? "Chest" : null, 3f, HeadRadius, HeadLength, _splitTorso ? 20f : RelaxedRagdoll ? 10f : 5f);
            foreach (var side in new[] { "L", "R" })
            {
                AddLink("UpperArm_" + side, "Forearm_" + side, _splitTorso ? "Chest" : null, 1.25f, UpperArmRadius, 0, RelaxedRagdoll ? 30f : 5f);
                AddLink("Forearm_" + side, "HandLogical_" + side, "UpperArm_" + side, .75f, ForearmRadius, 0, RelaxedRagdoll ? 60f : 25f);
                AddLink("Thigh_" + side, "Calf_" + side, null, 1.25f, ThighRadius, 0, RelaxedRagdoll ? 30f : 8f);
                AddLink("Calf_" + side, "Foot_" + side, "Thigh_" + side, RelaxedRagdoll ? 1.25f : .75f, CalfRadius, 0, RelaxedRagdoll ? 60f : 12f);
                if (!RelaxedRagdoll) AddLink("Foot_" + side, null, "Calf_" + side, .5f, .02f, .05f, 6f);
                else
                {
                    var calf = _links.Single(l=>l.Bone.name=="Calf_"+side).Body;
                    var foot = new GameObject("Foot_"+side+"_Collision"); foot.layer=2; foot.transform.SetPositionAndRotation(_bones["Foot_"+side].position,_bones["Foot_"+side].rotation); foot.transform.SetParent(calf.transform,true);
                    var capsule=foot.AddComponent<CapsuleCollider>(); capsule.direction=2; capsule.radius=.02f; capsule.height=.05f; capsule.center=Vector3.back*.025f; capsule.contactOffset=.002f; capsule.enabled=false; _colliders.Add(capsule);
                }
            }
            _ragdollMaterial = new PhysicsMaterial("Ragdoll candidate friction") { dynamicFriction = RagdollFriction, staticFriction = RagdollFriction, frictionCombine = RagdollFrictionCombine, bounciness = 0 };
            foreach (var collider in _colliders) collider.sharedMaterial = _ragdollMaterial;
            BuildSelfIgnores();
            ApplySelfIgnores();
            if (_splitTorso) foreach(var collider in _colliders) Physics.IgnoreCollision(_locomotion,collider);
            Down = actor.gameObject.AddComponent<DownStateReview>(); Down.Bind(actor, new DownTimingProfile(DownBaseSeconds, DownIncrementSeconds, DownMaximumSeconds), true);
            _probe = new RecoverySupportProbe(_splitTorso && !ProbeFromPelvis ? _links.Single(l=>l.Bone.name=="Chest").Body : actor.Motor.Body, (_splitTorso ? _colliders.Concat(new[]{_locomotion}).ToArray() : _colliders.ToArray()), .17f, 1f)
            { ProbeDistance = .65f, MaximumRelativeSpeed = .3f, MaximumRelativeAngularSpeed = .8f, RequiredStableSeconds = .15f };
            if (UseGetUpAnimation)
            {
                GetUpPose = actor.Pose.gameObject.AddComponent<GetUpAnimationPose>();
                GetUpPose.Procedural = ProceduralGetUp; GetUpPose.PlantedFeet = FeetPlantedGetUp;
                GetUpPose.Bind(ProceduralGetUp ? null : Resources.Load<AnimationClip>("ReviewGetUp/GetUp_FaceUp"), ProceduralGetUp ? null : Resources.Load<AnimationClip>("ReviewGetUp/GetUp_FaceDown"),
                    actor.PunchLibrary.clips[0].Sample(7f/30,0), actor.PunchLibrary.clips[1].Sample(7f/30,0));
            }
            _motorReset = actor.Motor.PresentationState.ResetVersion; _proxyRoot.SetActive(false);
        }
        private void BuildSelfIgnores()
        {
            _selfIgnores.Clear();
            for (var i = 0; i < _colliders.Count; i++) for (var j = i + 1; j < _colliders.Count; j++)
            {
                var a = _colliders[i]; var b = _colliders[j];
                if (!SelfCollision || Jointed(BodyOf(a), BodyOf(b)) || CapsuleGap(a, b) < .01f) _selfIgnores.Add((a, b));
            }
        }
        // Re-enabling a collider clears its ignore pairs, so they are re-applied after every toggle.
        private void ApplySelfIgnores() { foreach (var (a, b) in _selfIgnores) Physics.IgnoreCollision(a, b); foreach (var (a, b) in _entryOverlaps) Physics.IgnoreCollision(a, b); }
        private void CaptureEntryOverlaps()
        {
            _entryOverlaps.Clear();
            if (!SelfCollision) return;
            for (var i = 0; i < _colliders.Count; i++) for (var j = i + 1; j < _colliders.Count; j++)
            {
                var pair = (_colliders[i], _colliders[j]);
                if (!_selfIgnores.Contains(pair) && CapsuleGap(_colliders[i], _colliders[j]) < .005f) _entryOverlaps.Add(pair);
            }
        }
        private void ReleaseEntryOverlaps()
        {
            for (var i = _entryOverlaps.Count - 1; i >= 0; i--)
            {
                var (a, b) = _entryOverlaps[i];
                if (CapsuleGap(a, b) < .01f) continue;
                Physics.IgnoreCollision(a, b, false); _entryOverlaps.RemoveAt(i);
            }
        }
        private Rigidbody BodyOf(Collider collider) => collider.GetComponentInParent<Rigidbody>();
        private bool Jointed(Rigidbody a, Rigidbody b) => a == b || _links.Any(l => l.Body == a && l.ParentBody == b || l.Body == b && l.ParentBody == a);
        // Surface gap of two capsules in their current (bind) placement; negative when they overlap.
        private static float CapsuleGap(Collider a, Collider b)
        {
            Segment(a, out var a0, out var a1, out var ra); Segment(b, out var b0, out var b1, out var rb);
            return SegmentDistance(a0, a1, b0, b1) - ra - rb;
        }
        private static void Segment(Collider collider, out Vector3 p0, out Vector3 p1, out float radius)
        {
            var capsule = (CapsuleCollider)collider;
            var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            var half = Mathf.Max(0, capsule.height * .5f - capsule.radius);
            p0 = capsule.transform.TransformPoint(capsule.center - axis * half); p1 = capsule.transform.TransformPoint(capsule.center + axis * half);
            radius = capsule.radius;
        }
        private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a < 1e-8f && e < 1e-8f) return r.magnitude;
            if (a < 1e-8f) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                var c = Vector3.Dot(d1, r);
                if (e < 1e-8f) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    var b = Vector3.Dot(d1, d2); var denominator = a * e - b * b;
                    s = denominator > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); } else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }
        // Two more capsules on the link's body, beside the round proxy (bone x = sideways), spanning the same length.
        private void AddSides(string name, float radius, float offset)
        {
            if (radius <= 0) return;
            var body = _links.Single(l => l.Bone.name == name).Body; var main = body.GetComponent<CapsuleCollider>();
            foreach (var sign in new[] { -1f, 1f })
            {
                var side = body.gameObject.AddComponent<CapsuleCollider>(); side.direction = 2; side.radius = radius; side.height = Mathf.Max(radius * 2, main.height);
                side.center = main.center + Vector3.right * sign * offset; side.contactOffset = .002f; side.enabled = false; _colliders.Add(side);
            }
        }
        private void AddLink(string name, string tipName, string parentName, float mass, float radius, float length, float limit)
        {
            var bone = _bones[name]; var go = new GameObject(name + "_Physics"); go.layer = 2; go.transform.SetParent(_proxyRoot.transform, false);
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.isKinematic = true; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.interpolation = RigidbodyInterpolation.None;
            body.solverIterations = _splitTorso ? 256 : 96; body.solverVelocityIterations = _splitTorso ? 128 : RelaxedRagdoll ? 48 : 16; body.maxAngularVelocity = 4; body.angularDamping = 2; body.maxDepenetrationVelocity = 2;
            var collider = go.AddComponent<CapsuleCollider>(); collider.direction = 2; collider.radius = radius; collider.contactOffset = .002f;
            var delta = tipName == null ? Vector3.back * length : bone.InverseTransformPoint(_bones[tipName].position);
            collider.center = delta * .5f; collider.height = Mathf.Max(radius * 2, delta.magnitude); collider.enabled = false;
            var parent = parentName == null ? Actor.Motor.Body : _links.Single(l => l.Bone.name == parentName).Body;
            if (LooseUpperBody) limit = name == "Head" ? 40f : name == "Spine" || name == "Chest" ? 25f : name.StartsWith("UpperArm") ? 75f : limit;
            var joint = go.AddComponent<ConfigurableJoint>(); joint.connectedBody = parent; joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero; joint.connectedAnchor = parent.transform.InverseTransformPoint(bone.position);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -limit }; joint.highAngularXLimit = new SoftJointLimit { limit = limit };
            joint.angularYLimit = joint.angularZLimit = new SoftJointLimit { limit = name.StartsWith("Forearm") || name.StartsWith("Calf") ? 4f : limit };
            if (name != "Head") joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
            if (RelaxedRagdoll && (name.StartsWith("UpperArm") || name.StartsWith("Forearm")))
            {
                joint.axis = Vector3.up; joint.secondaryAxis = Vector3.right;
                if (name.StartsWith("UpperArm")) { joint.angularYMotion = ConfigurableJointMotion.Locked; }
                else
                {
                    var bend = LooseUpperBody ? LooseElbowBend : 60f;
                    joint.lowAngularXLimit = new SoftJointLimit { limit = name.EndsWith("_L") ? -bend : -5f };
                    joint.highAngularXLimit = new SoftJointLimit { limit = name.EndsWith("_L") ? 5f : bend };
                }
            }
            
            if (RelaxedRagdoll && name.StartsWith("Thigh")) { joint.lowAngularXLimit = new SoftJointLimit { limit = LooseLegs ? -80 : -20 }; joint.highAngularXLimit = new SoftJointLimit { limit = LooseLegs ? 60 : 25 }; }
            if (RelaxedRagdoll && name.StartsWith("Calf")) { joint.lowAngularXLimit = new SoftJointLimit { limit = LooseLegs ? -110 : -45 }; joint.highAngularXLimit = new SoftJointLimit { limit = 3 }; }
            if (_splitTorso)
            {
                if (name == "Spine" || name == "Chest")
                {
                    joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
                    joint.angularYLimit = new SoftJointLimit { limit = LooseUpperBody ? 15f : 4f };
                    joint.angularZMotion = ConfigurableJointMotion.Locked;
                }
                if (name.StartsWith("UpperArm") || name.StartsWith("Thigh"))
                {
                    // Open lateral swing while retaining the axial twist lock.
                    joint.angularYMotion = ConfigurableJointMotion.Limited;
                    joint.angularYLimit = new SoftJointLimit { limit = name.StartsWith("UpperArm") ? LooseUpperBody ? 70f : 4f : LooseLegs ? 25f : 2f };
                }
            }
            joint.rotationDriveMode = RotationDriveMode.XYAndZ; joint.angularXDrive = joint.angularYZDrive = new JointDrive { positionSpring = 4, positionDamper = 1, maximumForce = 20 };
            joint.projectionMode = JointProjectionMode.None;
            _links.Add(new Link { Bone = bone, Body = body, ParentBody = parent, Joint = joint, RestLocal = bone.localRotation, RestPosition = bone.localPosition, EntryAnchor = joint.connectedAnchor, BindConnectedAnchor = joint.connectedAnchor, BindActorPosition = transform.InverseTransformPoint(bone.position), BindActorRotation = Quaternion.Inverse(transform.rotation)*bone.rotation }); _colliders.Add(collider);
        }
        public bool BeginDown(Vector3 impulse, Vector3 angularImpulse) => Begin(impulse, angularImpulse, true);
        // A non-Down limp episode (the shared DropkickRecovery state: no DownCount or wait), e.g. a climb.
        public bool BeginTumble(Vector3 impulse, Vector3 angularImpulse) => Begin(impulse, angularImpulse, false);
        // While set, a limp body does not start any get-up (it is hanging from a ledge).
        public bool HoldTumble;
        // A held tumble pose (spring, 0 = limp): a dropkick flies as one stiff body in the pose it took off
        // in, then goes limp (ActiveUpperBodyReview.Dropkick).
        public float TumbleHoldSpring;
        // Review measurement: the strongest joint drive seen while limp (a knocked-out body must have none).
        public float MaximumLimpDriveSpring { get; private set; }
        private bool Begin(Vector3 impulse, Vector3 angularImpulse, bool down)
        {
            if (Active || !(down ? Down.TryBeginDown(Down.State.RoundGeneration, ++_sequence) : Down.State.TryBeginDropkickRecovery(Down.State.RoundGeneration, ++_sequence))) return false;
            if (!down) Down.RefreshLock();
            var body = Actor.Motor.Body; var momentum = body.mass * body.linearVelocity; _uprightSeconds = 0; _alignSeconds = _alignStuckSeconds = 0;
            if (_splitTorso)
            {
                // Re-enabling a joint rebuilds its reference frame. Put inactive
                // proxies in bind pose at the CURRENT actor heading first; old
                // world poses must not become the next episode's angular limits.
                foreach(var link in _links)
                {
                    var neutralPosition=body.position+body.rotation*link.BindActorPosition;
                    var neutralRotation=body.rotation*link.BindActorRotation;
                    link.Body.transform.SetPositionAndRotation(neutralPosition,neutralRotation);
                    link.Body.position=neutralPosition;link.Body.rotation=neutralRotation;
                    link.Joint.connectedAnchor=link.BindConnectedAnchor;
                }
            }
            _proxyRoot.SetActive(true);
            foreach (var link in _links)
            {
                link.Joint.angularXDrive = link.Joint.angularYZDrive = new JointDrive { positionSpring = 4, positionDamper = 1, maximumForce = 20 };
                var point = Actor.FixedPose!=null ? Actor.FixedPose.WorldPosition(link.Bone.name) : body.position+body.rotation*link.BindActorPosition;
                var rotation = Actor.FixedPose!=null ? Actor.FixedPose.WorldRotation(link.Bone.name) : body.rotation*link.BindActorRotation;
                link.Body.position = point; link.Body.rotation = rotation; link.Body.transform.SetPositionAndRotation(point, rotation);
                link.EntryAnchor = Quaternion.Inverse(link.ParentBody.rotation) * (point - link.ParentBody.position); link.Joint.connectedAnchor = link.EntryAnchor; link.Body.isKinematic = false; link.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; link.Body.linearVelocity = body.GetPointVelocity(link.Body.worldCenterOfMass); link.Body.angularVelocity = body.angularVelocity;
            }
            _standingColliderPrepared=false; _locomotion.enabled = false; foreach (var collider in _colliders) collider.enabled = true;
            CaptureEntryOverlaps();
            ApplySelfIgnores();
            body.mass = DownRootMass; body.linearVelocity = (momentum - _links.Aggregate(Vector3.zero, (sum, l) => sum + l.Body.mass * l.Body.linearVelocity)) / DownRootMass; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.interpolation = RigidbodyInterpolation.None; body.constraints = RigidbodyConstraints.None; body.ResetCenterOfMass(); body.ResetInertiaTensor();
            body.angularDamping = 2; body.maxDepenetrationVelocity = 2; body.maxAngularVelocity = 4; body.solverIterations = _splitTorso ? 256 : 96; body.solverVelocityIterations = _splitTorso ? 128 : RelaxedRagdoll ? 48 : 16;
            if (_splitTorso) RefreshJointFrames();
            ConfigureMuscleDrive(true);
            Actor.ExternalPhysicsPose = true; Active = true; _animationStarted = false; GetUpAge = 0; GetUpPose?.Cancel(true); _probe.Reset();
            InteractionTopologyVersion++;
            body.AddForce(Vector3.ClampMagnitude(impulse, 20), ForceMode.Impulse); body.AddTorque(Vector3.ClampMagnitude(angularImpulse, 5), ForceMode.Impulse);
            return true;
        }
        public Vector3 ApplyEntryHit(Vector3 impulse, Vector3 point, float maximumDeltaSpeed)
        {
            var body=Actor.Motor.Body;
            var applied=Vector3.ClampMagnitude(impulse,body.mass*Mathf.Max(0,maximumDeltaSpeed));
            body.AddForceAtPosition(applied,point,ForceMode.Impulse);
            return applied;
        }
        private JointDrive RecoveryDrive(Link link) => _splitTorso && (link.Bone.name == "Spine" || link.Bone.name == "Chest")
            ? new JointDrive { positionSpring = 150, positionDamper = 15, maximumForce = 60 }
            : new JointDrive { positionSpring = 35, positionDamper = 4, maximumForce = 60 };
        private void ConfigureMuscleDrive(bool limp)
        {
            if (!RelaxedRagdoll) return;
            foreach (var link in _links)
            { link.Joint.angularXDrive = link.Joint.angularYZDrive = limp ? TumbleHoldSpring > 0 ? new JointDrive { positionSpring = TumbleHoldSpring, positionDamper = TumbleHoldSpring * .06f, maximumForce = TumbleHoldSpring * .5f } : new JointDrive() : RecoveryDrive(link); link.Body.angularDamping = limp ? TumbleHoldSpring > 0 ? .3f : 1.2f : 2f; }
            Actor.Motor.Body.angularDamping = limp ? TumbleHoldSpring > 0 ? .3f : 1.2f : 2f;
        }
        public void ReadFixedDebugInput()
        {
            if (Actor == null || !Actor.ReadKeyboard || Keyboard.current == null) return;
            var keys = Keyboard.current;
            if (keys.digit1Key.wasPressedThisFrame) BeginDown(Vector3.zero, transform.right * .8f);
            if (keys.digit2Key.wasPressedThisFrame) BeginDown(Vector3.zero, -transform.right * .8f);
            if (keys.digit3Key.wasPressedThisFrame) BeginDown(Vector3.zero, transform.forward * .8f);
            if (keys.digit4Key.wasPressedThisFrame) BeginDown(Vector3.zero, -transform.forward * .8f);
        }
        private void OnGUI()
        {
            if (Actor == null || !Actor.ReadKeyboard) return;
            GUI.Label(new Rect(20, 185, 950, 65), "Ragdoll candidate | 1/2: front/back fall | 3/4: side fall | R: reset\n"
                + $"State: {Down.State.Phase} | Down count: {Down.State.DownCount} | Wait: {Down.State.RemainingSeconds:F2}s | Recovery: {Recoveries}");
        }
        private void FixedUpdate() { if (Actor != null && !Actor.Motor.ManualStep) Step(Time.fixedDeltaTime); }
        public void Step(float seconds)
        {
            if (_motorReset != Actor.Motor.PresentationState.ResetVersion)
            { _motorReset = Actor.Motor.PresentationState.ResetVersion; Actor.Motor.Body.rotation = _resetRotation; if (Active) RestoreLocomotion(true); Down.RefreshLock(); return; }
            if (!Active || !Actor.Motor.AuthorityEnabled) return;
            ReleaseEntryOverlaps();
            // Down has its own earlier/later automatic tick; manual harnesses
            // advance Down explicitly once before this call.
            var state = Down.State; var body = Actor.Motor.Body;
            foreach (var link in _links)
            {
                var gap = Vector3.Distance(link.Body.transform.TransformPoint(link.Joint.anchor), link.Joint.connectedBody.transform.TransformPoint(link.Joint.connectedAnchor)); if (gap > MaximumJointGap) { MaximumJointGap = gap; WorstJoint = link.Bone.name; WorstGapPhase=state.Phase; WorstGapGetUpAge=GetUpAge; WorstGapDownCount=state.DownCount; }
                MaximumSpeed = Mathf.Max(MaximumSpeed, link.Body.linearVelocity.magnitude, body.linearVelocity.magnitude);
            }
            // Waiting for room (the get-up phase before its clip starts) the body stays as limp as it lay: the recovery drive
            // pulls every joint to the bind pose, so the legs straightened out stiffly while it waited (2026-10-21). Rolling
            // onto a face keeps that drive: a knee left bent propped the body up and the roll never finished.
            ConfigureMuscleDrive(state.Phase == RecoveryPresentationPhase.PhysicsTumble || state.Phase == RecoveryPresentationPhase.SharedGetUp && !_animationStarted);
            if (state.Phase == RecoveryPresentationPhase.PhysicsTumble && TumbleHoldSpring <= 0)
                foreach (var link in _links) MaximumLimpDriveSpring = Mathf.Max(MaximumLimpDriveSpring, link.Joint.angularXDrive.positionSpring, link.Joint.angularYZDrive.positionSpring);
            _probe.CheckDistributedSupport=HasIncomingGrips;
            _probe.StandingReference=HasIncomingGrips || _splitTorso ? (Vector3?)(body.position+body.rotation*_pelvisLocal) : null;
            var horizontalForward=Vector3.ProjectOnPlane(ObservedTorsoRotation*Vector3.forward,Vector3.up);
            if(HasIncomingGrips && Vector3.Dot(ObservedTorsoRotation*Vector3.up,Vector3.up)>.55f && horizontalForward.sqrMagnitude>.01f
                && Actor.GetComponent<StrikeReviewTarget>().TryGripStandingRoot(Quaternion.LookRotation(horizontalForward.normalized,Vector3.up),out var gripRoot))
                _probe.StandingReference=gripRoot;
            var movingRecovery = state.Phase == RecoveryPresentationPhase.AligningToGetUp || state.Phase == RecoveryPresentationPhase.SharedGetUp || state.Phase == RecoveryPresentationPhase.UprightReturn;
            // A still body propped on a limb (on its side on an arm, say) has no floor right under its centre of mass, so the
            // probe found no support and it lay there for good (2026-10-21 side-lying review). After 1.5 s like that, any
            // part's floor contact counts as support until the get-up starts (at .5 s it also caught bodies about to find
            // the floor by themselves and sent them through a slower roll). The 1.5 s counts during the Down wait too, so a
            // body that lay propped through its knockout gets up the moment it ends (2026-10-22: it lay 1.5 s more).
            if (state.Phase != RecoveryPresentationPhase.PhysicsTumble) _proppedSeconds = 0;
            if (FeetPlantedGetUp && _proppedSeconds > 1.5f) _probe.CheckDistributedSupport = true;
            var sample = movingRecovery ? _probe.EvaluateDuringGetUp(seconds, _floorPoint) : _probe.Evaluate(seconds);
            if (state.Phase == RecoveryPresentationPhase.PhysicsTumble && !HoldTumble && !sample.HasSupport && body.linearVelocity.magnitude < .3f) _proppedSeconds += seconds;
            LastSupportSample=sample;
            // No room to stand because someone stands over the body: after a moment the get-up shoves them off,
            // again every RoomPushInterval until there is room (2026-10-20 review: otherwise standing next to a
            // downed body kept it down for good).
            // While it gets up, anyone still close around the standing spot keeps being shoved off as well (the two
            // otherwise stood up inside each other).
            var waitingForRoom = state.Phase == RecoveryPresentationPhase.PhysicsTumble && sample.HasSupport && !sample.Clear;
            var rising = state.Phase == RecoveryPresentationPhase.AligningToGetUp || state.Phase == RecoveryPresentationPhase.SharedGetUp || state.Phase == RecoveryPresentationPhase.UprightReturn;
            _blockedSeconds = FeetPlantedGetUp && state.CanStartGetUp && !HoldTumble && (waitingForRoom || rising) ? _blockedSeconds + seconds : 0;
            _roomPushed.Clear();
            if (_blockedSeconds <= 0) _clearTimer = 0;
            // From the moment the Down wait ends (2026-10-21: it used to wait .3 s more before making room).
            if (_blockedSeconds > 0) { var interval = (_clearTimer -= seconds) <= 0; if (interval) _clearTimer = RoomPushInterval; ClearRoom(waitingForRoom ? sample.StandingRoot : _standingRoot, waitingForRoom, interval); }
            if (state.Phase != RecoveryPresentationPhase.SharedGetUp || _animationStarted) _roomHold = _roomPushTimer = 0;
            if (_animationStarted) _scootSeconds = 0;
            StepScoot(seconds);
            StepFlinch(seconds);
            // The planted get-up, once moving, is not given up because someone steps into its room (that made a rising
            // body grabbable again, 2026-10-21): it keeps shoving them off instead (ClearRoom above).
            if (movingRecovery && (!sample.HasSupport || !sample.Clear && !FeetPlantedGetUp))
            { state.TryInterruptGetUp(state.RoundGeneration, state.EpisodeId); _animationStarted = false; GetUpAge = 0; GetUpPose?.Cancel(true); if(_standingColliderPrepared) PrepareStandingCollider(false); ConfigureMuscleDrive(true); _uprightSeconds = 0; Interruptions++; }
            if (state.Phase == RecoveryPresentationPhase.PhysicsTumble && state.CanStartGetUp && sample.Stable && (sample.Clear || FeetPlantedGetUp && sample.HasSupport && OnlyCharactersInRoom(sample.StandingRoot)) && !HoldTumble)
            {
                if((HasIncomingGrips || UprightReturnWithoutGrip) && Vector3.Dot(ObservedTorsoRotation*Vector3.up,sample.Normal)>0
                    && (!RecoveryOrientationResolver.TryClassify(ObservedTorsoRotation,sample.Normal,out _)
                        || UprightReturnWithoutGrip && Vector3.Dot(ObservedTorsoRotation*Vector3.up,sample.Normal)>.5f)
                    && state.TryBeginUprightReturn(state.RoundGeneration,state.EpisodeId,true,true,true))
                {
                    _floorPoint=sample.Point;_standingRoot=sample.StandingRoot;
                    _standingRotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(ObservedTorsoRotation*Vector3.forward,Vector3.up).normalized,Vector3.up);
                    _getUpEntryRotation=body.rotation;_getUpEntryCom=body.worldCenterOfMass;GetUpAge=0;_uprightSeconds=0;
                }
                if (state.TryBeginGetUp(state.RoundGeneration, state.EpisodeId, ObservedTorsoRotation, sample.Normal, true, true))
                {
                    _floorPoint = sample.Point; _standingRoot = sample.StandingRoot;
                    var heading = Vector3.ProjectOnPlane(ObservedTorsoRotation * Vector3.up, Vector3.up).normalized;
                    if (state.GetUpOrientation == RecoveryOrientation.FaceUp) heading = -heading;
                    _standingRotation = Quaternion.LookRotation(heading, Vector3.up);
                    if (state.Phase == RecoveryPresentationPhase.AligningToGetUp) AlignmentStarts++;
                }
            }
            // A loose, self-colliding body can come to rest curled on its side, which neither the face-up/down
            // GetUp nor the upright return above accepts (it was left lying for good, 2026-10-17). Once it may
            // get up and has been lying still on supported ground for 1.5 s, it returns upright from there.
            // Lying still through the Down wait counts as well, so it rolls and rises as the knockout ends (2026-10-22).
            _tumbleReadySeconds=state.Phase==RecoveryPresentationPhase.PhysicsTumble&&!HoldTumble&&(state.CanStartGetUp||body.linearVelocity.magnitude<.3f)?_tumbleReadySeconds+seconds:0;
            // Lying on a side or curled but lying: roll to the nearer of face down / face up and get up from there.
            if(FeetPlantedGetUp&&_tumbleReadySeconds>.8f&&sample.HasSupport&&body.linearVelocity.magnitude<.3f
                &&RecoveryOrientationResolver.TryClassify(ObservedTorsoRotation,sample.Normal,out _)
                &&state.TryBeginGetUp(state.RoundGeneration,state.EpisodeId,ObservedTorsoRotation,sample.Normal,true,true))
            {
                _floorPoint=sample.Point;_standingRoot=sample.StandingRoot;
                var heading=Vector3.ProjectOnPlane(ObservedTorsoRotation*Vector3.up,Vector3.up).normalized;
                if(state.GetUpOrientation==RecoveryOrientation.FaceUp)heading=-heading;
                _standingRotation=Quaternion.LookRotation(heading,Vector3.up);
                if(state.Phase==RecoveryPresentationPhase.AligningToGetUp)AlignmentStarts++;
            }
            else if(UprightReturnWithoutGrip&&_tumbleReadySeconds>1.5f&&sample.HasSupport&&body.linearVelocity.magnitude<.3f
                &&state.TryBeginUprightReturn(state.RoundGeneration,state.EpisodeId,true,true,true))
            {
                _floorPoint=sample.Point;_standingRoot=sample.StandingRoot;
                var heading=Vector3.ProjectOnPlane(ObservedTorsoRotation*Vector3.forward,Vector3.up);
                if(heading.sqrMagnitude<.01f)heading=Vector3.ProjectOnPlane(ObservedTorsoRotation*Vector3.up,Vector3.up);
                _standingRotation=Quaternion.LookRotation(heading.normalized,Vector3.up);
                _getUpEntryRotation=body.rotation;_getUpEntryCom=body.worldCenterOfMass;GetUpAge=0;_uprightSeconds=0;
            }
            if(state.Phase==RecoveryPresentationPhase.UprightReturn)
            {
                GetUpAge+=seconds*GetUpSpeed;
                var progress=Mathf.SmoothStep(0,1,Mathf.Clamp01(GetUpAge/.6f));
                if(_splitTorso && progress>=1 && !_standingColliderPrepared) PrepareStandingCollider(true);
                ReadyAngle=Quaternion.Angle(body.rotation,_standingRotation);ReadyDistance=Vector3.Distance(body.position,_standingRoot);
                var settled=progress>=1 && ReadyAngle<4 && ReadyDistance<.025f && body.linearVelocity.magnitude<.15f && body.angularVelocity.magnitude<.25f
                    && _links.All(l=>Vector3.Distance(l.Body.position,_standingRoot+_standingRotation*l.BindActorPosition)<.015f
                        && Quaternion.Angle(l.Body.rotation,_standingRotation*l.BindActorRotation)<5f);
                _uprightSeconds=settled ? _uprightSeconds+seconds : 0;
                if(_uprightSeconds>=.2f && state.TryCompleteUprightReturn(state.RoundGeneration,state.EpisodeId,true))
                { Recoveries++;RestoreLocomotion();Down.RefreshLock();return; }
                // As after the GetUp clip: a body that has not settled .6 s after the return finished (a curled
                // pose untangling) completes at the standing pose.
                if(SnapCompleteAfterAnimation && GetUpAge>=1.2f)
                {
                    body.position=_standingRoot;body.rotation=_standingRotation;body.transform.SetPositionAndRotation(_standingRoot,_standingRotation);
                    body.linearVelocity=body.angularVelocity=Vector3.zero;
                    if(state.TryCompleteUprightReturn(state.RoundGeneration,state.EpisodeId,true)){ Recoveries++;RestoreLocomotion();Down.RefreshLock();return; }
                }
                RotateToward(Quaternion.Slerp(_getUpEntryRotation,_standingRotation,progress));
                var goal=Vector3.Lerp(_getUpEntryCom,_standingRoot+_standingRotation*body.centerOfMass,progress);
                ApplyRecoveryAcceleration(Vector3.ClampMagnitude((goal-body.worldCenterOfMass)*35f-body.linearVelocity*10f-Physics.gravity,25f));
                return;
            }
            if (state.Phase == RecoveryPresentationPhase.AligningToGetUp && state.AlignmentTargetRotation.HasValue)
            {
                RotateToward(state.AlignmentTargetRotation.Value, true);
                ReadyAngle = Quaternion.Angle(ObservedTorsoRotation, state.AlignmentTargetRotation.Value); ReadyAngular = body.angularVelocity.magnitude;
                // A limp limb under the rolling body can hold it short (2026-10-20: a dropkicker lay at 14 deg, a body
                // that broke free of a hold at 38 deg, for good). The planted get-up accepts 20 deg once still, 35 deg
                // after a second of rolling and 60 deg after two; the get-up itself turns the rest.
                _alignSeconds += seconds;
                var aligned = ReadyAngle < AlignmentToleranceDegrees && body.angularVelocity.magnitude < .7f
                    || FeetPlantedGetUp && (ReadyAngle < 20f && body.angularVelocity.magnitude < .3f || _alignSeconds > 1f && ReadyAngle < 35f || _alignSeconds > 2f && ReadyAngle < 60f);
                // Planted: someone in the way does not hold the roll; HoldForRoom shoves them off before rising.
                if (state.TryFinishAlignment(state.RoundGeneration, state.EpisodeId, sample.HasSupport, sample.Clear || FeetPlantedGetUp, aligned)) _alignSeconds = _alignStuckSeconds = 0;
                else
                {
                    _alignStuckSeconds = ReadyAngle > AlignmentRestartDegrees && body.angularVelocity.magnitude < .3f ? _alignStuckSeconds + seconds : 0;
                    if (_alignStuckSeconds > AlignmentRestartSeconds && state.TryInterruptGetUp(state.RoundGeneration, state.EpisodeId)) { _alignSeconds = _alignStuckSeconds = 0; AlignmentRestarts++; }
                }
            }
            if (state.Phase == RecoveryPresentationPhase.SharedGetUp)
            {
                if (UseGetUpAnimation && !_animationStarted)
                {
                    var up = state.GetUpOrientation == RecoveryOrientation.FaceUp;
                    // Choose support under the pelvis, rather than dragging the
                    // feet to the former chest projection while lifting the whole body.
                    var pelvis = body.position + body.rotation * _pelvisLocal;
                    var destination = new Vector3(pelvis.x, _floorPoint.y, pelvis.z);
                    if (FeetPlantedGetUp)
                    {
                        var feet = (_bones["Foot_L"].position + _bones["Foot_R"].position) * .5f;
                        // Supine: the knees come up and the feet slide in toward the hips before rising over them.
                        var stand = up ? Vector3.Lerp(feet, pelvis, .4f) : feet;
                        destination = new Vector3(stand.x, _floorPoint.y, stand.z);
                    }
                    var footSupport = _probe.EvaluateDuringGetUp(seconds, destination);
                    // A blocked standing spot is first cleared by HoldForRoom (shove or scoot); only when that runs out does the
                    // planted get-up fall back to waiting on the floor, unless only characters are in the way (it rises through them).
                    if (!footSupport.HasSupport || !footSupport.Clear && (!FeetPlantedGetUp || _roomHold >= RoomHoldSeconds && !OnlyCharactersInRoom(footSupport.StandingRoot))) { state.TryInterruptGetUp(state.RoundGeneration,state.EpisodeId); ConfigureMuscleDrive(true); return; }
                    _floorPoint = footSupport.Point; _standingRoot = footSupport.StandingRoot; _getUpEntryPelvis = pelvis;
                    var anchors = new Vector3[2]; var planted = new[] { true, true }; var valid = true;
                    for (var side = 0; side < 2; side++)
                    {
                        var planned = body.position+body.rotation*GetUpAnimationPose.AuthoredHandLocalTarget(side,up);
                        if (FeetPlantedGetUp) { planted[side] = TryFloorAnchor(planned, body.rotation * (side == 0 ? Vector3.right : Vector3.left), body.rotation * Vector3.up, out anchors[side]); continue; }
                        var ray = planned + Vector3.up * .5f;
                        if (gameObject.scene.GetPhysicsScene().Raycast(ray, Vector3.down, out var hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) anchors[side] = hit.point + hit.normal * .01f;
                        else valid = false;
                    }
                    if (!valid) { state.TryInterruptGetUp(state.RoundGeneration,state.EpisodeId); ConfigureMuscleDrive(true); return; }
                    if (HoldForRoom(seconds, pelvis, anchors, planted)) return;
                    if (FeetPlantedGetUp && _roomHold >= RoomHoldSeconds) for (var side = 0; side < 2; side++) if (planted[side] && ArmCramped(pelvis, body.rotation * (side == 0 ? Vector3.left : Vector3.right))) planted[side] = false;
                    _roomHold = 0; _roomPushTimer = 0;
                    _animationStarted = true; GetUpAge = 0; _getUpEntryRotation = body.rotation; _getUpEntryCom = body.worldCenterOfMass;
                    if (Environment.GetEnvironmentVariable("PARTY_KO_DEBUG") == "1") Debug.Log($"GETUP {Actor.name} faceUp={up} alignments={AlignmentStarts} torsoFwdY={(ObservedTorsoRotation*Vector3.forward).y:F2} torsoUpY={(ObservedTorsoRotation*Vector3.up).y:F2} planted={planted[0]}/{planted[1]} pushes={RoomPushes} scoots={RoomScoots}");
                    Actor.FixedPose?.RestoreForEvaluation();
                    if (_bones.TryGetValue("Pelvis", out var pelvisBone)) _rootInPelvis = pelvisBone.worldToLocalMatrix * body.transform.localToWorldMatrix;
                    GetUpPose.Begin(up, anchors[0], anchors[1], _standingRoot, _standingRotation, planted[0], planted[1]);
                }
                if (UseGetUpAnimation) GetUpAge += seconds * GetUpSpeed;
                var sitUp = FeetPlantedGetUp && state.GetUpOrientation == RecoveryOrientation.FaceUp;
                var rise = UseGetUpAnimation ? sitUp ? GetUpAnimationPose.SitUpRotationProgress(GetUpAge) : GetUpAnimationPose.RotationProgress(GetUpAge) : 1f;
                if (UseGetUpAnimation && GetUpAge < GetUpAnimationPose.PreparationSeconds) return;
                if (_splitTorso && GetUpAge >= GetUpAnimationPose.Duration && !_standingColliderPrepared) PrepareStandingCollider(true);
                // Resolve handoff before queuing forces. A recovery torque must
                // never execute after changing the mass, inertia and constraints.
                if (UseGetUpAnimation && TryCompleteStableGetUp(body, seconds)) return;
                RotateToward(UseGetUpAnimation ? Quaternion.Slerp(_getUpEntryRotation,_standingRotation,rise) : _standingRotation);
                var goalCom = _standingRoot + _standingRotation * body.centerOfMass;
                if (UseGetUpAnimation)
                {
                    var rotation = Quaternion.Slerp(_getUpEntryRotation, _standingRotation, rise);
                    var pelvis = Vector3.Lerp(_getUpEntryPelvis, _standingRoot + _standingRotation * _pelvisLocal, sitUp ? GetUpAnimationPose.SitUpWeightTransfer(GetUpAge) : GetUpAnimationPose.WeightTransfer(GetUpAge));
                    var rootGoal = pelvis - rotation * _pelvisLocal;
                    if (rootGoal.y < _standingRoot.y) pelvis.y += _standingRoot.y - rootGoal.y;
                    goalCom = pelvis + rotation * (body.centerOfMass - _pelvisLocal);
                }
                // Catching up the rising goal the body coasted ~2.5 cm past standing height, lifting the planted feet off the
                // floor until the clip ended and snapped them down (2026-10-21 review): above the goal, rising is braked hard.
                var damping = body.linearVelocity * 10f;
                if (body.worldCenterOfMass.y > goalCom.y && body.linearVelocity.y > 0) damping.y = body.linearVelocity.y * RiseOvershootBrake;
                var acceleration = (goalCom - body.worldCenterOfMass) * 35f - damping - Physics.gravity;
                var boundedAcceleration=Vector3.ClampMagnitude(acceleration,25f);
                ApplyRecoveryAcceleration(boundedAcceleration);
                foreach (var link in _links) link.Joint.angularXDrive = link.Joint.angularYZDrive = RecoveryDrive(link);
                if (!UseGetUpAnimation && TryCompleteStableGetUp(body, seconds)) return;

            }
        }
        public float RiseOvershootBrake = 60f;
        private void ApplyRecoveryAcceleration(Vector3 acceleration)
        {
            var body=Actor.Motor.Body;
            if(_splitTorso)
            {
                body.AddForce(acceleration*body.mass,ForceMode.Force);
                foreach(var link in _links) link.Body.AddForce(acceleration*link.Body.mass,ForceMode.Force);
            }
            else body.AddForce(acceleration*TotalMass,ForceMode.Force);
        }
        private void RefreshJointFrames()
        {
            foreach(var link in _links)
            {
                link.Joint.anchor=Vector3.zero;
                link.Joint.connectedAnchor=link.EntryAnchor;
            }
        }
        private void PrepareStandingCollider(bool standing)
        {
            _standingColliderPrepared=standing; _torso.enabled=!standing; _locomotion.enabled=standing;
            // Ignore-pair state must be restored when a collider is re-enabled.
            if(standing) foreach(var collider in _colliders) Physics.IgnoreCollision(_locomotion,collider);
            else ApplySelfIgnores();
            Actor.Motor.Body.ResetCenterOfMass(); Actor.Motor.Body.ResetInertiaTensor(); RefreshJointFrames(); _uprightSeconds=0;
            InteractionTopologyVersion++;
        }
        private bool TryCompleteStableGetUp(Rigidbody body, float seconds)
        {
            var state = Down.State;
            ReadyAngle = Quaternion.Angle(body.rotation, _standingRotation); ReadyDistance = Vector3.Distance(body.position, _standingRoot); ReadySpeed = body.linearVelocity.magnitude; ReadyAngular = body.angularVelocity.magnitude;
            var ready = (!UseGetUpAnimation || GetUpAge >= GetUpAnimationPose.Duration) && ReadyAngle < 4f && ReadyAngular < .25f && ReadyDistance < .025f && ReadySpeed < .15f;
            if (!ready && SnapCompleteAfterAnimation && UseGetUpAnimation && GetUpAge >= GetUpAnimationPose.Duration + .25f)
            {
                // The presentation already stands; put the physics body where the clip ended.
                body.position = _standingRoot; body.rotation = _standingRotation; body.transform.SetPositionAndRotation(_standingRoot, _standingRotation);
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                if (state.TryCompleteGetUp(state.RoundGeneration, state.EpisodeId)) { Recoveries++; RestoreLocomotion(); Down.RefreshLock(); return true; }
            }
            _uprightSeconds = ready ? _uprightSeconds + seconds : 0;
            if (_uprightSeconds < .2f || !state.TryCompleteGetUp(state.RoundGeneration, state.EpisodeId)) return false;
            Recoveries++; RestoreLocomotion(); Down.RefreshLock(); return true;
        }
        private void RotateToward(Quaternion target, bool aligning = false)
        {
            var body = Actor.Motor.Body; var error = target * Quaternion.Inverse(_splitTorso && aligning ? ObservedTorsoRotation : body.rotation);
            if (error.w < 0) error = new Quaternion(-error.x, -error.y, -error.z, -error.w);
            error.ToAngleAxis(out var angle, out var axis);
            if (angle > 180) angle -= 360;
            body.maxAngularVelocity = aligning ? 2f : 4f;
            var desired = axis * (angle * Mathf.Deg2Rad * (aligning ? 80f : (RelaxedRagdoll ? 180f : 100f))) - body.angularVelocity * (aligning ? 18f : (RelaxedRagdoll ? 28f : 20f));
            if (_splitTorso)
            {
                var center=body.worldCenterOfMass*body.mass;
                foreach(var link in _links) center+=link.Body.worldCenterOfMass*link.Body.mass;
                center/=TotalMass;
                // The planted get-up rolls harder: limp limbs under the body stalled the 20 N·m roll.
                var rollTorque = aligning ? FeetPlantedGetUp ? 35f : 20f : 40f;
                var angularAcceleration=Vector3.ClampMagnitude(desired,aligning ? FeetPlantedGetUp ? 20f : 12f : 60f);
                var torque=AngularTorque(body,center,angularAcceleration);
                foreach(var link in _links) torque+=AngularTorque(link.Body,center,angularAcceleration);
                angularAcceleration*=Mathf.Min(1f,rollTorque/Mathf.Max(.0001f,torque.magnitude));
                ApplyAngularAcceleration(body,center,angularAcceleration);
                foreach(var link in _links) ApplyAngularAcceleration(link.Body,center,angularAcceleration);
            }
            else body.AddTorque(Vector3.ClampMagnitude(desired, aligning ? 12f : 60f), ForceMode.Acceleration);
        }
        private static void ApplyAngularAcceleration(Rigidbody body, Vector3 center, Vector3 acceleration)
        {
            body.AddTorque(acceleration,ForceMode.Acceleration);
            body.AddForce(Vector3.Cross(acceleration,body.worldCenterOfMass-center)*body.mass,ForceMode.Force);
        }
        private static Vector3 AngularTorque(Rigidbody body, Vector3 pivot, Vector3 acceleration)
        {
            var frame=body.rotation*body.inertiaTensorRotation;
            var torque=frame*Vector3.Scale(body.inertiaTensor,Quaternion.Inverse(frame)*acceleration);
            var offset=body.worldCenterOfMass-pivot;
            return torque+body.mass*(offset.sqrMagnitude*acceleration-offset*Vector3.Dot(offset,acceleration));
        }
        public void ApplyPose(float deltaTime = 1f / 60f)
        {
            if (!Active) return;
            if (UseGetUpAnimation) foreach (var pair in _bones) { pair.Value.localPosition = _restPositions[pair.Key]; pair.Value.localRotation = _restRotations[pair.Key]; }
            foreach (var link in _links) link.Bone.SetPositionAndRotation(link.Body.position, link.Body.rotation);
            foreach (var side in new[] { "L", "R" })
                _bones["HipDeform_" + side].localRotation = Quaternion.Slerp(_restRotations["HipDeform_" + side], _bones["Thigh_" + side].localRotation, .25f);
            if (UseGetUpAnimation && Down.State.Phase == RecoveryPresentationPhase.SharedGetUp && GetUpPose.Playing)
            {
                if(HasIncomingGrips) _heldRecoveryPose=true;
                else
                {
                    if(_heldRecoveryPose)GetUpPose.ReblendFromCurrentPose();
                    _heldRecoveryPose=false;GetUpPose.Apply(GetUpAge,deltaTime);
                }
            }
            else _heldRecoveryPose=false;
            if(Actor.Motor.ManualStep&&Actor.FixedPose!=null&&!Actor.FixedPose.Advancing)Actor.FixedPose.CaptureEvaluatedPose();
        }
        private void RestoreLocomotion(bool fromReset = false)
        {
            if (Actor == null) return;
            GetUpPose?.Cancel(true); _animationStarted = false;
            var positions = _links.Select(l => Actor.FixedPose!=null?Actor.FixedPose.WorldPosition(l.Bone.name):l.Body.position).ToArray();
            var momentum = Actor.Motor.Body.mass * Actor.Motor.Body.linearVelocity;
            foreach (var link in _links) momentum += link.Body.mass * link.Body.linearVelocity;
            foreach (var link in _links) { link.Body.linearVelocity = link.Body.angularVelocity = Vector3.zero; link.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; link.Body.isKinematic = true; }
            foreach (var collider in _colliders) collider.enabled = false;
            _locomotion.enabled = true;
            var body = Actor.Motor.Body; body.mass = 20; body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (!fromReset)
            {
                body.linearVelocity = momentum / 20f;
                if (Environment.GetEnvironmentVariable("PARTY_KO_DEBUG") == "1") Debug.Log($"HANDOFF v={body.linearVelocity:F2} fwd={(body.rotation*Vector3.forward):F2}");
            }
            body.collisionDetectionMode = _collisionMode; body.maxAngularVelocity = _maxAngularVelocity; body.angularDamping = _angularDamping; body.maxDepenetrationVelocity = _depenetrationVelocity; body.interpolation = _interpolation; body.solverIterations = _solverIterations; body.solverVelocityIterations = _solverVelocityIterations; body.ResetCenterOfMass(); body.ResetInertiaTensor();
            foreach (var pair in _bones) { pair.Value.localPosition = _restPositions[pair.Key]; pair.Value.localRotation = _restRotations[pair.Key]; }
            if (!fromReset) for (var i = 0; i < _links.Count; i++) MaximumHandoffBoneShift = Mathf.Max(MaximumHandoffBoneShift, Vector3.Distance(positions[i], _links[i].Bone.position));
            _proxyRoot.SetActive(false);
            // Clear residual recovery spin after all constraint/inertia changes.
            // Linear momentum and normal movement input remain available.
            if (!fromReset && UseGetUpAnimation) body.angularVelocity = Vector3.zero;
            Active = false; Actor.ExternalPhysicsPose = false;
            if(Actor.Pose.ResetAfterExternalPose)Actor.Pose.ResetTransientGait();
            InteractionTopologyVersion++;
        }
        // Review reset during a Down: drop the ragdoll at once and clear this round's Down state.
        public void AbortForReset()
        {
            if (!Active) return;
            RestoreLocomotion(true); Down.State.ResetRound(Down.State.IsMatchRound); Down.RefreshLock();
        }
        public void SuspendDetachedPhysicsForReview()
        {
            // The proxy root is separate from the actor hierarchy; local OOB must stop it too.
            if(_proxyRoot!=null)_proxyRoot.SetActive(false);
        }
        private void OnDestroy() { if (_proxyRoot != null) Destroy(_proxyRoot); if (_ragdollMaterial != null) Destroy(_ragdollMaterial); }
    }
}
