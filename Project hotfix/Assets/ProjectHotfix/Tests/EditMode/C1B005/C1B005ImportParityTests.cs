using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using ProjectHotfix.Editor.C1B005;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectHotfix.C1B005.Tests
{
    public sealed class C1B005ImportParityTests
    {
        [Test]
        public void ModelImporter_UsesTheExactApprovedStaticPreset()
        {
            var importer = AssetImporter.GetAtPath(C1B005ImportContract.ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(C1B005ImportContract.ValidateExactImporterSettings(importer), Is.Empty);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importTangents, Is.EqualTo(ModelImporterTangents.None));
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        [Test]
        public void ImportedNeutral_HasExactStaticHierarchyBoundsLandmarksAndScope()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(C1B005ImportContract.ModelPath);
            var importer = AssetImporter.GetAtPath(C1B005ImportContract.ModelPath) as ModelImporter;
            Assert.That(model, Is.Not.Null);
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(model);
                var inspection = C1B005ParityPipeline.InspectImportedHierarchy(instance, importer);
                Assert.DoesNotThrow(() => C1B005ParityPipeline.ValidateInspection(inspection));
                Assert.That(inspection.meshObjectCount, Is.EqualTo(6));
                Assert.That(inspection.landmarkCount, Is.EqualTo(17));
                Assert.That(inspection.combinedBoundsSize.x, Is.EqualTo(0.58f).Within(0.005f));
                Assert.That(inspection.combinedBoundsSize.y, Is.EqualTo(1.0f).Within(0.005f));
                Assert.That(inspection.combinedBoundsSize.z, Is.EqualTo(0.265f).Within(0.005f));
                Assert.That(inspection.groundHeight, Is.EqualTo(0.0f).Within(0.005f));
                Assert.That(inspection.maximumLandmarkDeviationH, Is.LessThanOrEqualTo(0.005f));
                Assert.That(inspection.negativeScaleCount, Is.Zero);
                Assert.That(inspection.axisReversalCount, Is.Zero);
                Assert.That(inspection.rootForward.x, Is.EqualTo(0.0f).Within(0.000001f));
                Assert.That(inspection.rootForward.y, Is.EqualTo(0.0f).Within(0.000001f));
                Assert.That(inspection.rootForward.z, Is.EqualTo(1.0f).Within(0.000001f));
                Assert.That(inspection.rootDeterminant, Is.GreaterThan(0.0f));
                Assert.That(inspection.exportRootForward.z, Is.EqualTo(1.0f).Within(0.000001f));
                Assert.That(inspection.exportRootDeterminant, Is.GreaterThan(0.0f));
                Assert.That(inspection.armatureCount, Is.Zero);
                Assert.That(inspection.animatorCount, Is.Zero);
                Assert.That(inspection.colliderCount, Is.Zero);
                Assert.That(inspection.importedMaterialAssetCount, Is.Zero);
                Assert.That(inspection.geometrySignature.vertexCount, Is.EqualTo(1336));
                Assert.That(inspection.geometrySignature.normalCount, Is.EqualTo(1336));
                Assert.That(inspection.geometrySignature.pointSha256,
                    Is.EqualTo(C1B005ImportContract.ExpectedImportedPointSignatureSha256));
                Assert.That(inspection.geometrySignature.surfaceSha256,
                    Is.EqualTo(C1B005ImportContract.ExpectedImportedSurfaceSignatureSha256));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void IdentityPrefab_IsTransformNeutralAndContainsNoGameplayComponents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(C1B005ImportContract.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true), Has.Length.EqualTo(6));
            Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
        }

        [Test]
        public void InspectionAndEightUnityCaptures_ArePresentAndHashBound()
        {
            var inspectionPath = RepositoryPath("artifacts/evidence/G0/C1B-005/UnityImportInspection.json");
            Assert.That(File.Exists(inspectionPath), Is.True);
            var inspection = JsonUtility.FromJson<ImportInspection>(File.ReadAllText(inspectionPath));
            Assert.That(inspection, Is.Not.Null);
            Assert.That(inspection.result, Is.EqualTo("PASS"));
            Assert.That(inspection.sourceSha256, Is.EqualTo(C1B005ImportContract.FbxSha256));
            Assert.That(inspection.modelInteropProfileId, Is.EqualTo("ModelInteropProfile-ART-001-r02"));
            Assert.That(inspection.importerOverrideId, Is.EqualTo("PHX-UNITY-C1B-BLOCKOUT-r02"));
            Assert.That(inspection.importerSettingsMatched, Is.True);
            Assert.That(inspection.captures, Has.Length.EqualTo(8));
            Assert.That(inspection.captures.Select(capture => capture.style).Distinct(), Is.EquivalentTo(new[] { "Neutral", "Silhouette" }));
            Assert.That(inspection.captures.Select(capture => capture.view).Distinct(), Is.EquivalentTo(new[] { "Front", "Side", "Back", "ThreeQuarter" }));
            foreach (var capture in inspection.captures)
            {
                var absolute = RepositoryPath(capture.path);
                Assert.That(File.Exists(absolute), Is.True, capture.path);
                Assert.That(new FileInfo(absolute).Length, Is.EqualTo(capture.bytes), capture.path);
                Assert.That(Sha256File(absolute), Is.EqualTo(capture.sha256), capture.path);
                Assert.That(capture.width, Is.EqualTo(2048));
                Assert.That(capture.height, Is.EqualTo(2048));
                Assert.That(capture.boundsPaddingRatioPerSide, Is.EqualTo(0.10f));
            }
            Assert.That(inspection.neutralStage.qaOnly, Is.True);
            Assert.That(inspection.neutralStage.productLighting, Is.False);
            Assert.That(inspection.neutralStage.keyRelativeIntensity, Is.EqualTo(1.0f));
            Assert.That(inspection.neutralStage.fillLightCount, Is.EqualTo(3));
            Assert.That(inspection.neutralStage.fillTotalRelativeIntensity, Is.EqualTo(0.35f));
            Assert.That(inspection.neutralStage.keyUnityRay.x, Is.EqualTo(0.321393818f).Within(0.000001f));
            Assert.That(inspection.neutralStage.keyUnityRay.y, Is.EqualTo(-0.556670368f).Within(0.000001f));
            Assert.That(inspection.neutralStage.keyUnityRay.z, Is.EqualTo(-0.766044438f).Within(0.000001f));
            Assert.That(inspection.neutralContrast, Has.Length.EqualTo(4));
            foreach (var contrast in inspection.neutralContrast)
            {
                Assert.That(contrast.approvalThresholdApplied, Is.False);
                Assert.That(contrast.absoluteLuminanceDifferenceObserved, Is.GreaterThan(0.0));
            }
        }

        [Test]
        public void FourViewSilhouetteParity_UsesObservedIouAndBoundedBboxGate()
        {
            var inspectionPath = RepositoryPath("artifacts/evidence/G0/C1B-005/UnityImportInspection.json");
            var inspection = JsonUtility.FromJson<ImportInspection>(File.ReadAllText(inspectionPath));
            Assert.That(inspection.silhouetteParity, Has.Length.EqualTo(4));
            Assert.That(inspection.silhouetteParity.Select(record => record.view),
                Is.EquivalentTo(new[] { "Front", "Side", "Back", "ThreeQuarter" }));
            foreach (var record in inspection.silhouetteParity)
            {
                Assert.That(record.threshold, Is.EqualTo(128));
                Assert.That(record.width, Is.EqualTo(2048));
                Assert.That(record.height, Is.EqualTo(2048));
                Assert.That(record.maximumBoundingBoxDriftHThreshold, Is.EqualTo(0.005));
                Assert.That(record.maximumBoundingBoxDriftH,
                    Is.LessThanOrEqualTo(record.maximumBoundingBoxDriftHThreshold), record.view);
                Assert.That(record.intersectionOverUnionObserved, Is.GreaterThan(0.0));
                Assert.That(record.iouApprovalThresholdApplied, Is.False);
                Assert.That(record.result, Is.EqualTo("PASS"));
            }
        }

        [Test]
        public void ActionPoseReview_IsReadOnlyAndMakesNoAnimationClaim()
        {
            var inspectionPath = RepositoryPath("artifacts/evidence/G0/C1B-005/UnityImportInspection.json");
            var inspection = JsonUtility.FromJson<ImportInspection>(File.ReadAllText(inspectionPath));
            var review = inspection.actionStaticReview;
            Assert.That(review.state, Is.EqualTo("READ_ONLY_SOURCE_EVIDENCE_RECONFIRMED"));
            Assert.That(review.fileCount, Is.EqualTo(20));
            Assert.That(review.png2048Matches, Is.EqualTo(20));
            Assert.That(review.orderedBundleSha256, Is.EqualTo(C1B005ImportContract.ActionReviewBundleSha256));
            Assert.That(review.checklistIds, Is.EquivalentTo(new[]
            {
                "STATIC_SILHOUETTE_UNCHANGED",
                "STATIC_INTERSECTION_REVIEW_UNCHANGED",
                "STATIC_JOINT_REGION_REVIEW_UNCHANGED",
            }));
            Assert.That(review.animationActionsPresent, Is.Zero);
            Assert.That(review.motionNaturalnessClaimed, Is.False);
            Assert.That(inspection.animationNaturalnessClaimed, Is.False);
            Assert.That(inspection.playerBuildsExecuted, Is.Zero);
            Assert.That(inspection.playModeTestsExecuted, Is.Zero);
            Assert.That(inspection.gameplayComponentsCreated, Is.Zero);
            Assert.That(inspection.manualTransformCorrections, Is.Zero);
        }

        [Test]
        public void C1B005UnityAssetFolder_HasOnlyFbxAndIdentityPrefabSources()
        {
            var folder = Path.GetDirectoryName(C1B005ImportContract.ModelPath);
            var paths = AssetDatabase.FindAssets(string.Empty, new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(paths, Is.EquivalentTo(new[]
            {
                C1B005ImportContract.ModelPath,
                C1B005ImportContract.PrefabPath,
            }));
            Assert.That(paths.Any(path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(paths.Any(path => path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(paths.Any(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        private static string RepositoryPath(string relative)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(Directory.GetParent(projectRoot).FullName, relative);
        }

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(stream);
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    result.Append(value.ToString("x2"));
                }
                return result.ToString();
            }
        }
    }
}
