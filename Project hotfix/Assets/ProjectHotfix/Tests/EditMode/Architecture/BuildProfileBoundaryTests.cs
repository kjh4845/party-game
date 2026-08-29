using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class BuildProfileBoundaryTests
    {
        private const string ProfileFolder = "Assets/Settings/Build Profiles";
        private const string DevelopmentProfilePath =
            ProfileFolder + "/Windows x64 Development.asset";
        private const string SteamReservedProfilePath =
            ProfileFolder + "/Windows x64 Steam Reserved.asset";

        private const string DevelopmentDefine = "PROJECTHOTFIX_BUILD_DEVELOPMENT";
        private const string SteamReservedDefine = "PROJECTHOTFIX_BUILD_STEAM_RESERVED";

        private static readonly string[] ExpectedProfilePaths =
        {
            DevelopmentProfilePath,
            SteamReservedProfilePath,
        };

        [Test]
        public void WindowsBuildProfiles_AreExactlyTwoAssetsWithUniqueGuids()
        {
            var profileGuids = AssetDatabase.FindAssets("t:BuildProfile", new[] { ProfileFolder });
            var profilePaths = profileGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(profilePaths, Is.EquivalentTo(ExpectedProfilePaths));
            Assert.That(profileGuids, Has.Length.EqualTo(2));
            Assert.That(profileGuids, Has.All.Not.Empty);
            Assert.That(
                profileGuids.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(2));

            foreach (var profilePath in ExpectedProfilePaths)
            {
                Assert.That(LoadProfile(profilePath), Is.Not.Null, profilePath);
            }
        }

        [TestCase(DevelopmentProfilePath, true)]
        [TestCase(SteamReservedProfilePath, false)]
        public void WindowsBuildProfile_TargetPlatformAndDevelopmentModeAreCorrect(
            string profilePath,
            bool expectedDevelopment)
        {
            var profile = LoadProfile(profilePath);

            Assert.That(
                ReadRequiredProperty<BuildTarget>(profile, "buildTarget"),
                Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(
                ReadRequiredProperty<StandaloneBuildSubtarget>(profile, "subtarget"),
                Is.EqualTo(StandaloneBuildSubtarget.Player));

            var platformSettings = ReadRequiredProperty(profile, "platformBuildProfile");
            Assert.That(platformSettings, Is.Not.Null, profilePath);
            Assert.That(
                platformSettings.GetType().FullName,
                Is.EqualTo("UnityEditor.WindowsStandalone.WindowsPlatformSettings"));
            Assert.That(
                ReadRequiredProperty<bool>(platformSettings, "development"),
                Is.EqualTo(expectedDevelopment));
            Assert.That(
                ReadRequiredProperty<OSArchitecture>(platformSettings, "architecture"),
                Is.EqualTo(OSArchitecture.x64));
        }

        [Test]
        public void BuildProfileDefines_AreExactMutuallyExclusiveAndDoNotLeakGlobally()
        {
            var developmentDefines = LoadProfile(DevelopmentProfilePath).scriptingDefines;
            var steamReservedDefines = LoadProfile(SteamReservedProfilePath).scriptingDefines;

            Assert.That(developmentDefines, Is.EqualTo(new[] { DevelopmentDefine }));
            Assert.That(steamReservedDefines, Is.EqualTo(new[] { SteamReservedDefine }));
            Assert.That(developmentDefines.Intersect(steamReservedDefines), Is.Empty);

            var globalDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            Assert.That(globalDefines, Does.Not.Contain(DevelopmentDefine));
            Assert.That(globalDefines, Does.Not.Contain(SteamReservedDefine));
        }

        [TestCase(DevelopmentProfilePath)]
        [TestCase(SteamReservedProfilePath)]
        public void BuildProfile_InheritsTheSingleGlobalSampleScene(string profilePath)
        {
            var profile = LoadProfile(profilePath);

            Assert.That(profile.overrideGlobalScenes, Is.False);

            var globalScenes = EditorBuildSettings.scenes;
            var scenesForBuild = profile.GetScenesForBuild();

            Assert.That(globalScenes, Has.Length.EqualTo(1));
            Assert.That(globalScenes[0].enabled, Is.True);
            Assert.That(globalScenes[0].path, Is.EqualTo("Assets/Scenes/SampleScene.unity"));
            Assert.That(scenesForBuild, Has.Length.EqualTo(1));
            Assert.That(scenesForBuild[0].enabled, Is.True);
            Assert.That(scenesForBuild[0].path, Is.EqualTo(globalScenes[0].path));
        }

        [TestCase(DevelopmentProfilePath)]
        [TestCase(SteamReservedProfilePath)]
        public void BuildProfile_HasNoQualityGraphicsOrPlayerSettingsOverrides(string profilePath)
        {
            var subAssetTypeNames = AssetDatabase.LoadAllAssetsAtPath(profilePath)
                .Where(AssetDatabase.IsSubAsset)
                .Select(asset => asset.GetType().FullName)
                .ToArray();

            Assert.That(
                subAssetTypeNames,
                Does.Not.Contain("UnityEditor.Build.Profile.BuildProfileQualitySettings"));
            Assert.That(
                subAssetTypeNames,
                Does.Not.Contain("UnityEditor.Build.Profile.BuildProfileGraphicsSettings"));
            Assert.That(subAssetTypeNames, Does.Not.Contain(typeof(PlayerSettings).FullName));
            Assert.That(subAssetTypeNames, Is.Empty);
        }

        [Test]
        public void GlobalStandalonePlayerSettings_UseTheApprovedTemporaryIdentityAndMono()
        {
            Assert.That(PlayerSettings.companyName, Is.EqualTo("KJH4845"));
            Assert.That(PlayerSettings.productName, Is.EqualTo("Project Hotfix"));
            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("0.1.0"));
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone),
                Is.EqualTo("com.kjh4845.projecthotfix"));
            Assert.That(
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
                Is.EqualTo(ScriptingImplementation.Mono2x));
        }

        [Test]
        public void WindowsX64BuildSupport_IsInstalled()
        {
            Assert.That(
                BuildPipeline.IsBuildTargetSupported(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64),
                Is.True);
        }

        private static BuildProfile LoadProfile(string assetPath)
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
            Assert.That(profile, Is.Not.Null, $"BuildProfile asset was not loadable: {assetPath}");
            return profile;
        }

        private static T ReadRequiredProperty<T>(object instance, string propertyName)
        {
            var value = ReadRequiredProperty(instance, propertyName);
            Assert.That(
                value,
                Is.TypeOf<T>(),
                $"{instance.GetType().FullName}.{propertyName} must return {typeof(T).FullName}.");
            return (T)value;
        }

        private static object ReadRequiredProperty(object instance, string propertyName)
        {
            Assert.That(instance, Is.Not.Null, $"Cannot read required property '{propertyName}' from null.");

            var property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(
                property,
                Is.Not.Null,
                $"Required Unity API property is missing: {instance.GetType().FullName}.{propertyName}");
            Assert.That(property.CanRead, Is.True);
            Assert.That(property.GetIndexParameters(), Is.Empty);

            return property.GetValue(instance);
        }
    }
}
