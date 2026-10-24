using System;
using System.Collections.Generic;
using System.IO;
using ProjectHotfix.Presentation;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ProjectHotfix.PhysicsJump
{
    // Local development harness. Generated source stays outside production Assets.
    [DefaultExecutionOrder(-200)]
    public sealed class PhysicsJumpReview : MonoBehaviour
    {
        public PhysicsJumpMotor Motor;
        public ProceduralJumpPose Pose;
        public ProceduralFreeArmSwing Arms;
        public ApprovedPunchPose Punches;
        public HandStrikeReview Hands;
        public GrabArmPose GrabPose;
        public PunchPoseLibrary PunchLibrary;
        public bool ReadKeyboard = true;
        public bool ExternalPhysicsPose;
        public FixedReviewPose FixedPose;
        private Vector3 _spawn;
        private bool _presentationBound;

        public static string TransportPath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../.codex_tmp/unity-physics-jump-20260905/ApprovedCharacter.bytes"));
        public static string LatWeightTransportPath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../.codex_tmp/lat-weight-20260909/Selected/LatCharacter.bytes"));

        // Every rig needs the approved punch library; it is kept in the repository (BlenderSource/Punch), the old local
        // export under .codex_tmp only as a fallback.
        public static string PunchTransportPath
        {
            get
            {
                var kept = Path.GetFullPath(Path.Combine(Application.dataPath, "../../BlenderSource/Punch/ApprovedPunch.json"));
                return File.Exists(kept) ? kept : Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/punch-unity-20260906/ApprovedPunch.json"));
            }
        }

        public bool TryPreviewPunch(int side)
        {
            if (!Motor.Grounded || Punches == null || side < 0 || side > 1) return false;
            FixedPose?.RestoreForEvaluation();
            return Punches.TryStart(side, side == 0 ? Arms.LeftAvailable : Arms.RightAvailable);
        }

        public void UseFootPlantPreset()
        {
            Motor.MoveSpeed = .70f;
            Motor.SprintMultiplier = 1.5f;
            Pose.WalkReferenceSpeed = .70f;
            Pose.SprintReferenceSpeed = 1.05f;
            Pose.WalkCadence = 3f;
            Pose.SprintCadence = 4.5f;
            Pose.WorldFootPlant = true;
            Pose.FootPlantStrideFullWeight=.75f;
            Pose.FootPlantWalkLift=Pose.FootPlantSprintLift=.010f;
            Pose.FootPlantPelvisDrop=.020f;Pose.FootPlantPelvisBob=.002f;
            Pose.PelvisSwayScale=Pose.PelvisRollScale=Pose.FreeArmSwingScale=1f;
            Pose.LimitCombinedShortening=false;Pose.CombinedShorteningLimit=.034f;
            Pose.TurnStrideMinimum=1;Pose.UseStepState=Pose.ResetAfterExternalPose=false;
        }

        public void UseCameraGaitCandidate()
        {
            UseFootPlantPreset();
            Pose.WalkCadence=2.4f;Pose.SprintCadence=3.6f;
            Pose.FootPlantStrideFullWeight=1f;
            Pose.FootPlantWalkLift=.018f;Pose.FootPlantSprintLift=.018f;
            Pose.FootPlantPelvisDrop=.010f;Pose.FootPlantPelvisBob=.014f;
            Pose.PelvisSwayScale=.6f;Pose.PelvisRollScale=.6f;Pose.FreeArmSwingScale=.75f;
            Pose.LimitCombinedShortening=true;Pose.CombinedShorteningLimit=.030f;
            Pose.TurnStrideMinimum=.65f;Pose.UseStepState=Pose.ResetAfterExternalPose=true;
        }

        // Scale 2 is the approved shared-arena profile; lower scales preserve comparisons.
        public void UseMovementSpeedCandidate(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 1f || scale > 2f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            UseCameraGaitCandidate();
            Motor.MoveSpeed *= scale; Motor.Acceleration = 10f * scale;
            Pose.WalkReferenceSpeed *= scale; Pose.SprintReferenceSpeed *= scale;
            var cadenceScale = scale;
            Pose.WalkCadence *= cadenceScale; Pose.SprintCadence *= cadenceScale;
            Pose.FootPlantWalkLift = Pose.FootPlantSprintLift = .018f - .002f * (scale - 1f);
            Pose.FootPlantPelvisBob = .014f - .002f * (scale - 1f);
        }

        private void Start()
        {
            _spawn = transform.position;
            if (_presentationBound) { EnsureFixedPose(); return; }
            // Editor-created review scenes lose nonserialized pose caches on
            // entering Play. Runtime factories already bind them explicitly.
            Pose.Bind(Pose.transform); Arms.Bind();
            if (Punches != null) Punches.Bind(PunchLibrary);
            if (GrabPose != null)
            {
                var frame=PunchLibrary.clips[0].frames[0];
                GrabPose.Bind(Vector3.Distance(frame.heads[3],frame.tails[3]),PunchLibrary.clips[0].Sample(7f/30,0),PunchLibrary.clips[1].Sample(7f/30,0));
            }
            _presentationBound = true;
            EnsureFixedPose();
        }
        private void EnsureFixedPose()
        { if(FixedPose==null)FixedPose=gameObject.AddComponent<FixedReviewPose>();FixedPose.Bind(this); }
        private void FixedUpdate()
        {
            if (Motor.ManualStep || !ReadKeyboard || Keyboard.current == null) return;
            var keys = Keyboard.current;
            if (Hands != null)
            {
                if (keys.escapeKey.wasPressedThisFrame) Hands.SetInputSuppressed(!Hands.InputSuppressed);
                if (Mouse.current != null) Hands.SubmitFixedMouse();
                if (Hands.InputSuppressed) { Motor.MoveInput = Vector2.zero; Motor.SprintRequested = false; return; }
            }
            Motor.MoveInput = new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            Motor.SprintRequested = keys.leftShiftKey.isPressed;
            if (keys.spaceKey.wasPressedThisFrame) Motor.RequestJump();
            if (Hands == null && keys.jKey.wasPressedThisFrame) TryPreviewPunch(0);
            if (Hands == null && keys.kKey.wasPressedThisFrame) TryPreviewPunch(1);
            if (keys.rKey.wasPressedThisFrame) { Motor.ResetAt(_spawn); if (Hands != null) Hands.ResetTargets(); }
            GetComponent<RagdollRecoveryReview>()?.ReadFixedDebugInput();
        }
        private void LateUpdate() { if (!Motor.ManualStep && FixedPose!=null) FixedPose.Present(ReviewFixedLoop.RenderAlpha); }
        public void ApplyPresentation(float deltaTime)
        {
            EvaluatePresentation(deltaTime);
            if(Motor.ManualStep&&FixedPose!=null&&!FixedPose.Advancing)FixedPose.CaptureEvaluatedPose();
        }
        internal void EvaluatePresentation(float deltaTime)
        {
            if (ExternalPhysicsPose) return;
            var state = Pose.UseStepState ? Motor.StepPoseState : Motor.PresentationState;
            var hanging=Pose.GetComponent<HangingBodyPose>();
            hanging?.RestoreBaseline();
            Pose.Apply(state, deltaTime);
            if (Punches != null)
            {
                if (Hands != null) Punches.ApplyAuthority(Hands.State.Capture(), Arms.LeftAvailable, Arms.RightAvailable);
                else Punches.Apply(deltaTime, state.ResetVersion, Arms.LeftAvailable, Arms.RightAvailable);
            }
            if (GrabPose != null) GrabPose.Apply(Hands.Grabs.Capture(), deltaTime, Arms.LeftAvailable, Arms.RightAvailable,
                Punches != null && Punches.IsActive(0), Punches != null && Punches.IsActive(1));
            if (Arms != null) Arms.Apply(Pose, deltaTime, state.ResetVersion,
                (Punches != null && Punches.IsActive(0)) || (GrabPose != null && GrabPose.IsControlling(0)),
                (Punches != null && Punches.IsActive(1)) || (GrabPose != null && GrabPose.IsControlling(1)));
            if(hanging!=null)hanging.Apply(GetComponent<LedgeGrabReview>().HangingState, Hands.Grabs.Capture(), GrabPose, deltaTime,
                Arms.LeftAvailable&&!Punches.IsActive(0),Arms.RightAvailable&&!Punches.IsActive(1));
        }
        public void EnableHandStrikeReview()
        {
            if (Hands != null) return;
            if (PunchLibrary == null) throw new InvalidOperationException("Export the approved punch before opening hand interaction review.");
            Hands = gameObject.AddComponent<HandStrikeReview>(); Hands.Actor = this; Hands.ConfigureThreshold(.15);
        }

        public void EnableGrabReview()
        {
            EnableHandStrikeReview();
            if (Hands.Grabs != null) return;
            Hands.Grabs = gameObject.AddComponent<GrabPhysicsReview>(); Hands.Grabs.Hands = Hands;
            GrabPose = Pose.gameObject.AddComponent<GrabArmPose>();
            var frame = PunchLibrary.clips[0].frames[0]; GrabPose.Bind(Vector3.Distance(frame.heads[3], frame.tails[3]),
                PunchLibrary.clips[0].Sample(7f / 30f, 0), PunchLibrary.clips[1].Sample(7f / 30f, 0));
        }

        private void OnGUI()
        {
            if (!ReadKeyboard) return;
            if (Hands != null)
                GUI.Label(new Rect(20, 108, 850, 70), (Hands.Grabs == null ? "LMB/RMB tap: punch | hold: GrabSeek (constraint pending) | ESC: input menu\n" : "LMB/RMB tap: punch | hold: grab | last hand release: throw | ESC: input menu\n")
                    + $"Left: {Hands.State.Phase(0)}  Right: {Hands.State.Phase(1)}  Hits: {Hands.State.ConfirmedHitCount}  Input menu: {Hands.InputSuppressed}");
            GUI.Label(new Rect(20, 20, 700, 85), "Physics prototype | SPACE jump | WASD move | SHIFT sprint | R reset | " + (Hands == null ? "J/K punch preview\n" : "LMB/RMB hands\n")
                + $"Grounded: {Motor.Grounded}   Jumps: {Motor.JumpCount}   Vertical speed: {Motor.Body.linearVelocity.y:F2} m/s\n"
                + "Rigidbody motion + procedural lower-body pose. No jump animation clips.");
        }

        public static PhysicsJumpReview Create(Vector3 position, bool useFootPlant = true, string skinTransportPath = null)
        {
            skinTransportPath = skinTransportPath ?? TransportPath;
            if (!File.Exists(skinTransportPath)) throw new FileNotFoundException("Missing character review transport.", skinTransportPath);
            var root = new GameObject("PhysicsJump_ReviewOnly"); root.transform.position = position;
            root.layer = 2; // Ignore Raycast; physical collisions remain enabled.
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.height = 1f; capsule.radius = .17f;
            capsule.center = new Vector3(0, .5f, 0);
            var body = root.AddComponent<Rigidbody>(); body.mass = 20f;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var motor = root.AddComponent<PhysicsJumpMotor>();
            var visual = new GameObject("ApprovedLowerWeightVisual"); visual.transform.SetParent(root.transform, false);
            LoadSkin(visual.transform, skinTransportPath);
            var surfaceMinimum = float.PositiveInfinity;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                surfaceMinimum = Mathf.Min(surfaceMinimum, skin.sharedMesh.bounds.min.y);
            // The source's neutral sole is slightly below its origin. Align the
            // review collider to that sole without translating or editing the skin.
            capsule.center = new Vector3(0f, .5f + surfaceMinimum, 0f);
            var pose = visual.AddComponent<ProceduralJumpPose>(); pose.Bind(visual.transform);
            var arms = visual.AddComponent<ProceduralFreeArmSwing>(); arms.Bind();
            var review = root.AddComponent<PhysicsJumpReview>(); review.Motor = motor; review.Pose = pose;
            review.Arms = arms;
            if (File.Exists(PunchTransportPath))
            {
                var library = JsonUtility.FromJson<PunchPoseLibrary>(File.ReadAllText(PunchTransportPath));
                if (library.sourceSha256 != "943ee808a0d1f8a1075ef9c5615444a1f2c28bba6597b51c94ab821ae58ca091")
                    throw new InvalidDataException("Punch source is not the visually approved candidate.");
                review.PunchLibrary = library;
                review.Punches = visual.AddComponent<ApprovedPunchPose>(); review.Punches.Bind(library);
            }
            if (useFootPlant) review.UseFootPlantPreset();
            review._presentationBound = true;
            review.EnsureFixedPose();
            return review;
        }

        [Serializable] private sealed class Header { public int version; public BoneRecord[] bones; public MeshRecord[] meshes; public NormalPoseRecord[] normalPoses; public SurfacePoseRecord[] surfacePoses; public HelperRecord[] shoulderHelpers; public float[] idleArmDirection; public float[] armColliderRadii; public int[] contactSearch; public bool ignoreUpperArmTorso; public float[] torsoColliderRadii; }
        [Serializable] private sealed class HelperRecord { public string name; public float fraction; }
        private static readonly string[] HelperNames={"ShoulderBlend_L","ShoulderBlend_R","ShoulderBlendInner_L","ShoulderBlendInner_R","ShoulderBlendOuter_L","ShoulderBlendOuter_R"};
        // 22 core bones, optionally followed by the skin-only shoulder helpers
        // (2 = one half-rotation helper per side, 6 = quarter/half/three-quarter).
        private static bool ValidRig(BoneRecord[] bones)
        {
            if(bones.Length!=22&&bones.Length!=24&&bones.Length!=28)return false;
            for(var i=22;i<bones.Length;i++)if(bones[i].name!=HelperNames[i-22])return false;
            return true;
        }
        [Serializable] private sealed class BoneRecord { public string name; public int parent; public float[] position; public float[] rotation; }
        [Serializable] private sealed class MeshRecord { public string name; public int vertices; public int triangleIndices; }
        [Serializable] private sealed class NormalPoseRecord { public string name; public int side; public float[] rotation; public bool hasNormals; }
        [Serializable] private sealed class SurfacePoseRecord { public string name; public int side, mode; public float[] rotation, elbowRotation, torsoRotation; public bool hasDelta; }

        internal static void LoadSkin(Transform root, string skinTransportPath)
        {
            using (var reader = new BinaryReader(File.OpenRead(skinTransportPath)))
            {
                var header = JsonUtility.FromJson<Header>(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())));
                if ((header.version < 1||header.version > 3) || !ValidRig(header.bones)) throw new InvalidDataException("Unexpected review rig.");
                var bones = new Transform[header.bones.Length];
                for (var i = 0; i < bones.Length; i++) bones[i] = new GameObject(header.bones[i].name).transform;
                for (var i = 0; i < bones.Length; i++)
                {
                    var record = header.bones[i]; var bone = bones[i];
                    bone.SetParent(record.parent < 0 ? root : bones[record.parent], false);
                    bone.localPosition = new Vector3(record.position[0], record.position[1], record.position[2]);
                    bone.localRotation = new Quaternion(record.rotation[0], record.rotation[1], record.rotation[2], record.rotation[3]);
                }
                var fractions=new Dictionary<string,float>();
                if(header.shoulderHelpers!=null)foreach(var helper in header.shoulderHelpers)fractions[helper.name]=helper.fraction;
                // Pose writers apply the helpers each step; at load they keep their rest.
                ShoulderHelperRig.Bind(root,bones,fractions);
                var bindposes = new Matrix4x4[bones.Length];
                for (var i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(.84f, .88f, .93f)); material.SetFloat("_Smoothness", .22f);
                SkinnedMeshRenderer bodySkin=null;
                foreach (var record in header.meshes)
                {
                    var vertices = new Vector3[record.vertices]; var normals = new Vector3[record.vertices];
                    for (var i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    for (var i = 0; i < normals.Length; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var weights = new BoneWeight[record.vertices];
                    for (var i = 0; i < weights.Length; i++) weights[i] = new BoneWeight {
                        boneIndex0 = reader.ReadInt32(), boneIndex1 = reader.ReadInt32(), boneIndex2 = reader.ReadInt32(), boneIndex3 = reader.ReadInt32() };
                    for (var i = 0; i < weights.Length; i++) {
                        weights[i].weight0 = reader.ReadSingle(); weights[i].weight1 = reader.ReadSingle();
                        weights[i].weight2 = reader.ReadSingle(); weights[i].weight3 = reader.ReadSingle(); }
                    var triangles = new int[record.triangleIndices];
                    for (var i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
                    var mesh = new Mesh { name = record.name, indexFormat = IndexFormat.UInt32 };
                    mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles;
                    mesh.bindposes = bindposes; mesh.boneWeights = weights; mesh.RecalculateBounds();
                    var obj = new GameObject(record.name); obj.transform.SetParent(root, false);
                    var skin = obj.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
                    skin.bones = bones; skin.rootBone = bones[0]; skin.sharedMaterial = material;
                    if(record.name.EndsWith("SkinnedBody"))bodySkin=skin;
                    skin.updateWhenOffscreen = true;
                }
                SmoothBodyNormalPoses normalPoses=null;
                if(header.version==2)
                {
                    if(bodySkin==null||header.normalPoses==null||header.normalPoses.Length<2||header.normalPoses.Length>16)
                        throw new InvalidDataException("Missing smooth body normal references.");
                    var mesh=bodySkin.sharedMesh;var zeroVertices=new Vector3[mesh.vertexCount];
                    normalPoses=bodySkin.gameObject.AddComponent<SmoothBodyNormalPoses>();normalPoses.Bind(bodySkin,bones);
                    foreach(var reference in header.normalPoses)
                    {
                        if(reference.side<0||reference.side>1||reference.rotation==null||reference.rotation.Length!=4)
                            throw new InvalidDataException("Invalid normal pose reference.");
                        var index=-1;
                        if(reference.hasNormals)
                        {
                            var deltaNormals=new Vector3[mesh.vertexCount];
                            for(var i=0;i<deltaNormals.Length;i++)deltaNormals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                            index=mesh.blendShapeCount;
                            mesh.AddBlendShapeFrame(reference.name,100,zeroVertices,deltaNormals,null);
                        }
                        var q=reference.rotation;
                        normalPoses.AddReference(reference.side,new Quaternion(q[0],q[1],q[2],q[3]),index);
                    }
                }
                SurfacePoseCorrectives surfacePoses=null;
                if(header.version==3)
                {
                    if(bodySkin==null||header.surfacePoses==null||header.surfacePoses.Length<2||header.surfacePoses.Length>64)
                        throw new InvalidDataException("Missing surface pose references.");
                    var mesh=bodySkin.sharedMesh;
                    surfacePoses=bodySkin.gameObject.AddComponent<SurfacePoseCorrectives>();surfacePoses.Bind(bodySkin,bones);
                    foreach(var reference in header.surfacePoses)
                    {
                        if(reference.side<0||reference.side>1||reference.mode<0||reference.mode>2||reference.rotation==null||reference.rotation.Length!=4||reference.elbowRotation==null||reference.elbowRotation.Length!=4||reference.torsoRotation==null||reference.torsoRotation.Length!=4)
                            throw new InvalidDataException("Invalid surface pose reference.");
                        var index=-1;
                        if(reference.hasDelta)
                        {
                            var deltaVertices=new Vector3[mesh.vertexCount];var deltaNormals=new Vector3[mesh.vertexCount];
                            for(var i=0;i<deltaVertices.Length;i++)deltaVertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                            for(var i=0;i<deltaNormals.Length;i++)deltaNormals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                            index=mesh.blendShapeCount;mesh.AddBlendShapeFrame(reference.name,100,deltaVertices,deltaNormals,null);
                        }
                        var q=reference.rotation;var e=reference.elbowRotation;var t=reference.torsoRotation;
                        surfacePoses.AddReference(reference.side,new Quaternion(q[0],q[1],q[2],q[3]),new Quaternion(e[0],e[1],e[2],e[3]),new Quaternion(t[0],t[1],t[2],t[3]),index,(SurfacePoseCorrectives.ReferenceMode)reference.mode);
                    }
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Transport trailing bytes.");
                var profile = root.gameObject.AddComponent<SkinTransportProfile>();
                if (header.idleArmDirection != null && header.idleArmDirection.Length == 3)
                    profile.IdleArmDirection = new Vector3(header.idleArmDirection[0], header.idleArmDirection[1], header.idleArmDirection[2]);
                if (header.contactSearch != null && header.contactSearch.Length == 2)
                { profile.ContactSearchSeeds = header.contactSearch[0]; profile.ContactEscapeRings = header.contactSearch[1]; }
                profile.UpperArmIgnoresTorso = header.ignoreUpperArmTorso;
                if (header.torsoColliderRadii != null && header.torsoColliderRadii.Length == 3)
                {
                    profile.HasTorsoColliderRadii = true; profile.TorsoRadius = header.torsoColliderRadii[0];
                    profile.RagdollSpineRadius = header.torsoColliderRadii[1]; profile.RagdollChestRadius = header.torsoColliderRadii[2];
                }
                if (header.armColliderRadii != null && header.armColliderRadii.Length == 2)
                {
                    profile.HasArmColliderRadii = true;
                    profile.UpperArmRadius = header.armColliderRadii[0]; profile.ForearmRadius = header.armColliderRadii[1];
                }
                // Idle is the common lowered-arm base, independent of jump presentation.
                foreach (var side in new[] { "L", "R" })
                {
                    var upper = Array.Find(bones, b => b.name == "UpperArm_"+side);
                    var forearm = Array.Find(bones, b => b.name == "Forearm_"+side);
                    var direction = profile.IdleDirection(side);
                    upper.rotation = Quaternion.FromToRotation(forearm.position-upper.position, root.TransformDirection(direction)) * upper.rotation;
                }
                normalPoses?.ApplyCurrentPose();
                surfacePoses?.ApplyCurrentPose();
            }
        }
    }
}
