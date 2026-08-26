using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class ModuleBoundaryTests
    {
        private const string Contracts = "ProjectHotfix.Contracts";
        private const string Simulation = "ProjectHotfix.Simulation";
        private const string Presentation = "ProjectHotfix.Presentation";
        private const string Input = "ProjectHotfix.Input";
        private const string Transport = "ProjectHotfix.Transport";

        private static readonly IReadOnlyDictionary<string, string[]> ExpectedReferences =
            new Dictionary<string, string[]>
            {
                [Contracts] = Array.Empty<string>(),
                [Simulation] = new[] { Contracts },
                [Presentation] = new[] { Contracts },
                [Input] = new[] { Contracts },
                [Transport] = new[] { Contracts },
            };

        private static readonly IReadOnlyDictionary<string, string> ExpectedFolderAssemblies =
            new Dictionary<string, string>
            {
                ["Contracts"] = Contracts,
                ["Simulation"] = Simulation,
                ["Presentation"] = Presentation,
                ["Input"] = Input,
                ["Transport"] = Transport,
            };

        [Test]
        public void RuntimeAssemblies_HaveOnlyApprovedProjectReferences()
        {
            var assemblies = GetRequiredAssemblies();

            foreach (var expected in ExpectedReferences)
            {
                var actual = assemblies[expected.Key].assemblyReferences
                    .Where(IsProjectOwnedAssembly)
                    .Select(reference => reference.name)
                    .OrderBy(name => name)
                    .ToArray();

                Assert.That(actual, Is.EquivalentTo(expected.Value), expected.Key);
            }
        }

        [Test]
        public void RuntimeAssemblyGraph_HasNoCycles()
        {
            var graph = GetProjectReferenceGraph();
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();

            foreach (var module in graph.Keys)
            {
                Assert.That(HasCycle(module, graph, visiting, visited), Is.False, module);
            }
        }

        [Test]
        public void Presentation_HasNoDirectOrTransitivePathToSimulation()
        {
            var graph = GetProjectReferenceGraph();

            Assert.That(CanReach(Presentation, Simulation, graph, new HashSet<string>()), Is.False);
        }

        [Test]
        public void ProjectRuntimeSources_AreOwnedByTheirFolderAssembly()
        {
            var misplacedSources = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .SelectMany(assembly => assembly.sourceFiles.Select(source => (assembly.name, source)))
                .Where(item => IsProjectRuntimeSource(item.source))
                .Select(item => (item.name, item.source, expected: ExpectedAssemblyForRuntimeSource(item.source)))
                .Where(item => item.name != item.expected)
                .Select(item => $"{item.source}: expected {item.expected}, actual {item.name}")
                .ToArray();

            Assert.That(misplacedSources, Is.Empty);
        }

        private static IReadOnlyDictionary<string, Assembly> GetRequiredAssemblies()
        {
            var playerAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var runtimeAssemblies = playerAssemblies
                .Where(assembly => assembly.sourceFiles.Any(IsProjectRuntimeSource))
                .ToDictionary(assembly => assembly.name);

            Assert.That(runtimeAssemblies.Keys, Is.EquivalentTo(ExpectedReferences.Keys));
            return ExpectedReferences.Keys.ToDictionary(
                assemblyName => assemblyName,
                assemblyName => playerAssemblies.Single(assembly => assembly.name == assemblyName));
        }

        private static IReadOnlyDictionary<string, string[]> GetProjectReferenceGraph()
        {
            return CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(IsProjectOwnedAssembly)
                .ToDictionary(
                    assembly => assembly.name,
                    assembly => assembly.assemblyReferences
                    .Where(IsProjectOwnedAssembly)
                    .Select(reference => reference.name)
                    .ToArray());
        }

        private static bool HasCycle(
            string module,
            IReadOnlyDictionary<string, string[]> graph,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visited.Contains(module))
            {
                return false;
            }

            if (!visiting.Add(module))
            {
                return true;
            }

            foreach (var dependency in graph[module])
            {
                if (HasCycle(dependency, graph, visiting, visited))
                {
                    return true;
                }
            }

            visiting.Remove(module);
            visited.Add(module);
            return false;
        }

        private static bool CanReach(
            string current,
            string target,
            IReadOnlyDictionary<string, string[]> graph,
            ISet<string> visited)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            foreach (var dependency in graph[current])
            {
                if (dependency == target || CanReach(dependency, target, graph, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProjectOwnedAssembly(Assembly assembly)
        {
            return assembly.sourceFiles.Any(IsAssetsSource);
        }

        private static bool IsAssetsSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');
            return normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || normalized.Contains("/Assets/");
        }

        private static bool IsProjectRuntimeSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');
            return normalized.StartsWith("Assets/ProjectHotfix/Runtime/", StringComparison.Ordinal)
                || normalized.Contains("/Assets/ProjectHotfix/Runtime/");
        }

        private static string ExpectedAssemblyForRuntimeSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');

            foreach (var expected in ExpectedFolderAssemblies)
            {
                var segment = $"/Assets/ProjectHotfix/Runtime/{expected.Key}/";
                if (normalized.Contains(segment)
                    || normalized.StartsWith(segment.Substring(1), StringComparison.Ordinal))
                {
                    return expected.Value;
                }
            }

            return "<unmapped runtime folder>";
        }
    }
}
