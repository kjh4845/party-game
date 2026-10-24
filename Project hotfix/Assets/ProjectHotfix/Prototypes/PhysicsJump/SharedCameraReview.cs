using System;
using System.Collections.Generic;
using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    [DefaultExecutionOrder(200)]
    public sealed class SharedCameraReview : MonoBehaviour
    {
        internal static readonly HashSet<SharedCameraReview> Live=new HashSet<SharedCameraReview>();
        public PhysicsJumpReview[] Actors=Array.Empty<PhysicsJumpReview>();
        public bool[] Included=Array.Empty<bool>();
        public Camera View;
        public bool ManualStep;
        public float Yaw,Pitch=34,VerticalFov=42,MinimumDolly=8,MaximumDolly=15,SafeMargin=.15f;
        public float FocusSeconds=.25f,DollySeconds=.3f,MaximumFocusSpeed=6,DollySpeed=5,DeadZone=.08f,LookAheadSeconds=.2f,MaximumLookAhead=.35f,SafetyReserve=.5f;
        public Vector3 SubjectCenter=new Vector3(0,.45f,0),SubjectExtents=new Vector3(.45f,.55f,.45f);
        public Bounds AuthorityBounds { get; private set; }
        public Vector3 TargetFocus { get; private set; }
        public Vector3 Focus { get; private set; }
        public float Dolly { get; private set; }
        public int SubjectCount { get; private set; }
        public bool CapacityExceeded { get; private set; }
        public Quaternion Orientation => Quaternion.Euler(Pitch,Yaw,0);
        private bool _initialized;
        private Vector3 _previousPosition,_currentPosition;
        private Quaternion _previousRotation,_currentRotation;
        private bool _hasRenderSnapshot;
        private void OnEnable(){Live.Add(this);}
        private void OnDisable(){Live.Remove(this);}
        private void LateUpdate() { if(!ManualStep)Render(ReviewFixedLoop.RenderAlpha); }
        public void AdvanceFixed(float seconds){SampleAuthority();Present(seconds);}
        public void Render(float alpha)
        {
            if(!_hasRenderSnapshot||View==null)return;alpha=Mathf.Clamp01(alpha);
            View.transform.SetPositionAndRotation(Vector3.Lerp(_previousPosition,_currentPosition,alpha),Quaternion.Slerp(_previousRotation,_currentRotation,alpha));
        }
        public void SampleAuthority()
        {
            var count=0;var velocity=Vector3.zero;var bounds=default(Bounds);
            for(var i=0;i<Actors.Length;i++)
            {
                var actor=Actors[i];if(actor==null||!actor.gameObject.activeInHierarchy||(Included.Length>i&&!Included[i]))continue;
                // Fixed world-upright bounds on the authority Root; never renderer or limb bounds.
                var b=new Bounds(actor.Motor.Body.position+SubjectCenter,SubjectExtents*2);
                if(count++==0)bounds=b;else bounds.Encapsulate(b);
                velocity+=actor.Motor.Body.linearVelocity;
            }
            SubjectCount=count;if(count==0){CapacityExceeded=false;_previousPosition=_currentPosition;_previousRotation=_currentRotation;return;}
            AuthorityBounds=bounds;var lead=Vector3.ProjectOnPlane(velocity/count,Vector3.up)*LookAheadSeconds;
            TargetFocus=bounds.center+Vector3.ClampMagnitude(lead,MaximumLookAhead);
        }
        public void Present(float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            if(seconds<=0)return;
            if(View==null||MinimumDolly<=0||MaximumDolly<MinimumDolly||FocusSeconds<=0||DollySeconds<=0)throw new InvalidOperationException("Invalid shared camera review profile.");
            View.orthographic=false;View.fieldOfView=VerticalFov;
            if(SubjectCount==0)return; // Empty roster holds the last valid framing.
            if(!_initialized){Focus=TargetFocus;Dolly=MinimumDolly;_initialized=true;}
            var delta=TargetFocus-Focus;
            if(delta.magnitude>DeadZone)
            {
                delta-=delta.normalized*DeadZone;
                Focus+=Vector3.ClampMagnitude(delta*(1-Mathf.Exp(-seconds/FocusSeconds)),MaximumFocusSpeed*seconds);
            }
            var needed=SharedCameraFraming.RequiredDistance(AuthorityBounds,Focus,Orientation,VerticalFov,View.aspect,SafeMargin,View.nearClipPlane);
            CapacityExceeded=needed>MaximumDolly;
            var desired=Mathf.Clamp(needed+SafetyReserve,MinimumDolly,MaximumDolly);
            Dolly+=Mathf.Clamp((desired-Dolly)*(1-Mathf.Exp(-seconds/DollySeconds)),-DollySpeed*seconds,DollySpeed*seconds);
            // Safety wins over zoom-out damping. Zoom-in and subject removal stay gradual.
            Dolly=Mathf.Clamp(Mathf.Max(Dolly,needed),MinimumDolly,MaximumDolly);
            View.transform.SetPositionAndRotation(Focus-Orientation*Vector3.forward*Dolly,Orientation);
            _previousPosition=_currentPosition;_previousRotation=_currentRotation;_currentPosition=View.transform.position;_currentRotation=View.transform.rotation;
            if(!_hasRenderSnapshot){_previousPosition=_currentPosition;_previousRotation=_currentRotation;_hasRenderSnapshot=true;}
        }
    }
}
