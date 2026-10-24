using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using ProjectHotfix.Simulation.TestSupport;

namespace ProjectHotfix.SimulationHarness.Tests.Unit
{
    [Category("Core")]
    public sealed class SimulationKernelUnitTests
    {
        [Test]
        public void KernelStep_AdvancesTheReadOnlySnapshot()
        {
            var kernel = new SimulationKernel();

            Assert.That(kernel.CaptureSnapshot().AuthorityCycle, Is.Zero);
            kernel.StepAuthorityCycle();
            Assert.That(kernel.CaptureSnapshot().AuthorityCycle, Is.EqualTo(1UL));
        }

        [Test]
        public void EquivalentCycleSequences_ProduceTheSameFingerprint()
        {
            var singleRun = new RenderlessSimulationHarness();
            var splitRun = new RenderlessSimulationHarness();

            singleRun.RunAuthorityCycles(120);
            splitRun.RunAuthorityCycles(60);
            splitRun.RunAuthorityCycles(60);

            Assert.That(singleRun.StateFingerprint, Is.EqualTo(120UL));
            Assert.That(splitRun.StateFingerprint, Is.EqualTo(singleRun.StateFingerprint));
        }

        [Test]
        public void NegativeCycleCount_IsRejectedWithoutMutation()
        {
            var harness = new RenderlessSimulationHarness();

            Assert.Throws<ArgumentOutOfRangeException>(() => harness.RunAuthorityCycles(-1));
            Assert.That(harness.StateFingerprint, Is.Zero);
        }

        [Test]
        public void DropkickRecovery_ReusesGetUpPresentationWithoutCreatingDownEpisode()
        {
            var recovery = new CharacterRecoveryStateMachine();

            recovery.BeginDropkickRecovery();

            Assert.That(recovery.Kind, Is.EqualTo(CharacterRecoveryKind.DropkickRecovery));
            Assert.That(recovery.PresentationPhase,
                Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble));
            Assert.That(recovery.IsDownEpisode, Is.False);
            Assert.That(recovery.DownCount, Is.Zero);
            Assert.That(recovery.BlocksNewAttacks, Is.True);

            recovery.BeginSharedGetUp(RecoveryOrientation.FaceUp);

            Assert.That(recovery.PresentationPhase,
                Is.EqualTo(RecoveryPresentationPhase.SharedGetUp));
            Assert.That(recovery.GetUpOrientation, Is.EqualTo(RecoveryOrientation.FaceUp));
            Assert.That(recovery.DownCount, Is.Zero);

            recovery.CompleteRecovery();

            Assert.That(recovery.Kind, Is.EqualTo(CharacterRecoveryKind.None));
            Assert.That(recovery.BlocksNewAttacks, Is.False);
        }

        [Test]
        public void DownEpisode_UsesSharedGetUpAndIncrementsDownCountOncePerEpisode()
        {
            var recovery = new CharacterRecoveryStateMachine();

            Assert.That(recovery.TryBeginDownEpisode(), Is.True);
            Assert.That(recovery.TryBeginDownEpisode(), Is.False);
            Assert.That(recovery.Kind, Is.EqualTo(CharacterRecoveryKind.DownEpisode));
            Assert.That(recovery.IsDownEpisode, Is.True);
            Assert.That(recovery.DownCount, Is.EqualTo(1U));

            recovery.BeginSharedGetUp(RecoveryOrientation.FaceDown);

            Assert.That(recovery.PresentationPhase,
                Is.EqualTo(RecoveryPresentationPhase.SharedGetUp));
            Assert.That(recovery.GetUpOrientation, Is.EqualTo(RecoveryOrientation.FaceDown));
            Assert.That(recovery.DownCount, Is.EqualTo(1U));
        }

        [Test]
        public void ActualDownDuringDropkickRecovery_ReplacesTheRecoveryAndCountsAsDown()
        {
            var recovery = new CharacterRecoveryStateMachine();
            recovery.BeginDropkickRecovery();

            Assert.That(recovery.TryBeginDownEpisode(), Is.True);
            Assert.That(recovery.Kind, Is.EqualTo(CharacterRecoveryKind.DownEpisode));
            Assert.That(recovery.IsDownEpisode, Is.True);
            Assert.That(recovery.DownCount, Is.EqualTo(1U));
        }

        [Test]
        public void RoundReset_ClearsRecoveryAndDownCount()
        {
            var recovery = new CharacterRecoveryStateMachine();
            recovery.TryBeginDownEpisode();
            recovery.BeginSharedGetUp(RecoveryOrientation.FaceUp);

            recovery.ResetRound();

            Assert.That(recovery.Kind, Is.EqualTo(CharacterRecoveryKind.None));
            Assert.That(recovery.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.None));
            Assert.That(recovery.GetUpOrientation, Is.Null);
            Assert.That(recovery.DownCount, Is.Zero);
            Assert.That(recovery.BlocksNewAttacks, Is.False);
        }
    }
}
