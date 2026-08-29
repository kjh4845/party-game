using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectHotfix.Editor.C1B005
{
    public static class C1B005ImportContract
    {
        public const string TaskId = "C1B-005";
        public const string ModelPath =
            "Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.fbx";
        public const string PrefabPath =
            "Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.prefab";
        public const string ModelInteropProfileId = "ModelInteropProfile-ART-001-r02";
        public const string ModelInteropProfileRevision = "r02";
        public const string BaseImportPresetId = "PHX-UNITY-MODEL-IMPORT-r01";
        public const string BaseImportPresetRevision = "r01";
        public const string BaseImportSettingsSha256 =
            "4a00f7ea259ef98d17a932948ca2ebfde7bc7173e632ac5f5b4ddc81fba94cf9";
        public const string ImportOverrideId = "PHX-UNITY-C1B-BLOCKOUT-r02";
        public const string ImportOverrideRevision = "r02";
        public const string ImportOverrideSettingsSha256 =
            "6e7dc965bf635789ed6447e53c873dd25fab915a1698e54db936564ded24303d";
        public const string FbxSha256 =
            "e2049505f6be24508783710c83691445b28fa39cad892bcd88aa0e2ad4807d9d";
        public const long FbxBytes = 70268;
        public const string ActionReviewBundleSha256 =
            "9f9cbe63a2cf5ec0cf606e89f5114ddc31c86e2a334a4853ae9ca4d467d24a1f";
        public const string ExpectedImportedPointSignatureSha256 =
            "cff38fcc751280c16e2efa77c061090bb8b5302b569b1f26ce202d674f39d3b6";
        public const string ExpectedImportedSurfaceSignatureSha256 =
            "289d5f3e8f9105e54b5293d587c51e58a3f73d9766fab9c9cba7a5fe26ecc5f6";
        public const float MaximumHeightRatioDeviation = 0.005f;

        public static readonly string[] ExpectedMeshObjectNames =
        {
            "CHR_C1B005_Arm_L",
            "CHR_C1B005_Arm_R",
            "CHR_C1B005_Head",
            "CHR_C1B005_Leg_L",
            "CHR_C1B005_Leg_R",
            "CHR_C1B005_Torso",
        };

        public static readonly LandmarkExpectation[] ExpectedLandmarks =
        {
            new LandmarkExpectation("LM_Crown", 0.0f, 1.0f, 0.0f),
            new LandmarkExpectation("LM_Chin", 0.0f, 0.8f, 0.0f),
            new LandmarkExpectation("LM_Shoulder_L", -0.205f, 0.69f, 0.0f),
            new LandmarkExpectation("LM_Shoulder_R", 0.205f, 0.69f, 0.0f),
            new LandmarkExpectation("LM_Elbow_L", -0.235f, 0.52f, 0.0f),
            new LandmarkExpectation("LM_Elbow_R", 0.235f, 0.52f, 0.0f),
            new LandmarkExpectation("LM_ForearmTerminal_L", -0.235f, 0.405f, 0.005f),
            new LandmarkExpectation("LM_ForearmTerminal_R", 0.235f, 0.405f, 0.005f),
            new LandmarkExpectation("LM_Chest", 0.0f, 0.585f, 0.0f),
            new LandmarkExpectation("LM_Pelvis", 0.0f, 0.395f, 0.0f),
            new LandmarkExpectation("LM_Crotch", 0.0f, 0.31f, 0.0f),
            new LandmarkExpectation("LM_Hip_L", -0.095f, 0.315f, 0.0f),
            new LandmarkExpectation("LM_Hip_R", 0.095f, 0.315f, 0.0f),
            new LandmarkExpectation("LM_Knee_L", -0.105f, 0.17f, 0.0f),
            new LandmarkExpectation("LM_Knee_R", 0.105f, 0.17f, 0.0f),
            new LandmarkExpectation("LM_LowerLegTerminal_L", -0.11f, 0.065f, 0.012f),
            new LandmarkExpectation("LM_LowerLegTerminal_R", 0.11f, 0.065f, 0.012f),
        };

        public static void ApplyExactImporterSettings(ModelImporter importer)
        {
            if (importer == null)
            {
                throw new ArgumentNullException(nameof(importer));
            }

            importer.globalScale = 1.0f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;
            importer.weldVertices = true;
            importer.preserveHierarchy = true;
            importer.addCollider = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
        }

        public static IReadOnlyList<string> ValidateExactImporterSettings(ModelImporter importer)
        {
            var errors = new List<string>();
            if (importer == null)
            {
                errors.Add("MODEL_IMPORTER_MISSING");
                return errors;
            }

            Check(Mathf.Approximately(importer.globalScale, 1.0f), "GLOBAL_SCALE", errors);
            Check(importer.useFileScale, "USE_FILE_SCALE", errors);
            Check(!importer.bakeAxisConversion, "BAKE_AXIS_CONVERSION", errors);
            Check(importer.importNormals == ModelImporterNormals.Import, "IMPORT_NORMALS", errors);
            Check(importer.importTangents == ModelImporterTangents.None, "IMPORT_TANGENTS", errors);
            Check(importer.meshCompression == ModelImporterMeshCompression.Off, "MESH_COMPRESSION", errors);
            Check(!importer.isReadable, "IS_READABLE", errors);
            Check(!importer.optimizeMeshPolygons, "OPTIMIZE_POLYGONS", errors);
            Check(!importer.optimizeMeshVertices, "OPTIMIZE_VERTICES", errors);
            Check(importer.weldVertices, "WELD_VERTICES", errors);
            Check(importer.preserveHierarchy, "PRESERVE_HIERARCHY", errors);
            Check(!importer.addCollider, "ADD_COLLIDER", errors);
            Check(!importer.importCameras, "IMPORT_CAMERAS", errors);
            Check(!importer.importLights, "IMPORT_LIGHTS", errors);
            Check(!importer.generateSecondaryUV, "GENERATE_SECONDARY_UV", errors);
            Check(importer.materialImportMode == ModelImporterMaterialImportMode.None, "IMPORT_MATERIALS", errors);
            Check(!importer.importAnimation, "IMPORT_ANIMATION", errors);
            Check(importer.animationType == ModelImporterAnimationType.None, "ANIMATION_TYPE", errors);
            Check(!importer.importBlendShapes, "IMPORT_BLEND_SHAPES", errors);
            return errors;
        }

        private static void Check(bool condition, string id, ICollection<string> errors)
        {
            if (!condition)
            {
                errors.Add(id);
            }
        }
    }

    [Serializable]
    public struct LandmarkExpectation
    {
        public string Name;
        public Vector3 Position;

        public LandmarkExpectation(string name, float x, float y, float z)
        {
            Name = name;
            Position = new Vector3(x, y, z);
        }
    }
}
