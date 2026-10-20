using System.Collections.Generic;
using ProjectHotfix.Contracts;
using ProjectHotfix.Presentation;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Sampled before hand simulation: every query/evaluation in a tick sees the
    // same completed support load, even when several actors query one another.
    public struct LedgeHangingState
    {
        public bool Left,Right;
        public float LeftLoad,RightLoad;
        public Vector3 LocalVelocity;
        public uint ResetVersion;
    }

    // A bounded, load-driven presentation layer. Never writes bodies or colliders.
    // The baseline is also retained for the existing physical reach admission rule.
    public sealed class HangingBodyPose : MonoBehaviour
    {
        private const float OneHandRoll=8f,OneHandDrop=.025f,TwoHandDrop=.035f;
        private Transform[] _bones;
        private Vector3[] _positions,_actorPositions;
        private Quaternion[] _rotations;
        private readonly Dictionary<string,int> _indices=new Dictionary<string,int>();
        private bool _baseline;
        private uint _reset;
        private float _weight,_weightVelocity,_roll,_rollVelocity,_pitch,_pitchVelocity,_drop,_dropVelocity,_pivotBias,_pivotVelocity;
        public float AppliedRoll {get;private set;}
        public float AppliedDrop {get;private set;}
        public float ContactScale {get;private set;}=1;
        public float LoadWeight=>_weight;
        public void Bind()
        {
            if(_bones!=null)return;
            _bones=GetComponentInChildren<SkinnedMeshRenderer>().bones;
            _positions=new Vector3[_bones.Length];_rotations=new Quaternion[_bones.Length];_actorPositions=new Vector3[_bones.Length];
            for(var i=0;i<_bones.Length;i++)_indices[_bones[i].name]=i;
        }
        private Transform Bone(string name)=>_bones[_indices[name]];
        public void Clear()
        {
            _baseline=false;_weight=_weightVelocity=_roll=_rollVelocity=_pitch=_pitchVelocity=_drop=_dropVelocity=_pivotBias=_pivotVelocity=0;
            AppliedRoll=AppliedDrop=0;ContactScale=1;
        }
        public bool TryBaselinePosition(string name,out Vector3 position)
        {
            position=default;if(!_baseline||!_indices.TryGetValue(name,out var index))return false;
            position=_actorPositions[index];return true;
        }
        public void RestoreBaseline()
        {
            if(!_baseline)return;
            for(var i=0;i<_bones.Length;i++){_bones[i].localPosition=_positions[i];_bones[i].localRotation=_rotations[i];}
        }
        private void CaptureBaseline()
        {
            for(var i=0;i<_bones.Length;i++)
            {_positions[i]=_bones[i].localPosition;_rotations[i]=_bones[i].localRotation;_actorPositions[i]=transform.InverseTransformPoint(_bones[i].position);}
            _baseline=true;
        }
        // Fixed-step damped response; no render delta and no looping fake sway.
        private static void Respond(ref float value,ref float velocity,float target,float dt)
        {
            if(dt<=0)return;
            const float frequency=14f,damping=.85f;
            velocity+=(frequency*frequency*(target-value)-2*damping*frequency*velocity)*dt;
            value+=velocity*dt;
        }
        public void Apply(LedgeHangingState state,GrabPresentationState grabs,GrabArmPose arms,float dt,bool leftFree,bool rightFree)
        {
            Bind();if(_reset!=state.ResetVersion){Clear();_reset=state.ResetVersion;}
            CaptureBaseline();
            var left=state.Left?Mathf.Clamp(state.LeftLoad,0,1.5f):0;
            var right=state.Right?Mathf.Clamp(state.RightLoad,0,1.5f):0;
            var load=Mathf.Clamp01(left+right);var both=state.Left&&state.Right;
            var bias=(right-left)/Mathf.Max(.01f,left+right);
            Respond(ref _weight,ref _weightVelocity,load,dt);
            Respond(ref _roll,ref _rollVelocity,(bias*(both?4f:OneHandRoll)-Mathf.Clamp(state.LocalVelocity.x*4f,-5,5))*load,dt);
            Respond(ref _pitch,ref _pitchVelocity,Mathf.Clamp(-state.LocalVelocity.z*5f,-7,7)*load,dt);
            Respond(ref _drop,ref _dropVelocity,load*(both?TwoHandDrop:OneHandDrop),dt);
            Respond(ref _pivotBias,ref _pivotVelocity,(state.Right?1f:0)-(state.Left?1f:0),dt);
            var drop=_drop;
            ContactScale=1;
            if(_weight<=.00001f){AppliedRoll=AppliedDrop=0;return;}
            // A near-full-extension contact takes precedence over the cosmetic
            // offset. Retry from the same baseline, never stretch the arm bones.
            for(var attempt=0;attempt<7;attempt++)
            {
                RestoreBaseline();Deform(state,grabs,drop,ContactScale,leftFree,rightFree);arms.ReapplyHeld(grabs);
                if(arms.MaximumHeldTargetError<=.002f||attempt==6)break;
                ContactScale=attempt==5?0:ContactScale*.5f;
            }
            AppliedRoll=_roll*ContactScale;AppliedDrop=drop*ContactScale;
        }
        private void Deform(LedgeHangingState state,GrabPresentationState grabs,float drop,float scale,bool leftFree,bool rightFree)
        {
            var weight=Mathf.Clamp01(_weight)*scale;if(weight<.00001f)return;
            var pelvis=Bone("Pelvis");var left=Bone("UpperArm_L");var right=Bone("UpperArm_R");
            var bias=Mathf.Clamp(_pivotBias,-1,1);
            var pivot=Vector3.Lerp((left.position+right.position)*.5f,bias<0?left.position:right.position,Mathf.Abs(bias));
            var roll=_roll*scale;var rotation=Quaternion.AngleAxis(roll,transform.forward)*Quaternion.AngleAxis(_pitch*scale,transform.right);
            pelvis.position=pivot+rotation*(pelvis.position-pivot)-Vector3.up*(drop*scale);
            pelvis.rotation=rotation*pelvis.rotation;
            Bone("Spine").rotation=Quaternion.AngleAxis(-roll*.15f,transform.forward)*Bone("Spine").rotation;
            Bone("Head").rotation=Quaternion.AngleAxis(-roll*.35f,transform.forward)*Bone("Head").rotation;
            // A free arm and the legs lag toward gravity instead of forming a
            // rigid diagonal stick with the trunk. Active punch/grab arms win.
            for(var side=0;side<2;side++)
            {
                var suffix=side==0?"L":"R";var hand=side==0?grabs.Left:grabs.Right;
                if((side==0?leftFree:rightFree)&&!hand.Active)
                {
                    var upper=Bone("UpperArm_"+suffix);var forearm=Bone("Forearm_"+suffix);
                    var down=Vector3.down+transform.right*(side==0?-.12f:.12f)+transform.forward*.08f;
                    var swing=Quaternion.FromToRotation(forearm.position-upper.position,down);
                    upper.rotation=Quaternion.Slerp(Quaternion.identity,swing,weight*.8f)*upper.rotation;
                }
                var thigh=Bone("Thigh_"+suffix);
                thigh.rotation=Quaternion.AngleAxis(-roll*.30f,transform.forward)*thigh.rotation;
            }
        }
    }
}
