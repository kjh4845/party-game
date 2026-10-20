using System;
using ProjectHotfix.Contracts;

namespace ProjectHotfix.Simulation
{
    // Engine-independent jump policy; the authority supplies observed support and velocity.
    public sealed class PhysicsJumpStateMachine
    {
        public float BufferSeconds = .12f;
        public float CoyoteSeconds = .10f;
        private float _time;
        private float _requestUntil = float.NegativeInfinity;
        private float _lastSupported = float.NegativeInfinity;
        private float _ignoreSupportUntil;
        private float _lastLanding = float.NegativeInfinity;
        private float _lastDownSpeed;
        private float _landingImpact;
        private bool _jumpSpent;
        private bool _airborne;

        public bool Grounded { get; private set; }
        public int JumpCount { get; private set; }
        public int LandingCount { get; private set; }
        public uint ResetVersion { get; private set; }

        public void RequestJump() { _requestUntil = _time + Math.Max(0f, BufferSeconds); }
        public void ClearPendingInput() { _requestUntil = float.NegativeInfinity; }
        public void NotifyExternalLaunch()
        {
            ClearPendingInput();_jumpSpent=_airborne=true;Grounded=false;_ignoreSupportUntil=_time+.08f;_lastDownSpeed=0;
        }
        public JumpPresentationState Capture(float verticalSpeed, float velocityX = 0f, float velocityZ = 0f,
            bool sprinting = false, float groundDistance = float.PositiveInfinity) => new JumpPresentationState(
            verticalSpeed, Grounded, _time - _lastLanding, _landingImpact, velocityX, velocityZ,
            sprinting, groundDistance, ResetVersion);

        public bool Step(float deltaTime, bool observedSupport, float verticalSpeed, bool authorityEnabled)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            _time += deltaTime;
            if (!authorityEnabled) { ClearPendingInput(); return false; }
            var support = observedSupport && _time >= _ignoreSupportUntil;
            if (_jumpSpent && verticalSpeed > .05f) support = false;
            Grounded = support;
            if (support)
            {
                _lastSupported = _time;
                if (_airborne)
                {
                    _lastLanding = _time;
                    _landingImpact = _lastDownSpeed;
                    LandingCount++;
                    _jumpSpent = false;
                    _airborne = false;
                }
                _lastDownSpeed = 0f;
            }
            else
            {
                _airborne = true;
                _lastDownSpeed = Math.Max(_lastDownSpeed, -verticalSpeed);
            }
            if (_jumpSpent || _requestUntil < _time || (!support && _time - _lastSupported > CoyoteSeconds)) return false;
            ClearPendingInput();
            _jumpSpent = _airborne = true;
            _ignoreSupportUntil = _time + .08f;
            Grounded = false;
            JumpCount++;
            return true;
        }

        public void Reset()
        {
            ResetVersion++;
            _requestUntil = _lastSupported = _lastLanding = float.NegativeInfinity;
            _ignoreSupportUntil = _time;
            _lastDownSpeed = _landingImpact = 0f;
            _jumpSpent = _airborne = Grounded = false;
            JumpCount = LandingCount = 0;
        }
    }
}
