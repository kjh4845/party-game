using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class GroundPunchWindowTests
    {
        [Test] public void CandidateWindowFollowsFixedTimeAndRejectsRecoveryHits()
        {
            var state=new GroundHandActionStateMachine(.15,1.75,.14,.35);
            state.Receive(1,0,true,0,true,true);state.Receive(2,0,false,.02,true,true);
            state.Advance(.099);Assert.That(state.TryConfirmHit(0,1),Is.False);
            state.Advance(.10);Assert.That(state.TryConfirmHit(0,1),Is.True);Assert.That(state.TryConfirmHit(0,1),Is.False);
            state.Advance(.221);Assert.That(state.TryConfirmHit(0,2),Is.False);
            state.Advance(.42);Assert.That(state.Phase(0),Is.EqualTo(HandIntentPhase.Idle));
        }
        [Test] public void ExistingCallersKeepTheirOriginalWindow()
        {
            var state=new GroundHandActionStateMachine();
            Assert.That(state.ActiveStartSourceSeconds,Is.EqualTo(GroundHandActionStateMachine.ActiveStart));
            Assert.That(state.ActiveEndSourceSeconds,Is.EqualTo(GroundHandActionStateMachine.ActiveEnd));
        }
        [TestCase(-.1,.2),TestCase(.3,.2),TestCase(.1,.7),TestCase(double.NaN,.2)]
        public void InvalidWindowCannotCreateAnAttack(double start,double end)
        {Assert.Throws<ArgumentOutOfRangeException>(()=>new GroundHandActionStateMachine(.15,1.75,start,end));}
    }
}
