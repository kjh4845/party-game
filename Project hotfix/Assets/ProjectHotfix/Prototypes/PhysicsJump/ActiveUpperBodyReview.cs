using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ProjectHotfix.Contracts;

namespace ProjectHotfix.PhysicsJump
{
    // Stage 1 articulation lab. No grab, strike, throw, damage or recovery authority.
    [DefaultExecutionOrder(-100)]
    public sealed partial class ActiveUpperBodyReview : MonoBehaviour
    {
        public enum ReviewStrikeStyle { Straight, SideSwing, DiagonalSlam, BodyStrike }
        public ReviewStrikeStyle StrikeStyle;
        public enum Exercise { Rest, Reach, Curl }
        public sealed class Link
        {
            public string Name, ParentName;
            public Transform Bone, Goal;
            public Rigidbody Body, ParentBody;
            public ConfigurableJoint Joint;
            public Quaternion StartRelative, JointFrame;
            public Vector3 StartPosition;
            public Quaternion StartRotation;
            public Vector3 StartConnectedAnchor;
            public JointDrive Drive;
            public Collider Shape;
        }

        internal static readonly HashSet<ActiveUpperBodyReview> Live = new HashSet<ActiveUpperBodyReview>();
        private readonly List<Link> _links = new List<Link>();
        private readonly Dictionary<string, Transform> _goals = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>();
        private readonly List<Collider> _shapes = new List<Collider>();
        private readonly HashSet<UnityEngine.Object> _ownedAssets = new HashSet<UnityEngine.Object>();
        private ShoulderHelperRig _shoulderHelpers;
        private Transform _goalRoot;
        private Vector3 _referencePelvisPosition;
        private bool _lowReachWasActive;
        private Vector3 _spawn;
        private Quaternion _spawnRotation=Quaternion.identity;
        private readonly bool[] _handActive=new bool[2];
        private readonly Vector3[] _handTargets=new Vector3[2];
        private readonly float[] _handWeights=new float[2];
        private readonly float[] _handDriveScale={1f,1f};
        private readonly bool[] _punchReference=new bool[2],_punchEntryPending=new bool[2];
        private readonly float[] _punchSourceAge=new float[2];
        private readonly Quaternion[][] _punchEntry={new Quaternion[4],new Quaternion[4]};
        private readonly bool[] _punchLimits=new bool[2];
        private readonly float[] _punchOriginalHighX={100,100};
        private bool _strikeTorsoLimit;
        private float _strikeOriginalChestY;
        private readonly bool[] _gripPosture=new bool[2];
        private readonly Quaternion[][] _gripRotations={new Quaternion[2],new Quaternion[2]};
        private float _stepSeconds=1f/60;
        public bool IndependentHands, LocomotionPresentation;
        public bool PreserveSkinBoneLengths;
        // Candidate-only reference controls. The physical rig and joint limits stay unchanged.
        public float ReferenceReachFraction=1f;
        public float LowReachRequest;
        public float LowReachWeight {get;private set;}
        public float MaximumSkinBodyOffset {get;private set;}
        public bool Supported=true, Sprinting;
        private uint _poseReset;
        public event Action<Rigidbody,Collision> PhysicalContact;
        private float _poseWeight, _curlWeight, _leftPower = 1, _rightPower = 1;
        private float _terminalLength;
        private bool _ready;
        public bool ManualStep, ReadKeyboard = true;
        public string SkinTransportPath;
        public Exercise TargetExercise;
        public bool LeftPowered = true, RightPowered = true;
        public PhysicsJumpReview Actor { get; private set; }
        public Camera View { get; private set; }
        public BoxCollider Obstacle { get; private set; }
        public IReadOnlyList<Link> Links => _links;
        public IReadOnlyList<Collider> Shapes => _shapes;
        public bool Ready => _ready;
        public ulong Steps { get; private set; }
        public ulong Captures { get; private set; }
        public int ObstacleContacts { get; private set; }
        public float MaximumContactImpulse { get; private set; }
        public float MaximumJointGap { get; private set; }
        public float MaximumBodySpeed { get; private set; }
        public float TotalMass => Actor.Motor.Body.mass + _links.Sum(link => link.Body.mass);

