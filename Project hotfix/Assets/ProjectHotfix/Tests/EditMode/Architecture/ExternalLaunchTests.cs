using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class ExternalLaunchTests
    {
        [Test] public void ExternalLaunchConsumesAirJumpAndStartsAFreshLandingImpactInterval()
        {
            var state=new PhysicsJumpStateMachine();state.Step(.01f,true,0,true);state.Step(.2f,false,-8,true);
            state.NotifyExternalLaunch();state.RequestJump();Assert.That(state.Step(.01f,true,3,true),Is.False);Assert.That(state.Grounded,Is.False);Assert.That(state.JumpCount,Is.Zero);
            state.Step(.1f,false,-1,true);state.Step(.1f,true,0,true);Assert.That(state.Grounded,Is.True);Assert.That(state.Capture(0).LandingImpactSpeed,Is.EqualTo(1).Within(.001));Assert.That(state.JumpCount,Is.Zero);
            state.RequestJump();Assert.That(state.Step(.01f,true,0,true),Is.True);Assert.That(state.JumpCount,Is.EqualTo(1));
        }
    }
}
