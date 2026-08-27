using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace ProjectHotfix.LocalStorage
{
    public sealed class AtomicLocalFileStore
    {
        private const int StorageFormatVersion = 1;
        private const int HashLength = 32;
        private const int HeaderLength = 8 + sizeof(int) + sizeof(int) + HashLength;
        private const int AddressLockCount = 64;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHLS0001");
        private static readonly object[] AddressLocks = CreateAddressLocks();

        private readonly string _rootPath;
        private readonly string _rootPrefix;
        private readonly int _maxPayloadBytes;
        private readonly IAtomicCommitter _committer;

        public AtomicLocalFileStore(string absoluteRootPath, int maxPayloadBytes)
            : this(absoluteRootPath, maxPayloadBytes, new SystemAtomicCommitter())
        {
        }

        internal AtomicLocalFileStore(
            string absoluteRootPath,
            int maxPayloadBytes,
            IAtomicCommitter committer)
        {
            if (string.IsNullOrWhiteSpace(absoluteRootPath))
            {
                throw new ArgumentException("A storage root is required.", nameof(absoluteRootPath));
            }

            if (!Path.IsPathRooted(absoluteRootPath))
            {
                throw new ArgumentException("The storage root must be absolute.", nameof(absoluteRootPath));
            }

            if (maxPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            }

            _committer = committer ?? throw new ArgumentNullException(nameof(committer));
            _maxPayloadBytes = maxPayloadBytes;
            _rootPath = TrimTrailingSeparators(Path.GetFullPath(absoluteRootPath));

            var volumeRoot = TrimTrailingSeparators(Path.GetPathRoot(_rootPath));
            if (string.Equals(_rootPath, volumeRoot, StringComparison.Ordinal))
            {
                throw new ArgumentException("A volume root cannot be used as the storage root.", nameof(absoluteRootPath));
            }

            Directory.CreateDirectory(_rootPath);
            if (!IsSafeDirectory(_rootPath))
            {
                throw new ArgumentException("The storage root must be a regular directory.", nameof(absoluteRootPath));
            }

            _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
        }

        public AtomicReadResult Read(string collection, string key)
        {
            return Read(collection, key, null);
        }

        public AtomicReadResult Read(string collection, string key, Func<byte[], bool> payloadValidator)
        {
            if (!TryResolvePaths(collection, key, out var paths))
            {
                return CreateUniformReadResult(AtomicSlotStatus.Rejected);
            }

            lock (GetAddressLock(paths))
            {
                var directoryStatus = PrepareCollectionDirectory(paths.Collection, false);
                if (directoryStatus == DirectoryStatus.Missing)
                {
                    return CreateUniformReadResult(AtomicSlotStatus.Missing);
                }

                if (directoryStatus == DirectoryStatus.Rejected)
                {
                    return CreateUniformReadResult(AtomicSlotStatus.Rejected);
                }

                if (directoryStatus == DirectoryStatus.IoFailure)
                {
                    return CreateUniformReadResult(AtomicSlotStatus.IoFailure);
                }

                var current = InspectSlot(paths.Current, payloadValidator);
                var lastGood = InspectSlot(paths.LastGood, payloadValidator);
                var origin = current.Status == AtomicSlotStatus.Valid
                    ? AtomicReadOrigin.Current
                    : lastGood.Status == AtomicSlotStatus.Valid
                        ? AtomicReadOrigin.LastGood
                        : AtomicReadOrigin.None;

                return new AtomicReadResult(current, lastGood, origin);
            }
        }

        public AtomicWriteResult Write(string collection, string key, byte[] payload)
        {
            return Write(collection, key, payload, null);
        }

        public AtomicWriteResult Write(
            string collection,
            string key,
            byte[] payload,
            Func<byte[], bool> payloadValidator)
        {
            if (payload == null || !TryResolvePaths(collection, key, out var paths))
            {
                return new AtomicWriteResult(AtomicWriteStatus.Rejected);
            }

            if (payload.Length > _maxPayloadBytes)
            {
                return new AtomicWriteResult(AtomicWriteStatus.PayloadTooLarge);
            }

            var payloadSnapshot = (byte[])payload.Clone();
            if (payloadValidator != null && !IsPayloadAccepted(payloadSnapshot, payloadValidator))
            {
                return new AtomicWriteResult(AtomicWriteStatus.Rejected);
            }

            lock (GetAddressLock(paths))
            {
                var directoryStatus = PrepareCollectionDirectory(paths.Collection, true);
                if (directoryStatus == DirectoryStatus.Rejected)
                {
                    return new AtomicWriteResult(AtomicWriteStatus.Rejected);
                }

                if (directoryStatus != DirectoryStatus.Ready)
                {
                    return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                }

                try
                {
                    if (!IsSafeTarget(paths.Current)
                        || !IsSafeTarget(paths.LastGood)
                        || !IsSafeTarget(paths.Pending))
                    {
                        return new AtomicWriteResult(AtomicWriteStatus.Rejected);
                    }
                }
                catch (Exception exception) when (IsStorageException(exception))
                {
                    return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                }

                var current = InspectSlot(paths.Current, payloadValidator);
                if (current.Status == AtomicSlotStatus.Rejected)
                {
                    return new AtomicWriteResult(AtomicWriteStatus.Rejected);
                }

                if (current.Status == AtomicSlotStatus.IoFailure)
                {
                    return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                }

                try
                {
                    DeleteStalePending(paths.Pending);
                    WritePending(paths.Pending, payloadSnapshot);

                    var pending = InspectSlot(paths.Pending, null);
                    if (pending.Status != AtomicSlotStatus.Valid
                        || !ByteArraysEqual(payloadSnapshot, pending.PayloadForSelection))
                    {
                        DeletePendingIfRegular(paths.Pending);
                        return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                    }

                    switch (current.Status)
                    {
                        case AtomicSlotStatus.Missing:
                            _committer.Move(paths.Pending, paths.Current);
                            break;
                        case AtomicSlotStatus.Valid:
                            _committer.Replace(paths.Pending, paths.Current, paths.LastGood);
                            break;
                        case AtomicSlotStatus.Corrupt:
                        case AtomicSlotStatus.TooLarge:
                            _committer.Replace(paths.Pending, paths.Current, null);
                            break;
                        default:
                            DeletePendingIfRegular(paths.Pending);
                            return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                    }

                    return new AtomicWriteResult(AtomicWriteStatus.Success);
                }
                catch (Exception exception) when (IsStorageException(exception))
                {
                    DeletePendingIfRegular(paths.Pending);
                    return new AtomicWriteResult(AtomicWriteStatus.IoFailure);
                }
            }
        }

        private static AtomicReadResult CreateUniformReadResult(AtomicSlotStatus status)
        {
            return new AtomicReadResult(
                new AtomicSlotResult(status),
                new AtomicSlotResult(status),
                AtomicReadOrigin.None);
        }

        private DirectoryStatus PrepareCollectionDirectory(string collectionPath, bool create)
        {
            try
            {
                if (!IsSafeDirectory(_rootPath))
                {
                    return DirectoryStatus.Rejected;
                }

                var kind = GetEntryKind(collectionPath);
                if (kind == EntryKind.Missing)
                {
                    if (!create)
                    {
                        return DirectoryStatus.Missing;
                    }

                    Directory.CreateDirectory(collectionPath);
                    kind = GetEntryKind(collectionPath);
                }

                return kind == EntryKind.RegularDirectory
                    ? DirectoryStatus.Ready
                    : DirectoryStatus.Rejected;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return DirectoryStatus.IoFailure;
            }
        }

        private AtomicSlotResult InspectSlot(string path, Func<byte[], bool> payloadValidator)
        {
            try
            {
                var kind = GetEntryKind(path);
                if (kind == EntryKind.Missing)
                {
                    return new AtomicSlotResult(AtomicSlotStatus.Missing);
                }

                if (kind != EntryKind.RegularFile)
                {
                    return new AtomicSlotResult(AtomicSlotStatus.Rejected);
                }

                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan))
                {
                    if (stream.Length < HeaderLength)
                    {
                        return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                    }

                    if (stream.Length > (long)HeaderLength + _maxPayloadBytes)
                    {
                        return new AtomicSlotResult(AtomicSlotStatus.TooLarge);
                    }

                    using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                    {
                        var magic = reader.ReadBytes(Magic.Length);
                        if (!ByteArraysEqual(Magic, magic))
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        var formatVersion = reader.ReadInt32();
                        var declaredLength = reader.ReadInt32();
                        if (formatVersion != StorageFormatVersion || declaredLength < 0)
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        if (declaredLength > _maxPayloadBytes)
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.TooLarge);
                        }

                        if (stream.Length != (long)HeaderLength + declaredLength)
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        var expectedHash = reader.ReadBytes(HashLength);
                        var payload = reader.ReadBytes(declaredLength);
                        if (expectedHash.Length != HashLength || payload.Length != declaredLength)
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        var actualHash = ComputeHash(payload);
                        if (!ByteArraysEqual(expectedHash, actualHash))
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        if (payloadValidator != null
                            && !IsPayloadAccepted(payload, payloadValidator))
                        {
                            return new AtomicSlotResult(AtomicSlotStatus.Corrupt);
                        }

                        return new AtomicSlotResult(AtomicSlotStatus.Valid, payload);
                    }
                }
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return new AtomicSlotResult(AtomicSlotStatus.IoFailure);
            }
        }

        private void WritePending(string path, byte[] payload)
        {
            var hash = ComputeHash(payload);
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(StorageFormatVersion);
                writer.Write(payload.Length);
                writer.Write(hash);
                writer.Write(payload);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private void DeleteStalePending(string path)
        {
            var kind = GetEntryKind(path);
            if (kind == EntryKind.Missing)
            {
                return;
            }

            if (kind != EntryKind.RegularFile)
            {
                throw new IOException("The pending entry is not a regular file.");
            }

            File.Delete(path);
        }

        private static void DeletePendingIfRegular(string path)
        {
            try
            {
                if (GetEntryKind(path) == EntryKind.RegularFile)
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                // Preserve the original typed failure. A later write revalidates this path.
            }
        }

        private static bool IsSafeDirectory(string path)
        {
            return GetEntryKind(path) == EntryKind.RegularDirectory;
        }

        private static bool IsSafeTarget(string path)
        {
            var kind = GetEntryKind(path);
            return kind == EntryKind.Missing || kind == EntryKind.RegularFile;
        }

        private static EntryKind GetEntryKind(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return EntryKind.ReparsePoint;
                }

                return (attributes & FileAttributes.Directory) != 0
                    ? EntryKind.RegularDirectory
                    : EntryKind.RegularFile;
            }
            catch (FileNotFoundException)
            {
                return EntryKind.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return EntryKind.Missing;
            }
        }

        private bool TryResolvePaths(string collection, string key, out StoragePaths paths)
        {
            paths = default;
            if (!IsSafeSegment(collection) || !IsSafeSegment(key))
            {
                return false;
            }

            try
            {
                var collectionPath = Path.GetFullPath(Path.Combine(_rootPath, collection));
                var currentPath = Path.GetFullPath(Path.Combine(collectionPath, $"entry-{key}.bin"));
                var lastGoodPath = Path.GetFullPath(Path.Combine(collectionPath, $"entry-{key}.last-good"));
                var pendingPath = Path.GetFullPath(Path.Combine(collectionPath, $"entry-{key}.pending"));

                if (!IsContained(collectionPath)
                    || !IsContained(currentPath)
                    || !IsContained(lastGoodPath)
                    || !IsContained(pendingPath))
                {
                    return false;
                }

                paths = new StoragePaths(collectionPath, currentPath, lastGoodPath, pendingPath);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException)
            {
                return false;
            }
        }

        private bool IsContained(string path)
        {
            return path.StartsWith(_rootPrefix, StringComparison.Ordinal);
        }

        private static object GetAddressLock(StoragePaths paths)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(paths.Current);
            return AddressLocks[hash & (AddressLockCount - 1)];
        }

        private static object[] CreateAddressLocks()
        {
            var locks = new object[AddressLockCount];
            for (var index = 0; index < locks.Length; index++)
            {
                locks[index] = new object();
            }

            return locks;
        }

        private static bool IsPayloadAccepted(byte[] payload, Func<byte[], bool> payloadValidator)
        {
            try
            {
                return payloadValidator((byte[])payload.Clone());
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAsciiAlphaNumeric(value[0]))
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!IsLowerAsciiAlphaNumeric(character) && character != '_' && character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLowerAsciiAlphaNumeric(char character)
        {
            return (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9');
        }

        private static byte[] ComputeHash(byte[] payload)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(payload);
            }
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsStorageException(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is NotSupportedException;
        }

        private enum DirectoryStatus
        {
            Ready,
            Missing,
            Rejected,
            IoFailure,
        }

        private enum EntryKind
        {
            Missing,
            RegularFile,
            RegularDirectory,
            ReparsePoint,
        }

        private readonly struct StoragePaths
        {
            public StoragePaths(string collection, string current, string lastGood, string pending)
            {
                Collection = collection;
                Current = current;
                LastGood = lastGood;
                Pending = pending;
            }

            public string Collection { get; }

            public string Current { get; }

            public string LastGood { get; }

            public string Pending { get; }
        }
    }
}
