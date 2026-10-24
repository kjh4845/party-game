using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class GripStressTests
    {
        [Test] public void IdleHolding_DoesNotAccumulateTimeFatigue()
        {
            var stress = new GripStressState();
            for (var i = 0; i < 36000; i++) Assert.That(stress.Step(1f / 60, 0), Is.False);
            Assert.That(stress.Value, Is.Zero);
        }
        [Test] public void SustainedLoad_Breaks_AndLowLoadRecovers()
        {
            var stress = new GripStressState();
            for (var i = 0; i < 60; i++) stress.Step(1f / 60, .8f);
            Assert.That(stress.Value, Is.GreaterThan(.4f));
            for (var i = 0; i < 120; i++) stress.Step(1f / 60, .1f);
            Assert.That(stress.Value, Is.Zero);
            var broken = false;
            for (var i = 0; i < 180; i++) broken |= stress.Step(1f / 60, .8f);
            Assert.That(broken, Is.True); stress.Reset(); Assert.That(stress.Value, Is.Zero);
        }
        [Test] public void GrabReleaseAndBreak_DoNotCreatePunchOrAutoRegrab()
        {
            var s = new GroundHandActionStateMachine();
            s.Receive(1, 0, true, 0, true, true); s.Advance(.3);
            Assert.That(s.TryBeginGrab(0, 10), Is.True); Assert.That(s.TryBeginGrab(0, 10), Is.False);
            var relation = s.GrabRelationId(0); Assert.That(s.Phase(0), Is.EqualTo(HandIntentPhase.Grabbing));
            Assert.That(s.Receive(2, 0, false, .4, true, true), Is.False);
            Assert.That(s.GrabTargetId(0), Is.Zero); Assert.That(s.PunchCount, Is.Zero);
            s.Receive(3, 0, true, .5, true, true); s.Advance(.8); s.TryBeginGrab(0, 11);
            Assert.That(s.GrabRelationId(0), Is.GreaterThan(relation));
            s.CancelHand(0); s.Advance(2);
            Assert.That(s.TryBeginGrab(0, 11), Is.False); Assert.That(s.PunchCount, Is.Zero);
        }
    }
}
