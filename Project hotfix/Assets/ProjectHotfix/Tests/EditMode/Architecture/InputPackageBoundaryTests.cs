using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class InputPackageBoundaryTests
    {
        private static readonly Regex LegacyInputApiPattern = new Regex(
            @"(?:(?:\bUnityEngine\s*\.\s*)|(?<![\w.]))Input\s*\.\s*(?:GetAxis|GetAxisRaw|GetButton|GetButtonDown|GetButtonUp|GetKey|GetKeyDown|GetKeyUp|GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|GetTouch|mousePosition|anyKey|anyKeyDown|inputString|touchCount|acceleration)\b",
            RegexOptions.CultureInvariant);

#if ENABLE_INPUT_SYSTEM
        private const bool NewInputSystemEnabled = true;
#else
        private const bool NewInputSystemEnabled = false;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        private const bool LegacyInputManagerEnabled = true;
#else
        private const bool LegacyInputManagerEnabled = false;
#endif

        [Test]
        public void PlayerSettings_EnableOnlyTheNewInputSystemBackend()
        {
            Assert.That(NewInputSystemEnabled, Is.True);
            Assert.That(LegacyInputManagerEnabled, Is.False);
        }

        [Test]
        public void OnlyInputModule_ReferencesTheSelectedPackageAssembly()
        {
            var owners = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(assembly => assembly.sourceFiles.Any(IsProjectRuntimeSource))
                .Where(assembly => assembly.assemblyReferences.Any(
                    reference => reference.name == "Unity.InputSystem"))
                .Select(assembly => assembly.name)
                .ToArray();

            Assert.That(owners, Is.EquivalentTo(new[] { "ProjectHotfix.Input" }));
        }

        [Test]
        public void InputPackage_IsLockedAsDirectRegistryDependency()
        {
            var package = PackageInfo.FindForPackageName("com.unity.inputsystem");

            Assert.That(package, Is.Not.Null);
            Assert.That(package.version, Is.EqualTo("1.18.0"));
            Assert.That(package.source, Is.EqualTo(PackageSource.Registry));
            Assert.That(package.isDirectDependency, Is.True);
        }

        [Test]
        public void ProductRuntimeSources_DoNotUseLegacyInputApis()
        {
            var runtimeRoot = Path.Combine(UnityEngine.Application.dataPath, "ProjectHotfix", "Runtime");
            var forbiddenHits = Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Select(path => (path, source: File.ReadAllText(path)))
                .Where(item => ContainsLegacyInputApi(item.source))
                .Select(item => item.path)
                .ToArray();

            Assert.That(forbiddenHits, Is.Empty);
        }

        private static bool ContainsLegacyInputApi(string source)
        {
            return LegacyInputApiPattern.IsMatch(source);
        }

        private static bool IsProjectRuntimeSource(string sourcePath)
        {
            var normalized = sourcePath.Replace('\\', '/');
            return normalized.StartsWith("Assets/ProjectHotfix/Runtime/", StringComparison.Ordinal)
                || normalized.Contains("/Assets/ProjectHotfix/Runtime/");
        }
    }
}
