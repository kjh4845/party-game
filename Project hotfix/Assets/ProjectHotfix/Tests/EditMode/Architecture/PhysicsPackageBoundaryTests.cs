using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class PhysicsPackageBoundaryTests
    {
        private const float PhysicsStep = 1f / 60f;

        private static readonly Regex RuntimePhysicsMutationPattern = new Regex(
            @"(?:\bTime\s*\.\s*fixedDeltaTime\s*(?:[+\-*/]?=|\+\+|--)|\bPhysics\s*\.\s*simulationMode\s*(?:[+\-*/]?=|\+\+|--)|\bPhysics\s*\.\s*Simulate\s*\(|(?:\b[_A-Za-z]\w*PhysicsScene\w*|GetPhysicsScene\s*\(\s*\))\s*\.\s*Simulate\s*\()",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        [Test]
        public void ProjectPhysicsSettings_UseSixtyHertzAutomaticSimulation()
        {
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(PhysicsStep).Within(0.0000001f));
            Assert.That(Physics.simulationMode, Is.EqualTo(SimulationMode.FixedUpdate));
            Assert.That(Physics.autoSyncTransforms, Is.False);
        }

        [Test]
        public void PhysicsPackage_IsBuiltInDirectAndHasNoSecondStack()
        {
            var physicsPackage = PackageInfo.FindForPackageName("com.unity.modules.physics");

            Assert.That(physicsPackage, Is.Not.Null);
            Assert.That(physicsPackage.version, Is.EqualTo("1.0.0"));
            Assert.That(physicsPackage.source, Is.EqualTo(PackageSource.BuiltIn));
            Assert.That(physicsPackage.isDirectDependency, Is.True);
            Assert.That(PackageInfo.FindForPackageName("com.unity.physics"), Is.Null);
            Assert.That(PackageInfo.FindForPackageName("com.havok.physics"), Is.Null);
        }

        [Test]
        public void ProductRuntimeSources_DoNotMutateGlobalPhysicsCadence()
        {
            var runtimeRoot = Path.Combine(Application.dataPath, "ProjectHotfix", "Runtime");
            var forbiddenHits = Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Select(path => (path, source: File.ReadAllText(path)))
                .Where(item => RuntimePhysicsMutationPattern.IsMatch(item.source))
                .Select(item => item.path)
                .ToArray();

            Assert.That(forbiddenHits, Is.Empty);
        }

        [Test]
        public void RuntimePhysicsMutationGuard_CatchesRepresentativeForbiddenCode()
        {
            var forbiddenSamples = new[]
            {
                "Time.fixedDeltaTime = 0.02f;",
                "Time . fixedDeltaTime += 0.001f;",
                "Time.fixedDeltaTime++;",
                "Physics.simulationMode = SimulationMode.Script;",
                "Physics.Simulate(deltaTime);",
                "_physicsScene.Simulate(deltaTime);",
                "scene.GetPhysicsScene().Simulate(deltaTime);",
            };

            foreach (var sample in forbiddenSamples)
            {
                Assert.That(RuntimePhysicsMutationPattern.IsMatch(sample), Is.True, sample);
            }
        }
    }
}
