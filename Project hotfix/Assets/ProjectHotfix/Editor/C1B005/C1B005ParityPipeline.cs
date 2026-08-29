using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ProjectHotfix.Editor.C1B005
{
    public static class C1B005ParityPipeline
    {
        private const int CaptureResolution = 2048;
        private const float PaddingRatioPerSide = 0.10f;
        private const float Epsilon = 0.000001f;
        private static bool useBatchCaptureScene;

        private static readonly ViewDefinition[] Views =
        {
            new ViewDefinition("Front", new Vector3(0.0f, 0.0f, -1.0f), Vector3.up),
            new ViewDefinition("Side", new Vector3(-1.0f, 0.0f, 0.0f), Vector3.up),
            new ViewDefinition("Back", new Vector3(0.0f, 0.0f, 1.0f), Vector3.up),
            new ViewDefinition("ThreeQuarter", new Vector3(-0.70710678f, 0.0f, -0.70710678f), Vector3.up),
        };

        public static void RunBatch()
        {
            useBatchCaptureScene = true;
            try
            {
                Run();
            }
            finally
            {
                useBatchCaptureScene = false;
            }
        }

        private static void Run()
        {
            var absoluteModelPath = AbsoluteProjectPath(C1B005ImportContract.ModelPath);
            Require(File.Exists(absoluteModelPath), "C1B005_FBX_MISSING");
            Require(new FileInfo(absoluteModelPath).Length == C1B005ImportContract.FbxBytes, "C1B005_FBX_SIZE");
            Require(Sha256File(absoluteModelPath) == C1B005ImportContract.FbxSha256, "C1B005_FBX_SHA");

            AssetDatabase.ImportAsset(
                C1B005ImportContract.ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(C1B005ImportContract.ModelPath) as ModelImporter;
            var importerErrors = C1B005ImportContract.ValidateExactImporterSettings(importer);
            Require(importerErrors.Count == 0, "C1B005_IMPORTER_" + string.Join("_", importerErrors));

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(C1B005ImportContract.ModelPath);
            Require(model != null, "C1B005_IMPORTED_MODEL_MISSING");

            GameObject inspectionInstance = null;
            try
            {
                inspectionInstance = Object.Instantiate(model);
                inspectionInstance.name = "CHR_MasterCharacter_C1B_Neutral_r02";

                var inspection = InspectImportedHierarchy(inspectionInstance, importer);
                Debug.Log("C1B005_IMPORT_DIAGNOSTIC=" + JsonUtility.ToJson(inspection));
                ValidateInspection(inspection);
                SaveIdentityPrefab(model);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(C1B005ImportContract.PrefabPath);
                Require(prefab != null, "C1B005_PREFAB_MISSING");
                ValidatePrefabIdentity(prefab);

                inspection.prefabPath = C1B005ImportContract.PrefabPath;
                inspection.prefabGuid = AssetDatabase.AssetPathToGUID(C1B005ImportContract.PrefabPath);
                inspection.modelGuid = AssetDatabase.AssetPathToGUID(C1B005ImportContract.ModelPath);
                inspection.captures = CaptureFourViews(prefab);
                inspection.neutralStage = NeutralStageRecord.Create();
                inspection.silhouetteParity = CompareSilhouetteMasks(inspection.captures);
                ValidateSilhouetteParity(inspection.silhouetteParity);
                inspection.neutralContrast = MeasureNeutralContrast(inspection.captures);
                inspection.actionStaticReview = InspectActionStaticReview();
                inspection.result = "PASS";
                inspection.playerBuildsExecuted = 0;
                inspection.playModeTestsExecuted = 0;
                inspection.gameplayComponentsCreated = 0;
                inspection.manualTransformCorrections = 0;
                inspection.animationNaturalnessClaimed = false;
                WriteInspection(inspection);
            }
            finally
            {
                if (inspectionInstance != null)
                {
                    Object.DestroyImmediate(inspectionInstance);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("C1B005_PARITY_RESULT=PASS");
        }

        public static void LogGeometrySignature()
        {
            AssetDatabase.ImportAsset(C1B005ImportContract.ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(C1B005ImportContract.ModelPath);
            Require(model != null, "C1B005_IMPORTED_MODEL_MISSING");
            var instance = Object.Instantiate(model);
            try
            {
                Debug.Log("C1B005_GEOMETRY_SIGNATURE=" + JsonUtility.ToJson(ComputeGeometrySignature(instance)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        public static ImportInspection InspectImportedHierarchy(GameObject root, ModelImporter importer)
        {
            Require(root != null, "C1B005_INSPECTION_ROOT_MISSING");
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length > 0, "C1B005_RENDERER_MISSING");
            var bounds = CombineBounds(renderers);
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var exportRoot = allTransforms.SingleOrDefault(transform => transform.name == "CHR_C1B005_ExportRoot");
            Require(exportRoot != null, "C1B005_EXPORT_ROOT_MISSING");

            var meshNames = meshFilters.Select(filter => filter.gameObject.name)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var landmarkRecords = new List<LandmarkRecord>();
            var maximumLandmarkDeviation = 0.0f;
            foreach (var expected in C1B005ImportContract.ExpectedLandmarks)
            {
                var transform = allTransforms.SingleOrDefault(item => item.name == expected.Name);
                Require(transform != null, "C1B005_LANDMARK_MISSING_" + expected.Name);
                var actual = root.transform.InverseTransformPoint(transform.position);
                var deviation = MaximumComponentAbs(actual - expected.Position);
                maximumLandmarkDeviation = Mathf.Max(maximumLandmarkDeviation, deviation);
                landmarkRecords.Add(new LandmarkRecord
                {
                    id = expected.Name,
                    expected = VectorRecord.From(expected.Position),
                    actual = VectorRecord.From(actual),
                    maximumDeviationH = deviation,
                });
            }

            var negativeScaleCount = allTransforms.Count(transform =>
                transform.localScale.x < 0.0f || transform.localScale.y < 0.0f || transform.localScale.z < 0.0f);
            var axisReversalCount = allTransforms.Count(transform => transform.localToWorldMatrix.determinant < 0.0f);
            var importedMaterials = AssetDatabase.LoadAllAssetsAtPath(C1B005ImportContract.ModelPath)
                .OfType<Material>().Count();

            return new ImportInspection
            {
                schemaVersion = 1,
                taskId = C1B005ImportContract.TaskId,
                state = "START",
                candidateStatus = "CANDIDATE",
                sourceOwner = "kjh4845",
                sourcePath = C1B005ImportContract.ModelPath,
                sourceBytes = C1B005ImportContract.FbxBytes,
                sourceSha256 = C1B005ImportContract.FbxSha256,
                modelInteropProfileId = C1B005ImportContract.ModelInteropProfileId,
                modelInteropProfileRevision = C1B005ImportContract.ModelInteropProfileRevision,
                baseImporterPresetId = C1B005ImportContract.BaseImportPresetId,
                baseImporterPresetRevision = C1B005ImportContract.BaseImportPresetRevision,
                baseImporterSettingsSha256 = C1B005ImportContract.BaseImportSettingsSha256,
                importerOverrideId = C1B005ImportContract.ImportOverrideId,
                importerOverrideRevision = C1B005ImportContract.ImportOverrideRevision,
                importerOverrideSettingsSha256 = C1B005ImportContract.ImportOverrideSettingsSha256,
                importerSettingsMatched = C1B005ImportContract.ValidateExactImporterSettings(importer).Count == 0,
                combinedBoundsCenter = VectorRecord.From(bounds.center),
                combinedBoundsSize = VectorRecord.From(bounds.size),
                groundHeight = bounds.min.y,
                meshObjectCount = meshFilters.Length,
                meshObjectNames = meshNames,
                landmarkCount = landmarkRecords.Count,
                landmarks = landmarkRecords.ToArray(),
                maximumLandmarkDeviationH = maximumLandmarkDeviation,
                rootScale = VectorRecord.From(root.transform.localScale),
                rootRotationEuler = VectorRecord.From(root.transform.localEulerAngles),
                rootForward = VectorRecord.From(root.transform.forward),
                rootDeterminant = root.transform.localToWorldMatrix.determinant,
                exportRootScale = VectorRecord.From(exportRoot.localScale),
                exportRootRotationEuler = VectorRecord.From(exportRoot.localEulerAngles),
                exportRootForward = VectorRecord.From(exportRoot.forward),
                exportRootDeterminant = exportRoot.localToWorldMatrix.determinant,
                negativeScaleCount = negativeScaleCount,
                axisReversalCount = axisReversalCount,
                armatureCount = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
                animatorCount = root.GetComponentsInChildren<Animator>(true).Length,
                colliderCount = root.GetComponentsInChildren<Collider>(true).Length,
                cameraCount = root.GetComponentsInChildren<Camera>(true).Length,
                lightCount = root.GetComponentsInChildren<Light>(true).Length,
                importedMaterialAssetCount = importedMaterials,
                geometrySignature = ComputeGeometrySignature(root),
            };
        }

        public static void ValidateInspection(ImportInspection inspection)
        {
            Require(inspection.importerSettingsMatched, "C1B005_IMPORTER_SETTINGS_DRIFT");
            Require(inspection.meshObjectCount == 6, "C1B005_MESH_COUNT");
            Require(inspection.meshObjectNames.SequenceEqual(C1B005ImportContract.ExpectedMeshObjectNames), "C1B005_MESH_SET");
            Require(inspection.landmarkCount == 17, "C1B005_LANDMARK_COUNT");
            Require(inspection.maximumLandmarkDeviationH <= C1B005ImportContract.MaximumHeightRatioDeviation, "C1B005_LANDMARK_DEVIATION");
            Require(Mathf.Abs(inspection.combinedBoundsSize.x - 0.58f) <= 0.005f, "C1B005_WIDTH");
            Require(Mathf.Abs(inspection.combinedBoundsSize.y - 1.0f) <= 0.005f, "C1B005_HEIGHT");
            Require(Mathf.Abs(inspection.combinedBoundsSize.z - 0.265f) <= 0.005f, "C1B005_DEPTH");
            Require(Mathf.Abs(inspection.groundHeight) <= 0.005f, "C1B005_GROUND_PIVOT");
            Require(Approximately(inspection.rootScale, Vector3.one), "C1B005_ROOT_SCALE");
            Require(ApproximatelyEulerZero(inspection.rootRotationEuler), "C1B005_ROOT_ROTATION");
            Require(Approximately(inspection.rootForward, Vector3.forward) && inspection.rootDeterminant > 0.0f,
                "C1B005_ROOT_FORWARD_DETERMINANT");
            Require(Approximately(inspection.exportRootScale, Vector3.one) &&
                ApproximatelyEulerZero(inspection.exportRootRotationEuler) &&
                Approximately(inspection.exportRootForward, Vector3.forward) &&
                inspection.exportRootDeterminant > 0.0f, "C1B005_EXPORT_ROOT_TRANSFORM");
            Require(inspection.negativeScaleCount == 0, "C1B005_NEGATIVE_SCALE");
            Require(inspection.axisReversalCount == 0, "C1B005_AXIS_REVERSAL");
            Require(inspection.armatureCount == 0, "C1B005_ARMATURE_SCOPE");
            Require(inspection.animatorCount == 0, "C1B005_ANIMATION_SCOPE");
            Require(inspection.colliderCount == 0, "C1B005_COLLIDER_SCOPE");
            Require(inspection.cameraCount == 0, "C1B005_CAMERA_SCOPE");
            Require(inspection.lightCount == 0, "C1B005_LIGHT_SCOPE");
            Require(inspection.importedMaterialAssetCount == 0, "C1B005_MATERIAL_IMPORT_SCOPE");
            Require(inspection.geometrySignature.pointSha256 == C1B005ImportContract.ExpectedImportedPointSignatureSha256,
                "C1B005_POINT_SIGNATURE");
            Require(inspection.geometrySignature.surfaceSha256 == C1B005ImportContract.ExpectedImportedSurfaceSignatureSha256,
                "C1B005_SURFACE_SIGNATURE");
        }

        private static void SaveIdentityPrefab(GameObject model)
        {
            GameObject instance = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
                Require(instance != null, "C1B005_MODEL_INSTANTIATION");
                Require(instance.transform.localPosition == Vector3.zero, "C1B005_PREFAB_SOURCE_POSITION");
                Require(Approximately(VectorRecord.From(instance.transform.localScale), Vector3.one), "C1B005_PREFAB_SOURCE_SCALE");
                Require(Quaternion.Angle(instance.transform.localRotation, Quaternion.identity) <= Epsilon, "C1B005_PREFAB_SOURCE_ROTATION");
                instance.name = "CHR_MasterCharacter_C1B_Neutral_r02";
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, C1B005ImportContract.PrefabPath, out var success);
                Require(success && saved != null, "C1B005_PREFAB_SAVE");
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static void ValidatePrefabIdentity(GameObject prefab)
        {
            Require(prefab.transform.localPosition == Vector3.zero, "C1B005_PREFAB_POSITION");
            Require(prefab.transform.localScale == Vector3.one, "C1B005_PREFAB_SCALE");
            Require(Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) <= Epsilon, "C1B005_PREFAB_ROTATION");
            Require(prefab.GetComponentsInChildren<MeshFilter>(true).Length == 6, "C1B005_PREFAB_MESH_COUNT");
            Require(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0, "C1B005_PREFAB_SKINNED_SCOPE");
            Require(prefab.GetComponentsInChildren<Animator>(true).Length == 0, "C1B005_PREFAB_ANIMATOR_SCOPE");
            Require(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "C1B005_PREFAB_COLLIDER_SCOPE");
            var nonTransformComponents = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => !(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer))
                .Select(component => component.GetType().FullName).Distinct().ToArray();
            Require(nonTransformComponents.Length == 0, "C1B005_PREFAB_COMPONENT_SCOPE_" + string.Join("_", nonTransformComponents));
        }

        private static CaptureRecord[] CaptureFourViews(GameObject prefab)
        {
            var outputDirectory = Path.Combine(RepositoryRoot(), "artifacts/evidence/G0/C1B-005/Captures/Unity");
            Directory.CreateDirectory(outputDirectory);
            foreach (var existing in Directory.GetFiles(outputDirectory, "*.png"))
            {
                File.Delete(existing);
            }

            var scene = useBatchCaptureScene
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            GameObject cameraObject = null;
            var lightObjects = new List<GameObject>();
            Material neutralMaterial = null;
            Material silhouetteMaterial = null;
            var records = new List<CaptureRecord>();
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientLight = RenderSettings.ambientLight;
            try
            {
                instance = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance, scene);
                Require(instance.transform.localPosition == Vector3.zero &&
                    instance.transform.localScale == Vector3.one &&
                    Quaternion.Angle(instance.transform.localRotation, Quaternion.identity) <= Epsilon,
                    "C1B005_CAPTURE_SOURCE_TRANSFORM");
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = CombineBounds(renderers);

                cameraObject = new GameObject("C1B005_QA_Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20.0f;

                var lightObject = new GameObject("C1B005_QA_Key", typeof(Light));
                lightObjects.Add(lightObject);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1.0f;
                light.shadows = LightShadows.Soft;
                var keyUnityRay = new Vector3(0.321393818f, -0.556670368f, -0.766044438f).normalized;
                lightObject.transform.rotation = Quaternion.LookRotation(keyUnityRay, Vector3.up);
                var fillSpecs = new[]
                {
                    new FillDefinition("Back", new Vector3(130.0f, 150.0f, 180.0f), new Vector3(-0.321393758f, -0.556670427f, 0.766044438f)),
                    new FillDefinition("Left", new Vector3(80.0f, 90.0f, 0.0f), new Vector3(-0.173648223f, 0.000000008f, -0.98480773f)),
                    new FillDefinition("Right", new Vector3(80.0f, -90.0f, 0.0f), new Vector3(0.173648223f, 0.000000008f, -0.98480773f)),
                };
                foreach (var fillSpec in fillSpecs)
                {
                    var fillObject = new GameObject("C1B005_QA_Fill_" + fillSpec.Id, typeof(Light));
                    lightObjects.Add(fillObject);
                    SceneManager.MoveGameObjectToScene(fillObject, scene);
                    var fill = fillObject.GetComponent<Light>();
                    fill.type = LightType.Directional;
                    fill.color = Color.white;
                    fill.intensity = 0.116666667f;
                    fill.shadows = LightShadows.None;
                    fillObject.transform.rotation = Quaternion.LookRotation(fillSpec.UnityRay.normalized, Vector3.up);
                }

                neutralMaterial = CreateNeutralMaterial();
                silhouetteMaterial = CreateSilhouetteMaterial();
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;

                foreach (var style in new[] { "Neutral", "Silhouette" })
                {
                    var material = style == "Neutral" ? neutralMaterial : silhouetteMaterial;
                    SetMaterial(renderers, material);
                    foreach (var qaLight in lightObjects) qaLight.SetActive(style == "Neutral");
                    camera.backgroundColor = style == "Neutral"
                        ? new Color(0.18f, 0.18f, 0.18f, 1.0f)
                        : new Color(0.75f, 0.75f, 0.75f, 1.0f);

                    foreach (var view in Views)
                    {
                        ConfigureCamera(camera, bounds, view);
                        var filename = $"CHR_MasterCharacter_C1B_Neutral_r02_{style}_{view.Id}.png";
                        var path = Path.Combine(outputDirectory, filename);
                        RenderCamera(camera, path);
                        records.Add(new CaptureRecord
                        {
                            style = style,
                            view = view.Id,
                            path = "artifacts/evidence/G0/C1B-005/Captures/Unity/" + filename,
                            bytes = new FileInfo(path).Length,
                            sha256 = Sha256File(path),
                            width = CaptureResolution,
                            height = CaptureResolution,
                            orthographicSize = camera.orthographicSize,
                            boundsPaddingRatioPerSide = PaddingRatioPerSide,
                        });
                    }
                }
            }
            finally
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                if (neutralMaterial != null) Object.DestroyImmediate(neutralMaterial);
                if (silhouetteMaterial != null) Object.DestroyImmediate(silhouetteMaterial);
                if (instance != null) Object.DestroyImmediate(instance);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                foreach (var qaLight in lightObjects) if (qaLight != null) Object.DestroyImmediate(qaLight);
                if (!useBatchCaptureScene)
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Require(records.Count == 8, "C1B005_CAPTURE_COUNT");
            return records.OrderBy(record => record.path, StringComparer.Ordinal).ToArray();
        }

        private static ActionStaticReview InspectActionStaticReview()
        {
            var renderRoot = Path.Combine(RepositoryRoot(), "BlenderSource/Characters/C1B-004/Renders");
            Require(Directory.Exists(renderRoot), "C1B005_ACTION_REVIEW_ROOT");
            var files = Directory.GetFiles(renderRoot, "*.png").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Require(files.Length == 20, "C1B005_ACTION_REVIEW_COUNT");
            var bundle = new StringBuilder();
            var dimensionsMatched = 0;
            foreach (var file in files)
            {
                var relative = "BlenderSource/Characters/C1B-004/Renders/" + Path.GetFileName(file);
                bundle.Append(relative).Append('=').Append(Sha256File(file)).Append('\n');
                if (ReadPngDimensions(file) == new Vector2Int(2048, 2048))
                {
                    dimensionsMatched++;
                }
            }
            var bundleSha = Sha256Bytes(Encoding.UTF8.GetBytes(bundle.ToString()));
            Require(bundleSha == C1B005ImportContract.ActionReviewBundleSha256, "C1B005_ACTION_REVIEW_BUNDLE_SHA");
            Require(dimensionsMatched == 20, "C1B005_ACTION_REVIEW_DIMENSIONS");
            return new ActionStaticReview
            {
                state = "READ_ONLY_SOURCE_EVIDENCE_RECONFIRMED",
                fileCount = files.Length,
                png2048Matches = dimensionsMatched,
                orderedBundleSha256 = bundleSha,
                checklistIds = new[]
                {
                    "STATIC_SILHOUETTE_UNCHANGED",
                    "STATIC_INTERSECTION_REVIEW_UNCHANGED",
                    "STATIC_JOINT_REGION_REVIEW_UNCHANGED",
                },
                animationActionsPresent = 0,
                motionNaturalnessClaimed = false,
            };
        }

        private static SilhouetteParityRecord[] CompareSilhouetteMasks(IEnumerable<CaptureRecord> captures)
        {
            const byte threshold = 128;
            var records = new List<SilhouetteParityRecord>();
            foreach (var view in Views)
            {
                var unityCapture = captures.Single(record => record.style == "Silhouette" && record.view == view.Id);
                var sourcePath = Path.Combine(RepositoryRoot(),
                    $"BlenderSource/Characters/C1B-003/Renders/CHR_MasterCharacter_C1B_Blockout_r01_Silhouette_{view.Id}.png");
                var unityPath = Path.Combine(RepositoryRoot(), unityCapture.path);
                var sourceMask = ReadMask(sourcePath, threshold);
                var unityMask = ReadMask(unityPath, threshold);
                Require(sourceMask.Width == CaptureResolution && sourceMask.Height == CaptureResolution,
                    "C1B005_SOURCE_MASK_DIMENSIONS");
                Require(unityMask.Width == CaptureResolution && unityMask.Height == CaptureResolution,
                    "C1B005_UNITY_MASK_DIMENSIONS");
                var intersection = 0;
                var union = 0;
                for (var index = 0; index < sourceMask.Values.Length; index++)
                {
                    if (sourceMask.Values[index] && unityMask.Values[index]) intersection++;
                    if (sourceMask.Values[index] || unityMask.Values[index]) union++;
                }
                Require(union > 0, "C1B005_SILHOUETTE_MASK_EMPTY");
                var maximumBboxDrift = Mathf.Max(
                    Mathf.Abs(sourceMask.Bounds.MinX - unityMask.Bounds.MinX),
                    Mathf.Abs(sourceMask.Bounds.MinY - unityMask.Bounds.MinY),
                    Mathf.Abs(sourceMask.Bounds.MaxX - unityMask.Bounds.MaxX),
                    Mathf.Abs(sourceMask.Bounds.MaxY - unityMask.Bounds.MaxY));
                records.Add(new SilhouetteParityRecord
                {
                    view = view.Id,
                    threshold = threshold,
                    sourcePath = Path.GetRelativePath(RepositoryRoot(), sourcePath).Replace('\\', '/'),
                    unityPath = unityCapture.path,
                    width = CaptureResolution,
                    height = CaptureResolution,
                    sourceForegroundPixels = sourceMask.ForegroundCount,
                    unityForegroundPixels = unityMask.ForegroundCount,
                    sourceBounds = sourceMask.Bounds.ToRecord(),
                    unityBounds = unityMask.Bounds.ToRecord(),
                    intersectionOverUnionObserved = (double)intersection / union,
                    maximumBoundingBoxDriftPixels = maximumBboxDrift,
                    sourceSilhouetteHeightPixels = sourceMask.Bounds.Height,
                    maximumBoundingBoxDriftH = (double)maximumBboxDrift / sourceMask.Bounds.Height,
                    maximumBoundingBoxDriftHThreshold = 0.005,
                    iouApprovalThresholdApplied = false,
                    result = "PASS",
                });
            }
            return records.ToArray();
        }

        private static void ValidateSilhouetteParity(IReadOnlyCollection<SilhouetteParityRecord> records)
        {
            Require(records.Count == 4, "C1B005_SILHOUETTE_VIEW_COUNT");
            foreach (var record in records)
            {
                Require(record.threshold == 128 && record.width == 2048 && record.height == 2048,
                    "C1B005_SILHOUETTE_MASK_CONTRACT");
                Require(record.maximumBoundingBoxDriftH <= 0.005,
                    "C1B005_SILHOUETTE_BBOX_DRIFT_" + record.view);
                Require(!record.iouApprovalThresholdApplied && record.intersectionOverUnionObserved > 0.0,
                    "C1B005_SILHOUETTE_IOU_OBSERVED_ONLY");
            }
        }

        private static NeutralContrastRecord[] MeasureNeutralContrast(IEnumerable<CaptureRecord> captures)
        {
            const byte threshold = 128;
            var records = new List<NeutralContrastRecord>();
            foreach (var view in Views)
            {
                var neutral = captures.Single(record => record.style == "Neutral" && record.view == view.Id);
                var silhouette = captures.Single(record => record.style == "Silhouette" && record.view == view.Id);
                var neutralPixels = ReadPixels(Path.Combine(RepositoryRoot(), neutral.path));
                var mask = ReadMask(Path.Combine(RepositoryRoot(), silhouette.path), threshold);
                Require(neutralPixels.Width == mask.Width && neutralPixels.Height == mask.Height,
                    "C1B005_NEUTRAL_CONTRAST_DIMENSIONS");
                long foregroundR = 0, foregroundG = 0, foregroundB = 0;
                long backgroundR = 0, backgroundG = 0, backgroundB = 0;
                var foregroundCount = 0;
                var backgroundCount = 0;
                for (var index = 0; index < neutralPixels.Values.Length; index++)
                {
                    var pixel = neutralPixels.Values[index];
                    if (mask.Values[index])
                    {
                        foregroundR += pixel.r; foregroundG += pixel.g; foregroundB += pixel.b;
                        foregroundCount++;
                    }
                    else
                    {
                        backgroundR += pixel.r; backgroundG += pixel.g; backgroundB += pixel.b;
                        backgroundCount++;
                    }
                }
                Require(foregroundCount > 0 && backgroundCount > 0, "C1B005_NEUTRAL_CONTRAST_SAMPLE");
                var fg = new DoubleVectorRecord
                {
                    x = (double)foregroundR / foregroundCount,
                    y = (double)foregroundG / foregroundCount,
                    z = (double)foregroundB / foregroundCount,
                };
                var bg = new DoubleVectorRecord
                {
                    x = (double)backgroundR / backgroundCount,
                    y = (double)backgroundG / backgroundCount,
                    z = (double)backgroundB / backgroundCount,
                };
                var foregroundLuminance = (fg.x + fg.y + fg.z) / 3.0;
                var backgroundLuminance = (bg.x + bg.y + bg.z) / 3.0;
                records.Add(new NeutralContrastRecord
                {
                    view = view.Id,
                    foregroundMeanRgbObserved = fg,
                    backgroundMeanRgbObserved = bg,
                    foregroundLuminanceObserved = foregroundLuminance,
                    backgroundLuminanceObserved = backgroundLuminance,
                    absoluteLuminanceDifferenceObserved = Math.Abs(foregroundLuminance - backgroundLuminance),
                    approvalThresholdApplied = false,
                });
            }
            return records.ToArray();
        }

        private static PixelData ReadPixels(string path)
        {
            Require(File.Exists(path), "C1B005_PIXEL_FILE_MISSING");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                Require(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), "C1B005_PIXEL_DECODE");
                return new PixelData(texture.width, texture.height, texture.GetPixels32());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static MaskData ReadMask(string path, byte threshold)
        {
            Require(File.Exists(path), "C1B005_MASK_FILE_MISSING");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                Require(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), "C1B005_MASK_DECODE");
                var pixels = texture.GetPixels32();
                var values = new bool[pixels.Length];
                var minX = texture.width;
                var minY = texture.height;
                var maxX = -1;
                var maxY = -1;
                var count = 0;
                for (var y = 0; y < texture.height; y++)
                for (var x = 0; x < texture.width; x++)
                {
                    var pixel = pixels[y * texture.width + x];
                    var foreground = (pixel.r + pixel.g + pixel.b) / 3 < threshold;
                    values[y * texture.width + x] = foreground;
                    if (!foreground) continue;
                    count++;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
                Require(count > 0, "C1B005_MASK_NO_FOREGROUND");
                return new MaskData(texture.width, texture.height, values, count,
                    new PixelBounds(minX, minY, maxX, maxY));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void WriteInspection(ImportInspection inspection)
        {
            var outputPath = Path.Combine(RepositoryRoot(), "artifacts/evidence/G0/C1B-005/UnityImportInspection.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, JsonUtility.ToJson(inspection, true) + "\n", new UTF8Encoding(false));
        }

        private static Material CreateNeutralMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null, "C1B005_URP_LIT_SHADER");
            var material = new Material(shader) { name = "C1B005_QA_Neutral_Transient" };
            material.SetColor("_BaseColor", new Color(0.90f, 0.90f, 0.90f, 1.0f));
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Smoothness", 0.25f);
            return material;
        }

        private static Material CreateSilhouetteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            Require(shader != null, "C1B005_URP_UNLIT_SHADER");
            var material = new Material(shader) { name = "C1B005_QA_Silhouette_Transient" };
            material.SetColor("_BaseColor", new Color(0.015f, 0.015f, 0.015f, 1.0f));
            return material;
        }

        private static void SetMaterial(IEnumerable<MeshRenderer> renderers, Material material)
        {
            foreach (var renderer in renderers)
            {
                var count = Math.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Enumerable.Repeat(material, count).ToArray();
            }
        }

        private static void ConfigureCamera(Camera camera, Bounds bounds, ViewDefinition view)
        {
            var direction = view.Direction.normalized;
            var rotation = Quaternion.LookRotation(direction, view.Up);
            camera.transform.SetPositionAndRotation(bounds.center - direction * 4.0f, rotation);
            var cameraRight = camera.transform.right;
            var cameraUp = camera.transform.up;
            var halfWidth = 0.0f;
            var halfHeight = 0.0f;
            foreach (var corner in BoundsCorners(bounds))
            {
                var offset = corner - bounds.center;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, cameraRight)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, cameraUp)));
            }
            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth) * (1.0f + 2.0f * PaddingRatioPerSide);
        }

        private static void RenderCamera(Camera camera, string path)
        {
            var renderTexture = new RenderTexture(CaptureResolution, CaptureResolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "C1B005_QA_RenderTexture",
            };
            var texture = new Texture2D(CaptureResolution, CaptureResolution, TextureFormat.RGBA32, false, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                renderTexture.Create();
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, CaptureResolution, CaptureResolution), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(texture);
            }
        }

        private static Bounds CombineBounds(IReadOnlyList<MeshRenderer> renderers)
        {
            Require(renderers.Count > 0, "C1B005_BOUNDS_RENDERERS");
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Count; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static GeometrySignatureRecord ComputeGeometrySignature(GameObject root)
        {
            var points = new StringBuilder();
            var surfaces = new StringBuilder();
            var vertexTotal = 0;
            var normalTotal = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)
                .OrderBy(item => item.gameObject.name, StringComparer.Ordinal))
            {
                var mesh = filter.sharedMesh;
                Require(mesh != null, "C1B005_SIGNATURE_MESH_MISSING");
                using (var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var meshData = meshDataArray[0];
                    var vertices = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    var normals = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    try
                    {
                        meshData.GetVertices(vertices);
                        Require(meshData.HasVertexAttribute(VertexAttribute.Normal),
                            "C1B005_SIGNATURE_NORMAL_ATTRIBUTE");
                        meshData.GetNormals(normals);
                        var records = new List<string>(meshData.vertexCount);
                        foreach (var index in Enumerable.Range(0, meshData.vertexCount))
                        {
                            var position = root.transform.InverseTransformPoint(
                                filter.transform.TransformPoint(vertices[index]));
                            var normal = root.transform.InverseTransformDirection(
                                filter.transform.TransformDirection(normals[index])).normalized;
                            var point = QuantizedVector(position);
                            points.Append(filter.gameObject.name).Append(':').Append(point).Append('\n');
                            records.Add(point + ":" + QuantizedVector(normal));
                        }
                        records.Sort(StringComparer.Ordinal);
                        foreach (var record in records)
                        {
                            surfaces.Append(filter.gameObject.name).Append(':').Append(record).Append('\n');
                        }
                        vertexTotal += vertices.Length;
                        normalTotal += normals.Length;
                    }
                    finally
                    {
                        vertices.Dispose();
                        normals.Dispose();
                    }
                }
            }
            return new GeometrySignatureRecord
            {
                quantizationPerH = 1000000,
                vertexCount = vertexTotal,
                normalCount = normalTotal,
                pointSha256 = Sha256Bytes(Encoding.UTF8.GetBytes(points.ToString())),
                surfaceSha256 = Sha256Bytes(Encoding.UTF8.GetBytes(surfaces.ToString())),
            };
        }

        private static string QuantizedVector(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                Mathf.RoundToInt(value.x * 1000000.0f),
                Mathf.RoundToInt(value.y * 1000000.0f),
                Mathf.RoundToInt(value.z * 1000000.0f));
        }

        private static IEnumerable<Vector3> BoundsCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
            for (var z = 0; z < 2; z++)
            {
                yield return new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            }
        }

        private static Vector2Int ReadPngDimensions(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Require(bytes.Length >= 24, "C1B005_PNG_HEADER_SIZE");
            Require(bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47, "C1B005_PNG_SIGNATURE");
            return new Vector2Int(ReadBigEndianInt(bytes, 16), ReadBigEndianInt(bytes, 20));
        }

        private static int ReadBigEndianInt(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static float MaximumComponentAbs(Vector3 value)
        {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static bool Approximately(VectorRecord actual, Vector3 expected)
        {
            return Mathf.Abs(actual.x - expected.x) <= Epsilon
                && Mathf.Abs(actual.y - expected.y) <= Epsilon
                && Mathf.Abs(actual.z - expected.z) <= Epsilon;
        }

        private static bool ApproximatelyEulerZero(VectorRecord actual)
        {
            return Mathf.Abs(Mathf.DeltaAngle(actual.x, 0.0f)) <= Epsilon
                && Mathf.Abs(Mathf.DeltaAngle(actual.y, 0.0f)) <= Epsilon
                && Mathf.Abs(Mathf.DeltaAngle(actual.z, 0.0f)) <= Epsilon;
        }

        private static string AbsoluteProjectPath(string assetPath)
        {
            return Path.Combine(ProjectRoot(), assetPath);
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string RepositoryRoot()
        {
            return Directory.GetParent(ProjectRoot()).FullName;
        }

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BytesToHex(sha.ComputeHash(stream));
            }
        }

        private static string Sha256Bytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BytesToHex(sha.ComputeHash(bytes));
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        private static void Require(bool condition, string rule)
        {
            if (!condition)
            {
                throw new InvalidOperationException(rule);
            }
        }

        private readonly struct ViewDefinition
        {
            public readonly string Id;
            public readonly Vector3 Direction;
            public readonly Vector3 Up;

            public ViewDefinition(string id, Vector3 direction, Vector3 up)
            {
                Id = id;
                Direction = direction;
                Up = up;
            }
        }

        private readonly struct FillDefinition
        {
            public readonly string Id;
            public readonly Vector3 BlenderEulerDegrees;
            public readonly Vector3 UnityRay;

            public FillDefinition(string id, Vector3 blenderEulerDegrees, Vector3 unityRay)
            {
                Id = id;
                BlenderEulerDegrees = blenderEulerDegrees;
                UnityRay = unityRay;
            }
        }

        private readonly struct MaskData
        {
            public readonly int Width;
            public readonly int Height;
            public readonly bool[] Values;
            public readonly int ForegroundCount;
            public readonly PixelBounds Bounds;

            public MaskData(int width, int height, bool[] values, int foregroundCount, PixelBounds bounds)
            {
                Width = width;
                Height = height;
                Values = values;
                ForegroundCount = foregroundCount;
                Bounds = bounds;
            }
        }

        private readonly struct PixelData
        {
            public readonly int Width;
            public readonly int Height;
            public readonly Color32[] Values;

            public PixelData(int width, int height, Color32[] values)
            {
                Width = width;
                Height = height;
                Values = values;
            }
        }

        private readonly struct PixelBounds
        {
            public readonly int MinX;
            public readonly int MinY;
            public readonly int MaxX;
            public readonly int MaxY;
            public int Height => MaxY - MinY + 1;

            public PixelBounds(int minX, int minY, int maxX, int maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public PixelBoundsRecord ToRecord()
            {
                return new PixelBoundsRecord { minX = MinX, minY = MinY, maxX = MaxX, maxY = MaxY };
            }
        }
    }

    [Serializable]
    public sealed class ImportInspection
    {
        public int schemaVersion;
        public string taskId;
        public string state;
        public string candidateStatus;
        public string result;
        public string sourceOwner;
        public string sourcePath;
        public long sourceBytes;
        public string sourceSha256;
        public string modelInteropProfileId;
        public string modelInteropProfileRevision;
        public string baseImporterPresetId;
        public string baseImporterPresetRevision;
        public string baseImporterSettingsSha256;
        public string importerOverrideId;
        public string importerOverrideRevision;
        public string importerOverrideSettingsSha256;
        public bool importerSettingsMatched;
        public string modelGuid;
        public string prefabPath;
        public string prefabGuid;
        public VectorRecord combinedBoundsCenter;
        public VectorRecord combinedBoundsSize;
        public float groundHeight;
        public int meshObjectCount;
        public string[] meshObjectNames;
        public int landmarkCount;
        public LandmarkRecord[] landmarks;
        public float maximumLandmarkDeviationH;
        public VectorRecord rootScale;
        public VectorRecord rootRotationEuler;
        public VectorRecord rootForward;
        public float rootDeterminant;
        public VectorRecord exportRootScale;
        public VectorRecord exportRootRotationEuler;
        public VectorRecord exportRootForward;
        public float exportRootDeterminant;
        public int negativeScaleCount;
        public int axisReversalCount;
        public int armatureCount;
        public int animatorCount;
        public int colliderCount;
        public int cameraCount;
        public int lightCount;
        public int importedMaterialAssetCount;
        public GeometrySignatureRecord geometrySignature;
        public CaptureRecord[] captures;
        public NeutralStageRecord neutralStage;
        public SilhouetteParityRecord[] silhouetteParity;
        public NeutralContrastRecord[] neutralContrast;
        public ActionStaticReview actionStaticReview;
        public int playerBuildsExecuted;
        public int playModeTestsExecuted;
        public int gameplayComponentsCreated;
        public int manualTransformCorrections;
        public bool animationNaturalnessClaimed;
    }

    [Serializable]
    public sealed class GeometrySignatureRecord
    {
        public int quantizationPerH;
        public int vertexCount;
        public int normalCount;
        public string pointSha256;
        public string surfaceSha256;
    }

    [Serializable]
    public sealed class NeutralStageRecord
    {
        public bool qaOnly;
        public bool productLighting;
        public string shader;
        public float offWhiteLinear;
        public float keyRelativeIntensity;
        public VectorRecord keyBlenderEulerDegrees;
        public VectorRecord keyBlenderRayLocalMinusZ;
        public VectorRecord keyUnityRay;
        public int fillLightCount;
        public float fillTotalRelativeIntensity;
        public float fillComponentRelativeIntensity;
        public string[] fillIds;
        public VectorRecord[] fillBlenderEulerDegrees;
        public VectorRecord[] fillUnityRays;

        public static NeutralStageRecord Create()
        {
            return new NeutralStageRecord
            {
                qaOnly = true,
                productLighting = false,
                shader = "Universal Render Pipeline/Lit",
                offWhiteLinear = 0.90f,
                keyRelativeIntensity = 1.0f,
                keyBlenderEulerDegrees = VectorRecord.From(new Vector3(50.0f, -30.0f, 0.0f)),
                keyBlenderRayLocalMinusZ = VectorRecord.From(new Vector3(0.321393818f, 0.766044438f, -0.556670368f)),
                keyUnityRay = VectorRecord.From(new Vector3(0.321393818f, -0.556670368f, -0.766044438f)),
                fillLightCount = 3,
                fillTotalRelativeIntensity = 0.35f,
                fillComponentRelativeIntensity = 0.116666667f,
                fillIds = new[] { "Back", "Left", "Right" },
                fillBlenderEulerDegrees = new[]
                {
                    VectorRecord.From(new Vector3(130.0f, 150.0f, 180.0f)),
                    VectorRecord.From(new Vector3(80.0f, 90.0f, 0.0f)),
                    VectorRecord.From(new Vector3(80.0f, -90.0f, 0.0f)),
                },
                fillUnityRays = new[]
                {
                    VectorRecord.From(new Vector3(-0.321393758f, -0.556670427f, 0.766044438f)),
                    VectorRecord.From(new Vector3(-0.173648223f, 0.000000008f, -0.98480773f)),
                    VectorRecord.From(new Vector3(0.173648223f, 0.000000008f, -0.98480773f)),
                },
            };
        }
    }

    [Serializable]
    public sealed class SilhouetteParityRecord
    {
        public string view;
        public byte threshold;
        public string sourcePath;
        public string unityPath;
        public int width;
        public int height;
        public int sourceForegroundPixels;
        public int unityForegroundPixels;
        public PixelBoundsRecord sourceBounds;
        public PixelBoundsRecord unityBounds;
        public double intersectionOverUnionObserved;
        public int maximumBoundingBoxDriftPixels;
        public int sourceSilhouetteHeightPixels;
        public double maximumBoundingBoxDriftH;
        public double maximumBoundingBoxDriftHThreshold;
        public bool iouApprovalThresholdApplied;
        public string result;
    }

    [Serializable]
    public struct PixelBoundsRecord
    {
        public int minX;
        public int minY;
        public int maxX;
        public int maxY;
    }

    [Serializable]
    public sealed class NeutralContrastRecord
    {
        public string view;
        public DoubleVectorRecord foregroundMeanRgbObserved;
        public DoubleVectorRecord backgroundMeanRgbObserved;
        public double foregroundLuminanceObserved;
        public double backgroundLuminanceObserved;
        public double absoluteLuminanceDifferenceObserved;
        public bool approvalThresholdApplied;
    }

    [Serializable]
    public struct DoubleVectorRecord
    {
        public double x;
        public double y;
        public double z;
    }

    [Serializable]
    public struct VectorRecord
    {
        public float x;
        public float y;
        public float z;

        public static VectorRecord From(Vector3 value)
        {
            return new VectorRecord { x = value.x, y = value.y, z = value.z };
        }
    }

    [Serializable]
    public sealed class LandmarkRecord
    {
        public string id;
        public VectorRecord expected;
        public VectorRecord actual;
        public float maximumDeviationH;
    }

    [Serializable]
    public sealed class CaptureRecord
    {
        public string style;
        public string view;
        public string path;
        public long bytes;
        public string sha256;
        public int width;
        public int height;
        public float orthographicSize;
        public float boundsPaddingRatioPerSide;
    }

    [Serializable]
    public sealed class ActionStaticReview
    {
        public string state;
        public int fileCount;
        public int png2048Matches;
        public string orderedBundleSha256;
        public string[] checklistIds;
        public int animationActionsPresent;
        public bool motionNaturalnessClaimed;
    }
}
