using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ProjectHotfix.LocalStorage;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.LocalStorage.Tests.PlayMode
{
    [Category("Core")]
    public sealed class AtomicLocalFileStorePlayModeTests
    {
        [UnityTest]
        public IEnumerator RecreatedStore_RecoversLastGoodWithoutSceneObjects()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), $"ProjectHotfix-FDN008-Play-{Guid.NewGuid():N}");
            var initialRootCount = SceneManager.GetActiveScene().rootCount;

            try
            {
                var first = new byte[] { 1, 2, 3 };
                var second = new byte[] { 4, 5, 6 };
                var writer = new AtomicLocalFileStore(rootPath, 64);
                Assert.That(writer.Write("presets", "player", first).Succeeded, Is.True);

                var recreated = new AtomicLocalFileStore(rootPath, 64);
                Assert.That(recreated.Read("presets", "player").GetPayloadCopy(), Is.EqualTo(first));
                Assert.That(recreated.Write("presets", "player", second).Succeeded, Is.True);
                yield return null;

                var currentPath = Path.Combine(rootPath, "presets", "entry-player.bin");
                var current = File.ReadAllBytes(currentPath);
                current[16] ^= 0xff;
                File.WriteAllBytes(currentPath, current);

                var recovered = new AtomicLocalFileStore(rootPath, 64).Read("presets", "player");
                Assert.That(recovered.Current.Status, Is.EqualTo(AtomicSlotStatus.Corrupt));
                Assert.That(recovered.LastGood.Status, Is.EqualTo(AtomicSlotStatus.Valid));
                Assert.That(recovered.Origin, Is.EqualTo(AtomicReadOrigin.LastGood));
                Assert.That(recovered.GetPayloadCopy(), Is.EqualTo(first));
                Assert.That(SceneManager.GetActiveScene().rootCount, Is.EqualTo(initialRootCount));
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }
    }
}
