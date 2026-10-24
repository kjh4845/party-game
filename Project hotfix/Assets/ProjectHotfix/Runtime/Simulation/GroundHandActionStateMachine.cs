using System;
using System.Collections.Generic;
using ProjectHotfix.Contracts;

namespace ProjectHotfix.Simulation
{
    public enum HandIntentPhase { Idle, GroundPending, AirPending, GrabSeek, Grabbing, Punch }

    // Local authority policy. Air attacks and physical Grab constraints are separate owners.
    public sealed class GroundHandActionStateMachine
    {
        private sealed class Hand
        {
            public bool Down;
            public double PressTime, AttackTime;
            public HandIntentPhase Phase;
            public ulong ActionId, GrabRelationId;
            public int GrabTargetId;
            public double SeekTime;
            public readonly HashSet<int> HitTargets = new HashSet<int>();
        }
        private readonly Hand[] _hands = { new Hand(), new Hand() };
        private double _now;
        private ulong _lastSequence, _nextActionId, _nextGrabRelationId;
        private bool _armed = true;
        public double HoldThresholdSeconds { get; }
        public double PunchPlaybackRate { get; }
        public double ActiveStartSourceSeconds { get; }
        public double ActiveEndSourceSeconds { get; }
        public const double AttackDuration = .7;
        public const double ActiveStart = .10;
        public const double ActiveEnd = .2666666666666667;
        public uint ResetVersion { get; private set; }
        public int PunchCount { get; private set; }
        public int ConfirmedHitCount { get; private set; }
        public bool Armed => _armed;

        public GroundHandActionStateMachine(double holdThresholdSeconds = .15, double punchPlaybackRate = 1,
            double activeStartSourceSeconds = ActiveStart, double activeEndSourceSeconds = ActiveEnd)
        {
            if (double.IsNaN(holdThresholdSeconds) || double.IsInfinity(holdThresholdSeconds) || holdThresholdSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(holdThresholdSeconds));
            HoldThresholdSeconds = holdThresholdSeconds;
            if (double.IsNaN(punchPlaybackRate) || double.IsInfinity(punchPlaybackRate) || punchPlaybackRate <= 0 || punchPlaybackRate > 2)
                throw new ArgumentOutOfRangeException(nameof(punchPlaybackRate));
            PunchPlaybackRate = punchPlaybackRate;
            if (!double.IsFinite(activeStartSourceSeconds) || !double.IsFinite(activeEndSourceSeconds)
                || activeStartSourceSeconds < 0 || activeEndSourceSeconds <= activeStartSourceSeconds || activeEndSourceSeconds >= AttackDuration)
                throw new ArgumentOutOfRangeException(nameof(activeEndSourceSeconds));
            ActiveStartSourceSeconds = activeStartSourceSeconds; ActiveEndSourceSeconds = activeEndSourceSeconds;
        }
        public HandIntentPhase Phase(int side) => _hands[side].Phase;
        public double SeekAge(int side) => _now - _hands[side].SeekTime;
        public ulong GrabRelationId(int side) => _hands[side].GrabRelationId;
        public int GrabTargetId(int side) => _hands[side].GrabTargetId;
        public bool CanReleaseGrab(ulong sequence, int side) => side >= 0 && side < 2 && sequence > _lastSequence
            && _armed && _hands[side].Down && _hands[side].Phase == HandIntentPhase.Grabbing;
        public bool TryBeginGrab(int side, int targetId)
        {
            var hand = _hands[side];
            if (!_armed || !hand.Down || hand.Phase != HandIntentPhase.GrabSeek || targetId == 0) return false;
            hand.Phase = HandIntentPhase.Grabbing; hand.GrabTargetId = targetId; hand.GrabRelationId = ++_nextGrabRelationId;
            return true;
        }
        public ulong ActionId(int side) => _hands[side].ActionId;
        public double PunchAge(int side) => _now - _hands[side].AttackTime;
        public double SourcePunchAge(int side) => PunchAge(side) * PunchPlaybackRate;
        public bool Active(int side) => _hands[side].Phase == HandIntentPhase.Punch
            && SourcePunchAge(side) >= ActiveStartSourceSeconds - 1e-9 && SourcePunchAge(side) <= ActiveEndSourceSeconds + 1e-9;

