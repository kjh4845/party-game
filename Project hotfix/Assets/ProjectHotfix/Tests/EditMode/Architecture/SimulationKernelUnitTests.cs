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
    }
}
