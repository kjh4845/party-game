using System;
using ProjectHotfix.Contracts;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Simulation.TestSupport
{
    public sealed class RenderlessSimulationHarness
    {
        private readonly SimulationKernel _simulation = new SimulationKernel();

        public SimulationSnapshot Snapshot => _simulation.CaptureSnapshot();

        public ulong StateFingerprint => Snapshot.AuthorityCycle;

        public void RunAuthorityCycles(int cycleCount)
        {
            if (cycleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycleCount));
            }

            for (var cycle = 0; cycle < cycleCount; cycle++)
            {
                _simulation.StepAuthorityCycle();
            }
        }
    }
}
