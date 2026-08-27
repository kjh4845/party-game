using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class TransportPackageBoundaryTests
    {
        [Test]
        public void OnlyTransportModule_ReferencesTheSelectedPackageAssembly()
        {
            var owners = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(assembly => assembly.sourceFiles.Any(IsProjectRuntimeSource))
                .Where(assembly => assembly.assemblyReferences.Any(
                    reference => reference.name == "Unity.Networking.Transport"))
                .Select(assembly => assembly.name)
                .ToArray();

            Assert.That(owners, Is.EquivalentTo(new[] { "ProjectHotfix.Transport" }));
        }

        [Test]
        public void TransportPackage_IsLockedAsDirectRegistryDependency()
        {
            var package = PackageInfo.FindForPackageName("com.unity.transport");

            Assert.That(package, Is.Not.Null);
            Assert.That(package.version, Is.EqualTo("2.6.0"));
            Assert.That(package.source, Is.EqualTo(PackageSource.Registry));
            Assert.That(package.isDirectDependency, Is.True);
        }

        [Test]
        public void HighLevelNetcodeAndUnityOnlineServices_AreAbsent()
        {
            var rejectedPackages = new[]
            {
                "com.unity.netcode.gameobjects",
                "com.unity.netcode",
                "com.unity.services.multiplayer",
                "com.unity.services.relay",
                "com.unity.services.lobby",
            };

            foreach (var packageName in rejectedPackages)
            {
                Assert.That(PackageInfo.FindForPackageName(packageName), Is.Null, packageName);
            }
        }

        [Test]
        public void TransportModule_HasNoAdapterImplementationBeforeNet001()
        {
            var transportAssembly = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Single(assembly => assembly.name == "ProjectHotfix.Transport");
            var sources = transportAssembly.sourceFiles
                .Select(path => path.Replace('\\', '/'))
                .ToArray();

            Assert.That(sources, Has.Length.EqualTo(1));
            Assert.That(sources[0], Does.EndWith("/Runtime/Transport/AssemblyMarker.cs"));
        }

        private static bool IsProjectRuntimeSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');
            return normalized.StartsWith("Assets/ProjectHotfix/Runtime/", StringComparison.Ordinal)
                || normalized.Contains("/Assets/ProjectHotfix/Runtime/");
        }
    }
}
