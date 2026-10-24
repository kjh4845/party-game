using System;
using System.Collections.Generic;

namespace ProjectHotfix.Simulation
{
    public enum PunchHitRegion { Body, Head }
    public readonly struct PunchDownProfile
    {
        public readonly int RequiredHits;
        public readonly int BodyPoints, HeadPoints, DecreasePerDown, MinimumPoints;
        public PunchDownProfile(int requiredHits) : this(requiredHits, 1, 1, 0, requiredHits) { }
        public PunchDownProfile(int requiredPoints, int bodyPoints, int headPoints, int decreasePerDown, int minimumPoints)
        {
            if (requiredPoints < 1 || bodyPoints < 1 || headPoints < 1 || decreasePerDown < 0 || minimumPoints < 1 || minimumPoints > requiredPoints)
                throw new ArgumentOutOfRangeException(nameof(requiredPoints));
            RequiredHits = requiredPoints; BodyPoints = bodyPoints; HeadPoints = headPoints;
            DecreasePerDown = decreasePerDown; MinimumPoints = minimumPoints;
        }
        public int RequiredPoints(uint downCount) => (int)Math.Max(MinimumPoints, (long)RequiredHits - (long)DecreasePerDown * downCount);
    }

    public enum PunchDownResult { Rejected, Accumulated, ThresholdReached, DownImpulseOnly }

    // No elapsed-time decay. Region points and episode thresholds come from an
    // explicit profile; repeated Down duration remains recovery-owned.
    public sealed class PunchDownAccumulation
    {
        private sealed class Receipts { public ulong Latest, Bits; }
        private readonly Dictionary<int, Receipts> _receipts = new Dictionary<int, Receipts>();
        public PunchDownProfile Profile { get; }
        public uint Generation { get; private set; }
        public int Hits { get; private set; }
        public int Points { get; private set; }
        public PunchDownAccumulation(PunchDownProfile profile, uint generation)
        {
            Profile = new PunchDownProfile(profile.RequiredHits, profile.BodyPoints, profile.HeadPoints, profile.DecreasePerDown, profile.MinimumPoints);
            Generation = generation;
        }
        public PunchDownResult Receive(uint generation, int attackerId, ulong actionId, bool down, PunchHitRegion region = PunchHitRegion.Body, uint downCount = 0)
        {
            if (generation != Generation || attackerId == 0 || actionId == 0 || (region != PunchHitRegion.Body && region != PunchHitRegion.Head)) return PunchDownResult.Rejected;
            if (!_receipts.TryGetValue(attackerId, out var r))
            {
                if (_receipts.Count >= 4) return PunchDownResult.Rejected;
                r = new Receipts(); _receipts.Add(attackerId, r);
            }
            // Per-attacker 64-action window permits overlapping left/right hits
            // arriving in reverse action order, with bounded duplicate storage.
            if (actionId > r.Latest)
            {
                var shift = actionId - r.Latest; r.Bits = shift >= 64 ? 0 : r.Bits << (int)shift; r.Latest = actionId;
            }
            var age = r.Latest - actionId;
            if (age >= 64 || (r.Bits & (1UL << (int)age)) != 0) return PunchDownResult.Rejected;
            r.Bits |= 1UL << (int)age;
            if (down) return PunchDownResult.DownImpulseOnly;
            var required = Profile.RequiredPoints(downCount);
            Hits = Math.Min(required, Hits + 1);
            Points = (int)Math.Min(required, (long)Points + (region == PunchHitRegion.Head ? Profile.HeadPoints : Profile.BodyPoints));
            return Points >= required ? PunchDownResult.ThresholdReached : PunchDownResult.Accumulated;
        }
        public void ConsumeDown() { Hits = Points = 0; }
        public void Reset(uint generation) { Generation = generation; Hits = Points = 0; _receipts.Clear(); }
    }
}
