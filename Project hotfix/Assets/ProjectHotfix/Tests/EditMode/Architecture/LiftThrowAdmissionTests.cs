using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class LiftThrowAdmissionTests
    {
        [Test] public void ReleaseAdmission_RejectsOldEdgesAgainstANewGrab()
        {
            var state = new GroundHandActionStateMachine();
            state.Receive(1, 0, true, 0, true, true); state.Advance(.3); state.TryBeginGrab(0, 10);
            Assert.That(state.CanReleaseGrab(1, 0), Is.False); Assert.That(state.CanReleaseGrab(2, 0), Is.True);
            state.Receive(2, 0, false, .4, true, true);
            state.Receive(3, 0, true, .5, true, true); state.Advance(.8); state.TryBeginGrab(0, 11);
            Assert.That(state.CanReleaseGrab(2, 0), Is.False); Assert.That(state.CanReleaseGrab(4, 0), Is.True);
            state.CancelHand(0); Assert.That(state.CanReleaseGrab(4, 0), Is.False);
            Assert.That(state.PunchCount, Is.Zero);
        }

        [Test] public void MenuAndReset_RearmCannotAuthorizeAThrow()
        {
            var state = new GroundHandActionStateMachine();
            state.Receive(1, 0, true, 0, true, true); state.Advance(.3); state.TryBeginGrab(0, 10);
            state.Suppress(); Assert.That(state.CanReleaseGrab(2, 0), Is.False);
            state.ObserveButtonsForRearm(false, false, false); Assert.That(state.CanReleaseGrab(2, 0), Is.False);
            state.Reset(); Assert.That(state.CanReleaseGrab(2, 0), Is.False);
        }
    }
}
