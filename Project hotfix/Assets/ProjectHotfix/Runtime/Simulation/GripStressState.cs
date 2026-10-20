using System;

namespace ProjectHotfix.Simulation
{
    // Approved policy: load builds fatigue; elapsed holding time alone does not.
    public sealed class GripStressState
    {
        public float Value { get; private set; }
        public float LoadThreshold = .25f;
        public float AccumulationRate = .8f;
        public float RecoveryRate = .35f;
        public bool Step(float deltaTime, float loadRatio)
        {
            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                || loadRatio < 0 || float.IsNaN(loadRatio) || float.IsInfinity(loadRatio)) throw new ArgumentOutOfRangeException();
            var change = loadRatio > LoadThreshold ? (loadRatio - LoadThreshold) * AccumulationRate : -RecoveryRate;
            Value = Math.Max(0f, Math.Min(1f, Value + change * deltaTime));
            return Value >= 1f;
        }
        public void Reset() { Value = 0f; }
    }
}
