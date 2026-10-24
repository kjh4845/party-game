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
        // A get-up making room nudges a standing player off (RagdollRecoveryReview), every tick it is in the way: its
        // speed away is raised to `speed` if slower (never added up). For ShoveSeconds after, its walk/strike drive is
        // damped to ShovedDrive and the strike brake is off, so it gives way instead of digging in. 2026-10-21 review:
        // a fixed 1.6 m/s shove every .4 s, sometimes twice at once, flung a player ~.9 m and a pressing one ping-ponged.
        public const float ShoveSeconds = .3f, ShovedDrive = .3f;
        private float _shoveSeconds;
        public bool Shoved => _shoveSeconds > 0;
        public void NudgeFromRoom(Vector3 direction, float speed)
        {
            _shoveSeconds = ShoveSeconds;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            var along = Vector3.Dot(Actor.Motor.Body.linearVelocity, direction);
            if (along < speed) ApplyKnockback(direction * (speed - along));
        }
        // Raises the body's speed away along `direction` (horizontal) to `speed` if slower, and nothing else: no shove,
        // its walk drive untouched (a standing player kept off a downed body's skin, 2026-10-22 review: the shove made
        // it bounce off and creep back, a visible gap most of the push). The root only, where the walk drive pushes:
        // given to the trunk and arms too, every tick the root was held off they got back a walk they never had, and
        // the upper body leant ~15 deg back, head ~7 cm behind the hips, all the push (2026-10-23 review: the pushing
        // player looked folded). The impulse is sized on the whole body's momentum, scaled by HoldOffShare: sized on
        // the root alone the upper body dragged it back in through the joints and the calves sank into the body (run
        // matrix 17/20 cases), the whole momentum bounced the walker off (contact lost ~30 ticks); .75 sinks 1-3/20.
        public const float HoldOffShare = .75f;
        public void HoldOff(Vector3 direction, float speed)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            var body = Actor.Motor.Body;
            var momentum = body.mass * body.linearVelocity; foreach (var link in _links) momentum += link.Body.mass * link.Body.linearVelocity;
            var along = Vector3.Dot(momentum, direction) / TotalMass;
            if (along < speed) body.AddForce(direction * ((speed - along) * TotalMass * HoldOffShare), ForceMode.Impulse);
        }

        private void ClearFistDrive()
        {
            for (var side = 0; side < 2; side++) { _fistGoalValid[side] = false; _lastFistForce[side] = Vector3.zero; }
        }
    }
}
