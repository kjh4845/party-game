using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class LocalStorageBoundaryTests
    {
        private static readonly Regex ForbiddenRuntimeDependencyPattern = new Regex(
            @"\b(?:UnityEngine|UnityEditor|Unity\s*\.\s*Networking|System\s*\.\s*Net)\b",
            RegexOptions.CultureInvariant);

        [Test]
        public void LocalStorageAssembly_IsANoEngineLeaf()
        {
            var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Single(candidate => candidate.name == "ProjectHotfix.LocalStorage");
            var forbiddenReferences = assembly.assemblyReferences
                .Select(reference => reference.name)
                .Where(name => name.StartsWith("ProjectHotfix.", StringComparison.Ordinal)
                    || name.StartsWith("Unity", StringComparison.Ordinal))
                .ToArray();
            var asmdefPath = Path.Combine(
                Application.dataPath,
                "ProjectHotfix",
                "Runtime",
                "LocalStorage",
                "ProjectHotfix.LocalStorage.asmdef");
            var asmdef = File.ReadAllText(asmdefPath);

            Assert.That(forbiddenReferences, Is.Empty);
            Assert.That(asmdef, Does.Contain("\"references\": []"));
            Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"));
        }

        [Test]
        public void LocalStorageRuntimeSources_HaveNoUnityOrNetworkNamespaceDependency()
        {
            var runtimePath = Path.Combine(Application.dataPath, "ProjectHotfix", "Runtime", "LocalStorage");
            var forbiddenHits = Directory.GetFiles(runtimePath, "*.cs", SearchOption.AllDirectories)
                .Select(path => (path, source: File.ReadAllText(path)))
                .Where(item => ForbiddenRuntimeDependencyPattern.IsMatch(item.source))
                .Select(item => item.path)
                .ToArray();

            Assert.That(forbiddenHits, Is.Empty);
        }
    }
}
