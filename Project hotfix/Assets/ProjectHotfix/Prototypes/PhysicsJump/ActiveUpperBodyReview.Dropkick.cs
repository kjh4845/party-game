using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Running dropkick (2026-10-20 review: while running only a dropkick, no punch). The whole body takes
    // off as one stiff ragdoll: the run's momentum plus a forward/up launch, pitching back so it flies
    // feet first. After DropkickHoldSeconds it goes limp and lands; the shared tumble get-up follows
    // (DropkickRecovery: no DownCount or Down wait, SRS-PHYS-013).
    public sealed partial class ActiveUpperBodyReview
    {
        public bool Dropkicking { get; private set; }
        public float DropkickAge { get; private set; }
        public int Dropkicks { get; private set; }
        // Launch (m/s added along the facing and up) and the stiff flight. The body turns to look to the side of
        // the clicked hand and lies back feet first (DropkickLean deg from upright), reaching that in
        // DropkickTurnSeconds (2026-10-20 review: fly looking left or right, not face first).
        // Out of a running jump the flight keeps the jump's rise plus DropkickAirUp, between DropkickAirMinimumUp and
        // DropkickUp (no second jump on top of the first).
        public float DropkickAirUp = 1f, DropkickAirMinimumUp = 1.2f;
        public float DropkickForward = 1.2f, DropkickUp = 2.4f, DropkickLean = 80f, DropkickTurnSeconds = .18f, DropkickHoldSeconds = .45f, DropkickStiffness = 400f;
        private Quaternion _dropkickTarget; private bool _dropkickTurning;

        private const float LimpMaximumSpin = 4f;

        public bool BeginDropkick(int side)
        {
            var root = Actor.Motor.Body;
            var direction = Vector3.ProjectOnPlane(root.rotation * Vector3.forward, Vector3.up).normalized;
            var run = Mathf.Max(0, Vector3.Dot(root.linearVelocity, direction));
            var up = Supported ? DropkickUp : Mathf.Clamp(root.linearVelocity.y + DropkickAirUp, DropkickAirMinimumUp, DropkickUp);
            if (!EnterRagdoll(false)) return false;
            EndDownProtection(); Dropkicking = true; DropkickAge = 0; root.constraints = RigidbodyConstraints.None;
            Knockdown.TumbleHoldSpring = DropkickStiffness;
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            var mass = 0f; var center = Vector3.zero;
            for (var i = 0; i < count; i++) { mass += bodies[i].mass; center += bodies[i].mass * bodies[i].worldCenterOfMass; }
            center /= mass;
            // One rigid launch about the centre of mass: the body rotates back (head back, feet forward up).
            var launch = direction * (run + DropkickForward) + Vector3.up * up;
            // Yaw a quarter turn toward the clicked side, then lean about the new facing so the head goes back.
            var yaw = Quaternion.AngleAxis(side == 0 ? -90f : 90f, Vector3.up); var face = yaw * direction;
            var lean = Vector3.Dot(Vector3.Cross(face, Vector3.up), -direction) >= 0 ? DropkickLean : -DropkickLean;
            (Quaternion.AngleAxis(lean, face) * yaw).ToAngleAxis(out var angle, out var axis);
            if (angle > 180) angle -= 360;
            var spin = axis * (angle * Mathf.Deg2Rad / DropkickTurnSeconds);
            _dropkickTarget = Quaternion.AngleAxis(lean, face) * yaw * root.rotation; _dropkickTurning = true;
            // The limp ragdoll caps spin at 4 rad/s; the turn needs more (at 4 a plain back pitch flew only ~45 deg
            // back and read as a backwards jump).
            for (var i = 0; i < count; i++)
            {
                bodies[i].maxAngularVelocity = Mathf.Max(LimpMaximumSpin, spin.magnitude * 1.5f);
                bodies[i].linearVelocity = launch + Vector3.Cross(spin, bodies[i].worldCenterOfMass - center);
                bodies[i].angularVelocity = spin;
            }
            Dropkicks++;
            return true;
        }

        // Before the ragdoll step: the flight is held stiff, then released limp.
        public void StepDropkick(float seconds)
        {
            if (!Dropkicking) return;
            DropkickAge += seconds;
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            if (_dropkickTurning)
            {
                // Steer the whole stiff body to the kick orientation as one rigid body, keeping its flight: the
                // take-off spin alone was eaten by foot friction and contacts (2026-10-20: it landed half turned).
                var root = Actor.Motor.Body; var mass = 0f; var center = Vector3.zero; var momentum = Vector3.zero;
                for (var i = 0; i < count; i++) { mass += bodies[i].mass; center += bodies[i].mass * bodies[i].worldCenterOfMass; momentum += bodies[i].mass * bodies[i].linearVelocity; }
                center /= mass; var flight = momentum / mass;
                var error = _dropkickTarget * Quaternion.Inverse(root.rotation);
                if (error.w < 0) error = new Quaternion(-error.x, -error.y, -error.z, -error.w);
                error.ToAngleAxis(out var angle, out var axis);
                var settled = DropkickAge >= DropkickTurnSeconds + .1f;
                var spin = settled || angle < .5f ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / Mathf.Max(DropkickTurnSeconds - DropkickAge, 3 * seconds));
                for (var i = 0; i < count; i++)
                {
                    bodies[i].linearVelocity = flight + Vector3.Cross(spin, bodies[i].worldCenterOfMass - center);
                    bodies[i].angularVelocity = spin;
                }
                _dropkickTurning = !settled;
            }
            if (DropkickAge < DropkickHoldSeconds || Knockdown.TumbleHoldSpring <= 0) return;
            Knockdown.TumbleHoldSpring = 0;
            for (var i = 0; i < count; i++) bodies[i].maxAngularVelocity = LimpMaximumSpin;
        }

        private void EndDropkick() { Dropkicking = false; if (Knockdown != null) Knockdown.TumbleHoldSpring = 0; }
    }
}
