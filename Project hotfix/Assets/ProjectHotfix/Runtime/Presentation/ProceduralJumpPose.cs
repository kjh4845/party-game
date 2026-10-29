using System;
using System.Collections.Generic;
using ProjectHotfix.Contracts;
using UnityEngine;

namespace ProjectHotfix.Presentation
{
    // Presentation only: writes the visual pelvis/legs, never Rigidbody or arms.
    public sealed class ProceduralJumpPose : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _rotations = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Vector3> _positions = new Dictionary<string, Vector3>();
        private readonly Vector3[] _footPositions = new Vector3[2];
        private readonly Quaternion[] _footRotations = new Quaternion[2];
        private readonly Vector3[] _restKneeDirections = new Vector3[2];
        private readonly Vector3[] _thighPositions = new Vector3[2];
        private Quaternion _pelvisRestRotation;
        private readonly bool[] _planted = new bool[2];
        private readonly Vector3[] _anchors = new Vector3[2];
        private readonly Vector3[] _plantOffsets = new Vector3[2];
        private Quaternion _previousRootRotation;
        // Approved prototype gait; activated together with its motor/cadence preset.
        public bool WorldFootPlant;
        public bool IsFootPlanted(int side) => _planted[side];
        private float _compression;
        private float _tuck, _easedTuck;
        private float _gaitWeight;
        private float _gaitPhase;
        private float _sprintBlend;
        private Vector3 _travel;
        private uint _resetVersion;
        private static readonly string[] LowerBones = { "Pelvis", "Thigh_L", "Calf_L", "Foot_L", "Thigh_R", "Calf_R", "Foot_R", "HipDeform_L", "HipDeform_R" };
        public float GaitWeight => _gaitWeight;
        public float GaitPhase => _gaitPhase;
        public float SprintBlend => _sprintBlend;
        public Vector3 TravelVelocity => _travel;
        // Rate (1/s) at which the stride fades once airborne; negative = the grounded rate (reviewed gait). The
        // r43/r46 stump legs otherwise flew on frozen mid-stride with a bent knee (2026-10-20 review).
        public float AirborneGaitFade = -1f;
        // Eased jump tuck (time constant, s; 0 = the reviewed curve). The knees still fold in the air, but they open
        // gradually from ~25 cm above the floor and finish easing after touchdown, instead of snapping straight over
        // the last 12 cm and on contact (2026-10-20 review: "무릎을 탁 피는 느낌").
        public float JumpTuckEase;
        public float MaximumCompression = 0.025f;
        public float MaximumTuck = 0.025f;
        public float AdditionalPelvisDrop;
        public float AdditionalPelvisPitch;
        public float AdditionalPelvisYaw;
        public bool ReadableGait = true;
        // Defaults preserve the reviewed motion. Speed-matched gait is a local
        // comparison option until the user selects a locomotion style.
        public bool SpeedMatchedGait;
        public float WalkReferenceSpeed = 1.5f;
        public float SprintReferenceSpeed = 2.25f;
        public float WalkCadence = 1.65f;
        public float SprintCadence = 2.5f;
        public float WalkPelvisDrop = .006f;
        public float SprintPelvisDrop = .008f;
        public float FootPlantStrideFullWeight = .75f;
        public float FootPlantWalkLift = .010f, FootPlantSprintLift = .010f;
        public float FootPlantPelvisDrop = .020f, FootPlantPelvisBob = .002f;
        public float PelvisSwayScale = 1f, PelvisRollScale = 1f, FreeArmSwingScale = 1f;
        public bool LimitCombinedShortening;
        public float CombinedShorteningLimit = .034f;
        // Sprint-only overrides, blended in with the sprint (negative = same as walk). They let a run use a
        // higher knee lift and a bouncier pelvis than the walk; the defaults keep the reviewed gait.
        public float SprintShorteningLimit = -1f, FootPlantSprintPelvisDrop = -1f, FootPlantSprintPelvisBob = -1f;
        private float ShorteningLimit => SprintShorteningLimit < 0f ? CombinedShorteningLimit : Mathf.Lerp(CombinedShorteningLimit, SprintShorteningLimit, _sprintBlend);
        public float TurnStrideMinimum = 1f;
        public bool UseStepState, ResetAfterExternalPose;
        // Set from outside for one pose (a wall climb, ActiveGrabPairReview.Climb; defaults change nothing): a leg
        // shortening that replaces the airborne tuck (negative = none) and the feet shifted forward.
        public float TuckOverride = -1f, FootForwardOffset;
        // Jump kick (prototype): both feet swing toward KickTarget (root-local; x is mirrored for the left foot) by
        // KickWeight, about the hip at the leg's own length, so a straight leg stays straight on the way out and back. A
        // straight-line blend drew the feet in past the hip and folded the knees ~100 deg on the way down, and the old
        // target above the hip read as a sit (2026-10-30 review). Neutral at 0.
        public float KickWeight;
        public Vector3 KickTarget = new Vector3(.09f, .15f, .15f);
        // Share of the thigh's swing the HipDeform_ helpers follow (the hip band of the skin). The source rig's quarter.
        public float HipDeformFollow = .25f;
        // Time constant (s) of the direction the strides are laid along; 0 = the travel velocity as it is. Running, the body's
        // sway swung that direction a little from side to side every step and the ~.15 m stride put each foot ~3 cm out of
        // its lane, so the legs splayed and read bow-legged from the front (2026-10-29 review).
        public float StrideDirectionEase;
        // How far a slightly bent knee follows the source's rest knee direction (slightly outward) before the bend turns it
        // forward. 1 = the reviewed blend; 0 = the knee always bends straight forward.
        public float RestKneePole = 1f;
        private Vector3 _strideDirection;
        public float MaximumFootTargetError { get; private set; }
        public float MaximumReachCorrection { get; private set; }

