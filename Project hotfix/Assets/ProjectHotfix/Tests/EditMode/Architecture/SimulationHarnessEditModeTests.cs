using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using ProjectHotfix.Simulation.TestSupport;
using UnityEditor.Compilation;

namespace ProjectHotfix.SimulationHarness.Tests.EditMode
{
    [Category("Core")]
    public sealed class SimulationHarnessEditModeTests
    {
        [Test]
        public void Harness_ExecutesTheRuntimeOwnedSimulationKernel()
        {
            var harness = new RenderlessSimulationHarness();

            harness.RunAuthorityCycles(30);

            Assert.That(typeof(SimulationKernel).Assembly.GetName().Name, Is.EqualTo("ProjectHotfix.Simulation"));
            Assert.That(typeof(RenderlessSimulationHarness).Assembly.GetName().Name,
                Is.EqualTo("ProjectHotfix.Simulation.TestSupport"));
            Assert.That(harness.Snapshot.AuthorityCycle, Is.EqualTo(30UL));
        }

        [Test]
        public void SimulationAndHarness_HaveNoPresentationRenderingOrUiDependency()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(assembly => assembly.name == "ProjectHotfix.Simulation"
                    || assembly.name == "ProjectHotfix.Simulation.TestSupport")
                .ToDictionary(assembly => assembly.name);
            var simulationProjectReferences = ProjectReferences(assemblies["ProjectHotfix.Simulation"]);
            var harnessProjectReferences = ProjectReferences(assemblies["ProjectHotfix.Simulation.TestSupport"]);

            Assert.That(simulationProjectReferences, Is.EquivalentTo(new[] { "ProjectHotfix.Contracts" }));
            Assert.That(harnessProjectReferences,
                Is.EquivalentTo(new[] { "ProjectHotfix.Contracts", "ProjectHotfix.Simulation" }));

            var forbiddenSourceHits = assemblies.Values
                .SelectMany(assembly => assembly.sourceFiles)
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
                .Select(path => (path, source: File.ReadAllText(path)))
                .Where(item => item.source.Contains("ProjectHotfix.Presentation", StringComparison.Ordinal)
                    || item.source.Contains("UnityEngine.UI", StringComparison.Ordinal)
                    || item.source.Contains("UnityEngine.Rendering", StringComparison.Ordinal)
                    || item.source.Contains("MonoBehaviour", StringComparison.Ordinal)
                    || item.source.Contains("Renderer", StringComparison.Ordinal)
                    || item.source.Contains("Canvas", StringComparison.Ordinal))
                .Select(item => item.path)
                .ToArray();

            Assert.That(forbiddenSourceHits, Is.Empty);
        }

        private static string[] ProjectReferences(Assembly assembly)
        {
            return assembly.assemblyReferences
                .Where(reference => reference.sourceFiles.Any(IsAssetsSource))
                .Select(reference => reference.name)
                .ToArray();
        }

        private static bool IsAssetsSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');
            return normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || normalized.Contains("/Assets/");
        }
    }
}
