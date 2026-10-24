using System;
using System.Collections.Generic;
using System.Linq;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Stage 2 touch/hold lab, with an explicitly enabled lift/throw candidate.
    // Strike and damage-driven down/recovery integration remain separate gates.
    [DefaultExecutionOrder(-100)]
    public sealed partial class ActiveGrabPairReview : MonoBehaviour
    {
        public enum HandPhase { Idle, Seeking, Holding, AwaitRelease }
        public enum GrabMotion { Original, ForwardReach, AdaptiveReach }
        public sealed class PhysicalGrip
        {
            public Rigidbody A,B;
            public Vector3 LocalA,LocalB;
            public ConfigurableJoint Joint;
            public int Owners;
            public float Force;
            public float Span;
            public SkinSurfaceReview.Anchor SurfaceB;
            public bool RollingHands;
            // A grip on static level geometry (a wall or ledge): no B body, LocalB is the world point.
            public bool World;
            public bool Valid=>Joint!=null&&A!=null&&!A.isKinematic&&A.gameObject.activeInHierarchy&&(World||B!=null&&!B.isKinematic&&B.gameObject.activeInHierarchy);
            public Vector3 PointA=>A.position+A.rotation*LocalA;
            public Vector3 PointB=>World?LocalB:B.position+B.rotation*LocalB;
            public float Gap=>Valid?Mathf.Max(0,Vector3.Distance(PointA,PointB)-Span):0;
        }
        public sealed class Hand
        {
            public bool Button,Previous,MustRelease;
            public float Age;
            public HandPhase Phase;
            public PhysicalGrip Connection;
            public Rigidbody TargetBody;
            public Collider TargetCollider;
            public ActiveUpperBodyReview TargetRig;
            public Vector3 MotionTargetLocal;
            public bool MotionTargetReady;
            internal bool LiftReady;
            internal float LiftWait;
            internal Vector3 LiftGoal;
            internal Vector3 LiftStart;
            public float LiftRise {get;internal set;}
            public readonly GripStressState Stress=new GripStressState();
        }
        private struct Touch
        {
            public ulong Tick;
            public Rigidbody Body;
            public Collider Collider;
            public ActiveUpperBodyReview Rig;
            public LimpGrabTargetReview LowTarget;
            public Vector3 LocalPoint;
            public float Distance;
            public SkinSurfaceReview.Anchor Surface;
            public bool World;
        }
        public sealed class Player
        {
            public ActiveUpperBodyReview Rig;
            public readonly Hand[] Hands={new Hand(),new Hand()};
            public Vector2 Move;
            public bool Sprint;
            // Set while down; until the player acts again it keeps the heading it stood up with (see MovePlayer).
            public bool HoldHeading,HeadingHeld;public Vector3 HeldHeading;
            // Seconds since a hand last reached, held or carried (ActiveGrabPairReview.Downed).
            public float HandsIdleSeconds;
            public int Acquisitions,Releases,Breaks;
            public int PalmTouches,RejectedArmTouches;
            public float ClosestRejectedTouch=float.PositiveInfinity;
            public string LastRelease;
            public bool Mutual;
            public bool InputArmed=true;
            public bool Jump;
            internal bool JumpPrevious;
            internal float ClimbAge=-1;
            // Climb on the ragdoll: one world joint per holding forearm, the ledge top and the way over it.
            internal readonly ConfigurableJoint[] ClimbHolds=new ConfigurableJoint[2];
            internal readonly float[] ClimbHoldTop=new float[2];
            internal readonly Collider[] ClimbHoldSurface=new Collider[2];
            internal Vector3 ClimbForward;
            internal Collider ClimbWall;
            internal bool ClimbSession;
            internal float ClimbFree,ClimbGrabWait;
            public int Hops;
            public int Climbs,Jumps;
            internal bool DropkickHit;
            public int DropkickHits;
        }

        internal static readonly HashSet<ActiveGrabPairReview> Live=new HashSet<ActiveGrabPairReview>();
        private readonly List<PhysicalGrip> _connections=new List<PhysicalGrip>();
        private readonly Touch[,] _touches=new Touch[2,2];
        private readonly HashSet<Material> _materials=new HashSet<Material>();
        private float _stepSeconds=1f/60;
        public Player[] Players {get;private set;}
        public Camera View {get;private set;}
        public bool ManualStep,ReadKeyboard=true;
        public bool Suppressed {get;private set;}
        public bool Ready {get;private set;}
        public ulong Tick {get;private set;}
        public int SharedConnectionsCreated {get;private set;}
        public float MaximumGripGap {get;private set;}
        public float MaximumGripForce {get;private set;}
        public float WalkSpeed=1.4f,SprintSpeed=2.1f,Acceleration=20f;
        public float HoldThreshold=.15f,PalmContactRadius=.055f,GripBreakForce=2000f;
        public float SpawnSeparation=.5f;
        public float ReachHalfWidth=.17f;
        public GrabMotion Motion=GrabMotion.Original;
        public string SkinTransportPath;
        public LimpGrabTargetReview ExternalTarget;
        public bool UseLimpTargetFixture;
        public bool MatchHandEnvelope;
        public bool EnableSkinContacts;
        public SkinContactReview SkinContacts {get;private set;}
        public bool EnableStandingSkinContacts;
        public StandingSkinContactReview StandingContacts {get;private set;}
        public bool EnablePhysicalPunch;
        public ActiveUpperBodyReview.ReviewStrikeStyle StrikeStyle;
        public PhysicalPunchReview PhysicalPunches {get;private set;}
        public LimpGrabTargetReview.Orientation FixtureOrientation;
        // Review aim height, not an acquisition query or a remote grab.
        public float AimHeightOffset;
        public int PhysicalConnections=>_connections.Count(c=>c.Valid&&c.Owners>0);
        public int LogicalGrips=>Players.Sum(p=>p.Hands.Count(h=>h.Phase==HandPhase.Holding));
        private static bool Active(Player player)=>player.Rig!=null&&player.Rig.isActiveAndEnabled&&player.Rig.Ready&&player.Rig.Actor!=null&&player.Rig.Actor.Motor.Body!=null;
        private static bool HandAvailable(Player player,int side)
        {
            if(!Active(player))return false;var shape=player.Rig.Find("Forearm_"+(side==0?"L":"R")).Shape;
            return shape!=null&&shape.enabled&&!shape.isTrigger&&shape.gameObject.activeInHierarchy&&shape.attachedRigidbody!=null;
        }
        private void OnEnable()=>Live.Add(this);
        private void OnDisable(){Live.Remove(this);if(Ready)ReleaseEveryone("Disabled");SkinContacts?.Dispose();SkinContacts=null;StandingContacts?.Dispose();StandingContacts=null;}
        private void OnDestroy(){foreach(var material in _materials)if(material!=null)Destroy(material);}
        private void Start(){if(!Ready)Build();}
        public static ActiveGrabPairReview Create(Scene scene,float separation=.5f,GrabMotion motion=GrabMotion.Original,string skinTransportPath=null,bool limpFixture=false,LimpGrabTargetReview.Orientation orientation=LimpGrabTargetReview.Orientation.FaceUp,bool matchHandEnvelope=false)
        {
            var go=new GameObject("Active Grab - Stage 2 - Press Play");SceneManager.MoveGameObjectToScene(go,scene);
            var review=go.AddComponent<ActiveGrabPairReview>();review.SpawnSeparation=separation;review.Motion=motion;review.SkinTransportPath=skinTransportPath;review.UseLimpTargetFixture=limpFixture;review.FixtureOrientation=orientation;review.MatchHandEnvelope=matchHandEnvelope;if(Application.isPlaying)review.Build();return review;
        }
        private void Build()
        {
            Players=new Player[2];
            for(var index=0;index<2;index++)
            {
                var rig=ActiveUpperBodyReview.CreateInteractionRig(gameObject.scene,new Vector3(0,.025f,(index==0?-1:1)*SpawnSeparation*.5f),Quaternion.Euler(0,index==0?0:180,0),SkinTransportPath);
                rig.StrikeStyle=StrikeStyle;
                rig.transform.SetParent(transform,true);rig.name="Physical Player "+(index+1);
                if(MatchHandEnvelope)rig.MatchHandSkinEnvelope();
                var slot=index;rig.PhysicalContact+=(body,contact)=>RecordContact(slot,body,contact);
                if(index==1)foreach(var skin in rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterial.SetColor("_BaseColor",new Color(.95f,.46f,.16f));
                Players[index]=new Player{Rig=rig};
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,gameObject.scene);floor.transform.SetParent(transform,true);
            floor.name="Stage2Floor";floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(14,.2f,14);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.24f,.28f,.33f));floor.GetComponent<Renderer>().sharedMaterial=material;_materials.Add(material);
            var cameraObject=new GameObject("Stage2Camera");cameraObject.transform.SetParent(transform,false);View=cameraObject.AddComponent<Camera>();
            View.orthographic=true;View.orthographicSize=.95f;View.nearClipPlane=.05f;View.farClipPlane=30;
            View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.12f,.17f,.23f);FrameCamera();
            var lamp=new GameObject("Stage2Light");lamp.transform.SetParent(transform,false);var light=lamp.AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=1.4f;light.shadows=LightShadows.Soft;light.shadowStrength=.65f;light.transform.rotation=Quaternion.Euler(38,140,0);
            if(UseLimpTargetFixture)
            {
                Players[1].Rig.gameObject.SetActive(false);
                ExternalTarget=LimpGrabTargetReview.Create(gameObject.scene,string.IsNullOrEmpty(SkinTransportPath)?PhysicsJumpReview.LatWeightTransportPath:SkinTransportPath,FixtureOrientation);
            }
            Ready=true;Physics.SyncTransforms();CapturePhysics();
        }
        public void Buttons(int player,bool left,bool right)
        { Players[player].Hands[0].Button=left;Players[player].Hands[1].Button=right; }
        public void UseStrikeStyle(ActiveUpperBodyReview.ReviewStrikeStyle style)
        {
            StrikeStyle=style;
            if(Players!=null)foreach(var player in Players)if(player?.Rig!=null)player.Rig.StrikeStyle=style;
        }
        public void SubmitPhysicalHandEdge(int player,int side,bool down,double heldSeconds=double.NaN)
        {
            if(!Ready||!EnablePhysicalPunch)return;
            if(PhysicalPunches==null)PhysicalPunches=new PhysicalPunchReview(this);
            Players[player].Hands[side].Button=down;PhysicalPunches.QueueEdge(player,side,down,heldSeconds);
        }
        public void SetSuppressed(bool value)
        { Suppressed=value;if(value)ReleaseEveryone("Menu"); }
        private void FixedUpdate()
        {
            if(!Ready||ManualStep)return;
            if(ReadKeyboard&&Keyboard.current!=null)
            {
                var keys=Keyboard.current;
                if(keys.escapeKey.wasPressedThisFrame)SetSuppressed(!Suppressed);
                if(keys.rKey.wasPressedThisFrame)ResetReview();
                Players[0].Move=new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
                Players[1].Move=new Vector2((keys.rightArrowKey.isPressed?1:0)-(keys.leftArrowKey.isPressed?1:0),(keys.upArrowKey.isPressed?1:0)-(keys.downArrowKey.isPressed?1:0));
                Players[0].Sprint=keys.leftShiftKey.isPressed;Players[1].Sprint=keys.rightShiftKey.isPressed;
                Players[0].Jump=keys.spaceKey.isPressed;Players[1].Jump=keys.rightCtrlKey.isPressed;
                AimHeightOffset=(keys.uKey.isPressed?.14f:0)-(keys.jKey.isPressed?.14f:0);
                if(Mouse.current!=null)Buttons(0,Mouse.current.leftButton.isPressed,Mouse.current.rightButton.isPressed);
                Buttons(1,keys.nKey.isPressed,keys.mKey.isPressed);
                if(EnablePhysicalPunch)
                {
                    foreach(var e in ReviewInputClock.MouseEdges)if(Mouse.current!=null&&e.Device==Mouse.current.deviceId)SubmitPhysicalHandEdge(0,e.Side,e.Down,e.HeldSeconds);
                    foreach(var e in ReviewInputClock.KeyboardEdges)if(e.Device==keys.deviceId)SubmitPhysicalHandEdge(1,e.Side,e.Down,e.HeldSeconds);
                }
            }
            Step(Time.fixedDeltaTime);
        }
        private void RecordContact(int player,Rigidbody ownBody,Collision collision)
        {
            if(!Ready||Suppressed||!Active(Players[player]))return;
            PhysicalPunches?.Contact(player,ownBody,collision);
            var owner=Players[player];var other=collision.rigidbody;
            if(other==null){RecordWorldContact(player,ownBody,collision);return;}
            if(other.isKinematic)return;
            var target=other.GetComponentInParent<ActiveUpperBodyReview>()??KnockedDownOwner(other);
            var lowTarget=ExternalTarget!=null&&ExternalTarget.Available&&ExternalTarget.Owns(other)?ExternalTarget:null;
            var skinPatch=collision.collider.GetComponent<SkinContactPatchReview>();
            var standingPatch=collision.collider.GetComponent<StandingSkinPatchReview>();
            if(standingPatch!=null&&(StandingContacts==null||standingPatch.Owner.attachedRigidbody!=ownBody))return;
            if(skinPatch!=null&&SkinContacts==null)return;
            if(skinPatch!=null&&skinPatch.OwnerShape.attachedRigidbody!=ownBody)return;
            if(target==owner.Rig||(target==null&&lowTarget==null)||(target!=null&&!target.Ready))return;
            for(var side=0;side<2;side++)
            {
                var hand=owner.Hands[side];
                if(hand.Phase!=HandPhase.Seeking||ownBody!=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body)continue;
                for(var c=0;c<collision.contactCount;c++)
                {
                    var contact=collision.GetContact(c);var distance=Vector3.Distance(contact.point,owner.Rig.HandPosition(side));
                    if(distance>PalmContactRadius){owner.RejectedArmTouches++;owner.ClosestRejectedTouch=Mathf.Min(owner.ClosestRejectedTouch,distance);continue;}
                    owner.PalmTouches++;
                    var previous=_touches[player,side];
                    if(previous.Tick==Tick&&(distance>previous.Distance+.000001f||Mathf.Abs(distance-previous.Distance)<.000001f&&previous.Body!=null&&other.GetInstanceID()>previous.Body.GetInstanceID()))continue;
                    var surface=StandingContacts!=null?StandingContacts.Contact(other,contact.point):skinPatch!=null?SkinContacts.Surface.Closest(contact.point):null;
                    if(surface!=null)surface.Body=other;
                    _touches[player,side]=new Touch{Tick=Tick,Body=other,Collider=collision.collider,Rig=target,LowTarget=lowTarget,LocalPoint=Quaternion.Inverse(other.rotation)*(contact.point-other.position),Distance=distance,Surface=surface};
                }
            }
        }
        public void Step(float seconds,bool reverseOrder=false)
        {
            if(!Ready)throw new InvalidOperationException("Stage 2 is not ready.");
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            Tick++;_stepSeconds=seconds;
            if(EnableSkinContacts&&SkinContacts==null&&ExternalTarget!=null&&isActiveAndEnabled)SkinContacts=new SkinContactReview(this);
            if(!EnableSkinContacts&&SkinContacts!=null){ReleaseEveryone("ContactModeChanged");SkinContacts.Dispose();SkinContacts=null;}
            SkinContacts?.Step();
            if(EnableStandingSkinContacts&&StandingContacts==null&&ExternalTarget==null&&isActiveAndEnabled&&Players.All(Active))StandingContacts=new StandingSkinContactReview(this);
            if(!EnableStandingSkinContacts&&StandingContacts!=null){ReleaseEveryone("ContactModeChanged");StandingContacts.Dispose();StandingContacts=null;}
            StandingContacts?.Step();
            if(EnablePhysicalPunch&&PhysicalPunches==null)PhysicalPunches=new PhysicalPunchReview(this);
            if(!EnablePhysicalPunch&&PhysicalPunches!=null){PhysicalPunches.Suppress();PhysicalPunches=null;}
            GateRunningHands();
            PhysicalPunches?.BeforeStep(seconds);
            foreach(var player in Players)if(!Suppressed&&!player.Hands.Any(h=>h.Button))player.InputArmed=true;
            for(var p=0;p<2;p++)for(var side=0;side<2;side++)
            {
                var hand=Players[p].Hands[side];
                if((Suppressed||!Players[p].InputArmed)&&hand.Button)hand.MustRelease=true;
                if(!HandAvailable(Players[p],side)){Release(p,side,Active(Players[p])?"HandLost":"OwnerLost",true);hand.Previous=hand.Button;hand.Age=0;continue;}
                if(!hand.Button){if(hand.Connection!=null)Release(p,side,"Release",false);hand.MustRelease=false;hand.Age=0;hand.Phase=HandPhase.Idle;}
                else if(Suppressed||hand.MustRelease)hand.Phase=HandPhase.AwaitRelease;
                else if(hand.Connection==null)
                {if(!hand.Previous)hand.Age=0;hand.Age+=seconds;hand.Phase=PhysicalPunches!=null?(PhysicalPunches.State(p).Phase(side)==HandIntentPhase.GrabSeek?HandPhase.Seeking:HandPhase.Idle):hand.Age+1e-6f>=HoldThreshold?HandPhase.Seeking:HandPhase.Idle;}
                if(hand.Connection!=null&&(!hand.Connection.Valid||hand.TargetCollider==null||!hand.TargetCollider.enabled||hand.TargetCollider.isTrigger||ExternalTarget!=null&&ExternalTarget.Owns(hand.TargetBody)&&!ExternalTarget.Available))Release(p,side,"ContactLost",true);
                hand.Previous=hand.Button;
            }
            // Both players' input is applied before admitting contact or resolving reciprocity.
            for(var slot=0;slot<2;slot++)
            {
                var p=reverseOrder?1-slot:slot;
                for(var side=0;side<2;side++)TryAcquire(p,side);
            }
            var mutual=Players[0].Hands.Any(h=>h.Phase==HandPhase.Holding&&h.TargetRig==Players[1].Rig)&&Players[1].Hands.Any(h=>h.Phase==HandPhase.Holding&&h.TargetRig==Players[0].Rig);
            foreach(var player in Players)player.Mutual=mutual;
            ApplyLimpLiftSupport(seconds);
            for(var p=0;p<2;p++)
            {
                var player=Players[p];if(!Active(player))continue;
                if(player.Rig.RagdollActive){IgnoreDownedWithRoot(p,false,true);player.HoldHeading=true;player.HeadingHeld=false;StepClimbRagdoll(p,seconds);StepDropkick(p,seconds);StepHeldWake(p,seconds);player.Rig.StepKnockdown(seconds);continue;}
                player.HandsIdleSeconds=player.Hands.Any(h=>h.Phase!=HandPhase.Idle)?0:player.HandsIdleSeconds+seconds;
                SeparateFromDowned(p);
                var body=player.Rig.Actor.Motor.Body;
                for(var side=0;side<2;side++)
                {
                    var hand=player.Hands[side];var active=hand.Phase==HandPhase.Seeking||hand.Phase==HandPhase.Holding;
                    var target=MotionTarget(p,side,active,seconds);
                    player.Rig.SetHandTarget(side,active,target);
                    player.Rig.SetGripPosture(side,Motion!=GrabMotion.AdaptiveReach&&hand.Phase==HandPhase.Holding);
                    // The skin point can transfer a one-hand load through a
                    // longer lever than the old trunk hull. Retain the lift goal
                    // with bounded arm torque, without increasing weight support.
                    var liftDrive=SkinContacts!=null&&player.Hands.Count(h=>h.Button&&!h.MustRelease)==1?3f:2.5f;
                    player.Rig.SetHandDriveScale(side,hand.Phase==HandPhase.Holding?(EnableLiftThrow?liftDrive:.65f):1f);
                    player.Rig.SetOverheadReach(side,active);
                    PhysicalPunches?.ApplyReference(p,side);
                }
                var lowHeight=player.Hands.Where(h=>h.Phase==HandPhase.Seeking||h.Phase==HandPhase.Holding).Select(h=>h.MotionTargetLocal.y).DefaultIfEmpty(1).Min();
                player.Rig.LowReachRequest=(ExternalTarget!=null||Active(Players[1-p])&&Players[1-p].Rig.KnockedDown)&&Motion==GrabMotion.AdaptiveReach?Mathf.Clamp01((.42f-lowHeight)/.22f):0;
                if(AimLowStrike(p))player.Rig.LowReachRequest=1;
                player.Rig.ReferenceReachFraction=Motion==GrabMotion.Original?1f:.96f;
                MovePlayer(p,seconds);player.Rig.Step(seconds);
            }
        }
        // A knocked-down rig's body is its ragdoll (RagdollRecoveryReview proxies outside the rig's
        // hierarchy). Resolve those bodies back to the rig so it can be grabbed, lifted and thrown.
        private static ActiveUpperBodyReview KnockedDownOwner(Rigidbody body)
        {
            var hurtbox=body.GetComponentInParent<StrikeReviewHurtbox>();
            var rig=hurtbox!=null&&hurtbox.ActorRoot!=null?hurtbox.ActorRoot.GetComponentInParent<ActiveUpperBodyReview>():null;
            return rig!=null&&rig.RagdollActive?rig:null;
        }
        private readonly Rigidbody[] _knockdownBodies=new Rigidbody[16];
        internal Rigidbody[] KnockdownBodies(ActiveUpperBodyReview rig)
        {var count=rig.Knockdown.CopyDynamicBodies(_knockdownBodies);return _knockdownBodies.Take(count).ToArray();}
        // Nearest point on the downed body's colliders to the reaching shoulder, preferring the trunk
        // (pelvis, spine, chest) when it is in reach so a lift raises the body, not a dangling foot.
        private bool TryKnockdownSurface(ActiveUpperBodyReview rig,Vector3 from,out Vector3 point)
        {
            point=default;var best=float.PositiveInfinity;var trunk=default(Vector3);var bestTrunk=float.PositiveInfinity;
            foreach(var body in KnockdownBodies(rig))foreach(var collider in body.GetComponents<Collider>())
            {
                if(!collider.enabled||collider.isTrigger)continue;
                var candidate=collider.ClosestPoint(from);var distance=(candidate-from).sqrMagnitude;
                if(distance<best){best=distance;point=candidate;}
                var isTrunk=body==rig.Actor.Motor.Body||body.name.StartsWith("Spine")||body.name.StartsWith("Chest");
                if(isTrunk&&distance<bestTrunk){bestTrunk=distance;trunk=candidate;}
            }
            if(bestTrunk<TrunkReach*TrunkReach)point=trunk;
            return best<float.PositiveInfinity;
        }
        // Shoulder-to-surface distance within which the trunk is preferred: about the arm's reach
        // (.445 m to the fist). Further away the hand takes the nearest part instead of straining.
        public float TrunkReach=.42f;
        private Vector3 MotionTarget(int player,int side,bool active,float seconds)
        {
            var owner=Players[player];var hand=owner.Hands[side];var rig=owner.Rig;var body=rig.Actor.Motor.Body;
            var local=new Vector3(side==0?-ReachHalfWidth:ReachHalfWidth,Motion==GrabMotion.Original?.55f:.60f,Motion==GrabMotion.Original?.32f:.40f);
            if(Motion==GrabMotion.AdaptiveReach)
            {
                var other=Players[1-player];
                if(Active(other)&&!other.Rig.RagdollActive)local.y=Mathf.Clamp(other.Rig.Find("Chest").Body.position.y-body.position.y+.09f+AimHeightOffset,.40f,.85f);
                if(active&&Active(other)&&other.Rig.RagdollActive&&TryKnockdownSurface(other.Rig,rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position,out var downPoint))
                {
                    // Press slightly into the limp body: a goal exactly on its surface stops the palm a
                    // hair short and never makes contact on a body lying flat.
                    downPoint+=(downPoint-rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position).normalized*.03f;
                    local=Quaternion.Inverse(body.rotation)*(downPoint-body.position);
                }
                else if(StandingContacts!=null&&active&&Active(other))
                {
                    var point=StandingContacts.Surface(1-player).Closest(rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position).Position;
                    local=Quaternion.Inverse(body.rotation)*(point-body.position);
                }
                if(ExternalTarget!=null&&active&&ExternalTarget.TrySurface(rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position,out var lowPoint))
                {
                    if(SkinContacts!=null&&ExternalTarget.Available)lowPoint=SkinContacts.Surface.Closest(rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position).Position;
                    local=Quaternion.Inverse(body.rotation)*(lowPoint-body.position);
                }
                if(hand.Phase==HandPhase.Seeking&&TryClimbSurface(player,side,out var ledge))local=Quaternion.Inverse(body.rotation)*(ledge-body.position);
                var grip=hand.Connection;
                if(hand.Phase==HandPhase.Holding&&grip!=null&&grip.Valid)
                {
                    // Follow the actual attachment's height and distance. Rounded hands
                    // retain their physical contact radius; no skin or body is teleported.
                    var forearm=rig.Find("Forearm_"+(side==0?"L":"R")).Body;
                    var point=grip.A==forearm?grip.PointB:grip.PointA;
                    var shoulder=rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position;
                    var palm=point+(shoulder-point).normalized*grip.Span;
                    var tip=palm+(rig.HandPosition(side)-rig.PalmCenter(side));
                    local=Quaternion.Inverse(body.rotation)*(tip-body.position);
                }
            }
            local=LiftMotionTarget(player,side,local,seconds);
            if(Motion==GrabMotion.Original)return body.position+body.rotation*local;
            if(!active){hand.MotionTargetReady=false;return body.position+body.rotation*local;}
            if(!hand.MotionTargetReady){hand.MotionTargetLocal=local;hand.MotionTargetReady=true;}
            else hand.MotionTargetLocal=Vector3.Lerp(hand.MotionTargetLocal,local,1-Mathf.Exp(-seconds/.10f));
            return body.position+body.rotation*hand.MotionTargetLocal;
        }
        private void TryAcquire(int player,int side)
        {
            var owner=Players[player];var hand=owner.Hands[side];var touch=_touches[player,side];
            if(Suppressed||!Active(owner)||hand.Phase!=HandPhase.Seeking||touch.Tick!=Tick-1||touch.Body==null&&!touch.World||(touch.Rig==null&&touch.LowTarget==null&&!touch.World)||(touch.LowTarget!=null&&!touch.LowTarget.Available)||touch.Collider==null||!touch.Collider.enabled||touch.Collider.isTrigger||!touch.Collider.gameObject.activeInHierarchy)return;
            if(touch.World){AcquireWorld(player,side,touch);return;}
            if(touch.Rig!=null&&touch.Rig.RagdollActive&&(touch.Rig.WokeInHands||touch.Rig.GettingUp))return;
            var point=touch.Surface!=null?touch.Surface.Position:touch.Body.position+touch.Body.rotation*touch.LocalPoint;
            if(Vector3.Distance(owner.Rig.HandPosition(side),point)>PalmContactRadius+.005f)return;
            var body=owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Body;
            var connection=_connections.FirstOrDefault(c=>c.Valid&&((c.A==body&&c.B==touch.Body)||(c.B==body&&c.A==touch.Body))
                &&Vector3.Distance(point,(c.A.position+c.A.rotation*c.LocalA+c.B.position+c.B.rotation*c.LocalB)*.5f)<.03f);
            if(connection==null)
            {
                // Constrain palm centres at their contact distance. Locking two
                // material points on rounded forearms prevents the hands from
                // rolling and makes the contact solver fight the grip solver.
                var palm=owner.Rig.PalmCenter(side);var targetPoint=point;
                var ownRadius=((CapsuleCollider)owner.Rig.Find("Forearm_"+(side==0?"L":"R")).Shape).radius;
                var minimumSpan=ownRadius+.004f;var rollingHands=false;
                if(touch.Rig!=null)foreach(var targetSide in new[]{0,1})
                {
                    var targetForearm=touch.Rig.Find("Forearm_"+(targetSide==0?"L":"R")).Body;
                    if(targetForearm==touch.Body&&Vector3.Distance(touch.Rig.PalmCenter(targetSide),point)<.055f)
                    {
                        targetPoint=touch.Rig.PalmCenter(targetSide);
                        minimumSpan=ownRadius+((CapsuleCollider)touch.Rig.Find("Forearm_"+(targetSide==0?"L":"R")).Shape).radius+.004f;
                        if(StandingContacts!=null&&touch.Surface!=null)
                        {minimumSpan=ownRadius+Mathf.Clamp(Vector3.Distance(targetPoint,point),.020f,.041f)+.001f;rollingHands=true;}
                    }
                }
                var span=rollingHands?minimumSpan:touch.Surface!=null?ownRadius+.001f:Mathf.Max(minimumSpan,Vector3.Distance(palm,targetPoint)+.002f);
                var joint=body.gameObject.AddComponent<ConfigurableJoint>();joint.connectedBody=touch.Body;joint.autoConfigureConnectedAnchor=false;
                joint.anchor=Quaternion.Inverse(body.rotation)*(palm-body.position);joint.connectedAnchor=Quaternion.Inverse(touch.Body.rotation)*(targetPoint-touch.Body.position);
                joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Limited;
                joint.linearLimit=new SoftJointLimit{limit=span,contactDistance=.001f};
                joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
                joint.projectionMode=JointProjectionMode.None;joint.enableCollision=true;joint.breakForce=GripBreakForce;joint.breakTorque=GripBreakForce;
                connection=new PhysicalGrip{A=body,B=touch.Body,LocalA=joint.anchor,LocalB=joint.connectedAnchor,Joint=joint,Span=span,SurfaceB=rollingHands?null:touch.Surface,RollingHands=rollingHands};_connections.Add(connection);SharedConnectionsCreated++;
            }
            connection.Owners++;hand.Connection=connection;hand.TargetBody=touch.Body;hand.TargetCollider=touch.Collider;hand.TargetRig=touch.Rig;
            hand.Phase=HandPhase.Holding;hand.Stress.Reset();owner.Acquisitions++;
            PhysicalPunches?.Acquired(player,side);
        }
        private void MovePlayer(int index,float seconds)
        {
            var player=Players[index];var rig=player.Rig;var body=rig.Actor.Motor.Body;
            // A punching arm is not locomotion: its forward momentum made the feet brake (or match
            // the walk speed) backwards, so every strike pulled the attacker back (2026-10-16).
            var momentum=body.mass*body.linearVelocity;var mass=body.mass;
            foreach(var link in rig.Links){if(rig.IsStrikingArm(link))continue;momentum+=link.Body.mass*link.Body.linearVelocity;mass+=link.Body.mass;}
            var velocity=momentum/mass;var horizontal=new Vector3(velocity.x,0,velocity.z);
            var input=Suppressed?Vector2.zero:Vector2.ClampMagnitude(player.Move,1);var speed=player.Sprint?SprintSpeed:WalkSpeed;
            var desired=new Vector3(input.x,0,input.y)*speed;
            if(PushDowned(index,desired))desired*=DownedPushDrag;
            var shoved=rig.Shoved;
            var force=input.sqrMagnitude>.0001f?(shoved?ActiveUpperBodyReview.ShovedDrive:1)*Vector3.ClampMagnitude((desired-horizontal)*rig.TotalMass/seconds,rig.TotalMass*Acceleration)
                // After a punch shove the feet dig in harder, so the knockback reads
                // as one decisive step back instead of a long slide.
                // While striking, the feet stop any slide within a step (both ways, so no net travel is
                // added): the arm's ~10 m/s extension otherwise slid the attacker ~1.3 cm back.
                :rig.IsStriking&&!rig.ImpactReactionActive&&!shoved?Vector3.ClampMagnitude(-horizontal*rig.TotalMass/seconds,600)
                :Vector3.ClampMagnitude(-horizontal*rig.TotalMass*(rig.ImpactReactionActive?7:3),rig.ImpactReactionActive?160:80);
            force=EasePressIntoDowned(index,force);
            rig.Supported=Grounded(rig)&&body.linearVelocity.y<.5f;rig.Sprinting=player.Sprint;
            var opponent=Players[1-index];var range=Active(opponent)&&!opponent.Rig.RagdollActive?Vector3.ProjectOnPlane(opponent.Rig.Actor.Motor.Body.position-body.position,Vector3.up).magnitude:float.PositiveInfinity;
            rig.WalkArmSwingScale=Mathf.InverseLerp(.8f,1.3f,range);
            StepJumpAndClimb(index,seconds);
            body.AddForce(force*(rig.Supported?1:.3f)*rig.ImpactMovementControl,ForceMode.Force);
            if(!Suppressed&&rig.Supported)body.AddForce(rig.BodyStrikeAcceleration*rig.TotalMass*(shoved?ActiveUpperBodyReview.ShovedDrive:1),ForceMode.Force);
            // The local two-player lab faces the opponent, allowing backpedal/strafe while holding;
            // a character holding a wall faces the wall.
            var walls=WorldGripPoints(player);
            if(walls.Length==0&&ExternalTarget==null&&!Active(Players[1-index]))return;
            // After getting up it keeps the heading it stood up with until the player moves, sprints or uses a hand: a
            // face-up get-up stands facing its feet, often away from the opponent, and the facing torque then spun it
            // round on the spot (2026-10-21 review: "돌아보는거 없애").
            if(input.sqrMagnitude>.0001f||player.Sprint||rig.IsStriking||player.Hands.Any(h=>h.Button||h.Phase!=HandPhase.Idle))player.HoldHeading=false;
            // Held, it is still turned back to that heading: switching the facing torque off let a hit right after
            // standing spin it freely (2026-10-21 review).
            if(!player.HoldHeading)player.HeadingHeld=false;
            else if(!player.HeadingHeld){player.HeldHeading=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,Vector3.up);player.HeadingHeld=true;}
            var direction=player.HoldHeading&&walls.Length==0?player.HeldHeading:(walls.Length>0?walls.Aggregate(Vector3.zero,(sum,p)=>sum+p)/walls.Length:ExternalTarget!=null?ExternalTarget.Center:Players[1-index].Rig.Actor.Motor.Body.position)-body.position;direction.y=0;
            // Carrying a downed body overhead: its root is right above, so face the walk input instead
            // (or hold the current heading) and throw where the carrier is facing.
            if(walls.Length==0&&player.Hands.Any(h=>h.Phase==HandPhase.Holding&&h.TargetRig!=null&&h.TargetRig.KnockedDown))
                direction=input.sqrMagnitude>.0001f?new Vector3(input.x,0,input.y):body.rotation*Vector3.forward;
            if(direction.sqrMagnitude>.001f)
            {
                var error=Vector3.SignedAngle(body.rotation*Vector3.forward,direction,Vector3.up)*Mathf.Deg2Rad;
                var acceleration=Mathf.Clamp(error*80-body.angularVelocity.y*16,-100,100);
                // The root is only 8 kg now. Rotate the articulated character's
                // inertia, not just that root's much smaller inertia tensor.
                var inertia=YawInertia(body,body.worldCenterOfMass);
                foreach(var link in rig.Links)inertia+=YawInertia(link.Body,body.worldCenterOfMass);
                body.AddTorque(Vector3.up*(acceleration*inertia)*rig.ImpactMovementControl,ForceMode.Force);
            }
        }
        private static float YawInertia(Rigidbody body,Vector3 origin)
        {
            var axis=Quaternion.Inverse(body.rotation*body.inertiaTensorRotation)*Vector3.up;
            var diagonal=body.inertiaTensor;var offset=body.worldCenterOfMass-origin;
            return diagonal.x*axis.x*axis.x+diagonal.y*axis.y*axis.y+diagonal.z*axis.z*axis.z+body.mass*(offset.x*offset.x+offset.z*offset.z);
        }
        public void CapturePhysics()
        {
            if(!Ready)return;
            PhysicalPunches?.AfterPhysics();
            foreach(var connection in _connections)
            {
                if(!connection.Valid)continue;connection.Force=connection.Joint.currentForce.magnitude;
                MaximumGripGap=Mathf.Max(MaximumGripGap,connection.Gap);MaximumGripForce=Mathf.Max(MaximumGripForce,connection.Force);
            }
            for(var p=0;p<2;p++)for(var side=0;side<2;side++)
            {
                var hand=Players[p].Hands[side];if(hand.Connection==null)continue;
                // A carry loads the grip joint by design: that load is not a pull-off, and the swinging
                // limp body may stretch it further before it counts as lost.
                var carry=hand.LiftRise>0&&hand.TargetRig!=null&&hand.TargetRig.KnockedDown;
                if(!hand.Connection.Valid||!float.IsFinite(hand.Connection.Force)||hand.Connection.Gap>(carry?.15f:.06f))Release(p,side,"ContactLost",true);
                else if(!carry&&hand.Stress.Step(_stepSeconds,hand.Connection.Force/250f))Release(p,side,"GripStress",true);
            }
            foreach(var player in Players)if(Active(player))player.Rig.CapturePhysics();
            if(ExternalTarget!=null)ExternalTarget.Capture();
        }
        private void Release(int player,int side,string reason,bool broken)
        {
            var owner=Players[player];var hand=owner.Hands[side];var connection=hand.Connection;
            if(connection!=null&&Environment.GetEnvironmentVariable("PARTY_GRAB_DEBUG")=="1")Debug.Log($"GRAB release t{Tick} p{player} s{side} {reason} gap={connection.Gap:F3} force={connection.Force:F0} rise={hand.LiftRise:F2}");
            var throwBodies=ReleaseThrowBodies(player,side,reason,broken);
            PhysicalPunches?.Released(player,side);
            if(connection!=null)
            {
                connection.Owners--;
                if(connection.Owners<=0)
                {
                    if(connection.Joint!=null)
                    {
                        connection.Joint.xMotion=connection.Joint.yMotion=connection.Joint.zMotion=ConfigurableJointMotion.Free;
                        Destroy(connection.Joint);
                    }
                    _connections.Remove(connection);
                }
                owner.Releases++;if(broken)owner.Breaks++;owner.LastRelease=reason;
            }
            hand.Connection=null;hand.TargetBody=null;hand.TargetCollider=null;hand.TargetRig=null;hand.Stress.Reset();
            hand.MotionTargetReady=false;hand.LiftReady=false;hand.LiftRise=0;hand.LiftWait=0;
            hand.MustRelease=broken&&hand.Button;hand.Phase=hand.MustRelease?HandPhase.AwaitRelease:HandPhase.Idle;
            if(throwBodies!=null)ThrowReleasedTarget(player,throwBodies);
        }
        private void ReleaseEveryone(string reason)
        {
            PhysicalPunches?.Suppress();
            LiftSupportForce=0;_liftSupportWeight=0;
            for(var p=0;p<2;p++)
            {
                Players[p].Move=Vector2.zero;
                Players[p].InputArmed=false;Players[p].Mutual=false;
                for(var side=0;side<2;side++){Release(p,side,reason,false);Players[p].Hands[side].MustRelease=Players[p].Hands[side].Button;}
            }
        }
        public void ResetReview()
        {
            ReleaseEveryone("Reset");Array.Clear(_touches,0,_touches.Length);
            for(var p=0;p<Players.Length;p++)IgnoreDownedWithRoot(p,false,true);
            PhysicalPunches?.Reset();
            SkinContacts?.Dispose();SkinContacts=null;
            StandingContacts?.Dispose();StandingContacts=null;
            foreach(var player in Players){EndClimbHolds(player);player.ClimbAge=-1;player.Rig.ResetReview();player.Mutual=false;player.HoldHeading=player.HeadingHeld=false;}
            if(ExternalTarget!=null)ExternalTarget.ResetFixture();
            MaximumGripGap=MaximumGripForce=0;Physics.SyncTransforms();
        }
        private void FrameCamera()
        {
            if(Players==null||!Active(Players[0])||(ExternalTarget==null&&!Active(Players[1])))return;
            var center=(Players[0].Rig.Actor.Motor.Body.position+(ExternalTarget!=null?ExternalTarget.Center:Players[1].Rig.Actor.Motor.Body.position))*.5f+Vector3.up*(ExternalTarget!=null?.32f:.5f);
            View.transform.SetPositionAndRotation(center+new Vector3(2.3f,1.05f,1.9f),Quaternion.LookRotation(-new Vector3(2.3f,1.05f,1.9f)));
        }
        private void LateUpdate()
        {
            if(!Ready||ManualStep)return;
            Present(ReviewFixedLoop.RenderAlpha);
        }
        public void Present(float alpha)
        {
            foreach(var player in Players)if(Active(player))player.Rig.Actor.FixedPose.Present(alpha);if(ExternalTarget!=null)ExternalTarget.Present(alpha);FrameCamera();
            if(_feedback==null)_feedback=GetComponent<PhysicalPunchFeedbackReview>();
            if(_feedback!=null)_feedback.Present(View);
        }
        private PhysicalPunchFeedbackReview _feedback;
    }
}
