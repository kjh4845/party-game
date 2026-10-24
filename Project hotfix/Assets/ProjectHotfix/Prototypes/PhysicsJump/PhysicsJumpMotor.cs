using System.Collections.Generic;
using ProjectHotfix.Contracts;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Local authority prototype. Contact callbacks, not animation frames, own support.
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PhysicsJumpMotor : MonoBehaviour
    {
        public float JumpHeight = 0.25f;
        public float BufferSeconds = 0.12f;
        public float CoyoteSeconds = 0.10f;
        public float MaximumSlopeDegrees = 50f;
        public float MoveSpeed = 1.5f;
        public float Acceleration = 10f;
        public float ExternalLoadAccelerationScale = 1f;
        public bool ManualStep;
        public bool AuthorityEnabled = true;
        public bool ActionsBlocked { get; internal set; }
        public Vector2 MoveInput;
        public bool SprintRequested;
        public float SprintMultiplier = 1.5f;
        public float GroundTurnDegreesPerSecond = 360f;
        public float AirTurnDegreesPerSecond = 240f;

        private readonly Dictionary<Collider, Vector3> _contacts = new Dictionary<Collider, Vector3>();
        private Rigidbody _body;
        private readonly PhysicsJumpStateMachine _state = new PhysicsJumpStateMachine();
        private JumpPresentationState _stepPose;
        private bool _hasStepPose;
        public JumpPresentationState StepPoseState => _hasStepPose ? _stepPose : PresentationState;

        public bool JumpCommittedThisStep { get; private set; }
        public ulong SimulationTick { get; private set; }
        public float LastJumpLaunchSpeed { get; private set; }
        public bool Grounded => _state.Grounded;
        public int JumpCount => _state.JumpCount;
        public int LandingCount => _state.LandingCount;
        public Rigidbody Body => _body != null ? _body : (_body = GetComponent<Rigidbody>());
        public JumpPresentationState PresentationState => _state.Capture(Body.linearVelocity.y,
            Body.linearVelocity.x, Body.linearVelocity.z, SprintRequested, ProbeGroundDistance());

        private void Awake() { _body = GetComponent<Rigidbody>(); }
        private void FixedUpdate() { if (!ManualStep) Step(Time.fixedDeltaTime); }

        public void ClearPendingInput() => _state.ClearPendingInput();

        public void RequestJump()
        {
            _state.BufferSeconds = BufferSeconds;
            if (AuthorityEnabled && !ActionsBlocked)
            {
                var ledge=GetComponent<LedgeGrabReview>();
                if(ledge!=null&&ledge.QueueClimbRequest())return;
                _state.RequestJump();
            }
        }

        internal void ApplyClimbLaunch(float upwardSpeed)
        {
            var velocity=Body.linearVelocity;Body.linearVelocity=new Vector3(velocity.x,Mathf.Max(velocity.y,upwardSpeed),velocity.z);
            _state.NotifyExternalLaunch();_stepPose=PresentationState;_hasStepPose=true;
        }

        public void Step(float deltaTime)
        {
            SimulationTick++;
            JumpCommittedThisStep = false;
            _state.BufferSeconds = BufferSeconds;
            _state.CoyoteSeconds = CoyoteSeconds;
            var support = false;
            var minimumNormalY = Mathf.Cos(MaximumSlopeDegrees * Mathf.Deg2Rad);
            foreach (var entry in _contacts)
            {
                if (entry.Key != null && entry.Key.enabled && entry.Key.gameObject.activeInHierarchy
                    && entry.Value.y >= minimumNormalY) support = true;
            }
            var enabled = AuthorityEnabled && !ActionsBlocked && !Body.isKinematic;
            var jumpAccepted = _state.Step(deltaTime, support, Body.linearVelocity.y, enabled);
            if (!enabled) { _stepPose=PresentationState;_hasStepPose=true;GetComponent<LedgeGrabReview>()?.PreparePresentationState();return; }
            if (jumpAccepted)
            {
                var speed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * Mathf.Max(0.01f, JumpHeight));
                // Commit the same initial vertical velocity immediately. A shared
                // grab-jump top-up in this physics step can then see it and cannot
                // stack a second queued velocity-change impulse on this body.
                var current = Body.linearVelocity;
                Body.linearVelocity = new Vector3(current.x, speed, current.z);
                JumpCommittedThisStep = true; LastJumpLaunchSpeed = speed;
            }
            var desired = Vector2.ClampMagnitude(MoveInput, 1f) * MoveSpeed * (SprintRequested ? SprintMultiplier : 1f);
            var velocity = Body.linearVelocity;
            var change = Vector2.ClampMagnitude(desired - new Vector2(velocity.x, velocity.z), Acceleration * Mathf.Clamp(ExternalLoadAccelerationScale, 1f, 2f) * deltaTime);
            Body.AddForce(new Vector3(change.x, 0f, change.y), ForceMode.VelocityChange);
            var travel = new Vector3(velocity.x, 0f, velocity.z);
            if (travel.sqrMagnitude > .01f)
            {
                var error = Vector3.SignedAngle(Body.rotation * Vector3.forward, travel, Vector3.up);
                // At a near-exact reversal, keep the existing turn direction.
                if (Mathf.Abs(error) > 175f && Mathf.Abs(Body.angularVelocity.y) > .05f)
                    error = Mathf.Abs(error) * Mathf.Sign(Body.angularVelocity.y);
                var limit = (Grounded ? GroundTurnDegreesPerSecond : AirTurnDegreesPerSecond) * Mathf.Deg2Rad;
                var targetRate = Mathf.Clamp(error * Mathf.Deg2Rad * 5f, -limit, limit);
                var acceleration = Mathf.Clamp((targetRate - Body.angularVelocity.y) * 12f, -24f, 24f);
                Body.AddTorque(Vector3.up * acceleration, ForceMode.Acceleration);
            }
            else Body.AddTorque(Vector3.up * (-Body.angularVelocity.y * 12f), ForceMode.Acceleration);
            _stepPose=PresentationState;_hasStepPose=true;
            GetComponent<LedgeGrabReview>()?.PreparePresentationState();
        }

        private float ProbeGroundDistance()
        {
            var capsule = GetComponent<CapsuleCollider>();
            var center = transform.TransformPoint(capsule.center);
            var halfHeight = capsule.height * Mathf.Abs(transform.lossyScale.y) * .5f;
            if (gameObject.scene.GetPhysicsScene().Raycast(center, Vector3.down, out var hit,
                halfHeight + .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y >= Mathf.Cos(MaximumSlopeDegrees * Mathf.Deg2Rad))
                return Mathf.Max(0f, hit.distance - halfHeight);
            return float.PositiveInfinity;
        }

        public void ResetAt(Vector3 position)
        {
            _hasStepPose=false;
            JumpCommittedThisStep = false; LastJumpLaunchSpeed = 0f;
            Body.position = position;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            _contacts.Clear();
            _state.Reset();
            MoveInput = Vector2.zero;
            SprintRequested = false; ExternalLoadAccelerationScale = 1f;
        }

        private void OnCollisionEnter(Collision collision) { RecordContact(collision); }
        private void OnCollisionStay(Collision collision) { RecordContact(collision); }
        private void OnCollisionExit(Collision collision) { _contacts.Remove(collision.collider); }
        private void OnDisable() { _contacts.Clear(); _state.ClearPendingInput(); }

        private void RecordContact(Collision collision)
        {
            var normal = Vector3.down;
            for (var index = 0; index < collision.contactCount; index++)
            {
                var contact = collision.GetContact(index);
                if (contact.separation > .002f) continue;
                var candidate = contact.normal;
                if (candidate.y > normal.y) normal = candidate;
            }
            _contacts[collision.collider] = normal;
        }
    }
}