        public void Advance(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now));
            _now = now;
            foreach (var hand in _hands)
            {
                if ((hand.Phase == HandIntentPhase.GroundPending || hand.Phase == HandIntentPhase.AirPending)
                    && now - hand.PressTime >= HoldThresholdSeconds - 1e-9) { hand.Phase = HandIntentPhase.GrabSeek; hand.SeekTime = hand.PressTime + HoldThresholdSeconds; }
                if (hand.Phase == HandIntentPhase.Punch && (now - hand.AttackTime) * PunchPlaybackRate >= AttackDuration - 1e-9)
                    hand.Phase = HandIntentPhase.Idle;
            }
        }

        // Stage all releases in a fixed-tick batch before Receive advances either hand.
        // Otherwise processing the left edge could time out the right pending tap first.
        public void PrepareTimedRelease(int side, double now, double heldSeconds)
        {
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now));
            if (double.IsNaN(heldSeconds) || double.IsInfinity(heldSeconds) || heldSeconds < 0) throw new ArgumentOutOfRangeException(nameof(heldSeconds));
            var hand = _hands[side];
            if (hand.Down && (hand.Phase == HandIntentPhase.GroundPending || hand.Phase == HandIntentPhase.AirPending))
                hand.PressTime = now - heldSeconds;
        }

        // Ordered local edge stream. Replayed/out-of-order sequences have no effect.
        public bool Receive(ulong sequence, int side, bool down, double now, bool groundedAtPress, bool handAvailable,double? actualHeldSeconds=null)
        {
            if (side < 0 || side > 1 || sequence <= _lastSequence) return false;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now));
            if (!down && actualHeldSeconds.HasValue) PrepareTimedRelease(side, now, actualHeldSeconds.Value);
            Advance(now); _lastSequence = sequence;
            if (!_armed) return false;
            var hand = _hands[side];
            if (down == hand.Down) return false;
            hand.Down = down;
            if (!handAvailable) { CancelHand(side); return false; }
            if (down)
            {
                if (hand.Phase != HandIntentPhase.Idle) return false;
                hand.PressTime = now;
                hand.Phase = groundedAtPress ? HandIntentPhase.GroundPending : HandIntentPhase.AirPending;
                return false;
            }
            if (hand.Phase == HandIntentPhase.GroundPending)
            {
                hand.Phase = HandIntentPhase.Punch; hand.AttackTime = now;
                hand.ActionId = ++_nextActionId; hand.HitTargets.Clear(); PunchCount++;
                return true;
            }
            if (hand.Phase != HandIntentPhase.Punch) { hand.Phase = HandIntentPhase.Idle; hand.GrabTargetId = 0; }
            return false;
        }

        public bool TryConfirmHit(int side, int targetId)
        {
            if (!_armed || !Active(side) || targetId == 0 || !_hands[side].HitTargets.Add(targetId)) return false;
            ConfirmedHitCount++; return true;
        }
        public void CancelHand(int side) { _hands[side].Phase = HandIntentPhase.Idle; _hands[side].GrabTargetId = 0; _hands[side].HitTargets.Clear(); }
        public void Suppress()
        {
            _armed = false;
            for (var side = 0; side < 2; side++) CancelHand(side);
            ResetVersion++;
        }
        public void ObserveButtonsForRearm(bool leftDown, bool rightDown, bool inputSuppressed)
        {
            if (inputSuppressed || _armed || leftDown || rightDown) return;
            foreach (var hand in _hands) hand.Down = false;
            _armed = true; // This all-up sample never emits an action.
        }
        public void Reset()
        {
            Suppress(); PunchCount = ConfirmedHitCount = 0;
            // Keep sequence/action identity monotonic so an old edge cannot revive an attack.
        }
        public HandActionPresentationState Capture() => new HandActionPresentationState(
            Phase(0) == HandIntentPhase.Punch, (float)PunchAge(0), Phase(1) == HandIntentPhase.Punch, (float)PunchAge(1), ResetVersion, (float)PunchPlaybackRate);
    }
}
