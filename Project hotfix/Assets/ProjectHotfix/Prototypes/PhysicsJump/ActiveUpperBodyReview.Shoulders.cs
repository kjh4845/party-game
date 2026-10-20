using System;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    public sealed partial class ActiveUpperBodyReview
    {
        public bool NaturalShoulderMotion = true;
        private readonly Transform[] _clavicles = new Transform[2];
        private readonly Vector3[] _upperArmAxes = new Vector3[2];
        private readonly float[] _armLengths = new float[2];
        private readonly float[] _shoulderForward = new float[2], _shoulderUp = new float[2];
        private Quaternion _chestToAnatomicalFrame;

        // Arm length relative to the r40-era rig (shoulder -> HandLogical .3205 m) that the punch
        // distances were tuned on. Longer arms (r43: 1.2) scale them; shorter or equal rigs keep 1.
        public float ArmReachScale
        {
            get { var scale = (_armLengths[0] + _armLengths[1]) * .5f / .3205f; return scale < 1.02f ? 1 : scale; }
        }
        public float ShoulderProtraction(int side) => _shoulderForward[side];
        public float ShoulderElevation(int side) => _shoulderUp[side];

        private void InitializeShoulderMotion(Transform[] bones)
        {
            _chestToAnatomicalFrame = Quaternion.Inverse(Find("Chest").Body.rotation) * Actor.Motor.Body.rotation;
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R";
                _clavicles[side] = Array.Find(bones, b => b.name == "Clavicle_" + suffix);
                var upper = Find("UpperArm_" + suffix);
                _upperArmAxes[side] = Quaternion.Inverse(upper.Body.rotation) *
                    (Find("Forearm_" + suffix).Body.position - upper.Body.position).normalized;
                _armLengths[side] = Vector3.Distance(upper.Goal.position, Find("Forearm_" + suffix).Goal.position) +
                    Vector3.Distance(Find("Forearm_" + suffix).Goal.position, GoalHand(side));
            }
        }

        private void ApplyNaturalReachTorso()
        {
            if (!NaturalShoulderMotion || StrikeStyle != ReviewStrikeStyle.BodyStrike || ImpactReactionActive ||
                _punchReference[0] || _punchReference[1] || _gripPosture[0] || _gripPosture[1] || LowReachWeight > .01f) return;
            var reach = IndependentHands ? Mathf.Max(_handWeights[0], _handWeights[1]) : _poseWeight;
            if (reach <= 0) return;
            // A small chest/waist contribution supports ordinary forward reach.
            // The existing combat torso curve remains in charge during punches.
            var spine = Find("Spine");
            spine.Goal.rotation = Quaternion.AngleAxis(3.5f * reach, _goalRoot.right) * spine.Goal.rotation;
        }

        private void ApplyShoulderMotion(float seconds)
        {
            var enabled = NaturalShoulderMotion && StrikeStyle == ReviewStrikeStyle.BodyStrike;
            var chest = Find("Chest");
            var observedFrame = chest.Body.rotation * _chestToAnatomicalFrame;
            var goalFrame = chest.Goal.rotation * _chestToAnatomicalFrame;
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R";
                var sign = side == 0 ? -1f : 1f;
                var upper = Find("UpperArm_" + suffix);
                var active = enabled && (_punchReference[side] || _guardWeight[side] > 0 ||
                    _handActive[side] || _gripPosture[side] || TargetExercise != Exercise.Rest);
                var direction = Quaternion.Inverse(observedFrame) * (upper.Body.rotation * _upperArmAxes[side]);
                var hand = Quaternion.Inverse(observedFrame) * (HandPosition(side) - upper.Body.position);
                var lift = Mathf.Max(Vector3.Angle(Vector3.down, direction), Vector3.Angle(Vector3.down, hand));
                var reach = Mathf.Max(direction.z, hand.z / _armLengths[side]);
                var forward = active ? 10f * Smooth((reach - .1f) / .85f) : 0;
                var up = active ? 4f * Smooth((lift - 25f) / 85f) : 0;
                if (active && _punchReference[side])
                {
                    var t = _punchSourceAge[side] / PhysicalPunchReview.PlaybackRate;
                    var strike = StrikeCurve((t - .045f) / .08f);
                    var envelope = Smooth(t / .04f) * (1 - Smooth((t - .19f) / .17f));
                    forward = Mathf.Lerp(forward, Mathf.Lerp(-4f, 13f, strike), envelope);
                    up = Mathf.Lerp(up, 4f * strike, envelope);
                    if (PunchHasContact(side))
                        forward = Mathf.Lerp(forward, 3f, Smooth((t - _punchImpactAge[side]) / .07f));
                }
                else if (active && _punchReference[1 - side] && !_handActive[side] && !_gripPosture[side])
                {
                    var t = _punchSourceAge[1 - side] / PhysicalPunchReview.PlaybackRate;
                    forward -= 3f * Smooth((t - .055f) / .07f) * (1 - Smooth((t - .20f) / .15f));
                }
                if (_gripPosture[side] && enabled)
                {
                    // Keep the shoulder base at the posture where the grip was
                    // established; do not pull a held hand with a pose target.
                    forward = _shoulderForward[side]; up = _shoulderUp[side];
                }
                var powered = side == 0 ? _leftPower : _rightPower;
                forward *= powered; up *= powered;
                _shoulderForward[side] = Mathf.MoveTowards(_shoulderForward[side], forward, seconds * 110f);
                _shoulderUp[side] = Mathf.MoveTowards(_shoulderUp[side], up, seconds * 65f);
                var clavicle = _goals["Clavicle_" + suffix];
                clavicle.rotation = Quaternion.AngleAxis(-sign * _shoulderForward[side], goalFrame * Vector3.up) *
                    Quaternion.AngleAxis(sign * _shoulderUp[side], goalFrame * Vector3.forward) * clavicle.rotation;
                // The physical socket and the skinned clavicle follow the same
                // fixed-step pose. No Rigidbody position is written here.
                upper.Joint.connectedAnchor = chest.Goal.InverseTransformPoint(upper.Goal.position);
            }
        }

        private void CaptureShoulderMotion()
        {
            for (var side = 0; side < 2; side++)
                if (_clavicles[side] != null)
                    _clavicles[side].localRotation = _goals[side == 0 ? "Clavicle_L" : "Clavicle_R"].localRotation;
        }

        private void ResetShoulderMotion()
        {
            for (var side = 0; side < 2; side++)
            {
                _shoulderForward[side] = _shoulderUp[side] = 0;
                var name = side == 0 ? "Clavicle_L" : "Clavicle_R";
                if (!_goals.TryGetValue(name, out var goal)) continue;
                goal.localRotation = _rest[name];
                if (_clavicles[side] != null) _clavicles[side].localRotation = _rest[name];
                var upper = Find(side == 0 ? "UpperArm_L" : "UpperArm_R");
                upper.Joint.connectedAnchor = upper.StartConnectedAnchor;
            }
        }
    }
}
