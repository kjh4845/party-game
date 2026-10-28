using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectHotfix.PhysicsJump
{
    // Third-person fight (2026-10-28 review: "마우스로 바라보게 하고 움직이게 하자, tps 전투"). The mouse turns player 1's view
    // (yaw, pitch) and the body faces the view's yaw; WASD (and player 2's arrows) move relative to the view. The camera
    // orbits behind player 1 and is pulled in front of anything between it and the character, so the character is never
    // hidden. Esc frees the cursor (and pauses input, as before).
    public sealed partial class ActiveGrabPairReview
    {
        public bool ThirdPerson;
        // Degrees per mouse count; pitch positive looks down.
        public float LookSensitivity=.12f,LookPitchMin=-25f,LookPitchMax=65f,LookPitchStart=14f;
        // Over the right shoulder and just above the head (2026-10-28: centred at chest height the opponent ahead was hidden
        // behind the character, and pulled in by a wall the view was all head). CameraHead is the point inside the
        // character the shoulder offset is cast from, so nothing can come between the character and the pivot.
        public float CameraDistance=2.2f,CameraPivotHeight=.95f,CameraShoulder=.32f,CameraHead=.8f,CameraRadius=.14f,CameraMinimumDistance=.25f,CameraFieldOfView=60f,CameraReturnSpeed=3f;
        // The body's yaw follows the view's (2026-10-28 probe, a 90 deg turn over 4 ticks then mouse sweeps): the lock-on
        // turn (80/16 on the root's own yaw rate, capped 100 rad/s²) settled in 26 ticks and trailed a 360 deg/s sweep by
        // 66 deg. Damped on the root's own rate at any higher gain it swung ±35 deg every tick: the 8 kg root rattles on
        // its joints about the heavier trunk and arms, so the damping reads the whole body's yaw rate (BodyYawRate).
        public float LookTurnStiffness=250f,LookTurnDamping=32f,LookTurnMaxAcceleration=600f;
        public float LookYaw {get;private set;}
        public float LookPitch {get;private set;}=12f;
        private float _previousLookYaw,_previousLookPitch,_lookYawRate,_cameraDistanceNow=-1;
        private bool _lookMoved,_lookStarted;
        private Vector3 _previousRoot,_currentRoot;
        private readonly RaycastHit[] _cameraHits=new RaycastHit[32];
        private readonly HashSet<Rigidbody> _cameraIgnore=new HashSet<Rigidbody>();

        public Vector3 LookForward=>Quaternion.Euler(0,LookYaw,0)*Vector3.forward;
        // Movement input in the view's frame, as the world-plane Move every other system reads.
        public Vector2 ViewMove(Vector2 input)
        {
            if(!ThirdPerson)return input;
            var world=Quaternion.Euler(0,LookYaw,0)*new Vector3(input.x,0,input.y);
            return new Vector2(world.x,world.z);
        }
        // Once per fixed step, before the step: the view turned by this step's mouse motion.
        public void Look(Vector2 mouseDelta,float seconds)
        {
            if(!_lookStarted)StartLook();
            _previousLookYaw=LookYaw;_previousLookPitch=LookPitch;
            _lookMoved=mouseDelta.sqrMagnitude>0;
            LookYaw+=mouseDelta.x*LookSensitivity;
            LookPitch=Mathf.Clamp(LookPitch-mouseDelta.y*LookSensitivity,LookPitchMin,LookPitchMax);
            _lookYawRate=(LookYaw-_previousLookYaw)*Mathf.Deg2Rad/Mathf.Max(1e-4f,seconds);
        }
        private void StartLook()
        {
            _lookStarted=true;
            var body=Players[0].Rig.Actor.Motor.Body;var forward=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,Vector3.up);
            LookYaw=_previousLookYaw=forward.sqrMagnitude>1e-6f?Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg:0;
            LookPitch=_previousLookPitch=LookPitchStart;
            _previousRoot=_currentRoot=body.position;
        }
        private void ReadLook()
        {
            var mouse=Mouse.current;
            Look(mouse!=null&&!Suppressed&&Application.isFocused?mouse.delta.ReadValue():Vector2.zero,Time.fixedDeltaTime);
        }
        // MovePlayer: player 1 faces the view (a wall it holds still turns it to the wall).
        private bool LookTurn(int index,out Vector3 direction,out float rate)
        {
            direction=default;rate=0;
            if(!ThirdPerson||index!=0||Players[0].HoldHeading)return false;
            direction=LookForward;rate=_lookYawRate;return true;
        }
        // The whole body's yaw rate: its angular momentum about the vertical through its centre of mass over its inertia
        // there. The 8 kg root's own rate is no measure of the body's turn: on its joints it rattles about the heavier
        // trunk and arms.
        private static float BodyYawRate(ActiveUpperBodyReview rig)
        {
            var root=rig.Actor.Motor.Body;var mass=root.mass;var center=root.mass*root.worldCenterOfMass;var velocity=root.mass*root.linearVelocity;
            foreach(var link in rig.Links){mass+=link.Body.mass;center+=link.Body.mass*link.Body.worldCenterOfMass;velocity+=link.Body.mass*link.Body.linearVelocity;}
            center/=mass;velocity/=mass;
            float momentum=0,inertia=0;
            void Add(Rigidbody body)
            {
                var axis=Quaternion.Inverse(body.rotation*body.inertiaTensorRotation)*Vector3.up;var d=body.inertiaTensor;
                var own=d.x*axis.x*axis.x+d.y*axis.y*axis.y+d.z*axis.z*axis.z;
                var r=Vector3.ProjectOnPlane(body.worldCenterOfMass-center,Vector3.up);var v=body.linearVelocity-velocity;
                momentum+=own*body.angularVelocity.y+body.mass*Vector3.Cross(r,v).y;inertia+=own+body.mass*r.sqrMagnitude;
            }
            Add(root);foreach(var link in rig.Links)Add(link.Body);
            return momentum/Mathf.Max(1e-6f,inertia);
        }
        // The view's turn rate fed forward through the damping, damped on the whole body's yaw rate, applied as the
        // lock-on turn is: on the root, sized by the whole body's inertia. (Aiming a step ahead as well made the body lead
        // a 360 deg/s sweep by 11 deg instead of 5.)
        private void ApplyLookTurn(ActiveUpperBodyReview rig,float error,float rate)
        {
            var root=rig.Actor.Motor.Body;
            var acceleration=Mathf.Clamp(error*LookTurnStiffness+(rate-BodyYawRate(rig))*LookTurnDamping,-LookTurnMaxAcceleration,LookTurnMaxAcceleration)*rig.ImpactMovementControl;
            var inertia=YawInertia(root,root.worldCenterOfMass);foreach(var link in rig.Links)inertia+=YawInertia(link.Body,root.worldCenterOfMass);
            root.AddTorque(Vector3.up*(acceleration*inertia),ForceMode.Force);
        }
        // After the step: the root's position for the camera's render interpolation.
        private void TrackCameraRoot()
        {
            if(!ThirdPerson||Players==null||!Active(Players[0]))return;
            if(!_lookStarted)StartLook();
            _previousRoot=_currentRoot;_currentRoot=Players[0].Rig.Actor.Motor.Body.position;
        }
        private void FrameThirdPerson(float alpha)
        {
            if(!_lookStarted)StartLook();
            View.orthographic=false;View.fieldOfView=CameraFieldOfView;View.nearClipPlane=.03f;
            var yaw=Mathf.LerpAngle(_previousLookYaw,LookYaw,alpha);var pitch=Mathf.Lerp(_previousLookPitch,LookPitch,alpha);
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var root=Vector3.Lerp(_previousRoot,_currentRoot,alpha);var head=root+Vector3.up*CameraHead;
            var back=rotation*Vector3.back;
            _cameraIgnore.Clear();
            var rig=Players[0].Rig;_cameraIgnore.Add(rig.Actor.Motor.Body);foreach(var link in rig.Links)_cameraIgnore.Add(link.Body);
            if(rig.Knockdown!=null)foreach(var body in KnockdownBodies(rig))_cameraIgnore.Add(body);
            // The pivot: from inside the head up and over the shoulder, kept short of anything in the way.
            var shoulder=root+Vector3.up*CameraPivotHeight+Quaternion.Euler(0,yaw,0)*Vector3.right*CameraShoulder-head;
            var pivot=head+shoulder.normalized*CameraClear(head,shoulder.normalized,shoulder.magnitude);
            // Pulled in at once in front of whatever is between (walls, the floor looking up, the other player), let out
            // again at CameraReturnSpeed: the character is always in view.
            var reach=Mathf.Max(CameraMinimumDistance,CameraClear(pivot,back,CameraDistance));
            var dt=Mathf.Max(0,Time.unscaledDeltaTime);
            _cameraDistanceNow=_cameraDistanceNow<0||reach<_cameraDistanceNow?reach:Mathf.MoveTowards(_cameraDistanceNow,reach,CameraReturnSpeed*dt);
            View.transform.SetPositionAndRotation(pivot+back*_cameraDistanceNow,rotation);
        }
        // How far a camera-sized sphere gets from origin along direction before anything but player 1 is in the way.
        private float CameraClear(Vector3 origin,Vector3 direction,float distance)
        {
            var reach=distance;
            var count=gameObject.scene.GetPhysicsScene().SphereCast(origin,CameraRadius,direction,_cameraHits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for(var i=0;i<count;i++)
            {
                var hit=_cameraHits[i];if(hit.collider==null||hit.rigidbody!=null&&_cameraIgnore.Contains(hit.rigidbody))continue;
                // The other player already touching the start (a clinch) cannot be seen past by moving in; level
                // geometry there means the start is inside it, so it stays as close as it goes.
                if(hit.distance<=0&&hit.rigidbody!=null)continue;
                reach=Mathf.Min(reach,hit.distance);
            }
            return reach;
        }
        private void LockCursor()
        {
            if(!ThirdPerson||ManualStep||!Application.isFocused)return;
            var locked=!Suppressed;
            var mode=locked?CursorLockMode.Locked:CursorLockMode.None;
            if(Cursor.lockState!=mode)Cursor.lockState=mode;
            Cursor.visible=!locked;
        }
    }
}
