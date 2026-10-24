using System.Collections.Generic;
using System.Linq;
using ProjectHotfix.Presentation;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // A separate fixed-step pose rig. Hit queries never consume rendered bones.
    // Reuses the same pose evaluators with immutable bind data and authority inputs.
    public sealed class PunchHitRig : MonoBehaviour
    {
        private static readonly HashSet<PunchHitRig> Live = new HashSet<PunchHitRig>();
        private PhysicsJumpReview _actor;
        private StrikeReviewTarget _target;
        private GameObject _root;
        private Rigidbody _body;
        private ProceduralJumpPose _legs;
        private ProceduralFreeArmSwing _arms;
        private ApprovedPunchPose _punch;
        private GrabArmPose _grab;
        private HangingBodyPose _hanging;
        private readonly Dictionary<string,Transform> _bones = new Dictionary<string,Transform>();
        private readonly Dictionary<string,Vector3> _restPositions = new Dictionary<string,Vector3>();
        private readonly Dictionary<string,Quaternion> _restRotations = new Dictionary<string,Quaternion>();
        private ulong _lastTick = ulong.MaxValue;
        private uint _lastReset = uint.MaxValue;
        private bool _wasExternalPose;
        private uint _lastTopology;
        public Transform Bone(string name) => _bones[name];
        private void Awake() { Live.Add(this); }
        private void OnDisable() { if(_root!=null)_root.SetActive(false); }
        private void OnDestroy() { Live.Remove(this); if(_root!=null)Destroy(_root); }
        public static void SyncScene(Scene scene,float seconds)
        {
            foreach(var rig in Live)if(rig!=null && rig.gameObject.scene==scene)rig.Sync(seconds);
            UnityEngine.Physics.SyncTransforms();
        }
        public void Bind(StrikeReviewTarget target)
        {
            if(_root!=null)return;
            _target=target;_actor=target.GetComponent<PhysicsJumpReview>();
            _lastTopology=target.InteractionTopologyVersion;
            _root=new GameObject("AuthorityPunchHitRig");SceneManager.MoveGameObjectToScene(_root,gameObject.scene);
            _root.transform.SetPositionAndRotation(_actor.Motor.Body.position,_actor.Motor.Body.rotation);
            _root.AddComponent<StrikeReviewHurtbox>().ActorRoot=target.transform;
            _body=_root.AddComponent<Rigidbody>();_body.isKinematic=true;_body.useGravity=false;
            var source=_actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>();var mapping=new Dictionary<Transform,Transform>();
            foreach(var bone in source.bones)mapping[bone]=new GameObject(bone.name).transform;
            foreach(var bone in source.bones)
            {
                var copy=mapping[bone];copy.SetParent(mapping.TryGetValue(bone.parent,out var parent)?parent:_root.transform,false);
                copy.localPosition=bone.localPosition;copy.localRotation=bone.localRotation;copy.localScale=bone.localScale;_bones[bone.name]=copy;
                _restPositions[bone.name]=copy.localPosition;_restRotations[bone.name]=copy.localRotation;
            }
            // Immutable bindposes are needed by the shared grab pose evaluator.
            // This renderer is disabled and never rendered or baked for hit tests.
            var bindSkin=_root.AddComponent<SkinnedMeshRenderer>();bindSkin.sharedMesh=source.sharedMesh;bindSkin.bones=source.bones.Select(b=>mapping[b]).ToArray();bindSkin.rootBone=mapping[source.rootBone];bindSkin.enabled=false;
            _legs=_root.AddComponent<ProceduralJumpPose>();var settings=_actor.Pose;
            _legs.WorldFootPlant=settings.WorldFootPlant;_legs.ReadableGait=settings.ReadableGait;_legs.SpeedMatchedGait=settings.SpeedMatchedGait;
            _legs.WalkReferenceSpeed=settings.WalkReferenceSpeed;_legs.SprintReferenceSpeed=settings.SprintReferenceSpeed;
            _legs.WalkCadence=settings.WalkCadence;_legs.SprintCadence=settings.SprintCadence;
            _legs.WalkPelvisDrop=settings.WalkPelvisDrop;_legs.SprintPelvisDrop=settings.SprintPelvisDrop;
            _legs.FootPlantStrideFullWeight=settings.FootPlantStrideFullWeight;
            _legs.FootPlantWalkLift=settings.FootPlantWalkLift;_legs.FootPlantSprintLift=settings.FootPlantSprintLift;
            _legs.FootPlantPelvisDrop=settings.FootPlantPelvisDrop;_legs.FootPlantPelvisBob=settings.FootPlantPelvisBob;
            _legs.PelvisSwayScale=settings.PelvisSwayScale;_legs.PelvisRollScale=settings.PelvisRollScale;_legs.FreeArmSwingScale=settings.FreeArmSwingScale;
            _legs.LimitCombinedShortening=settings.LimitCombinedShortening;_legs.CombinedShorteningLimit=settings.CombinedShorteningLimit;
            _legs.TurnStrideMinimum=settings.TurnStrideMinimum;_legs.UseStepState=settings.UseStepState;_legs.ResetAfterExternalPose=settings.ResetAfterExternalPose;
            _legs.MaximumCompression=settings.MaximumCompression;_legs.MaximumTuck=settings.MaximumTuck;_legs.Bind(_root.transform);
            _arms=_root.AddComponent<ProceduralFreeArmSwing>();_arms.Bind();
            _punch=_root.AddComponent<ApprovedPunchPose>();_punch.Bind(_actor.PunchLibrary);
            _grab=_root.AddComponent<GrabArmPose>();var clips=_actor.PunchLibrary.clips;
            _grab.Bind(Vector3.Distance(clips[0].frames[0].heads[3],clips[0].frames[0].tails[3]),clips[0].Sample(7f/30,0),clips[1].Sample(7f/30,0));
            if(_actor.Pose.GetComponent<HangingBodyPose>()!=null){_hanging=_root.AddComponent<HangingBodyPose>();_hanging.Bind();}
            AddVolume("Torso",_bones["Spine"],new Vector3(0,.40f,0),.155f,.54f,PunchHitRegion.Body);
            AddVolume("Head",_bones["Head"],new Vector3(0,.855f,.003f),.13f,.29f,PunchHitRegion.Head);
            foreach(var side in new[]{"L","R"})
            {
                AddSegment("UpperArm_"+side,"Forearm_"+side,.045f,0);
                AddSegment("Forearm_"+side,"HandLogical_"+side,.035f,.06f);
                AddSegment("Thigh_"+side,"Calf_"+side,.045f,0);
                AddSegment("Calf_"+side,"Foot_"+side,.03f,0);
                AddSegment("Foot_"+side,null,.025f,.05f);
            }
        }
        private void Label(GameObject go,PunchHitRegion region)
        { go.layer=2;var hit=go.AddComponent<PunchHurtbox>();hit.Target=_target;hit.Region=region;hit.StandingOnly=true; }
        private void AddVolume(string name,Transform bone,Vector3 actorCenter,float radius,float height,PunchHitRegion region)
        {
            var go=new GameObject(name+"PunchVolume");go.transform.SetParent(bone,false);
            go.transform.SetPositionAndRotation(_root.transform.TransformPoint(actorCenter),_root.transform.rotation);
            var collider=go.AddComponent<CapsuleCollider>();collider.radius=radius;collider.height=height;collider.isTrigger=true;Label(go,region);
        }
        private void AddSegment(string boneName,string tipName,float radius,float terminal)
        {
            var bone=_bones[boneName];var go=new GameObject(boneName+"PunchVolume");go.transform.SetParent(bone,false);
            var end=tipName==null ? Vector3.back*terminal : bone.InverseTransformPoint(_bones[tipName].TransformPoint(Vector3.back*terminal));
            var collider=go.AddComponent<CapsuleCollider>();collider.direction=2;collider.radius=radius;collider.height=Mathf.Max(radius*2,end.magnitude+radius);collider.center=end*.5f;collider.isTrigger=true;Label(go,PunchHitRegion.Body);
        }
        private void Sync(float seconds)
        {
            if(_root==null)return;
            var externalPose=_target!=null&&(_target.GetComponent<RagdollRecoveryReview>()?.Active ?? false);
            var topology=_target!=null ? _target.InteractionTopologyVersion : _lastTopology;
            var changedTopology=topology!=_lastTopology;_lastTopology=topology;
            var active=_target!=null && _target.gameObject.activeInHierarchy && !externalPose;
            if(externalPose){_wasExternalPose=true;_hanging?.Clear();}
            _root.SetActive(active);if(!active)return;
            if((_wasExternalPose||changedTopology)&&_legs.ResetAfterExternalPose)
            {
                foreach(var pair in _bones){pair.Value.localPosition=_restPositions[pair.Key];pair.Value.localRotation=_restRotations[pair.Key];}
                _legs.ResetTransientGait();_hanging?.Clear();
            }
            _wasExternalPose=false;
            _body.position=_actor.Motor.Body.position;_body.rotation=_actor.Motor.Body.rotation;
            // Once this tick has its authoritative post-physics snapshot, use it
            // for later queries too. Re-evaluating against the new root position
            // would mix a second contact pose with the earlier gait state.
            var fixedPose=_actor.FixedPose;
            if(fixedPose!=null&&fixedPose.EvaluationCount>0&&fixedPose.LastEvaluatedSimulationTick==_actor.Motor.SimulationTick)
            {
                foreach(var pair in _bones)pair.Value.SetPositionAndRotation(fixedPose.WorldPosition(pair.Key),fixedPose.WorldRotation(pair.Key));
                return;
            }
            var state=_legs.UseStepState ? _actor.Motor.StepPoseState : _actor.Motor.PresentationState;
            var dt=_lastTick!=_actor.Motor.SimulationTick || _lastReset!=state.ResetVersion ? seconds : 0f;
            _lastTick=_actor.Motor.SimulationTick;_lastReset=state.ResetVersion;
            if(_hanging==null&&_actor.Pose.GetComponent<HangingBodyPose>()!=null){_hanging=_root.AddComponent<HangingBodyPose>();_hanging.Bind();}
            _hanging?.RestoreBaseline();
            if(dt>0)_legs.Apply(state,dt);
            var left=_actor.Arms.LeftAvailable;var right=_actor.Arms.RightAvailable;
            if(_actor.Hands!=null)_punch.ApplyAuthority(_actor.Hands.State.Capture(),left,right);
            if(_actor.Hands?.Grabs!=null)_grab.Apply(_actor.Hands.Grabs.Capture(),dt,left,right,_punch.IsActive(0),_punch.IsActive(1));
            _arms.Apply(_legs,dt,state.ResetVersion,_punch.IsActive(0)||_grab.IsControlling(0),_punch.IsActive(1)||_grab.IsControlling(1));
            if(_hanging!=null)_hanging.Apply(_actor.GetComponent<LedgeGrabReview>().HangingState,_actor.Hands.Grabs.Capture(),_grab,dt,left&&!_punch.IsActive(0),right&&!_punch.IsActive(1));
        }
    }
}
