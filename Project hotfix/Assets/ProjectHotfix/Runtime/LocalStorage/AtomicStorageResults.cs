using System;

namespace ProjectHotfix.LocalStorage
{
    public enum AtomicSlotStatus
    {
        Missing,
        Valid,
        Corrupt,
        TooLarge,
        Rejected,
        IoFailure,
    }

    public enum AtomicReadOrigin
    {
        None,
        Current,
        LastGood,
    }

    public enum AtomicWriteStatus
    {
        Success,
        Rejected,
        PayloadTooLarge,
        IoFailure,
    }

    public sealed class AtomicSlotResult
    {
        private readonly byte[] _payload;

        internal AtomicSlotResult(AtomicSlotStatus status, byte[] payload = null)
        {
            Status = status;
            _payload = payload;
        }

        public AtomicSlotStatus Status { get; }

        public bool HasPayload => Status == AtomicSlotStatus.Valid && _payload != null;

        public byte[] GetPayloadCopy()
        {
            if (!HasPayload)
            {
                return Array.Empty<byte>();
            }

            return (byte[])_payload.Clone();
        }

        internal byte[] PayloadForSelection => _payload;
    }

    public sealed class AtomicReadResult
    {
        internal AtomicReadResult(
            AtomicSlotResult current,
            AtomicSlotResult lastGood,
            AtomicReadOrigin origin)
        {
            Current = current ?? throw new ArgumentNullException(nameof(current));
            LastGood = lastGood ?? throw new ArgumentNullException(nameof(lastGood));
            Origin = origin;
        }

        public AtomicSlotResult Current { get; }

        public AtomicSlotResult LastGood { get; }

        public AtomicReadOrigin Origin { get; }

        public bool HasPayload => Origin != AtomicReadOrigin.None;

        public byte[] GetPayloadCopy()
        {
            switch (Origin)
            {
                case AtomicReadOrigin.Current:
                    return Current.GetPayloadCopy();
                case AtomicReadOrigin.LastGood:
                    return LastGood.GetPayloadCopy();
                default:
                    return Array.Empty<byte>();
            }
        }
    }

    public readonly struct AtomicWriteResult
    {
        internal AtomicWriteResult(AtomicWriteStatus status)
        {
            Status = status;
        }

        public AtomicWriteStatus Status { get; }

        public bool Succeeded => Status == AtomicWriteStatus.Success;
    }
}
