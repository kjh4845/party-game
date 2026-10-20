using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Strike feel: a fist drive that keeps the striking hand on the reference
    // path during the extension, and a whole-body knockback for the victim.
    public sealed partial class ActiveUpperBodyReview
    {
        // The joint drives alone trail the fast reference by ~30 degrees and the
        // fist arrives late and slow. During the extension only, the hand is pulled
        // toward the reference hand with a bounded muscle force whose reaction is
        // taken by the grounded root, so no momentum is created from nothing.
        public bool FistDrive = true;
        public float FistGain = 700, FistDamping = 45, FistMaxForce = 180;
        public const float FistWindowStart = .045f, FistWindowEnd = .15f;
        private readonly Vector3[] _fistGoalPrevious = new Vector3[2];
        private readonly bool[] _fistGoalValid = new bool[2];
        public Vector3 LastFistForce(int side) => _lastFistForce[side];
        private readonly Vector3[] _lastFistForce = new Vector3[2];

        private void ApplyFistDrive(float seconds)
        {
            for (var side = 0; side < 2; side++)
            {
                _lastFistForce[side] = Vector3.zero;
                if (!FistDrive || !_punchReference[side] || PunchHasContact(side) || _gripPosture[side] || _handActive[side])
                {
                    _fistGoalValid[side] = false;
                    continue;
                }
                var goal = GoalHand(side);
                var goalVelocity = _fistGoalValid[side] ? (goal - _fistGoalPrevious[side]) / seconds : Vector3.zero;
                _fistGoalPrevious[side] = goal;
                _fistGoalValid[side] = true;
                var t = _punchSourceAge[side] / PhysicalPunchReview.PlaybackRate;
                var window = Smooth((t - FistWindowStart) / .02f) * (1 - Smooth((t - FistWindowEnd) / .03f));
                if (window <= 0) continue;
                var suffix = side == 0 ? "L" : "R";
                var forearm = Find("Forearm_" + suffix);
                var upper = Find("UpperArm_" + suffix);
                var hand = HandPosition(side);
                var velocity = forearm.Body.GetPointVelocity(hand);
                var muscle = side == 0 ? _leftPower : _rightPower;
                var mass = forearm.Body.mass + .5f * upper.Body.mass;
                var force = mass * (FistGain * (goal - hand) + FistDamping * (goalVelocity - velocity)) * window * muscle;
                force = Vector3.ClampMagnitude(force, FistMaxForce * window);
                forearm.Body.AddForceAtPosition(force, hand, ForceMode.Force);
                Actor.Motor.Body.AddForce(-force, ForceMode.Force);
                _lastFistForce[side] = force;
            }
        }

        // The same velocity change for every body of the victim: the whole
        // character is shoved without stretching its joints. The struck part's
        // own impulse (applied by the punch review) still leads the reaction.
        public void ApplyKnockback(Vector3 velocityChange)
        {
            Actor.Motor.Body.AddForce(velocityChange, ForceMode.VelocityChange);
            foreach (var link in _links) link.Body.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        private void ClearFistDrive()
        {
            for (var side = 0; side < 2; side++) { _fistGoalValid[side] = false; _lastFistForce[side] = Vector3.zero; }
        }
    }
}