        private void OnEnable() => Live.Add(this);
        private void OnDisable() => Live.Remove(this);
        private void OnDestroy()
        { foreach(var asset in _ownedAssets)if(asset!=null)Destroy(asset); }
        private void Start() { if (!_ready) Build(); }
        public static ActiveUpperBodyReview Create(Scene scene,string skinTransportPath=null)
        {
            var root = new GameObject("Active Upper Body - Stage 1 - Press Play");
            SceneManager.MoveGameObjectToScene(root, scene);
            var review = root.AddComponent<ActiveUpperBodyReview>();
            review.SkinTransportPath=skinTransportPath;
            if (Application.isPlaying) review.Build();
            return review;
        }
        public static ActiveUpperBodyReview CreateInteractionRig(Scene scene,Vector3 position,Quaternion rotation,string skinTransportPath=null)
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Interaction rigs are built on entering Play.");
            var rig=Create(scene,skinTransportPath);rig.ManualStep=true;rig.ReadKeyboard=false;
            rig.View.gameObject.SetActive(false);rig.Obstacle.gameObject.SetActive(false);
            rig.transform.Find("Stage1Floor").gameObject.SetActive(false);rig.transform.Find("Stage1Light").gameObject.SetActive(false);
            var origin=rig.Actor.Motor.Body.position;
            foreach(var link in rig._links)
            {
                link.StartPosition=position+rotation*(link.StartPosition-origin);link.StartRotation=rotation*link.StartRotation;
                link.Body.transform.SetPositionAndRotation(link.StartPosition,link.StartRotation);
                link.Body.position=link.StartPosition;link.Body.rotation=link.StartRotation;
            }
            rig._spawn=position;rig._spawnRotation=rotation;rig.Actor.transform.SetPositionAndRotation(position,rotation);
            rig.Actor.Motor.Body.position=position;rig.Actor.Motor.Body.rotation=rotation;
            rig._goalRoot.SetPositionAndRotation(position,rotation);
            rig.IndependentHands=rig.LocomotionPresentation=true;rig.Actor.UseMovementSpeedCandidate(2);rig.UseDistinctRunGait();
            rig.PreserveSkinBoneLengths=true;
            rig.Actor.Motor.Body.solverIterations=48;rig.Actor.Motor.Body.solverVelocityIterations=16;
            foreach(var link in rig._links){link.Body.solverIterations=48;link.Body.solverVelocityIterations=16;}
            // Fit the standing torso, rather than reusing the slimmer down-only
            // volumes. This profile is isolated from the approved stage-1 lab.
            var trunk=rig.Actor.GetComponentInChildren<SkinTransportProfile>();
            foreach(var name in new[]{"Spine","Chest"})
            {var shape=(CapsuleCollider)rig.Find(name).Shape;shape.radius=trunk!=null&&trunk.HasTorsoColliderRadii?trunk.TorsoRadius:.15f;shape.height=name=="Spine"?.42f:.38f;}
            // Firmer trunk for fights (2026-10-16 review: the head whipped 22-35 deg after the attacker's
            // own hits and the body read as floppy). Head x2.5, Spine/Chest x1.5 spring and force.
            // Trunk dampers x2.5 as well: at x1.5 the chest still overshot ~7 deg back after a strike.
            foreach(var (name,scale,damping) in new[]{("Head",2.5f,2.5f),("Spine",1.5f,2.5f),("Chest",1.5f,2.5f)})
            {var link=rig.Find(name);link.Drive=new JointDrive{positionSpring=link.Drive.positionSpring*scale,positionDamper=link.Drive.positionDamper*damping,maximumForce=link.Drive.maximumForce*scale};link.Joint.slerpDrive=link.Drive;}
            var profile=rig.Actor.GetComponentInChildren<SkinTransportProfile>();
            foreach(var name in new[]{"UpperArm_L","UpperArm_R"})
            {if(profile!=null&&profile.UpperArmIgnoresTorso){rig._ignoreUpperArmTorso=true;Physics.IgnoreCollision(rig.Find(name).Shape,rig.Find("Spine").Shape,true);}
            var joint=rig.Find(name).Joint;joint.angularYLimit=joint.angularZLimit=new SoftJointLimit{limit=15f};joint.highAngularXLimit=new SoftJointLimit{limit=100f};}
            var material=rig.Actor.GetComponent<CapsuleCollider>().sharedMaterial;
            material.staticFriction=.25f;material.dynamicFriction=.15f;material.frictionCombine=PhysicsMaterialCombine.Minimum;
            rig.BindKnockdown();
            Physics.SyncTransforms();rig.CapturePhysics();rig.Actor.FixedPose.CaptureEvaluatedPose();
            return rig;
        }
        private void Build()
        {
            if (_ready) return;
            Actor = PhysicsJumpReview.Create(new Vector3(0, .025f, 0), false,string.IsNullOrEmpty(SkinTransportPath)?PhysicsJumpReview.LatWeightTransportPath:SkinTransportPath);
            SceneManager.MoveGameObjectToScene(Actor.gameObject, gameObject.scene); Actor.transform.SetParent(transform, true);
            Actor.enabled = false; Actor.ReadKeyboard = false; Actor.Motor.ManualStep = true; Actor.Motor.enabled = false;
            _spawn = Actor.Motor.Body.position;
            var rootBody = Actor.Motor.Body;
            rootBody.mass = 8f; // Lower body/pelvis 8 + upper body 12 = original 20 kg.
            rootBody.interpolation = RigidbodyInterpolation.None;
            rootBody.solverIterations = 24; rootBody.solverVelocityIterations = 8;
            var baseShape = Actor.GetComponent<CapsuleCollider>();
            baseShape.radius = .13f; baseShape.height = .34f; baseShape.center = new Vector3(0, .167f, 0);
            baseShape.contactOffset = .002f;
            baseShape.sharedMaterial = new PhysicsMaterial("Stage 1 grounded support")
            { staticFriction = .8f, dynamicFriction = .6f, frictionCombine = PhysicsMaterialCombine.Maximum };
            _shapes.Add(baseShape);

            var bones = Actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            _shoulderHelpers=Actor.Pose.GetComponentInChildren<ShoulderHelperRig>();
            _goalRoot = new GameObject("Reference pose - no collisions or visible skin").transform;
            _goalRoot.SetParent(transform, false); _goalRoot.SetPositionAndRotation(rootBody.position, rootBody.rotation);
            var mapping = new Dictionary<Transform, Transform>();
            foreach (var bone in bones) mapping[bone] = new GameObject(bone.name).transform;
            foreach (var bone in bones)
            {
                var goal = mapping[bone]; goal.SetParent(mapping.TryGetValue(bone.parent, out var parent) ? parent : _goalRoot, false);
                goal.localPosition = bone.localPosition; goal.localRotation = bone.localRotation;
                _goals[bone.name] = goal; _rest[bone.name] = goal.localRotation;
            }
            var frame = Actor.PunchLibrary.clips[0].frames[0];
            _referencePelvisPosition=_goals["Pelvis"].localPosition;
            _terminalLength = Vector3.Distance(frame.heads[3], frame.tails[3]);

            AddLink(bones, "Spine", "Chest", null, 2.5f, .105f, 0, 10, 4, 500, 15, 60);
            AddLink(bones, "Chest", "Neck", "Spine", 2.5f, .115f, 0, 12, 5, 450, 12, 60);
            AddLink(bones, "Head", null, "Chest", 3f, .14f, .24f, 15, 15, 100, 5, 15);
            var profile = Actor.GetComponentInChildren<SkinTransportProfile>();
            var upperRadius = profile != null && profile.HasArmColliderRadii ? profile.UpperArmRadius : .041f;
            var forearmRadius = profile != null && profile.HasArmColliderRadii ? profile.ForearmRadius : .037f;
            foreach (var side in new[] { "L", "R" })
            {
                AddLink(bones, "UpperArm_" + side, "Forearm_" + side, "Chest", 1.25f, upperRadius, 0, 110, 15, 100, 4, 15);
                AddLink(bones, "Forearm_" + side, "HandLogical_" + side, "UpperArm_" + side, .75f, forearmRadius, _terminalLength, 135, 4, 60, 2, 8);
            }
            InitializeShoulderMotion(bones);
            // Only anatomically adjacent shapes overlap at their connection.
            // Forearms still collide with the torso, opposite arm and environment.
            foreach (var link in _links)
            {
                var parentShape = link.ParentName == null ? baseShape : Find(link.ParentName).Shape;
                Physics.IgnoreCollision(link.Shape, parentShape, true);
            }

            var floor = Primitive("Stage1Floor", new Vector3(0, -.10f, 0), new Vector3(12, .2f, 12), new Color(.24f,.28f,.33f));
            floor.GetComponent<Collider>().sharedMaterial = baseShape.sharedMaterial;
            var block = Primitive("Physical arm obstacle", new Vector3(-.18f, .55f, .23f), new Vector3(.16f,.30f,.08f), new Color(.95f,.42f,.12f));
            Obstacle = block.GetComponent<BoxCollider>(); block.SetActive(false);
            var cameraObject = new GameObject("Stage1Camera"); cameraObject.transform.SetParent(transform, false);
            View = cameraObject.AddComponent<Camera>(); View.orthographic = true; View.orthographicSize = .65f;
            View.nearClipPlane = .05f; View.farClipPlane = 30;
            View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.12f,.17f,.23f);
            View.transform.position = new Vector3(-1.6f, 1.15f, 2.6f); View.transform.LookAt(new Vector3(0,.52f,.04f));
            var lightObject = new GameObject("Stage1Light"); lightObject.transform.SetParent(transform, false);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
            light.shadows=LightShadows.Soft;light.shadowStrength=.65f;
            light.transform.rotation = Quaternion.Euler(38,140,0);
            foreach(var skin in Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())_ownedAssets.Add(skin.sharedMesh);
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))_ownedAssets.Add(renderer.sharedMaterial);
            _ownedAssets.Add(baseShape.sharedMaterial);
            _ready = true; Physics.SyncTransforms(); CapturePhysics();
        }

        private GameObject Primitive(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            go.name = name; go.transform.SetParent(transform, true); go.transform.position = position; go.transform.localScale = scale;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }

        private void AddLink(Transform[] bones, string name, string tipName, string parentName, float mass, float radius,
            float extension, float bendLimit, float swingLimit, float spring, float damper, float maximumForce)
        {
            var bone = Array.Find(bones, item => item.name == name);
            var go = new GameObject(name + "_ActivePhysics"); go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 24; body.solverVelocityIterations = 8;
            body.maxAngularVelocity = 18; body.maxDepenetrationVelocity = 2; body.angularDamping = .15f;
            var shape = go.AddComponent<CapsuleCollider>(); shape.direction = 2; shape.radius = radius; shape.contactOffset = .002f;
            var end = tipName == null ? Vector3.back * extension : bone.InverseTransformPoint(Array.Find(bones, item => item.name == tipName).position) + Vector3.back * extension;
            shape.center = end * .5f; shape.height = Mathf.Max(radius * 2, end.magnitude);
            var parentBody = parentName == null ? Actor.Motor.Body : Find(parentName).Body;
            var joint = go.AddComponent<ConfigurableJoint>(); joint.connectedBody = parentBody; joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero; joint.connectedAnchor = parentBody.transform.InverseTransformPoint(body.position);
            joint.axis = bone.InverseTransformDirection(Vector3.right);
            joint.secondaryAxis = bone.InverseTransformDirection(Vector3.up);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = name.StartsWith("Forearm") ? -5 : name.StartsWith("UpperArm") ? -25 : -bendLimit };
            joint.highAngularXLimit = new SoftJointLimit { limit = bendLimit };
            joint.angularYLimit = joint.angularZLimit = new SoftJointLimit { limit = swingLimit };
            joint.rotationDriveMode = RotationDriveMode.Slerp; joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = true; joint.enablePreprocessing = true;
            var drive = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = maximumForce };
            joint.slerpDrive = drive;
            var axis = joint.axis.normalized; var forward = Vector3.Cross(axis, joint.secondaryAxis).normalized;
            var link = new Link { Name = name, ParentName = parentName, Bone = bone, Goal = _goals[name], Body = body,
                ParentBody = parentBody, Joint = joint, Shape = shape, Drive = drive,
                StartRelative = Quaternion.Inverse(parentBody.rotation) * body.rotation,
                JointFrame = Quaternion.LookRotation(forward, Vector3.Cross(forward, axis)),
                StartPosition = body.position, StartRotation = body.rotation,StartConnectedAnchor=joint.connectedAnchor };
            _links.Add(link); _shapes.Add(shape); go.AddComponent<ActiveUpperContactProbe>().Review = this;
        }

        public Link Find(string name) => _links.First(link => link.Name == name);
        public Vector3 HandPosition(int side) => Find("Forearm_" + (side == 0 ? "L" : "R")).Body.position +
            Find("Forearm_" + (side == 0 ? "L" : "R")).Body.rotation *
            (_goals["HandLogical_" + (side == 0 ? "L" : "R")].localPosition + Vector3.back * _terminalLength);
        public Vector3 GoalHand(int side) => _goals["HandLogical_" + (side == 0 ? "L" : "R")].TransformPoint(Vector3.back * _terminalLength);
        public Vector3 PalmCenter(int side)
        {
            var link=Find("Forearm_"+(side==0?"L":"R"));var shape=(CapsuleCollider)link.Shape;
            return link.Body.position+link.Body.rotation*(shape.center+Vector3.back*Mathf.Max(0,shape.height*.5f-shape.radius));
        }
        public void MatchHandSkinEnvelope()
        {
            // The authored skin extends 12.23 mm beyond the logical hand tail.
            // Add that extent plus a small contact margin, without changing reach goals.
            foreach(var side in new[]{"L","R"})
            {
                var link=Find("Forearm_"+side);var shape=(CapsuleCollider)link.Shape;
                var length=Vector3.Distance(_goals["Forearm_"+side].position,_goals["HandLogical_"+side].position)+_terminalLength+.016f;
                shape.height=Mathf.Max(length,shape.radius*2);shape.center=Vector3.back*(length*.5f);link.Body.ResetCenterOfMass();link.Body.ResetInertiaTensor();
            }
        }
        public void SetHandTarget(int side,bool active,Vector3 worldPoint)
        {
            _handActive[side]=active;
            _handTargets[side]=Quaternion.Inverse(Actor.Motor.Body.rotation)*(worldPoint-Actor.Motor.Body.position);
        }
        public void SetHandDriveScale(int side,float scale)=>_handDriveScale[side]=Mathf.Clamp(scale,0,8);
        // A reaching/holding hand may go overhead (ledge grab, overhead carry): the grab-only shoulder
        // limit of 100 deg stops the hand at about head height (2026-10-17). Punches keep their own limits.
        public float OverheadArmLimit=165f;
        private readonly bool[] _overheadReach=new bool[2];
        public void SetOverheadReach(int side,bool on)=>_overheadReach[side]=on;
        public void SetPunchReference(int side,bool active,float sourceAge,float compactness=0)
        {
            if(active&&!_punchReference[side]){_strikeTouchAge[side]=-1;_plantStarted[side]=false;_punchEntryPending[side]=true;_punchCompact[side]=Mathf.Clamp01(compactness);_punchOriginalHighX[side]=Find("UpperArm_"+(side==0?"L":"R")).Joint.highAngularXLimit.limit;}
            if(!active)_punchBlocked[side]=false;
            _punchReference[side]=active;_punchSourceAge[side]=sourceAge;
        }
        public void CancelPunchReference(int side)
        {
            var guarding=_guardWeight[side]>0;
            _guardRemaining[side]=_guardWeight[side]=0;
            if(!_punchReference[side]&&!_punchLimits[side]&&!guarding)return;
            _punchReference[side]=_punchEntryPending[side]=_punchLimits[side]=false;_punchSourceAge[side]=0;
            _punchImpactAge[side]=-1;
            var upper=Find("UpperArm_"+(side==0?"L":"R"));var forearm=Find("Forearm_"+(side==0?"L":"R"));
            upper.Joint.angularYLimit=upper.Joint.angularZLimit=new SoftJointLimit{limit=15};
            upper.Joint.highAngularXLimit=new SoftJointLimit{limit=_punchOriginalHighX[side]};
            foreach(var link in new[]{upper,forearm}){link.Joint.targetRotation=Quaternion.identity;link.Joint.targetAngularVelocity=Vector3.zero;_punchPreviousTarget.Remove(link);link.Joint.slerpDrive=link.Drive;link.Body.maxAngularVelocity=18;}
            if(!_punchReference[0]&&!_punchReference[1])
            {
                RestoreStrikeTorsoLimit();
                foreach(var link in _links)if(!link.Name.StartsWith("UpperArm")&&!link.Name.StartsWith("Forearm"))
                {link.Joint.targetRotation=Quaternion.identity;link.Joint.slerpDrive=link.Drive;}
            }
        }
        public void SetGripPosture(int side,bool holding)
        {
            if(holding&&!_gripPosture[side])
            {
                var suffix=side==0?"L":"R";
                for(var part=0;part<2;part++)
                {
                    var link=Find((part==0?"UpperArm_":"Forearm_")+suffix);
                    var observed=Quaternion.Inverse(link.ParentBody.rotation)*link.Body.rotation;
                    _gripRotations[side][part]=Quaternion.Inverse(link.JointFrame)*Quaternion.Inverse(observed)*link.StartRelative*link.JointFrame;
                }
            }
            _gripPosture[side]=holding;
        }
        public float TrackingError(string name)
        {
            var link=Find(name);var parentGoal=link.ParentName==null?_goalRoot:_goals[link.ParentName];
            return Quaternion.Angle(Quaternion.Inverse(link.ParentBody.rotation)*link.Body.rotation,
                Quaternion.Inverse(parentGoal.rotation)*link.Goal.rotation);
        }
        public void SetObstacle(bool value) { Obstacle.gameObject.SetActive(value); Physics.SyncTransforms(); }
        public void PushArm(int side, Vector3 impulse) => Find("Forearm_" + (side == 0 ? "L" : "R")).Body.AddForceAtPosition(impulse,HandPosition(side),ForceMode.Impulse);

        private void FixedUpdate()
        {
            if (!_ready || ManualStep) return;
            if (ReadKeyboard && Keyboard.current != null)
            {
                var keys = Keyboard.current;
                if (keys.cKey.wasPressedThisFrame) TargetExercise = (Exercise)(((int)TargetExercise + 1) % 3);
                if (keys.fKey.wasPressedThisFrame) PushArm(0, new Vector3(.15f,0,-.25f));
                if (keys.gKey.wasPressedThisFrame) LeftPowered = !LeftPowered;
                if (keys.oKey.wasPressedThisFrame) SetObstacle(!Obstacle.gameObject.activeSelf);
                if (keys.rKey.wasPressedThisFrame) ResetReview();
            }
            Step(Time.fixedDeltaTime);
        }
        public void Step(float seconds)
        {
            if (!_ready) throw new InvalidOperationException("Stage 1 lab has not been built.");
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            _stepSeconds=seconds;
            for(var side=0;side<2;side++)_handWeights[side]=Mathf.MoveTowards(_handWeights[side],_handActive[side]?1:0,seconds/.12f);
            _poseWeight = Mathf.MoveTowards(_poseWeight, TargetExercise == Exercise.Rest ? 0 : 1, seconds * 4);
            _curlWeight = Mathf.MoveTowards(_curlWeight, TargetExercise == Exercise.Curl ? 1 : 0, seconds * 4);
            _leftPower = LeftPowered ? Mathf.MoveTowards(_leftPower, 1, seconds * 5) : 0;
            _rightPower = RightPowered ? Mathf.MoveTowards(_rightPower, 1, seconds * 5) : 0;
            _goalRoot.SetPositionAndRotation(Actor.Motor.Body.position, Actor.Motor.Body.rotation);
            foreach (var pair in _goals) pair.Value.localRotation = _rest[pair.Key];
            ApplyLowReachReference(seconds);
            ApplyPunchBodyReference();
            ApplyNaturalReachTorso();
            ApplyShoulderMotion(seconds);
            for (var side = 0; side < 2; side++) SolveReferenceArm(side);
            ApplyLocomotionReference(seconds);
            ApplyCombatGuard(seconds);
            for(var side=0;side<2;side++)if(_punchReference[side])ApplyJointSpacePunch(side);
            ApplyImpactReaction(seconds);
            foreach (var link in _links)
            {
                var parentGoal = link.ParentName == null ? _goalRoot : _goals[link.ParentName];
                var desired = Quaternion.Inverse(parentGoal.rotation) * link.Goal.rotation;
                // Express the inverse angular error in the joint's own axes.
                link.Joint.targetRotation = Quaternion.Inverse(link.JointFrame) * Quaternion.Inverse(desired) * link.StartRelative * link.JointFrame;
                if(link.Name.StartsWith("UpperArm")||link.Name.StartsWith("Forearm"))
                {
                    var side=link.Name.EndsWith("_L")?0:1;
                    if(link.Name.StartsWith("UpperArm")&&(_punchReference[side]||_punchLimits[side]))
                    {
                        // Punching needs shoulder internal rotation beyond the
                        // narrower grab-only profile; translation stays jointed.
                        var arc=StrikeStyle!=ReviewStrikeStyle.Straight;
                        link.Joint.angularYLimit=new SoftJointLimit{limit=_punchReference[side]?(arc?70:35):15};
                        link.Joint.angularZLimit=new SoftJointLimit{limit=_punchReference[side]?(arc?50:35):15};
                        link.Joint.highAngularXLimit=new SoftJointLimit{limit=_punchReference[side]&&StrikeStyle==ReviewStrikeStyle.DiagonalSlam&&!_handActive[1-side]&&!_gripPosture[1-side]?145:_punchOriginalHighX[side]};
                        _punchLimits[side]=_punchReference[side];
                    }
                    else if(link.Name.StartsWith("UpperArm"))
                        link.Joint.highAngularXLimit=new SoftJointLimit{limit=_overheadReach[side]?OverheadArmLimit:_punchOriginalHighX[side]};
                    // Maintain the posture in which actual contact was made.
                    // Physics can yield against these bounded muscle drives;
                    // no body or rendered bone is locked to that posture.
                    if(_gripPosture[side])link.Joint.targetRotation=_gripRotations[side][link.Name.StartsWith("UpperArm")?0:1];
                }
                var power = link.Name.StartsWith("UpperArm") || link.Name.StartsWith("Forearm") ? (link.Name.EndsWith("_L") ? _leftPower : _rightPower) : 1;
                if(link.Name.StartsWith("UpperArm")||link.Name.StartsWith("Forearm"))power*=_handDriveScale[link.Name.EndsWith("_L")?0:1];
                var drive = link.Drive; drive.positionSpring *= power; drive.positionDamper *= power; drive.maximumForce *= power;
                if(link.Name.StartsWith("UpperArm")||link.Name.StartsWith("Forearm"))
                {
                    var side=link.Name.EndsWith("_L")?0:1;
                    link.Body.maxAngularVelocity=_punchReference[side]?(link.Name.StartsWith("UpperArm")?40:60):18;
                    if(_punchReference[side])
                    {
                        var muscle=side==0?_leftPower:_rightPower;
                        drive.positionSpring=link.Drive.positionSpring*8*muscle;
                        drive.positionDamper=link.Drive.positionDamper*(link.Name.StartsWith("UpperArm")?2:1.25f)*muscle;
                        var age=_punchSourceAge[side]/PhysicalPunchReview.PlaybackRate;
                        var recovery=Smooth((age-.24f)/.11f);
                        drive.positionSpring*=Mathf.Lerp(1,.65f,recovery);
                        drive.positionDamper*=Mathf.Lerp(1,1.3f,recovery);
                        // Allow the shoulder to accelerate through the compact wind-up.
                        var strike=Smooth((age-.05f)/.02f)*(1-Smooth((age-.18f)/.05f));
                        drive.maximumForce=link.Drive.maximumForce*Mathf.Lerp(3,8,strike)*muscle;
                        ApplyPunchContactDrive(side,ref drive);
                    }
                    else ApplyRunArmDrive(side,ref drive);
                    if(!_punchReference[side]&&_guardWeight[side]>0&&!_gripPosture[side]&&!_handActive[side])
                    {
                        drive.positionSpring*=Mathf.Lerp(1,4,_guardWeight[side]);
                        drive.positionDamper*=Mathf.Lerp(1,2,_guardWeight[side]);
                        drive.maximumForce*=Mathf.Lerp(1,3,_guardWeight[side]);
                    }
                }
                ApplyPunchVelocity(link,seconds);
                var reaction=HitReactionScale(link,seconds);drive.positionSpring*=reaction;drive.maximumForce*=reaction;
                ApplyImpactReactionDrive(link,ref drive);
                link.Joint.slerpDrive = drive;
            }
            ApplyFistDrive(seconds);
            Steps++;
        }
        private void ApplyLowReachReference(float seconds)
        {
            if(LowReachRequest<=0&&LowReachWeight<=0&&!_lowReachWasActive)return;
            LowReachWeight=Mathf.MoveTowards(LowReachWeight,Mathf.Clamp01(LowReachRequest),seconds*3);
            var drop=.025f*LowReachWeight;var lean=35*LowReachWeight;
            _goals["Pelvis"].localPosition=_referencePelvisPosition+Vector3.down*drop;
            _goals["Pelvis"].rotation=Quaternion.AngleAxis(lean*.7f,_goalRoot.right)*_goals["Pelvis"].rotation;
            var spine=Find("Spine");var chest=Find("Chest");
            // Carry the pelvis hinge through the physical spine anchor as well
            // as the leg pose, rather than shearing the skin at a fixed waist.
            spine.Joint.connectedAnchor=Quaternion.Inverse(Actor.Motor.Body.rotation)*(spine.Goal.position-Actor.Motor.Body.position);
            chest.Goal.rotation=Quaternion.AngleAxis(lean*.3f,_goalRoot.right)*chest.Goal.rotation;
            var spineLimit=Mathf.Max(10,lean*.7f+3);var chestLimit=Mathf.Max(12,lean*.3f+3);
            spine.Joint.lowAngularXLimit=new SoftJointLimit{limit=-spineLimit};spine.Joint.highAngularXLimit=new SoftJointLimit{limit=spineLimit};
            chest.Joint.lowAngularXLimit=new SoftJointLimit{limit=-chestLimit};chest.Joint.highAngularXLimit=new SoftJointLimit{limit=chestLimit};
            Actor.Pose.AdditionalPelvisDrop=drop;Actor.Pose.AdditionalPelvisPitch=lean*.7f;_lowReachWasActive=LowReachWeight>0;
        }

        private void SolveReferenceArm(int side)
        {
            var suffix = side == 0 ? "L" : "R"; var upper = _goals["UpperArm_" + suffix]; var elbow = _goals["Forearm_" + suffix];
            var hand = _goals["HandLogical_" + suffix]; var upperRest = upper.localRotation; var elbowRest = elbow.localRotation;
            var target = _goalRoot.TransformPoint(IndependentHands?_handTargets[side]:new Vector3(side == 0 ? -.18f : .18f, Mathf.Lerp(.55f,.66f,_curlWeight), Mathf.Lerp(.28f,.20f,_curlWeight)));
            var a = Vector3.Distance(upper.position, elbow.position); var b = Vector3.Distance(elbow.position, hand.position) + _terminalLength;
            var delta = target - upper.position; var distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a-b)+.0001f, (a+b)*Mathf.Clamp(ReferenceReachFraction,.7f,1f)-.0001f);
            var direction = delta.normalized;
            var bend = Vector3.ProjectOnPlane(-_goalRoot.up, direction).normalized;
            var projection = (a*a + distance*distance - b*b)/(2*distance); var height = Mathf.Sqrt(Mathf.Max(0,a*a-projection*projection));
            var goalElbow = upper.position + direction*projection + bend*height;
            upper.rotation = Quaternion.FromToRotation(elbow.position-upper.position,goalElbow-upper.position)*upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.TransformPoint(Vector3.back*_terminalLength)-elbow.position,upper.position+direction*distance-elbow.position)*elbow.rotation;
            var weight=Mathf.SmoothStep(0,1,IndependentHands?_handWeights[side]:_poseWeight);
            upper.localRotation = Quaternion.Slerp(upperRest,upper.localRotation,weight);
            elbow.localRotation = Quaternion.Slerp(elbowRest,elbow.localRotation,weight);
        }

        public void CapturePhysics()
        {
            if (!_ready) return;
            if (_linksSuspended) { CaptureKnockdown(); return; }
            ApplyStrikePlant();
            Actor.FixedPose.RestoreForEvaluation();
            if(LocomotionPresentation)
            {
                var velocity=Actor.Motor.Body.linearVelocity;
                Actor.Pose.Apply(new JumpPresentationState(velocity.y,Supported,1,0,velocity.x,velocity.z,Sprinting,Supported?0:1,_poseReset),_stepSeconds);
            }
            CaptureShoulderMotion();
            foreach (var link in _links)
            {
                var a = link.Body.position + link.Body.rotation * link.Joint.anchor;
                var b = link.ParentBody.position + link.ParentBody.rotation * link.Joint.connectedAnchor;
                MaximumJointGap = Mathf.Max(MaximumJointGap, Vector3.Distance(a,b));
                MaximumBodySpeed = Mathf.Max(MaximumBodySpeed, link.Body.linearVelocity.magnitude);
                if(PreserveSkinBoneLengths&&link.Name!="Spine")
                {
                    // Solver slop must not stretch the dense skin at the shoulder.
                    // Keep the authored chain lengths and follow physical rotations.
                    link.Bone.localPosition=link.Goal.localPosition;link.Bone.rotation=link.Body.rotation;
                }
                else link.Bone.SetPositionAndRotation(link.Body.position,link.Body.rotation);
                MaximumSkinBodyOffset=Mathf.Max(MaximumSkinBodyOffset,Vector3.Distance(link.Bone.position,link.Body.position));
            }
            // Skin-only shoulder helpers share the chest/upper-arm rotation.
            // They have no own drive or Rigidbody. The optional fixed-step skin
            // contact candidate reads the resulting surface separately.
            if(_shoulderHelpers!=null)_shoulderHelpers.Apply();
            Actor.FixedPose.CaptureEvaluatedPose(); Captures++;
        }
        private void LateUpdate() { if (_ready && !ManualStep) Actor.FixedPose.Present(ReviewFixedLoop.RenderAlpha); }
        public void ObserveContact(Rigidbody body,Collision collision)
        {
            PhysicalContact?.Invoke(body,collision);
            if (Obstacle == null || collision.collider != Obstacle) return;
            ObstacleContacts++; MaximumContactImpulse = Mathf.Max(MaximumContactImpulse, collision.impulse.magnitude);
        }
        public void ResetReview()
        {
            if(_linksSuspended){Knockdown.AbortForReset();ResumeLinks();}
            _plantValid=false;_plantTimer=0;_settleLean=0;
            ClearImpactReaction();
            RestoreBodyStrikePose();
            RestoreStrikeTorsoLimit();
            _hitReaction.Clear();_punchPreviousTarget.Clear();_punchImpactAge[0]=_punchImpactAge[1]=-1;ClearFistDrive();
            _guardRemaining[0]=_guardRemaining[1]=_guardWeight[0]=_guardWeight[1]=0;
            for(var side=0;side<2;side++)
            {
                _punchReference[side]=_punchEntryPending[side]=false;_punchSourceAge[side]=0;
                if(_punchLimits[side]){var joint=Find("UpperArm_"+(side==0?"L":"R")).Joint;joint.angularYLimit=joint.angularZLimit=new SoftJointLimit{limit=15};joint.highAngularXLimit=new SoftJointLimit{limit=_punchOriginalHighX[side]};_punchLimits[side]=false;}
            }
            LowReachRequest=0;LowReachWeight=0;
            if(_lowReachWasActive)
            {
                var spine=Find("Spine");var chest=Find("Chest");spine.Joint.connectedAnchor=spine.StartConnectedAnchor;
                spine.Joint.lowAngularXLimit=new SoftJointLimit{limit=-10};spine.Joint.highAngularXLimit=new SoftJointLimit{limit=10};
                chest.Joint.lowAngularXLimit=new SoftJointLimit{limit=-12};chest.Joint.highAngularXLimit=new SoftJointLimit{limit=12};
                _goals["Pelvis"].localPosition=_referencePelvisPosition;_goals["Pelvis"].localRotation=_rest["Pelvis"];
                Actor.Pose.AdditionalPelvisDrop=Actor.Pose.AdditionalPelvisPitch=0;_lowReachWasActive=false;
            }
            var body = Actor.Motor.Body;body.transform.SetPositionAndRotation(_spawn,_spawnRotation);body.position = _spawn; body.rotation = _spawnRotation;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            foreach (var link in _links)
            {
                link.Body.transform.SetPositionAndRotation(link.StartPosition,link.StartRotation);
                link.Body.position = link.StartPosition; link.Body.rotation = link.StartRotation;
                link.Body.linearVelocity = link.Body.angularVelocity = Vector3.zero; link.Body.WakeUp();
                link.Body.maxAngularVelocity=18;
                link.Joint.targetRotation = Quaternion.identity; link.Joint.targetAngularVelocity=Vector3.zero; link.Joint.slerpDrive = link.Drive;
            }
            TargetExercise = Exercise.Rest; LeftPowered = RightPowered = true;
            _poseWeight = _curlWeight = 0; _leftPower = _rightPower = 1;
            Array.Clear(_handActive,0,2);Array.Clear(_handWeights,0,2);_poseReset++;
            _handDriveScale[0]=_handDriveScale[1]=1;
            Array.Clear(_gripPosture,0,2);
            MaximumJointGap = MaximumBodySpeed = MaximumContactImpulse = 0; ObstacleContacts = 0; SetObstacle(false);
            MaximumSkinBodyOffset=0;
            Physics.SyncTransforms(); CapturePhysics(); Actor.FixedPose.CaptureEvaluatedPose();
        }
    }

    public sealed class ActiveUpperContactProbe : MonoBehaviour
    {
        public ActiveUpperBodyReview Review;
        private void OnCollisionEnter(Collision collision) { if(Review!=null)Review.ObserveContact(GetComponent<Rigidbody>(),collision); }
        private void OnCollisionStay(Collision collision) { if(Review!=null)Review.ObserveContact(GetComponent<Rigidbody>(),collision); }
    }
}
