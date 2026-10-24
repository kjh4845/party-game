using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class TimedDownTests
    {
        // Fixture values exercise accumulation and cap; they are not game tuning.
        private static TimedDownStateMachine Create(bool match = true) => new TimedDownStateMachine(new DownTimingProfile(1.5, .5, 3), match);
        private static void Finish(TimedDownStateMachine state)
        {
            state.Advance(10);
            Assert.That(state.TryBeginGetUp(state.RoundGeneration, state.EpisodeId, Quaternion.Euler(-90, 0, 0), Vector3.up, true, true), Is.True);
            Assert.That(state.TryCompleteGetUp(state.RoundGeneration, state.EpisodeId), Is.True);
        }
        [Test] public void MatchAccumulatesOncePerEpisodeAndCapsDuration()
        {
            var state = Create();
            for (ulong i = 1; i <= 7; i++)
            {
                Assert.That(state.TryBeginDown(1, i * 10), Is.True);
                Assert.That(state.DownCount, Is.EqualTo((uint)i));
                Assert.That(state.RemainingSeconds, Is.EqualTo(Math.Min(3, 1.5 + .5 * (i - 1))));
                state.Advance(.2); var remaining = state.RemainingSeconds;
                Assert.That(state.TryBeginDown(1, i * 10 + 1), Is.False);
                Assert.That(state.RemainingSeconds, Is.EqualTo(remaining));
                Assert.That(state.DownCount, Is.EqualTo((uint)i)); Finish(state);
                Assert.That(state.TryBeginDown(1, i * 10 + 1), Is.False, "Previously consumed contact cannot reopen after get-up");
            }
        }
        [Test] public void LobbyAlwaysUsesBaseAndDoesNotCount()
        {
            var state = Create(false);
            for (ulong i = 1; i <= 5; i++) { Assert.That(state.TryBeginDown(1, i), Is.True); Assert.That(state.DownCount, Is.Zero); Assert.That(state.RemainingSeconds, Is.EqualTo(1.5)); Finish(state); }
            state.ResetRound(true); Assert.That(state.TryBeginDown(state.RoundGeneration, 6), Is.True); Assert.That(state.DownCount, Is.EqualTo(1)); Assert.That(state.RemainingSeconds, Is.EqualTo(1.5));
        }
        [Test] public void ExpiryOnlyPermitsSupportedRecoveryAndNeverUnlocksItself()
        {
            var state = Create(); state.TryBeginDown(1, 1);
            Assert.That(state.TryBeginGetUp(1, 1, Quaternion.Euler(-90, 0, 0), Vector3.up, true, true), Is.False);
            state.Advance(1.499); Assert.That(state.CanStartGetUp, Is.False); state.Advance(.0011);
            Assert.That(state.RemainingSeconds, Is.Zero); Assert.That(state.BlocksActions, Is.True);
            Assert.That(state.TryCompleteGetUp(1, 1), Is.False);
            Assert.That(state.TryBeginGetUp(1, 1, Quaternion.Euler(-90, 0, 0), Vector3.up, false, true), Is.False);
            Assert.That(state.TryBeginGetUp(1, 1, Quaternion.Euler(-90, 0, 0), Vector3.up, true, false), Is.False);
            Finish(state); Assert.That(state.BlocksActions, Is.False);
        }
        [TestCase(-90, 0)] [TestCase(90, 0)] [TestCase(0, 90)] [TestCase(0, -90)]
        public void AllFourEntriesUseExistingAlignmentAndInterruptionPreservesEpisode(float pitch, float roll)
        {
            var state = Create(); state.TryBeginDown(1, 1); state.Advance(2);
            Assert.That(state.TryBeginGetUp(1, 1, Quaternion.Euler(pitch, 23, roll), Vector3.up, true, true), Is.True);
            if (state.Phase == RecoveryPresentationPhase.AligningToGetUp)
            {
                Assert.That(state.TryCompleteGetUp(1, 1), Is.False);
                Assert.That(state.TryFinishAlignment(1, 1, false, true, true), Is.False);
                Assert.That(state.TryFinishAlignment(1, 1, true, true, true), Is.True);
            }
            Assert.That(state.TryInterruptGetUp(1, 1), Is.True);
            Assert.That(state.DownCount, Is.EqualTo(1)); Assert.That(state.EpisodeId, Is.EqualTo(1)); Assert.That(state.RemainingSeconds, Is.Zero);
            Assert.That(state.TryCompleteGetUp(1, 1), Is.False); Finish(state);
        }
        [Test] public void DropkickHasNoDownTimerOrCountAndRealDownSupersedesItOnce()
        {
            var state = Create(); Assert.That(state.TryBeginDropkickRecovery(1, 1), Is.True);
            Assert.That(state.DownCount, Is.Zero); Assert.That(state.RemainingSeconds, Is.Zero); Assert.That(state.BlocksActions, Is.True);
            Assert.That(state.TryBeginDown(1, 2), Is.True); Assert.That(state.DownCount, Is.EqualTo(1)); Assert.That(state.RemainingSeconds, Is.EqualTo(1.5));
            Assert.That(state.TryCompleteGetUp(1, 1), Is.False); Assert.That(state.TryBeginDropkickRecovery(1, 3), Is.False);
            Finish(state); Assert.That(state.TryBeginDropkickRecovery(1, 4), Is.True); Assert.That(state.DownCount, Is.EqualTo(1)); Assert.That(state.RemainingSeconds, Is.Zero); Finish(state);
        }
        [Test] public void ResetAndNewEpisodeRejectStaleEventsAndGetUpCompletions()
        {
            var state = Create(); state.TryBeginDown(1, 1); Finish(state); state.TryBeginDown(1, 2);
            Assert.That(state.TryCompleteGetUp(1, 1), Is.False); state.ResetRound(true);
            Assert.That(state.DownCount, Is.Zero); Assert.That(state.RemainingSeconds, Is.Zero); Assert.That(state.BlocksActions, Is.False);
            Assert.That(state.TryBeginDown(1, 100), Is.False); Assert.That(state.TryBeginDown(2, 2), Is.False); Assert.That(state.TryBeginDown(2, 3), Is.True);
            Assert.That(state.TryCompleteGetUp(1, 2), Is.False); Assert.That(state.DownCount, Is.EqualTo(1));
        }
        [Test] public void UprightReturnRequiresExpiredTimerRealSupportAndSettledCompletion()
        {
            var state=Create();state.TryBeginDown(1,1);
            Assert.That(state.TryBeginUprightReturn(1,1,true,true,true),Is.False);
            state.Advance(1.5);
            Assert.That(state.TryBeginUprightReturn(1,1,false,true,true),Is.False);
            Assert.That(state.TryBeginUprightReturn(1,1,true,false,true),Is.False);
            Assert.That(state.TryBeginUprightReturn(1,1,true,true,false),Is.False);
            Assert.That(state.TryBeginUprightReturn(0,1,true,true,true),Is.False);
            Assert.That(state.TryBeginUprightReturn(1,1,true,true,true),Is.True);
            Assert.That(state.BlocksActions,Is.True);Assert.That(state.DownCount,Is.EqualTo(1));
            Assert.That(state.TryCompleteUprightReturn(1,1,false),Is.False);
            Assert.That(state.TryCompleteUprightReturn(1,1,true),Is.True);Assert.That(state.BlocksActions,Is.False);
            state.TryBeginDown(1,2);Assert.That(state.AppliedDurationSeconds,Is.EqualTo(2));
            Assert.That(state.TryCompleteUprightReturn(1,1,true),Is.False);
        }
        [Test] public void InvalidTuningAndClockCannotMutateState()
        {
            foreach (var value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity }) Assert.Throws<ArgumentOutOfRangeException>(() => new DownTimingProfile(value, .5, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownTimingProfile(2, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimedDownStateMachine(default, true));
            var state = Create(); state.TryBeginDown(1, 1);
            foreach (var value in new[] { -1d, double.NaN, double.PositiveInfinity }) Assert.Throws<ArgumentOutOfRangeException>(() => state.Advance(value));
            Assert.That(state.RemainingSeconds, Is.EqualTo(1.5));
        }
    }
}
