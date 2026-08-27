namespace ProjectHotfix.Contracts
{
    public readonly struct SimulationSnapshot
    {
        public SimulationSnapshot(ulong authorityCycle)
        {
            AuthorityCycle = authorityCycle;
        }

        public ulong AuthorityCycle { get; }
    }
}
