using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class GroundHandActionTests
    {
        [TestCase(.147, true)] [TestCase(.150, false)] [TestCase(.153, false)]
        public void HardwareDurationWinsOverFixedTickRounding(double duration, bool punch)
        {
            var state = new GroundHandActionStateMachine();
            state.Receive(1, 0, true, 1, true, true);
            state.Advance(1 + 8d / 60);
            Assert.That(state.Receive(2, 0, false, 1.15, true, true, duration), Is.EqualTo(punch));
            Assert.That(state.PunchCount, Is.EqualTo(punch ? 1 : 0));
        }

        [Test] public void SimultaneousTimedTapsAreIndependentOfHandProcessingOrder()
        {
            foreach (var first in new[] { 0, 1 })
            {
                var state = new GroundHandActionStateMachine();
                state.Receive(1, 0, true, 1, true, true); state.Receive(2, 1, true, 1, true, true);
                state.Advance(1 + 8d / 60);
                state.PrepareTimedRelease(0, 1.15, .147); state.PrepareTimedRelease(1, 1.15, .147);
                Assert.That(state.Receive(3, first, false, 1.15, true, true, .147), Is.True);
                Assert.That(state.Receive(4, 1 - first, false, 1.15, true, true, .147), Is.True);
                Assert.That(state.PunchCount, Is.EqualTo(2));
            }
        }

        [Test] public void InvalidTimestampCannotCorruptPendingHardwareDuration()
        {
            var state = new GroundHandActionStateMachine();
            state.Receive(1, 0, true, 1, true, true);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => state.Receive(2, 0, false, double.NaN, true, true, .147));
            Assert.That(state.Receive(2, 0, false, 1.15, true, true, .147), Is.True);
        }

        [Test] public void FasterPunchSharesHitClockAndRecoveryWithoutChangingHoldThreshold()
        {
            var state=new GroundHandActionStateMachine(.15,1.4);
            state.Receive(1,0,true,0,true,true);state.Receive(2,0,false,.02,true,true);
            state.Advance(.09);Assert.That(state.Active(0),Is.False);
            state.Advance(.10);Assert.That(state.Active(0),Is.True);
            Assert.That(state.Capture().PunchPlaybackRate,Is.EqualTo(1.4f));
            state.Advance(.22);Assert.That(state.Active(0),Is.False);
            state.Advance(.52);Assert.That(state.Phase(0),Is.EqualTo(HandIntentPhase.Idle));
            state.Receive(3,1,true,.52,true,true);state.Advance(.669);Assert.That(state.Phase(1),Is.EqualTo(HandIntentPhase.GroundPending));
            state.Advance(.67);Assert.That(state.Phase(1),Is.EqualTo(HandIntentPhase.GrabSeek));
        }
        [TestCase(.12)] [TestCase(.15)] [TestCase(.18)]
        public void Threshold_TapAndHoldAreExclusive(double threshold)
        {
            var tap = new GroundHandActionStateMachine(threshold);
            Assert.That(tap.Receive(1, 0, true, 0, true, true), Is.False);
            Assert.That(tap.PunchCount, Is.Zero);
            Assert.That(tap.Receive(2, 0, false, threshold - .001, true, true), Is.True);
            Assert.That(tap.PunchCount, Is.EqualTo(1));
            foreach (var release in new[] { threshold, threshold + .001, threshold + .4 })
            {
                var hold = new GroundHandActionStateMachine(threshold);
                hold.Receive(1, 0, true, 0, true, true); hold.Advance(threshold);
                Assert.That(hold.Phase(0), Is.EqualTo(HandIntentPhase.GrabSeek));
                Assert.That(hold.Receive(2, 0, false, release, true, true), Is.False);
                Assert.That(hold.Phase(0), Is.EqualTo(HandIntentPhase.Idle)); Assert.That(hold.PunchCount, Is.Zero);
            }
        }

        [Test] public void DuplicateAndOldEdges_CannotRepeatOrRestartAttack()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, true); s.Receive(2, 0, false, .05, true, true);
            Assert.That(s.Receive(2, 0, false, .06, true, true), Is.False);
            Assert.That(s.Receive(1, 0, true, .07, true, true), Is.False);
            s.Receive(3, 0, true, .08, true, true); s.Advance(.8);
            Assert.That(s.Receive(4, 0, false, .81, true, true), Is.False, "Held input during recovery is not a new press");
            Assert.That(s.PunchCount, Is.EqualTo(1));
        }

        [Test] public void HandsHaveIndependentActions_AndPerActionTargetDeduplication()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, true); s.Receive(2, 1, true, 0, true, true);
            s.Receive(3, 0, false, .03, true, true); s.Receive(4, 1, false, .04, true, true);
            Assert.That(s.ActionId(0), Is.Not.EqualTo(s.ActionId(1)));
            Assert.That(s.TryConfirmHit(0, 10), Is.False, "Preparation cannot hit");
            s.Advance(.15);
            Assert.That(s.TryConfirmHit(0, 10), Is.True); Assert.That(s.TryConfirmHit(0, 10), Is.False);
            Assert.That(s.TryConfirmHit(1, 10), Is.True); Assert.That(s.TryConfirmHit(0, 11), Is.True);
            s.Advance(.4); Assert.That(s.TryConfirmHit(0, 12), Is.False, "Recovery cannot hit");
        }

        [Test] public void GroundContextStaysGround_AirContextDoesNotCreatePunch()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, true);
            Assert.That(s.Receive(2, 0, false, .05, false, true), Is.True);
            s.Receive(3, 1, true, .05, false, true);
            Assert.That(s.Receive(4, 1, false, .1, true, true), Is.False);
            Assert.That(s.PunchCount, Is.EqualTo(1));
            s.Receive(5, 1, true, .2, false, true); s.Advance(.4);
            Assert.That(s.Phase(1), Is.EqualTo(HandIntentPhase.GrabSeek));
        }

        [Test] public void Suppress_ResetAndAllUp_DoNotEmitActions()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, true); s.Suppress();
            s.Receive(2, 0, false, .05, true, true);
            s.ObserveButtonsForRearm(false, false, true); Assert.That(s.Armed, Is.False);
            s.ObserveButtonsForRearm(true, false, false); Assert.That(s.Armed, Is.False);
            s.ObserveButtonsForRearm(false, false, false); Assert.That(s.Armed, Is.True); Assert.That(s.PunchCount, Is.Zero);
            s.Receive(3, 0, true, .2, true, true); s.Receive(4, 0, false, .25, true, true); s.Advance(.36);
            Assert.That(s.Active(0), Is.True); s.Reset(); Assert.That(s.TryConfirmHit(0, 1), Is.False);
            s.ObserveButtonsForRearm(false, false, false);
            Assert.That(s.Receive(3, 0, true, .37, true, true), Is.False);
            Assert.That(s.PunchCount, Is.Zero);
        }

        [Test] public void OccupiedHand_CannotStart_AndCancellationRemovesHitEligibility()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, false); s.Receive(2, 0, false, .05, true, false);
            Assert.That(s.PunchCount, Is.Zero);
            s.Receive(3, 0, true, .1, true, true); s.Receive(4, 0, false, .15, true, true); s.Advance(.26);
            s.CancelHand(0); Assert.That(s.TryConfirmHit(0, 1), Is.False);
        }
    
        // Press-to-punch (2026-10-28): the playable scenes' punch clock (PhysicalPunchReview: hit window ends at .25 s).
        private static GroundHandActionStateMachine PressPunch() => new GroundHandActionStateMachine(.15, 1.75, .1575, .4375) { PunchOnPress = true };

        [Test] public void PressStartsThePunchAtOnceAndAShortTapKeepsIt()
        {
            var s = PressPunch();
            Assert.That(s.Receive(1, 0, true, 1, true, true), Is.True);
            Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Punch)); Assert.That(s.PunchAge(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(s.Receive(2, 0, false, 1.05, true, true), Is.False);
            s.Advance(1.2); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Punch)); Assert.That(s.PunchCount, Is.EqualTo(1));
        }

        [Test] public void HeldPastTheThresholdThePunchTurnsIntoAGrab()
        {
            var s = PressPunch();
            s.Receive(1, 0, true, 1, true, true); s.Advance(1.149);
            Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Punch));
            s.Advance(1.15); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.GrabSeek));
            Assert.That(s.TryBeginGrab(0, 2), Is.True);
            s.Receive(2, 0, false, 1.5, true, true); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Idle));
            Assert.That(s.PunchCount, Is.EqualTo(1));
        }

        [Test] public void SameHandWaitsItsCooldownAndKeepsOnlyALatePress()
        {
            var s = PressPunch();
            s.Receive(1, 0, true, 1, true, true); s.Receive(2, 0, false, 1.05, true, true);
            Assert.That(s.Receive(3, 0, true, 1.1, true, true), Is.False, "too early: dropped");
            s.Receive(4, 0, false, 1.12, true, true); s.Advance(1.45);
            Assert.That(s.PunchCount, Is.EqualTo(1));
            Assert.That(s.Receive(5, 0, true, 1.5, true, true), Is.True, "ready again after .4 s");
            s.Receive(6, 0, false, 1.52, true, true);
            Assert.That(s.Receive(7, 0, true, 1.8, true, true), Is.False, "within .15 s of ready: kept");
            s.Receive(8, 0, false, 1.82, true, true);
            s.Advance(1.89); Assert.That(s.PunchCount, Is.EqualTo(2));
            s.Advance(1.9); Assert.That(s.PunchCount, Is.EqualTo(3)); Assert.That(s.PunchAge(0), Is.EqualTo(0).Within(1e-9));
        }

        [Test] public void TheOtherHandFollowsAfterTheGap()
        {
            var s = PressPunch();
            s.Receive(1, 0, true, 1, true, true);
            Assert.That(s.Receive(2, 1, true, 1.01, true, true), Is.False, "too early for the other hand: dropped");
            s.Receive(3, 1, false, 1.02, true, true);
            Assert.That(s.Receive(4, 1, true, 1.06, true, true), Is.False, "kept for the gap's end");
            s.Advance(1.19); Assert.That(s.PunchCount, Is.EqualTo(1));
            s.Advance(1.2); Assert.That(s.PunchCount, Is.EqualTo(2)); Assert.That(s.Phase(1), Is.EqualTo(HandIntentPhase.Punch));
        }

        [TestCase(true, 16)] [TestCase(false, 8)]
        public void AnAutoClickerIsHeldToTheCap(bool bothHands, int cap)
        {
            var s = PressPunch(); ulong seq = 0;
            for (var tick = 0; tick < 180; tick++)
            {
                var now = 1 + tick / 60.0; var down = tick % 2 == 0;
                for (var side = 0; side < (bothHands ? 2 : 1); side++) s.Receive(++seq, side, down, now, true, true);
                s.Advance(now);
            }
            Assert.That(s.PunchCount, Is.LessThanOrEqualTo(cap), "3 s: 2.5/s for one hand, 5/s alternating");
            Assert.That(s.PunchCount, Is.GreaterThanOrEqualTo(cap - 2));
        }

        [Test] public void APressDroppedByTheCooldownStillGrabsWhenHeld()
        {
            var s = PressPunch();
            s.Receive(1, 0, true, 1, true, true); s.Receive(2, 0, false, 1.02, true, true);
            s.Receive(3, 0, true, 1.05, true, true);
            s.Advance(1.19); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Punch));
            s.Advance(1.2); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.GrabSeek));
            Assert.That(s.PunchCount, Is.EqualTo(1));
        }

        [Test] public void WherePunchingIsRefusedAPressOnlyGrabs()
        {
            var s = PressPunch();
            Assert.That(s.Receive(1, 0, true, 1, false, true), Is.False);
            Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.AirPending));
            s.Advance(1.15); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.GrabSeek));
            Assert.That(s.PunchCount, Is.EqualTo(0));
        }

        [Test] public void CancelDropsAQueuedPunch()
        {
            var s = PressPunch();
            s.Receive(1, 0, true, 1, true, true); s.Receive(2, 0, false, 1.05, true, true); s.Receive(3, 0, true, 1.3, true, true);
            s.CancelHand(0); s.Receive(4, 0, false, 1.32, true, true); s.Advance(1.5);
            Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Idle)); Assert.That(s.PunchCount, Is.EqualTo(1));
        }
    }
}