        private void Start() { if (_bones.Count == 0) Bind(transform); }

        public void Bind(Transform root)
        {
            _bones.Clear(); _positions.Clear(); _rotations.Clear();
            foreach (var bone in root.GetComponentsInChildren<Transform>())
            {
                _bones[bone.name] = bone;
                _positions[bone.name] = bone.localPosition;
                _rotations[bone.name] = bone.localRotation;
            }
            _previousRootRotation = transform.rotation;
            _pelvisRestRotation = Quaternion.Inverse(transform.rotation) * _bones["Pelvis"].rotation;
            for (var i = 0; i < 2; i++)
            {
                var foot = _bones["Foot_" + (i == 0 ? "L" : "R")];
                _footPositions[i] = transform.InverseTransformPoint(foot.position);
                _footRotations[i] = Quaternion.Inverse(transform.rotation) * foot.rotation;
                var side = i == 0 ? "L" : "R";
                var thigh = _bones["Thigh_" + side]; var calf = _bones["Calf_" + side];
                _thighPositions[i] = transform.InverseTransformPoint(thigh.position);
                _restKneeDirections[i] = transform.InverseTransformDirection(Vector3.ProjectOnPlane(
                    calf.position - thigh.position, foot.position - thigh.position).normalized);
            }
        }

        public void Apply(JumpPresentationState state, float deltaTime)
        {
            if (_bones.Count == 0) return;
            if (_resetVersion != state.ResetVersion)
            {
                _resetVersion = state.ResetVersion;
                _compression = _tuck = _easedTuck = _gaitWeight = _gaitPhase = _sprintBlend = 0f;
                _travel = _strideDirection = Vector3.zero;
                for (var i = 0; i < 2; i++) { _planted[i] = false; _plantOffsets[i] = Vector3.zero; }
                _previousRootRotation = transform.rotation;
            }
            var headingStep = Quaternion.Angle(_previousRootRotation, transform.rotation);
            _previousRootRotation = transform.rotation;
            var horizontal = new Vector3(state.VelocityX, 0f, state.VelocityZ);
            _travel = Vector3.Lerp(_travel, horizontal, 1f - Mathf.Exp(-12f * deltaTime));
            var travelDirection = _travel / Mathf.Max(.2f, _travel.magnitude);
            _strideDirection = StrideDirectionEase > 0f ? Vector3.Lerp(_strideDirection, travelDirection, 1f - Mathf.Exp(-deltaTime / StrideDirectionEase)) : travelDirection;
            _sprintBlend = Mathf.Lerp(_sprintBlend, state.Sprinting ? 1f : 0f, 1f - Mathf.Exp(-10f * deltaTime));
            var referenceSpeed = Mathf.Max(.01f, Mathf.Lerp(WalkReferenceSpeed, SprintReferenceSpeed, _sprintBlend));
            var cadence = Mathf.Max(.01f, Mathf.Lerp(WalkCadence, SprintCadence, _sprintBlend));
            var speedWeight = Mathf.Clamp01(_travel.magnitude / referenceSpeed);
            var facingMatch=_travel.sqrMagnitude>.0001f ? Mathf.Clamp01(Vector3.Dot(transform.forward,_travel.normalized)) : 1f;
            var turnStride=Mathf.Lerp(TurnStrideMinimum,1f,Mathf.SmoothStep(0,1,facingMatch));
            var gaitFade = !state.Grounded && AirborneGaitFade >= 0f ? AirborneGaitFade : WorldFootPlant ? 8f : 14f;
            _gaitWeight = Mathf.Lerp(_gaitWeight, state.Grounded ? speedWeight : 0f, 1f - Mathf.Exp(-gaitFade * deltaTime));
            // Continue the same alternating-foot phase through landing; never restart a cycle on contact.
            _gaitPhase = Mathf.Repeat(_gaitPhase + deltaTime * cadence
                * _gaitWeight, 1f);
            var age = Mathf.Clamp01(state.LandingAge / 0.30f);
            var pulse = state.Grounded && age < 1f ? Mathf.Sin(age * Mathf.PI) : 0f;
            var desiredCompression = MaximumCompression * Mathf.Clamp01(state.LandingImpactSpeed / 3.5f) * pulse;
            var desiredTuck = state.Grounded ? 0f : MaximumTuck * Mathf.Clamp01((state.VerticalSpeed + 1.5f) / 3.5f);
            var clearance = state.VerticalSpeed < 0f ? Mathf.Clamp01(state.GroundDistance / .12f) : 1f;
            desiredTuck *= clearance;
            var blend = 1f - Mathf.Exp(-25f * deltaTime);
            _compression = Mathf.Lerp(_compression, desiredCompression, blend);
            // Confirmed support takes priority over the airborne pose blend.
            // Near a straight knee, angular change grows roughly with sqrt(shortening).
            // Smooth in that space so takeoff does not snap the knee closed.
            var tuckScale = Mathf.Max(.000001f, MaximumTuck);
            var tuckAmount = Mathf.Sqrt(Mathf.Clamp01(_tuck / tuckScale));
            var desiredAmount = Mathf.Sqrt(Mathf.Clamp01(desiredTuck / tuckScale));
            tuckAmount = Mathf.MoveTowards(tuckAmount, desiredAmount, 5f * deltaTime);
            _tuck = state.Grounded ? 0f : Mathf.Min(MaximumTuck * tuckAmount * tuckAmount, MaximumTuck * clearance);
            if (JumpTuckEase > 0f)
            {
                var landingRoom = state.VerticalSpeed < 0f ? Mathf.SmoothStep(0f, 1f, state.GroundDistance / .25f) : 1f;
                var easedGoal = state.Grounded ? 0f : MaximumTuck * Mathf.Clamp01((state.VerticalSpeed + 2f) / 3f) * landingRoom;
                _easedTuck = Mathf.Lerp(_easedTuck, easedGoal, 1f - Mathf.Exp(-deltaTime / JumpTuckEase));
                _tuck = _easedTuck;
            }
            if (TuckOverride >= 0f) _tuck = _easedTuck = TuckOverride;
            foreach (var name in LowerBones)
            {
                var bone = _bones[name]; bone.localPosition = _positions[name]; bone.localRotation = _rotations[name];
            }
            var readable = ReadableGait && !SpeedMatchedGait;
            // Ease the small-amplitude region separately: linear shortening near
            // a straight knee otherwise creates a visible angular pop on landing.
            var gaitPoseWeight = readable ? _gaitWeight * Mathf.SmoothStep(0f,1f,Mathf.Clamp01(_gaitWeight/(WorldFootPlant ? .35f : .25f))) : _gaitWeight;
            var gaitDrop = Mathf.Lerp(WalkPelvisDrop, SprintPelvisDrop, _sprintBlend) * _gaitWeight;
            if (readable)
            {
                var doubleSupport = .5f * (1f - Mathf.Cos(_gaitPhase * Mathf.PI * 4f));
                gaitDrop = (Mathf.Lerp(.010f,.012f,_sprintBlend)
                    + Mathf.Lerp(.003f,.004f,_sprintBlend)*doubleSupport) * gaitPoseWeight;
            }
            if (WorldFootPlant)
            {
                var plantDrop = FootPlantSprintPelvisDrop < 0f ? FootPlantPelvisDrop : Mathf.Lerp(FootPlantPelvisDrop, FootPlantSprintPelvisDrop, _sprintBlend);
                var plantBob = FootPlantSprintPelvisBob < 0f ? FootPlantPelvisBob : Mathf.Lerp(FootPlantPelvisBob, FootPlantSprintPelvisBob, _sprintBlend);
                gaitDrop = (plantDrop + plantBob * .5f * (1f - Mathf.Cos(_gaitPhase * Mathf.PI * 4f))) * Mathf.Sqrt(gaitPoseWeight);
            }
            var pelvisDrop = Mathf.Min(_compression + gaitDrop + Mathf.Clamp(AdditionalPelvisDrop,0,.025f), .025f);
            if(LimitCombinedShortening)pelvisDrop=Mathf.Min(pelvisDrop,Mathf.Max(0,ShorteningLimit-_tuck));
            var pelvis = _bones["Pelvis"];
            pelvis.position -= transform.up * pelvisDrop;
            if (readable)
            {
                // Move toward the supporting leg; return through the center at handoff.
                var support = Mathf.Cos(_gaitPhase * Mathf.PI * 2f) * gaitPoseWeight;
                pelvis.position += transform.right * (support * Mathf.Lerp(.004f,.007f,_sprintBlend)*PelvisSwayScale);
                pelvis.rotation = Quaternion.AngleAxis(-support * Mathf.Lerp(1.5f,2.5f,_sprintBlend)*PelvisRollScale, transform.forward) * pelvis.rotation;
            }
            MaximumFootTargetError = 0f;
            if(AdditionalPelvisPitch!=0)pelvis.rotation=Quaternion.AngleAxis(Mathf.Clamp(AdditionalPelvisPitch,-40,40),transform.right)*pelvis.rotation;
            if(AdditionalPelvisYaw!=0)pelvis.rotation=Quaternion.AngleAxis(Mathf.Clamp(AdditionalPelvisYaw,-12,12),transform.up)*pelvis.rotation;
            MaximumReachCorrection = 0f;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? "L" : "R";
                var thigh = _bones["Thigh_" + side]; var calf = _bones["Calf_" + side]; var foot = _bones["Foot_" + side];
                var phase = _gaitPhase * Mathf.PI * 2f + i * Mathf.PI;
                var stride = Mathf.Sin(phase) * Mathf.Lerp(.016f, .024f, _sprintBlend) * _gaitWeight;
                var lift = Mathf.Max(0f, Mathf.Cos(phase)) * Mathf.Lerp(.005f, .008f, _sprintBlend) * _gaitWeight;
                if (readable)
                {
                    var contactPhase = Mathf.Repeat(_gaitPhase + i*.5f - .25f, 1f);
                    var halfStride = WorldFootPlant
                        ? referenceSpeed / (4f * cadence) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_gaitWeight / Mathf.Max(.1f,FootPlantStrideFullWeight))) * turnStride
                        : Mathf.Lerp(.040f,.050f,_sprintBlend) * gaitPoseWeight;
                    if (contactPhase < .5f)
                    {
                        stride = halfStride * (1f - 4f * contactPhase);
                        lift = 0f;
                    }
                    else
                    {
                        var u = (contactPhase-.5f)*2f;
                        stride = halfStride * (-8f*u*u*u+12f*u*u-2f*u-1f);
                        var clearanceArc = Mathf.Sin(Mathf.PI*u);
                        lift = clearanceArc*clearanceArc*(WorldFootPlant ? Mathf.Lerp(FootPlantWalkLift,FootPlantSprintLift,_sprintBlend) : Mathf.Lerp(.020f,.022f,_sprintBlend))*gaitPoseWeight;
                    }
                }
                if (SpeedMatchedGait)
                {
                    var contactPhase = Mathf.Repeat(_gaitPhase + i*.5f - .25f, 1f);
                    var halfStride = referenceSpeed / (4f * cadence);
                    if (contactPhase < .5f)
                    {
                        stride = halfStride * (1f - 4f * contactPhase);
                        lift = 0f;
                    }
                    else
                    {
                        var u = (contactPhase - .5f)*2f;
                        // Hermite swing has the same endpoint velocity as stance.
                        stride = halfStride * (-8f*u*u*u + 12f*u*u - 2f*u - 1f);
                        lift = Mathf.Sin(Mathf.PI*u) * Mathf.Sin(Mathf.PI*u) * .008f;
                    }
                    // Fade out at rest; the steady-state branch matches root speed.
                    var activation = Mathf.Clamp01(_gaitWeight / .1f);
                    stride *= activation;
                    lift *= activation;
                }
                var directionOfTravel = StrideDirectionEase > 0f ? _strideDirection : _travel / Mathf.Max(.2f, _travel.magnitude);
                if (readable)
                {
                    // Account for the raised swing-side hip during weight transfer.
                    // Stay inside the previously reviewed leg-shortening range.
                    var hipDrop = Vector3.Dot(transform.TransformPoint(_thighPositions[i])-thigh.position,transform.up);
                    lift = Mathf.Min(lift,Mathf.Max(0f,ShorteningLimit-hipDrop-_tuck));
                }
                var target = transform.TransformPoint(_footPositions[i]) + transform.up * (_tuck + lift)
                    + directionOfTravel * stride;
                if (WorldFootPlant)
                {
                    var contactPhase = Mathf.Repeat(_gaitPhase + i * .5f - .25f, 1f);
                    var stable = state.Grounded && state.LandingAge > .35f && _gaitWeight > .6f
                        && (horizontal - _travel).magnitude < .06f && headingStep < .25f && (TurnStrideMinimum>=1f||facingMatch>.95f);
                    // Lock only stable stance. Turns, acceleration, jump and stop
                    // release through an offset blend rather than dragging an old anchor.
                    if (stable && contactPhase < .5f)
                    {
                        if (!_planted[i]) { _anchors[i] = target + _plantOffsets[i]; _planted[i] = true; }
                        _plantOffsets[i] = _anchors[i] - target;
                        if (_plantOffsets[i].magnitude > .012f) _planted[i] = false;
                    }
                    else _planted[i] = false;
                    if (!_planted[i]) _plantOffsets[i] = Vector3.MoveTowards(_plantOffsets[i], Vector3.zero, .15f * deltaTime);
                    target += _plantOffsets[i];
                }
                else { _planted[i] = false; _plantOffsets[i] = Vector3.zero; }
                target += transform.forward * FootForwardOffset;
                if (KickWeight > 0f) target = thigh.position + Vector3.Slerp(target - thigh.position, transform.TransformPoint(new Vector3((i == 0 ? -1f : 1f) * Mathf.Abs(KickTarget.x), KickTarget.y, KickTarget.z)) - thigh.position, Mathf.Clamp01(KickWeight));
                var a = Vector3.Distance(thigh.position, calf.position);
                var b = Vector3.Distance(calf.position, foot.position);
                var delta = target - thigh.position;
                var distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a-b) + 0.000001f, a+b-0.000001f);
                var direction = delta.normalized;
                MaximumReachCorrection = Mathf.Max(MaximumReachCorrection, Mathf.Abs(delta.magnitude - distance));
                target = thigh.position + direction * distance;
                // At rest the source knees bend slightly outward. Preserve that
                // plane at zero compression rather than introducing a new Idle.
                var pole = Vector3.Slerp(transform.TransformDirection(_restKneeDirections[i]), transform.forward,
                    1f - RestKneePole * (1f - Mathf.Clamp01((pelvisDrop + _tuck + lift) / .01f)));
                var bend = Vector3.ProjectOnPlane(pole, direction).normalized;
                var projection = (a*a + distance*distance - b*b) / (2f*distance);
                var height = Mathf.Sqrt(Mathf.Max(0f, a*a - projection*projection));
                var knee = thigh.position + direction*projection + bend*height;
                thigh.rotation = PreciseSwing(calf.position-thigh.position, knee-thigh.position) * thigh.rotation;
                calf.rotation = PreciseSwing(foot.position-calf.position, target-calf.position) * calf.rotation;
                foot.rotation = transform.rotation * _footRotations[i];
                MaximumFootTargetError = Mathf.Max(MaximumFootTargetError, Vector3.Distance(foot.position, target));
                // Preserve the source's quarter-follow hip helper. The source
                // parent-space translation is (0,-.0025,.0005) at -25 degrees.
                var localDelta = Quaternion.Inverse(_rotations[thigh.name]) * thigh.localRotation;
                var helper = _bones["HipDeform_" + side];
                var angle = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(localDelta.x, localDelta.w) * Mathf.Rad2Deg);
                helper.localRotation = _rotations[helper.name] * Quaternion.AngleAxis(angle * HipDeformFollow, Vector3.right);
                var pelvisDelta = pelvis.rotation * Quaternion.Inverse(transform.rotation * _pelvisRestRotation);
                helper.position += pelvisDelta * transform.TransformVector(new Vector3(0f, .0005f, .0025f)) * (angle / -25f) * (HipDeformFollow / .25f);
            }
        }

        public void ResetTransientGait()
        {
            _compression=_tuck=_easedTuck=_gaitWeight=_gaitPhase=_sprintBlend=0f;_travel=_strideDirection=Vector3.zero;
            for(var i=0;i<2;i++){_planted[i]=false;_plantOffsets[i]=Vector3.zero;}
            _previousRootRotation=transform.rotation;
        }

        private static Quaternion PreciseSwing(Vector3 from, Vector3 to)
        {
            from.Normalize(); to.Normalize();
            var dot = Vector3.Dot(from, to);
            var cross = Vector3.Cross(from, to);
            if (dot < -.999999f)
            {
                var axis = Vector3.Cross(from, Vector3.right);
                if (axis.sqrMagnitude < .000001f) axis = Vector3.Cross(from, Vector3.up);
                return Quaternion.AngleAxis(180f, axis.normalized);
            }
            // Preserve tiny alignment corrections even when float dot rounds to 1.
            return new Quaternion(cross.x, cross.y, cross.z, 1f + dot).normalized;
        }
    }
}
