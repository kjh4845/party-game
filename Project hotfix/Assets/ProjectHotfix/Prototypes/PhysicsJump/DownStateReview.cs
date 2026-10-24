using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Opt-in state/lock harness. Does not pretend the capsule is a ragdoll,
    // classify hits, select tuning, or fabricate support/get-up completion.
    [DefaultExecutionOrder(-100)]
    public sealed class DownStateReview : MonoBehaviour
    {
        public PhysicsJumpReview Actor { get; private set; }
        public TimedDownStateMachine State { get; private set; }
        private uint _motorReset;
        private RecoverySupportProbe _probe;
        private ulong _probeEpisode;
        public RecoverySupportSample SupportSample { get; private set; }
        public void BindRecoveryProbe(RecoverySupportProbe probe) { _probe = probe; _probe?.Reset(); }
        public void Bind(PhysicsJumpReview actor, DownTimingProfile profile, bool isMatchRound)
        {
            Actor = actor; State = new TimedDownStateMachine(profile, isMatchRound);
            _motorReset = actor.Motor.PresentationState.ResetVersion;
        }
        public bool TryBeginDown(uint generation, ulong sequence)
        {
            SynchronizeReset();
            if (!Actor.Motor.AuthorityEnabled || !State.TryBeginDown(generation, sequence)) return false;
            ApplyLock(); return true;
        }
        private void FixedUpdate() { if (Actor != null && !Actor.Motor.ManualStep) Step(Time.fixedDeltaTime); }
        private void SynchronizeReset()
        {
            var version = Actor.Motor.PresentationState.ResetVersion;
            if (_motorReset == version) return;
            _motorReset = version; State.ResetRound(State.IsMatchRound); _probe?.Reset(); SupportSample = default; ApplyLock();
        }
        public void Step(float seconds)
        {
            SynchronizeReset();
            if (Actor.Motor.AuthorityEnabled)
            {
                State.Advance(seconds);
                if (_probe != null && State.BlocksActions)
                {
                    if (_probeEpisode != State.EpisodeId) { _probeEpisode = State.EpisodeId; _probe.Reset(); }
                    SupportSample = _probe.Evaluate(seconds);
                    if (!SupportSample.Stable || !SupportSample.Clear)
                        State.TryInterruptGetUp(State.RoundGeneration, State.EpisodeId);
                    else if (State.CanStartGetUp)
                        State.TryBeginGetUp(State.RoundGeneration, State.EpisodeId, _probe.TorsoRotation,
                            SupportSample.Normal, true, true);
                    // Alignment/body motion and actual get-up completion belong
                    // to the subsequent ragdoll/presentation integration.
                }
            }
            else { _probe?.Reset(); SupportSample = default; }
            ApplyLock();
        }
        public void RefreshLock() { SynchronizeReset(); ApplyLock(); }
        private void ApplyLock()
        {
            Actor.Motor.ActionsBlocked = State.BlocksActions;
            if (!State.BlocksActions) return;
            Actor.Motor.MoveInput = Vector2.zero; Actor.Motor.SprintRequested = false; Actor.Motor.ClearPendingInput();
            if (Actor.Hands == null) return;
            if (Actor.Hands.Grabs != null) Actor.Hands.Grabs.ReleaseAll("Down");
            Actor.Hands.State.Suppress();
        }
    }
}
