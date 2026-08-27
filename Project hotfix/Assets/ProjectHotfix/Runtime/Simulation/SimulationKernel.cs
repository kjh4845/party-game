using ProjectHotfix.Contracts;

namespace ProjectHotfix.Simulation
{
    public sealed class SimulationKernel
    {
        private ulong _authorityCycle;

        public SimulationSnapshot CaptureSnapshot()
        {
            return new SimulationSnapshot(_authorityCycle);
        }

        public void StepAuthorityCycle()
        {
            _authorityCycle = checked(_authorityCycle + 1UL);
        }
    }
}
