using System.Collections.Generic;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Only Advance mutates pose state. Present interpolates immutable snapshots.
    public sealed class FixedReviewPose : MonoBehaviour
    {
        internal static readonly HashSet<FixedReviewPose> Live=new HashSet<FixedReviewPose>();
        public PhysicsJumpReview Actor;
        public bool Advancing { get; private set; }
        public ulong EvaluationCount { get; private set; }
        public ulong LastEvaluatedSimulationTick { get; private set; }
        private Transform[] _bones;
        private SurfacePoseCorrectives[] _surfaceCorrectives;
        private Vector3[] _previousPositions,_positions,_actorPositions;
        private Quaternion[] _previousRotations,_rotations,_actorRotations;
        private Vector3 _previousRoot,_root;
        private Quaternion _previousHeading,_heading;
        private uint _resetVersion;
        // Presentation-only hit stop: the rendered pose holds the impact frame for a
        // few physics ticks, then blends back to the live pose. Physics, evaluation
        // and fixed-step contact skin never read these values.
        private Vector3[] _holdPositions;private Quaternion[] _holdRotations;private Vector3 _holdRoot;private Quaternion _holdHeading;
        private int _holdTicks,_catchTicks,_catchTotal;private bool _holdStarted;
        public bool PresentationHeld=>_holdTicks>0;
        public bool PresentationCatchingUp=>_holdTicks==0&&_catchTicks>0;
        private readonly Dictionary<string,int> _indices=new Dictionary<string,int>();
        private void OnEnable(){Live.Add(this);}
        private void OnDisable(){Live.Remove(this);}
        public void Bind(PhysicsJumpReview actor)
        {
            if(_bones!=null)return;Actor=actor;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;
            _bones=actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            _surfaceCorrectives=actor.Pose.GetComponentsInChildren<SurfacePoseCorrectives>();
            var n=_bones.Length;_positions=new Vector3[n];_previousPositions=new Vector3[n];_actorPositions=new Vector3[n];_rotations=new Quaternion[n];_previousRotations=new Quaternion[n];_actorRotations=new Quaternion[n];
            for(var i=0;i<n;i++)_indices[_bones[i].name]=i;
            Capture();CopyCurrentToPrevious();_resetVersion=Actor.Motor.StepPoseState.ResetVersion;
        }
        private void CopyCurrentToPrevious()
        {System.Array.Copy(_positions,_previousPositions,_positions.Length);System.Array.Copy(_rotations,_previousRotations,_rotations.Length);_previousRoot=_root;_previousHeading=_heading;}
        public void CaptureEvaluatedPose()
        {
            if(_bones==null)Bind(Actor);CopyCurrentToPrevious();Capture();
            var version=Actor.Motor.StepPoseState.ResetVersion;
            // Respawn is a discontinuity: never interpolate back from an old OOB location.
            if(version!=_resetVersion){CopyCurrentToPrevious();_resetVersion=version;ClearHold();}
            if(_holdStarted)_holdStarted=false;else if(_holdTicks>0)_holdTicks--;else if(_catchTicks>0)_catchTicks--;
            ApplySurfaceCorrectives();
        }
        private void Capture()
        {
            var body=Actor.Motor.Body;_root=body.position;_heading=body.rotation;var inverse=Quaternion.Inverse(_heading);
            for(var i=0;i<_bones.Length;i++)
            {_positions[i]=_bones[i].localPosition;_rotations[i]=_bones[i].localRotation;_actorPositions[i]=inverse*(_bones[i].position-_root);_actorRotations[i]=inverse*_bones[i].rotation;}
        }
        public Vector3 WorldPosition(string bone) => Actor.Motor.Body.position+Actor.Motor.Body.rotation*_actorPositions[_indices[bone]];
        public Vector3 WorldReachPosition(string bone)
        {
            var hanging=Actor.Pose.GetComponent<HangingBodyPose>();
            return hanging!=null&&hanging.TryBaselinePosition(bone,out var position)
                ? Actor.Motor.Body.position+Actor.Motor.Body.rotation*position : WorldPosition(bone);
        }
        public Quaternion WorldRotation(string bone) => Actor.Motor.Body.rotation*_actorRotations[_indices[bone]];
        public void RestoreForEvaluation()
        {
            if(_bones==null)Bind(Actor);
            Actor.Pose.transform.SetPositionAndRotation(Actor.Motor.Body.position,Actor.Motor.Body.rotation);
            for(var i=0;i<_bones.Length;i++){_bones[i].localPosition=_positions[i];_bones[i].localRotation=_rotations[i];}
        }
        public void Advance(float seconds)
        {
            RestoreForEvaluation();Advancing=true;
            try
            {
                var rag=Actor.GetComponent<RagdollRecoveryReview>();
                if(rag!=null&&rag.Active){Actor.Pose.GetComponent<HangingBodyPose>()?.Clear();rag.ApplyPose(seconds);}else Actor.EvaluatePresentation(seconds);
                CaptureEvaluatedPose();
                EvaluationCount++;LastEvaluatedSimulationTick=Actor.Motor.SimulationTick;
            }
            finally{Advancing=false;}
        }
        public void HoldPresentation(int holdTicks,int catchUpTicks)
        {
            if(_bones==null)Bind(Actor);
            var n=_bones.Length;
            if(_holdPositions==null||_holdPositions.Length!=n){_holdPositions=new Vector3[n];_holdRotations=new Quaternion[n];}
            // A second hit during a hold keeps the first frozen frame.
            if(_holdTicks==0)
            {
                System.Array.Copy(_positions,_holdPositions,n);System.Array.Copy(_rotations,_holdRotations,n);
                _holdRoot=_root;_holdHeading=_heading;
            }
            _holdTicks=Mathf.Max(_holdTicks,holdTicks);_catchTicks=_catchTotal=Mathf.Max(1,catchUpTicks);_holdStarted=true;
        }
        public void ClearHold(){_holdTicks=_catchTicks=0;_holdStarted=false;}
        public void Present(float alpha)
        {
            if(_bones==null)Bind(Actor);alpha=Mathf.Clamp01(alpha);
            // Weight of the frozen impact frame: 1 while held, then easing out.
            var hold=_holdTicks>0?1f:_catchTicks>0?Mathf.SmoothStep(0,1,(_catchTicks-alpha)/_catchTotal):0f;
            var root=Vector3.Lerp(_previousRoot,_root,alpha);var heading=Quaternion.Slerp(_previousHeading,_heading,alpha);
            if(hold>0){root=Vector3.Lerp(root,_holdRoot,hold);heading=Quaternion.Slerp(heading,_holdHeading,hold);}
            Actor.Pose.transform.SetPositionAndRotation(root,heading);
            for(var i=0;i<_bones.Length;i++)
            {
                var position=Vector3.Lerp(_previousPositions[i],_positions[i],alpha);var rotation=Quaternion.Slerp(_previousRotations[i],_rotations[i],alpha);
                if(hold>0){position=Vector3.Lerp(position,_holdPositions[i],hold);rotation=Quaternion.Slerp(rotation,_holdRotations[i],hold);}
                _bones[i].localPosition=position;_bones[i].localRotation=rotation;
            }
            ApplySurfaceCorrectives();
        }
        private void ApplySurfaceCorrectives()
        {if(_surfaceCorrectives!=null)foreach(var corrective in _surfaceCorrectives)corrective.ApplyCurrentPose();}
    }
}
