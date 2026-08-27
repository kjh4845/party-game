using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectHotfix.LocalStorage;

namespace ProjectHotfix.Architecture.Tests
{
    [Category("Core")]
    public sealed class AtomicLocalFileStoreTests
    {
        private const int MaxPayloadBytes = 64;
        private string _rootPath;
        private AtomicLocalFileStore _store;

        private string CollectionPath => Path.Combine(_rootPath, "presets");

        private string CurrentPath => Path.Combine(CollectionPath, "entry-player.bin");

        private string LastGoodPath => Path.Combine(CollectionPath, "entry-player.last-good");

        private string PendingPath => Path.Combine(CollectionPath, "entry-player.pending");

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"ProjectHotfix-FDN008-{Guid.NewGuid():N}");
            _store = new AtomicLocalFileStore(_rootPath, MaxPayloadBytes);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, true);
            }
        }

        [Test]
        public void FirstSave_NewRepositoryInstanceReadsExactCurrentWithoutBackup()
        {
            var payload = Bytes(1, 2, 3, 4);

            Assert.That(_store.Write("presets", "player", payload).Status, Is.EqualTo(AtomicWriteStatus.Success));
            var recreated = new AtomicLocalFileStore(_rootPath, MaxPayloadBytes);
            var read = recreated.Read("presets", "player");

            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.Current));
            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Missing));
            Assert.That(read.GetPayloadCopy(), Is.EqualTo(payload));
            Assert.That(File.Exists(LastGoodPath), Is.False);
        }

        [Test]
        public void Overwrite_RotatesPriorCurrentIntoLastGood()
        {
            var first = Bytes(1, 1, 1);
            var second = Bytes(2, 2, 2);

            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", second).Succeeded, Is.True);
            var read = _store.Read("presets", "player");

            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.Current));
            Assert.That(read.Current.GetPayloadCopy(), Is.EqualTo(second));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.LastGood.GetPayloadCopy(), Is.EqualTo(first));
        }

        [Test]
        public void ThirdWrite_ReplacesExistingLastGoodWithPriorCurrent()
        {
            Assert.That(_store.Write("presets", "player", Bytes(1)).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(2)).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(3)).Succeeded, Is.True);

            var read = _store.Read("presets", "player");

            Assert.That(read.Current.GetPayloadCopy(), Is.EqualTo(Bytes(3)));
            Assert.That(read.LastGood.GetPayloadCopy(), Is.EqualTo(Bytes(2)));
        }

        [TestCase("truncated", AtomicSlotStatus.Corrupt)]
        [TestCase("hash", AtomicSlotStatus.Corrupt)]
        [TestCase("version", AtomicSlotStatus.Corrupt)]
        [TestCase("length", AtomicSlotStatus.TooLarge)]
        public void InvalidCurrent_SelectsLastGoodWithoutMutatingFiles(
            string corruption,
            AtomicSlotStatus expectedCurrentStatus)
        {
            var first = Bytes(10, 11, 12);
            var second = Bytes(20, 21, 22);
            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", second).Succeeded, Is.True);
            CorruptCurrent(corruption);
            var currentBefore = File.ReadAllBytes(CurrentPath);
            var lastGoodBefore = File.ReadAllBytes(LastGoodPath);

            var read = _store.Read("presets", "player");

            Assert.That(read.Current.Status, Is.EqualTo(expectedCurrentStatus));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.LastGood));
            Assert.That(read.GetPayloadCopy(), Is.EqualTo(first));
            Assert.That(File.ReadAllBytes(CurrentPath), Is.EqualTo(currentBefore));
            Assert.That(File.ReadAllBytes(LastGoodPath), Is.EqualTo(lastGoodBefore));
        }

        [Test]
        public void CorruptCurrentThenSave_PreservesExistingValidLastGood()
        {
            var first = Bytes(1, 2);
            var second = Bytes(3, 4);
            var replacement = Bytes(5, 6);
            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", second).Succeeded, Is.True);
            CorruptCurrent("hash");

            Assert.That(_store.Write("presets", "player", replacement).Succeeded, Is.True);
            var read = _store.Read("presets", "player");

            Assert.That(read.Current.GetPayloadCopy(), Is.EqualTo(replacement));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.LastGood.GetPayloadCopy(), Is.EqualTo(first));
        }

        [Test]
        public void StalePending_IsIgnoredByReadAndRemovedByNextWrite()
        {
            var first = Bytes(1, 2, 3);
            var second = Bytes(4, 5, 6);
            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            File.WriteAllBytes(PendingPath, Bytes(99, 98, 97));

            var beforeWrite = _store.Read("presets", "player");
            Assert.That(beforeWrite.GetPayloadCopy(), Is.EqualTo(first));
            Assert.That(File.Exists(PendingPath), Is.True);

            Assert.That(_store.Write("presets", "player", second).Succeeded, Is.True);
            Assert.That(File.Exists(PendingPath), Is.False);
            Assert.That(_store.Read("presets", "player").GetPayloadCopy(), Is.EqualTo(second));
        }

        [Test]
        public void ReplaceFailure_LeavesCurrentAndLastGoodUnchangedAndCleansPending()
        {
            Assert.That(_store.Write("presets", "player", Bytes(1)).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(2)).Succeeded, Is.True);
            var currentBefore = File.ReadAllBytes(CurrentPath);
            var lastGoodBefore = File.ReadAllBytes(LastGoodPath);
            var failingStore = new AtomicLocalFileStore(
                _rootPath,
                MaxPayloadBytes,
                new ReplaceFailingCommitter());

            var result = failingStore.Write("presets", "player", Bytes(3));

            Assert.That(result.Status, Is.EqualTo(AtomicWriteStatus.IoFailure));
            Assert.That(File.ReadAllBytes(CurrentPath), Is.EqualTo(currentBefore));
            Assert.That(File.ReadAllBytes(LastGoodPath), Is.EqualTo(lastGoodBefore));
            Assert.That(File.Exists(PendingPath), Is.False);
        }

        [Test]
        public void InvalidAddressMatrix_IsRejectedWithoutWritingOutsideRoot()
        {
            var invalidSegments = new[]
            {
                null,
                string.Empty,
                ".",
                "..",
                "Upper",
                "white space",
                "unicode-한글",
                "a/b",
                "a\\b",
                "a:b",
                "-leading",
                "_leading",
                "trailing.",
                new string('a', 65),
            };
            var outsidePath = _rootPath + "-outside-sentinel";
            File.WriteAllBytes(outsidePath, Bytes(7, 7, 7));

            try
            {
                foreach (var invalid in invalidSegments)
                {
                    Assert.That(
                        _store.Write(invalid, "player", Bytes(1)).Status,
                        Is.EqualTo(AtomicWriteStatus.Rejected),
                        invalid ?? "<null collection>");
                    Assert.That(
                        _store.Write("presets", invalid, Bytes(1)).Status,
                        Is.EqualTo(AtomicWriteStatus.Rejected),
                        invalid ?? "<null key>");
                    Assert.That(
                        _store.Read("presets", invalid).Current.Status,
                        Is.EqualTo(AtomicSlotStatus.Rejected),
                        invalid ?? "<null read key>");
                }

                Assert.That(File.ReadAllBytes(outsidePath), Is.EqualTo(Bytes(7, 7, 7)));
                Assert.That(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories), Is.Empty);
            }
            finally
            {
                File.Delete(outsidePath);
            }
        }

        [Test]
        public void PayloadLimit_AcceptsExactLimitAndRejectsLargerWithoutChangingCurrent()
        {
            var exact = Enumerable.Repeat((byte)8, MaxPayloadBytes).ToArray();
            var oversized = Enumerable.Repeat((byte)9, MaxPayloadBytes + 1).ToArray();
            Assert.That(_store.Write("presets", "player", exact).Succeeded, Is.True);
            var currentBefore = File.ReadAllBytes(CurrentPath);

            var rejected = _store.Write("presets", "player", oversized);

            Assert.That(rejected.Status, Is.EqualTo(AtomicWriteStatus.PayloadTooLarge));
            Assert.That(File.ReadAllBytes(CurrentPath), Is.EqualTo(currentBefore));
            Assert.That(_store.Read("presets", "player").GetPayloadCopy(), Is.EqualTo(exact));
        }

        [Test]
        public void OversizedDeclaredLength_IsRejectedBeforeAllocationAndFallsBack()
        {
            var first = Bytes(1, 2, 3);
            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(4, 5, 6)).Succeeded, Is.True);
            var current = File.ReadAllBytes(CurrentPath);
            BitConverter.GetBytes(MaxPayloadBytes + 1).CopyTo(current, 12);
            File.WriteAllBytes(CurrentPath, current);

            var read = _store.Read("presets", "player");

            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.TooLarge));
            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.LastGood));
            Assert.That(read.GetPayloadCopy(), Is.EqualTo(first));
        }

        [Test]
        public void PhysicalFileOverLimit_IsRejectedBeforePayloadReadAndFallsBack()
        {
            var first = Bytes(1, 2, 3);
            var second = Bytes(4, 5, 6);
            Assert.That(_store.Write("presets", "player", first).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", second).Succeeded, Is.True);
            var envelopeLength = File.ReadAllBytes(CurrentPath).Length;
            var headerLength = envelopeLength - second.Length;
            using (var stream = new FileStream(CurrentPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(headerLength + MaxPayloadBytes + 1L);
            }

            var read = _store.Read("presets", "player");

            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.TooLarge));
            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.LastGood));
            Assert.That(read.GetPayloadCopy(), Is.EqualTo(first));
        }

        [Test]
        public void BothSlotsInvalid_ReturnsNoPayloadAndDoesNotRewriteEitherFile()
        {
            Assert.That(_store.Write("presets", "player", Bytes(1)).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(2)).Succeeded, Is.True);
            File.WriteAllBytes(CurrentPath, Bytes(9));
            File.WriteAllBytes(LastGoodPath, Bytes(8));
            var currentBefore = File.ReadAllBytes(CurrentPath);
            var lastGoodBefore = File.ReadAllBytes(LastGoodPath);

            var read = _store.Read("presets", "player");

            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.None));
            Assert.That(read.HasPayload, Is.False);
            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.Corrupt));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Corrupt));
            Assert.That(File.ReadAllBytes(CurrentPath), Is.EqualTo(currentBefore));
            Assert.That(File.ReadAllBytes(LastGoodPath), Is.EqualTo(lastGoodBefore));
        }

        [Test]
        public void ConcurrentWrites_OnOneStoreNeverExposeATornEnvelope()
        {
            var payloads = Enumerable.Range(1, 12)
                .Select(value => Enumerable.Repeat((byte)value, 32).ToArray())
                .ToArray();
            var tasks = payloads
                .Select(payload => Task.Run(() => _store.Write("presets", "player", payload)))
                .ToArray();

            Task.WaitAll(tasks);
            var read = _store.Read("presets", "player");

            Assert.That(tasks.Select(task => task.Result.Status), Is.All.EqualTo(AtomicWriteStatus.Success));
            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(payloads.Any(payload => payload.SequenceEqual(read.Current.GetPayloadCopy())), Is.True);
            Assert.That(payloads.Any(payload => payload.SequenceEqual(read.LastGood.GetPayloadCopy())), Is.True);
        }

        [Test]
        public void ConcurrentRepositoryInstances_SerializeTheSameAddress()
        {
            Assert.That(_store.Write("presets", "player", Bytes(1)).Succeeded, Is.True);
            var blockingCommitter = new BlockingReplaceCommitter();
            var firstWriter = new AtomicLocalFileStore(_rootPath, MaxPayloadBytes, blockingCommitter);
            var secondWriter = new AtomicLocalFileStore(_rootPath, MaxPayloadBytes);
            var firstWrite = Task.Run(() => firstWriter.Write("presets", "player", Bytes(2)));
            Assert.That(blockingCommitter.WaitUntilReplace(TimeSpan.FromSeconds(5)), Is.True);
            var secondStarted = new ManualResetEventSlim(false);
            var secondWrite = Task.Run(() =>
            {
                secondStarted.Set();
                return secondWriter.Write("presets", "player", Bytes(3));
            });
            Assert.That(secondStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);

            try
            {
                Assert.That(secondWrite.Wait(200), Is.False, "A second repository bypassed the address lock.");
            }
            finally
            {
                blockingCommitter.Release();
            }

            Assert.That(Task.WaitAll(new Task[] { firstWrite, secondWrite }, 5000), Is.True);
            Assert.That(firstWrite.Result.Status, Is.EqualTo(AtomicWriteStatus.Success));
            Assert.That(secondWrite.Result.Status, Is.EqualTo(AtomicWriteStatus.Success));
            var read = _store.Read("presets", "player");
            Assert.That(read.Current.GetPayloadCopy(), Is.EqualTo(Bytes(3)));
            Assert.That(read.LastGood.GetPayloadCopy(), Is.EqualTo(Bytes(2)));
        }

        [Test]
        public void CallerValidator_CanRejectCurrentAndSelectCompatibleLastGood()
        {
            var compatible = Bytes(1, 10);
            Func<byte[], bool> validator = payload => payload[0] == 1;
            Assert.That(_store.Write("presets", "player", compatible).Succeeded, Is.True);
            Assert.That(_store.Write("presets", "player", Bytes(2, 20)).Succeeded, Is.True);

            var read = _store.Read("presets", "player", validator);

            Assert.That(read.Current.Status, Is.EqualTo(AtomicSlotStatus.Corrupt));
            Assert.That(read.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
            Assert.That(read.Origin, Is.EqualTo(AtomicReadOrigin.LastGood));
            Assert.That(read.GetPayloadCopy(), Is.EqualTo(compatible));

            Assert.That(
                _store.Write("presets", "player", Bytes(1, 30), validator).Status,
                Is.EqualTo(AtomicWriteStatus.Success));
            var afterSave = _store.Read("presets", "player", validator);
            Assert.That(afterSave.Current.GetPayloadCopy(), Is.EqualTo(Bytes(1, 30)));
            Assert.That(afterSave.LastGood.GetPayloadCopy(), Is.EqualTo(compatible));
        }

        [Test]
        public void NonRegularTarget_IsRejectedWithoutReplacement()
        {
            Directory.CreateDirectory(CollectionPath);
            Directory.CreateDirectory(CurrentPath);

            var result = _store.Write("presets", "player", Bytes(1));

            Assert.That(result.Status, Is.EqualTo(AtomicWriteStatus.Rejected));
            Assert.That(Directory.Exists(CurrentPath), Is.True);
        }

        private void CorruptCurrent(string corruption)
        {
            var bytes = File.ReadAllBytes(CurrentPath);
            switch (corruption)
            {
                case "truncated":
                    File.WriteAllBytes(CurrentPath, bytes.Take(10).ToArray());
                    break;
                case "hash":
                    bytes[16] ^= 0xff;
                    File.WriteAllBytes(CurrentPath, bytes);
                    break;
                case "version":
                    BitConverter.GetBytes(2).CopyTo(bytes, 8);
                    File.WriteAllBytes(CurrentPath, bytes);
                    break;
                case "length":
                    BitConverter.GetBytes(MaxPayloadBytes + 1).CopyTo(bytes, 12);
                    File.WriteAllBytes(CurrentPath, bytes);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }
        }

        private static byte[] Bytes(params byte[] values)
        {
            return values;
        }

        private sealed class ReplaceFailingCommitter : IAtomicCommitter
        {
            public void Move(string pendingPath, string currentPath)
            {
                File.Move(pendingPath, currentPath);
            }

            public void Replace(string pendingPath, string currentPath, string lastGoodPath)
            {
                throw new IOException("Injected atomic commit failure.");
            }
        }

        private sealed class BlockingReplaceCommitter : IAtomicCommitter
        {
            private readonly ManualResetEventSlim _replaceEntered = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);

            public void Move(string pendingPath, string currentPath)
            {
                File.Move(pendingPath, currentPath);
            }

            public void Replace(string pendingPath, string currentPath, string lastGoodPath)
            {
                _replaceEntered.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Timed out waiting to release the injected commit.");
                }

                File.Replace(pendingPath, currentPath, lastGoodPath, true);
            }

            public bool WaitUntilReplace(TimeSpan timeout)
            {
                return _replaceEntered.Wait(timeout);
            }

            public void Release()
            {
                _release.Set();
            }
        }
    }
}
