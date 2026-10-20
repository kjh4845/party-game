using UnityEngine;
using System.Collections.Generic;

namespace ProjectHotfix.PhysicsJump
{
    public sealed class StrikeReviewTarget : MonoBehaviour
    {
        public bool Eligible = true;
        public bool GrabEligible;
        public uint ResetVersion { get; private set; }
        private Vector3 _spawn;
        private Quaternion _spawnRotation;
        private void Awake() { _spawn = transform.position; _spawnRotation = transform.rotation; }
        public void ResetReview()
        {
            var receiver = GetComponent<PunchDownReview>();
            if (receiver != null && receiver.Ragdoll != null) receiver.ResetReview();
            ResetVersion++;
            Body.position = _spawn; Body.rotation = _spawnRotation;
            Body.linearVelocity = Body.angularVelocity = Vector3.zero;
            HitCount = 0; LastActionId = 0; LastImpulse = 0;
        }
        public int HitCount { get; private set; }
        public ulong LastActionId { get; private set; }
        public float LastImpulse { get; private set; }
        internal void RecordAppliedImpulse(Vector3 impulse) { LastImpulse = impulse.magnitude; }
        public Rigidbody Body => GetComponent<Rigidbody>();
        private readonly Dictionary<ConfigurableJoint,Vector3> _incomingGrips = new Dictionary<ConfigurableJoint,Vector3>();
        public bool HasIncomingGrips
        {
            get { foreach(var pair in _incomingGrips)if(pair.Key!=null && pair.Key.gameObject.activeInHierarchy)return true;return false; }
        }
        internal void RegisterGrip(ConfigurableJoint joint)
        {
            var ragdoll=GetComponent<RagdollRecoveryReview>();
            _incomingGrips[joint]=ragdoll!=null && ragdoll.Actor!=null ? ragdoll.StandingLocalContact(joint.connectedBody,joint.connectedAnchor) : joint.connectedAnchor;
        }
        public bool TryGripStandingRoot(Quaternion heading,out Vector3 root)
        {
            root=Vector3.zero;var count=0;
            foreach(var pair in _incomingGrips)
            {
                var joint=pair.Key;if(joint==null || !joint.gameObject.activeInHierarchy || joint.connectedBody==null)continue;
                var body=joint.connectedBody;var point=body.position+body.rotation*Vector3.Scale(body.transform.lossyScale,joint.connectedAnchor);
                root+=point-heading*pair.Value;count++;
            }
            if(count==0)return false;root/=count;return true;
        }
        internal void UnregisterGrip(ConfigurableJoint joint) { if(!ReferenceEquals(joint,null))_incomingGrips.Remove(joint); }
        public uint InteractionTopologyVersion => GetComponent<RagdollRecoveryReview>()?.InteractionTopologyVersion ?? 0;
        public bool TryFindInteractionCollider(Vector3 point, float tolerance, out Collider collider)
        {
            var ragdoll=GetComponent<RagdollRecoveryReview>();
            if(ragdoll!=null && ragdoll.Actor!=null) return ragdoll.TryFindInteractionCollider(point,tolerance,out collider);
            collider=null;return false;
        }
        private readonly Rigidbody[] _interactionBodies = new Rigidbody[16];
        private int CollectInteractionBodies()
        {
            var ragdoll=GetComponent<RagdollRecoveryReview>();
            if(ragdoll!=null && ragdoll.Actor!=null && ragdoll.Active) return ragdoll.CopyDynamicBodies(_interactionBodies);
            if(Body==null || Body.isKinematic) return 0;
            _interactionBodies[0]=Body; return 1;
        }
        public Collider[] InteractionColliders()
        {
            var count=CollectInteractionBodies();var shapes=new List<Collider>();
            for(var i=0;i<count;i++)foreach(var shape in _interactionBodies[i].GetComponentsInChildren<Collider>())
                if(shape.attachedRigidbody==_interactionBodies[i]&&shape.enabled&&!shape.isTrigger)shapes.Add(shape);
            return shapes.ToArray();
        }
        public float InteractionMass
        {
            get { var count=CollectInteractionBodies();var mass=0f;for(var i=0;i<count;i++)mass+=_interactionBodies[i].mass;return mass; }
        }
        public float InteractionVerticalVelocity
        {
            get
            {
                var count=CollectInteractionBodies();if(count==1)return _interactionBodies[0].linearVelocity.y;
                var mass=0f;var momentum=0f;
                for(var i=0;i<count;i++) { mass+=_interactionBodies[i].mass;momentum+=_interactionBodies[i].mass*_interactionBodies[i].linearVelocity.y; }
                return mass>0 ? momentum/mass : 0;
            }
        }
        public void ApplyInteractionImpulse(Vector3 impulse)
        {
            var count=CollectInteractionBodies();var mass=0f;for(var i=0;i<count;i++)mass+=_interactionBodies[i].mass;
            if(mass<=0)return;
            for(var i=0;i<count;i++)_interactionBodies[i].AddForce(impulse*(_interactionBodies[i].mass/mass),ForceMode.Impulse);
        }
        public float AssistInteractionUp(float speedChange)
        {
            var count=CollectInteractionBodies();var impulse=0f;
            for(var i=0;i<count;i++)
            {
                var body=_interactionBodies[i];var velocity=body.linearVelocity;
                body.linearVelocity=new Vector3(velocity.x,velocity.y+speedChange,velocity.z);impulse+=body.mass*speedChange;
            }
            return impulse;
        }
        public void ConfirmHit(ulong actionId, Vector3 impulse, HandStrikeReview attacker = null, Collider hit = null)
        {
            var receiver = GetComponent<PunchDownReview>();
            if (receiver != null)
            {
                if (!receiver.Confirm(attacker, actionId, hit, impulse, out var applied)) return;
                HitCount++; LastActionId = actionId; LastImpulse = applied.magnitude;
                return;
            }
            HitCount++; LastActionId = actionId; LastImpulse = impulse.magnitude;
            Body.AddForce(impulse, ForceMode.Impulse);
        }
    }
}
