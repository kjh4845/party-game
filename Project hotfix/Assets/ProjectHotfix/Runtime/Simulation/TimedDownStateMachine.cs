using System;
using UnityEngine;

namespace ProjectHotfix.Simulation
{
    // No production defaults: the caller supplies an explicit tuning profile.
    public readonly struct DownTimingProfile
    {
        public readonly double BaseSeconds, IncrementSeconds, MaximumSeconds;
        public DownTimingProfile(double baseSeconds, double incrementSeconds, double maximumSeconds)
        {
            if (!Finite(baseSeconds) || baseSeconds <= 0 || !Finite(incrementSeconds) || incrementSeconds < 0
                || !Finite(maximumSeconds) || maximumSeconds < baseSeconds) throw new ArgumentOutOfRangeException(nameof(baseSeconds));
            BaseSeconds = baseSeconds; IncrementSeconds = incrementSeconds; MaximumSeconds = maximumSeconds;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public double Duration(uint count) => Math.Min(MaximumSeconds, BaseSeconds + IncrementSeconds * Math.Max(0d, (double)count - 1d));
    }

    // Authority lifecycle. A timer expiring permits recovery; it never finishes
    // get-up or invents support, clearance, damage, collision, or an animation.
    public sealed class TimedDownStateMachine
    {
        private readonly CharacterRecoveryStateMachine _recovery = new CharacterRecoveryStateMachine();
        private readonly DownTimingProfile _profile;
        private ulong _lastEventSequence;
        public uint RoundGeneration { get; private set; } = 1;
        public ulong EpisodeId { get; private set; }
        public bool IsMatchRound { get; private set; }
        public uint DownCount => _recovery.DownCount;
        public CharacterRecoveryKind Kind => _recovery.Kind;
        public RecoveryPresentationPhase Phase => _recovery.PresentationPhase;
        public RecoveryOrientation? GetUpOrientation => _recovery.GetUpOrientation;
        public Quaternion? AlignmentTargetRotation => _recovery.AlignmentTargetRotation;
        public bool BlocksActions => _recovery.BlocksNewAttacks;
        public double AppliedDurationSeconds { get; private set; }
        public double RemainingSeconds { get; private set; }
        public bool CanStartGetUp => BlocksActions && RemainingSeconds <= 0;

        public TimedDownStateMachine(DownTimingProfile profile, bool isMatchRound)
        {
            // Also reject a default(struct), which bypasses its constructor.
            _profile = new DownTimingProfile(profile.BaseSeconds, profile.IncrementSeconds, profile.MaximumSeconds);
            IsMatchRound = isMatchRound;
        }
        private bool Admit(uint generation, ulong sequence)
        {
            if (generation != RoundGeneration || sequence <= _lastEventSequence) return false;
            _lastEventSequence = sequence;
            return true;
        }
        public bool TryBeginDown(uint generation, ulong sequence)
        {
            if (!Admit(generation, sequence) || !_recovery.TryBeginDownEpisode(IsMatchRound)) return false;
            EpisodeId = checked(EpisodeId + 1);
            AppliedDurationSeconds = IsMatchRound ? _profile.Duration(DownCount) : _profile.BaseSeconds;
            RemainingSeconds = AppliedDurationSeconds;
            return true;
        }
        public bool TryBeginDropkickRecovery(uint generation, ulong sequence)
        {
            if (!Admit(generation, sequence) || BlocksActions) return false;
            _recovery.BeginDropkickRecovery(); EpisodeId = checked(EpisodeId + 1);
            AppliedDurationSeconds = RemainingSeconds = 0;
            return true;
        }
        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            RemainingSeconds = Math.Max(0, RemainingSeconds - seconds);
        }
        private bool Current(uint generation, ulong episode) => BlocksActions && generation == RoundGeneration && episode == EpisodeId;
        public bool TryBeginGetUp(uint generation, ulong episode, Quaternion torso, Vector3 supportNormal, bool supported, bool clear)
            => Current(generation, episode) && CanStartGetUp && _recovery.TryBeginSharedGetUp(torso, supportNormal, supported, clear);
        public bool TryFinishAlignment(uint generation, ulong episode, bool supported, bool clear, bool aligned)
            => Current(generation, episode) && CanStartGetUp && _recovery.TryFinishAlignment(supported, clear, aligned);
        public bool TryInterruptGetUp(uint generation, ulong episode)
        {
            if (!Current(generation, episode)) return false;
            _recovery.InterruptRecoveryPresentation(); return true;
        }
        public bool TryCompleteGetUp(uint generation, ulong episode)
        {
            if (!Current(generation, episode) || !CanStartGetUp || Phase != RecoveryPresentationPhase.SharedGetUp) return false;
            _recovery.CompleteRecovery(); AppliedDurationSeconds = RemainingSeconds = 0; return true;
        }
        public bool TryBeginUprightReturn(uint generation, ulong episode, bool supported, bool clear, bool upright)
            => Current(generation,episode) && CanStartGetUp && _recovery.TryBeginUprightReturn(supported,clear,upright);
        public bool TryCompleteUprightReturn(uint generation, ulong episode, bool settled)
        {
            if(!Current(generation,episode) || !CanStartGetUp || Phase!=RecoveryPresentationPhase.UprightReturn || !settled) return false;
            _recovery.CompleteRecovery();AppliedDurationSeconds=RemainingSeconds=0;return true;
        }
        public void ResetRound(bool isMatchRound)
        {
            RoundGeneration = checked(RoundGeneration + 1);
            _recovery.ResetRound(); AppliedDurationSeconds = RemainingSeconds = 0; IsMatchRound = isMatchRound;
            // Event and episode identities survive reset, so an old completion
            // cannot unlock a later episode even when its duration is identical.
        }
    }
}
