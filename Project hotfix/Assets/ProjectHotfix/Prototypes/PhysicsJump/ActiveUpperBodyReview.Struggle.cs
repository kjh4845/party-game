using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Waking up in someone's hands (2026-10-20 review: a carried knocked-out body never woke, so holding it
    // kept it out forever). When the Down time runs out while held, overhead or on the floor, the body
    // flails for StruggleSeconds instead of starting a get-up; then the pair breaks the holder's grips
    // (ActiveGrabPairReview.StepHeldWake) and it lands and gets up as usual. It cannot be grabbed again
    // until it stands.
    public sealed partial class ActiveUpperBodyReview
    {
        // Flail: limb spin acceleration (rad/s^2) and rate (Hz), plus a whole-body wriggle.
        public float StruggleSeconds = 1f, StruggleLimbSpin = 70f, StruggleRate = 3.5f, StruggleWriggle = 20f;
        private float _struggleAge = -1f;
        public bool Struggling => _struggleAge >= 0;
        public bool StruggleDone => _struggleAge >= StruggleSeconds;
        // Broke out of a hold during this Down: not grabbable again until standing.
        public bool WokeInHands { get; private set; }

        public void StepStruggle(float seconds)
        {
            if (_struggleAge < 0) _struggleAge = 0;
            _struggleAge += seconds; Knockdown.HoldTumble = true;
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            var ramp = Mathf.SmoothStep(0, 1, _struggleAge / .2f); var time = _struggleAge * StruggleRate * Mathf.PI * 2;
            for (var i = 0; i < count; i++)
            {
                var body = bodies[i]; var name = body.name;
                if (body == Actor.Motor.Body)
                { body.AddTorque(body.rotation * Vector3.forward * Mathf.Sin(time * .5f) * StruggleWriggle * ramp, ForceMode.Acceleration); continue; }
                if (!name.StartsWith("UpperArm") && !name.StartsWith("Forearm") && !name.StartsWith("Thigh") && !name.StartsWith("Calf")) continue;
                // Each limb on its own phase and a mix of two axes, so the kicks and swings do not move in step.
                var axis = (body.rotation * Vector3.right + body.rotation * Vector3.up * Mathf.Sin(time * .7f + i)).normalized;
                body.AddTorque(axis * Mathf.Sin(time + i * 1.7f) * StruggleLimbSpin * ramp, ForceMode.Acceleration);
            }
        }

        public void EndStruggle(bool escaped)
        {
            if (!Struggling) return;
            _struggleAge = -1f; Knockdown.HoldTumble = false;
            if (escaped) WokeInHands = true;
        }

        private void ClearStruggle() { _struggleAge = -1f; WokeInHands = false; }
    }
}
