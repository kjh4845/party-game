using System.Collections;
using NUnit.Framework;
using ProjectHotfix.Simulation.TestSupport;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.SimulationHarness.Tests.PlayMode
{
    [Category("Core")]
    public sealed class SimulationHarnessPlayModeTests
    {
        private Scene _scene;
        private Scene _previousActiveScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousActiveScene = SceneManager.GetActiveScene();
            _scene = SceneManager.CreateScene($"FDN007-{System.Guid.NewGuid():N}");
            Assert.That(SceneManager.SetActiveScene(_scene), Is.True);
            Assert.That(_scene.rootCount, Is.Zero);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Assert.That(SceneManager.SetActiveScene(_previousActiveScene), Is.True);
            var unload = SceneManager.UnloadSceneAsync(_scene);
            Assert.That(unload, Is.Not.Null);
            while (!unload.isDone)
            {
                yield return null;
            }

            Assert.That(_scene.isLoaded, Is.False);
        }

        [UnityTest]
        public IEnumerator SameRuntimeKernel_RunsInPlayModeWithoutSceneObjects()
        {
            var harness = new RenderlessSimulationHarness();

            harness.RunAuthorityCycles(30);
            yield return null;

            Assert.That(harness.StateFingerprint, Is.EqualTo(30UL));
            Assert.That(_scene.rootCount, Is.Zero);
        }
    }
}
