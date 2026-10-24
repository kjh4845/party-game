using System;
using System.Linq;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    [DefaultExecutionOrder(-200)]
    public sealed class SharedArenaReview : MonoBehaviour
    {
        public const float DefaultMovementScale=2f;
        public PhysicsJumpReview[] Actors;
        public SharedCameraReview SharedCamera;
        public int ControlledIndex;
        public bool ReadKeyboard=true,ShowHelp=true;
        public bool InputSuppressed { get; private set; }
        private bool _requireMouseUp=true;
        private void Start() { InitializeParticipants(); }
        public void InitializeParticipants()
        {
            foreach(var actor in Actors)
            {
                actor.ReadKeyboard=false;var receiver=actor.GetComponent<PunchDownReview>();
                if(receiver.Ragdoll==null)receiver.Bind(actor);
                actor.Hands.ReviewTargets=Actors.Where(a=>a!=actor).Select(a=>a.GetComponent<StrikeReviewTarget>()).ToArray();
            }
        }
        public static SharedArenaReview Create(Scene scene,int count,bool cameraGaitCandidate=true,float movementScale=DefaultMovementScale,Vector3[] spawnPositions=null)
        {
            if(count<2||count>4)throw new ArgumentOutOfRangeException(nameof(count));
            if(spawnPositions!=null&&spawnPositions.Length<count)throw new ArgumentException("Not enough review spawns.",nameof(spawnPositions));
            var root=new GameObject("SharedArenaReview");SceneManager.MoveGameObjectToScene(root,scene);var review=root.AddComponent<SharedArenaReview>();review.Actors=new PhysicsJumpReview[count];
            var spawn=spawnPositions??new[]{new Vector3(-.7f,.02f,-.65f),new Vector3(.7f,.02f,-.65f),new Vector3(-.7f,.02f,.65f),new Vector3(.7f,.02f,.65f)};
            var colors=new[]{new Color(.86f,.9f,.95f),new Color(.95f,.48f,.16f),new Color(.28f,.66f,.92f),new Color(.62f,.38f,.83f)};
            for(var i=0;i<count;i++)
            {
                var actor=PhysicsJumpReview.Create(spawn[i],true,PhysicsJumpReview.LatWeightTransportPath);SceneManager.MoveGameObjectToScene(actor.gameObject,scene);actor.transform.SetParent(root.transform,true);
                if(cameraGaitCandidate)actor.UseMovementSpeedCandidate(movementScale);
                actor.name="ReviewPlayer"+(i+1);actor.transform.rotation=Quaternion.LookRotation(new Vector3(-spawn[i].x,0,-spawn[i].z));actor.Motor.Body.rotation=actor.transform.rotation;
                actor.ReadKeyboard=false;actor.EnableGrabReview();actor.Hands.ConfigurePunchTiming(1.4);actor.Hands.PreciseHits=true;
                actor.gameObject.AddComponent<StrikeReviewTarget>().GrabEligible=true;actor.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
                var receiver=actor.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.ShowDebugOverlay=false;
                foreach(var skin in actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterial.SetColor("_BaseColor",colors[i]);review.Actors[i]=actor;
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.SetParent(root.transform,true);floor.name="ReviewFloor";floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(20,.2f,20);
            var checker=new Texture2D(2,2){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};checker.SetPixels(new[]{new Color(.25f,.29f,.34f),new Color(.30f,.34f,.39f),new Color(.30f,.34f,.39f),new Color(.25f,.29f,.34f)});checker.Apply();
            var material=floor.GetComponent<Renderer>().material;material.SetTexture("_BaseMap",checker);material.SetTextureScale("_BaseMap",new Vector2(20,20));
            var cameraObject=new GameObject("SharedGameplayCameraReview");SceneManager.MoveGameObjectToScene(cameraObject,scene);cameraObject.transform.SetParent(root.transform,true);
            var view=cameraObject.AddComponent<Camera>();view.tag="MainCamera";view.nearClipPlane=.05f;view.farClipPlane=100;
            review.SharedCamera=cameraObject.AddComponent<SharedCameraReview>();review.SharedCamera.View=view;review.SharedCamera.Actors=review.Actors;review.SharedCamera.Included=Enumerable.Repeat(true,count).ToArray();
            review.SharedCamera.SampleAuthority();review.SharedCamera.Present(1f/60);
            var lightObject=new GameObject("ReviewLight");SceneManager.MoveGameObjectToScene(lightObject,scene);lightObject.transform.SetParent(root.transform,true);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(45,-35,0);
            return review;
        }
        private void Neutralize(PhysicsJumpReview actor)
        { actor.Motor.MoveInput=Vector2.zero;actor.Motor.SprintRequested=false;actor.Motor.ClearPendingInput();if(!actor.Hands.InputSuppressed)actor.Hands.SetInputSuppressed(true); }
        public void SelectPlayer(int index)
        {
            if(index<0||index>=Actors.Length||index==ControlledIndex||!Actors[index].gameObject.activeInHierarchy)return;
            Neutralize(Actors[ControlledIndex]);ControlledIndex=index;Neutralize(Actors[index]);_requireMouseUp=true;
        }
        public void SetMenu(bool open)
        {
            InputSuppressed=open;Neutralize(Actors[ControlledIndex]);_requireMouseUp=true;
        }
        public void SubmitLocal(Vector2 move,bool sprint,bool jump,bool left,bool right,bool leftTap=false,bool rightTap=false,float frameSeconds=0,bool fixedMouseEvents=false)
        {
            var actor=Actors[ControlledIndex];
            if(!actor.gameObject.activeInHierarchy)return;
            if(InputSuppressed){Neutralize(actor);return;}
            var world=SharedCameraFraming.ScreenMove(move,SharedCamera.Orientation);
            actor.Motor.MoveInput=new Vector2(world.x,world.z);actor.Motor.SprintRequested=sprint;
            if(jump)actor.Motor.RequestJump();
            if(_requireMouseUp)
            {
                if(left||right)return;
                actor.Hands.SetInputSuppressed(false);actor.Hands.SubmitButtons(false,false);_requireMouseUp=false;return;
            }
            if(fixedMouseEvents)actor.Hands.SubmitFixedMouse();else actor.Hands.SubmitButtons(left,right,leftTap,rightTap,frameSeconds);
        }
        public void ExcludeReviewActor(int index)
        {
            var actor=Actors[index];Neutralize(actor);var target=actor.GetComponent<StrikeReviewTarget>();
            foreach(var other in Actors)other.Hands.Grabs?.ReleaseTarget(target,"ReviewOOB");
            SharedCamera.Included[index]=false;actor.GetComponent<RagdollRecoveryReview>()?.SuspendDetachedPhysicsForReview();actor.gameObject.SetActive(false);
            if(index==ControlledIndex)_requireMouseUp=true;
        }
        public void ResetReview()
        {
            foreach(var actor in Actors)Neutralize(actor);
            GetComponent<ArenaBoundaryReview>()?.PrepareReset();
            foreach(var actor in Actors)actor.GetComponent<StrikeReviewTarget>().ResetReview();
            _requireMouseUp=true;
        }
        private void OnApplicationFocus(bool focus) { if(!focus)SetMenu(true); }
        private void OnDisable() { if(Actors!=null)foreach(var actor in Actors)if(actor!=null&&actor.Hands!=null)Neutralize(actor); }
        private void FixedUpdate()
        {
            if(!ReadKeyboard||Keyboard.current==null)return;
            var keys=Keyboard.current;
            for(var i=0;i<Actors.Length;i++)if(keys[(Key)((int)Key.F1+i)].wasPressedThisFrame)SelectPlayer(i);
            if(keys.escapeKey.wasPressedThisFrame)SetMenu(!InputSuppressed);
            if(keys.rKey.wasPressedThisFrame)ResetReview();
            if(keys.f8Key.wasPressedThisFrame)ShowHelp=!ShowHelp;
            var mouse=Mouse.current;
            SubmitLocal(new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0)),keys.leftShiftKey.isPressed,keys.spaceKey.wasPressedThisFrame,
                mouse!=null&&mouse.leftButton.isPressed,mouse!=null&&mouse.rightButton.isPressed,
                false,false,Time.fixedUnscaledDeltaTime,true);
        }
        private void OnGUI()
        {
            if(!ShowHelp)return;
            GUI.Label(new Rect(14,12,900,65),$"LOCAL REVIEW — {Actors.Length} actors | F1–F{Actors.Length}: select | selected P{ControlledIndex+1}\nWASD screen directions | Shift sprint | Space jump | L/R tap punch, hold grab, release throw\nEsc input menu (physics continues) | R reset all | F8 hide help");
            if(InputSuppressed)GUI.Label(new Rect(14,84,900,30),"Input menu open — Esc to return, release mouse buttons to rearm.");
            if(SharedCamera.CapacityExceeded)GUI.Label(new Rect(14,112,900,30),"Review camera reached its dolly limit; subjects exceed the current framing profile.");
        }
    }
}
