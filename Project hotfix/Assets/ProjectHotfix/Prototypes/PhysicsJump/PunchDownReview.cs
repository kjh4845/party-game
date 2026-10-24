using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Opt-in local Punch integration; no collision callback produces damage.
    [DefaultExecutionOrder(-25)]
    public sealed class PunchDownReview : MonoBehaviour
    {
        public RagdollRecoveryReview Ragdoll { get; private set; }
        public PunchDownAccumulation Accumulation { get; private set; }
        public StrikeReviewTarget Target { get; private set; }
        private ulong _episode;
        private Vector3 _spawn;
        private bool _pendingDown;
        private Vector3 _pendingImpulse, _pendingLocalPoint;
        private float _pendingDeltaSpeed;
        private uint _pendingGeneration;
        public bool RegionPointCandidate;
        public bool ShowDebugOverlay = true;
        public int PointDecreasePerDown = 1;
        public int MinimumPointThreshold = 6;
        public PunchHitRegion LastHitRegion { get; private set; }
        public string LastHitPart { get; private set; }
        public int RequiredPoints => Accumulation.Profile.RequiredPoints(Ragdoll.Down.State.DownCount);
        public int RequiredHits => Accumulation.Profile.RequiredHits;
        private void Start() { if (Ragdoll == null) Bind(GetComponent<PhysicsJumpReview>()); }
        public void Bind(PhysicsJumpReview actor)
        {
            _spawn = actor.transform.position;
            Target = actor.GetComponent<StrikeReviewTarget>() ?? actor.gameObject.AddComponent<StrikeReviewTarget>();
            Target.GrabEligible = true;
            Ragdoll = actor.GetComponent<RagdollRecoveryReview>() ?? actor.gameObject.AddComponent<RagdollRecoveryReview>();
            if (Ragdoll.Actor == null) Ragdoll.Bind(actor);
            // Review START values. Production balance remains playtest-owned.
            Accumulation = new PunchDownAccumulation(RegionPointCandidate
                ? new PunchDownProfile(10,1,2,PointDecreasePerDown,MinimumPointThreshold) : new PunchDownProfile(5), Ragdoll.Down.State.RoundGeneration);
            if(RegionPointCandidate)PunchHurtbox.AddStanding(Target);
            _episode = Ragdoll.Down.State.EpisodeId;
        }
        public void Synchronize()
        {
            Ragdoll.Down.RefreshLock();
            var state = Ragdoll.Down.State;
            if (Accumulation.Generation != state.RoundGeneration) { Accumulation.Reset(state.RoundGeneration); _pendingDown = false; }
            if (_episode != state.EpisodeId)
            {
                _episode = state.EpisodeId;
                if (state.Kind == CharacterRecoveryKind.DownEpisode) Accumulation.ConsumeDown();
            }
        }
        private void FixedUpdate() { if (Ragdoll != null && !Ragdoll.Actor.Motor.ManualStep) Step(); }
        private void OnDisable() { _pendingDown = false; }
        public void Step()
        {
            Synchronize();
            if (!_pendingDown) return;
            _pendingDown = false;
            if (!Ragdoll.Actor.Motor.AuthorityEnabled || _pendingGeneration != Ragdoll.Down.State.RoundGeneration) return;
            // Enter before the motor queues this tick's forces. Never change
            // mass/inertia underneath locomotion forces already awaiting a solve.
            if (!Ragdoll.Active && !Ragdoll.BeginDown(Vector3.zero, Vector3.zero)) return;
            var applied = Ragdoll.ApplyEntryHit(_pendingImpulse, Ragdoll.Actor.transform.TransformPoint(_pendingLocalPoint), _pendingDeltaSpeed);
            Target.RecordAppliedImpulse(applied);
            Synchronize();
        }
        public bool Confirm(HandStrikeReview attacker, ulong actionId, Collider hit, Vector3 impulse, out Vector3 applied)
        {
            applied = Vector3.zero;
            if (Ragdoll == null || attacker == null || hit == null || !enabled || !Ragdoll.Actor.Motor.AuthorityEnabled
                || !attacker.Actor.Motor.AuthorityEnabled || attacker.Actor.Motor.ActionsBlocked
                || attacker.Actor == Ragdoll.Actor || StrikeReviewHurtbox.Resolve(hit) != Target) return false;
            if (!(attacker.CanHit(0) && attacker.State.ActionId(0) == actionId)
                && !(attacker.CanHit(1) && attacker.State.ActionId(1) == actionId)) return false;
            Synchronize();
            var state = Ragdoll.Down.State;
            var zone=hit.GetComponent<PunchHurtbox>();
            var region=zone!=null ? zone.Region : hit.attachedRigidbody!=null && hit.attachedRigidbody.name=="Head_Physics" ? PunchHitRegion.Head : PunchHitRegion.Body;
            var result = Accumulation.Receive(state.RoundGeneration, attacker.GetInstanceID(), actionId, _pendingDown || state.Kind == CharacterRecoveryKind.DownEpisode,region,state.DownCount);
            if (result == PunchDownResult.Rejected) return false;
            LastHitRegion=region;
            LastHitPart=hit.gameObject.name;
            var side=attacker.State.ActionId(0)==actionId ? 0 : 1;
            var point = hit.ClosestPoint(attacker.PreciseHits ? attacker.StrikePoint(side,attacker.State.PunchAge(side))
                : attacker.transform.TransformPoint(new Vector3(0,.66f,.38f)));
            if (result == PunchDownResult.ThresholdReached)
            {
                _pendingDown = true; _pendingGeneration = state.RoundGeneration; _pendingImpulse = impulse;
                _pendingLocalPoint = Ragdoll.Actor.transform.InverseTransformPoint(point);
                _pendingDeltaSpeed = Mathf.Max(0, attacker.MaximumDeltaSpeed);
                return true;
            }
            var body = hit.attachedRigidbody;
            if (body == null || body.isKinematic) body = Ragdoll.Actor.Motor.Body;
            applied = Vector3.ClampMagnitude(impulse, body.mass * Mathf.Max(0, attacker.MaximumDeltaSpeed));
            body.AddForceAtPosition(applied, point, ForceMode.Impulse);
            return true;
        }
        public void ResetReview()
        {
            Ragdoll.Actor.Motor.ResetAt(_spawn); Ragdoll.Down.RefreshLock(); Ragdoll.Step(1f / 60); Synchronize();
        }
        private void OnGUI()
        {
            if (!ShowDebugOverlay || Ragdoll == null || Accumulation == null) return;
            GUI.Label(new Rect(12, 180, 900, 30), RegionPointCandidate
                ? $"Punch points {Accumulation.Points}/{RequiredPoints}, last {LastHitRegion}, DownCount {Ragdoll.Down.State.DownCount}, wait {Ragdoll.Down.State.RemainingSeconds:F2}s"
                : $"Punch Down review: hits {Accumulation.Hits}/{RequiredHits}, DownCount {Ragdoll.Down.State.DownCount}, wait {Ragdoll.Down.State.RemainingSeconds:F2}s (START fixed 5 hits, no time decay)");
        }
    }
}
