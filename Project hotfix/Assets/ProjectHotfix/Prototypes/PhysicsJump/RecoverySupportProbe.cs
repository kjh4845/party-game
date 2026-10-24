using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    public readonly struct RecoverySupportSample
    {
        public readonly bool HasSupport, Stable, Clear;
        public readonly Vector3 Point, Normal, StandingRoot;
        public RecoverySupportSample(bool supported, bool stable, bool clear, Vector3 point, Vector3 normal, Vector3 root)
        { HasSupport = supported; Stable = stable; Clear = clear; Point = point; Normal = normal; StandingRoot = root; }
    }

    // Read-only authority queries, independent of rendered bones. Parameters
    // describe the caller's torso and upright capsule, not a final ragdoll profile.
    public sealed class RecoverySupportProbe
    {
        private readonly Rigidbody _torso;
        private readonly HashSet<Collider> _own;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Collider[] _overlaps = new Collider[64];
        private Collider _support;
        private Vector3 _normal, _localPoint;
        private float _stableSeconds;
        public readonly float StandingRadius, StandingHeight;
        public Quaternion TorsoRotation => _torso.rotation;
        public float ProbeRadius = .06f, ProbeDistance = .35f, MaximumSlopeDegrees = 50f, MaximumSupportGap = .03f;
        public float RequiredStableSeconds = .2f, MaximumRelativeSpeed = .15f, MaximumRelativeAngularSpeed = .5f;
        public int SupportMask = Physics.DefaultRaycastLayers;
        public int ClearanceMask = ~0;
        public bool CheckDistributedSupport;
        public Vector3? StandingReference;
        public RecoverySupportProbe(Rigidbody torso, Collider[] ownColliders, float standingRadius, float standingHeight)
        {
            if (torso == null || ownColliders == null) throw new ArgumentNullException(nameof(torso));
            if (!Finite(standingRadius) || standingRadius <= 0 || !Finite(standingHeight) || standingHeight < 2 * standingRadius)
                throw new ArgumentOutOfRangeException(nameof(standingRadius));
            _torso = torso; _own = new HashSet<Collider>(ownColliders); StandingRadius = standingRadius; StandingHeight = standingHeight;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Reset() { _support = null; _stableSeconds = 0; }
        public RecoverySupportSample Evaluate(float seconds) => EvaluateInternal(seconds, null);
        // Only use after a stable, clear recovery entry has latched this floor
        // point. Assisted get-up can temporarily lift the torso off the ground.
        public RecoverySupportSample EvaluateDuringGetUp(float seconds, Vector3 floorPoint)
            => EvaluateInternal(seconds, floorPoint);
        private bool NearestExternalRay(Vector3 origin,float distance,out RaycastHit hit)
        {
            hit=default;var count=_torso.gameObject.scene.GetPhysicsScene().Raycast(origin,Vector3.down,_hits,distance,SupportMask,QueryTriggerInteraction.Ignore);
            if(count==_hits.Length)return false;var nearest=float.PositiveInfinity;
            for(var i=0;i<count;i++)if(!_own.Contains(_hits[i].collider)&&_hits[i].distance<nearest){hit=_hits[i];nearest=hit.distance;}
            return hit.collider!=null;
        }
        private bool DistributedContact(out RaycastHit hit)
        {
            hit=default;var lowest=float.PositiveInfinity;
            foreach(var collider in _own)
            {
                if(collider==null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)continue;
                var near=collider.ClosestPoint(collider.bounds.center-Vector3.up*(collider.bounds.extents.y+.05f));
                if(!NearestExternalRay(near+Vector3.up*.02f,MaximumSupportGap+.02f,out var candidate)
                    || Vector3.Dot(candidate.normal,Vector3.up)<Mathf.Cos(MaximumSlopeDegrees*Mathf.Deg2Rad)
                    || Vector3.Distance(candidate.point,collider.ClosestPoint(candidate.point))>MaximumSupportGap)continue;
                if(candidate.point.y<lowest){hit=candidate;lowest=candidate.point.y;}
            }
            if(hit.collider==null)return false;
            if(StandingReference.HasValue)
            {
                var footprint=hit.point+Vector3.ProjectOnPlane(StandingReference.Value-hit.point,hit.normal);
                if(!NearestExternalRay(footprint+Vector3.up*.1f,.2f,out var projected)
                    || projected.collider!=hit.collider || Vector3.Angle(projected.normal,hit.normal)>5f)return false;
                hit=projected;
            }
            return true;
        }
        private RecoverySupportSample EvaluateInternal(float seconds, Vector3? floorPoint)
        {
            if (!Finite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Finite(ProbeRadius) || ProbeRadius <= 0 || !Finite(ProbeDistance) || ProbeDistance <= 0
                || !Finite(MaximumSupportGap) || MaximumSupportGap < 0
                || !Finite(RequiredStableSeconds) || RequiredStableSeconds < 0 || !Finite(MaximumSlopeDegrees) || MaximumSlopeDegrees < 0 || MaximumSlopeDegrees >= 90
                || !Finite(MaximumRelativeSpeed) || MaximumRelativeSpeed < 0 || !Finite(MaximumRelativeAngularSpeed) || MaximumRelativeAngularSpeed < 0)
                throw new InvalidOperationException("Invalid recovery query profile.");
            var scene = _torso.gameObject.scene.GetPhysicsScene();
            var origin = (floorPoint.HasValue ? floorPoint.Value + Vector3.up * .15f : _torso.worldCenterOfMass) + Vector3.up * ProbeRadius;
            var count = scene.SphereCast(origin, ProbeRadius, Vector3.down, _hits, ProbeDistance + ProbeRadius, SupportMask, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) { Reset(); return default; }
            var nearest = float.PositiveInfinity; RaycastHit hit = default;
            for (var i = 0; i < count; i++)
            {
                var candidate = _hits[i];
                if (_own.Contains(candidate.collider) || candidate.distance >= nearest) continue;
                // The closest non-owned surface occludes deeper ground even if
                // it is too steep. Never pick a floor through an obstacle.
                hit = candidate; nearest = candidate.distance;
            }
            var distributed=!floorPoint.HasValue && CheckDistributedSupport;
            if(distributed && !DistributedContact(out hit)){Reset();return default;}
            if (hit.collider == null || Vector3.Dot(hit.normal, Vector3.up) < Mathf.Cos(MaximumSlopeDegrees * Mathf.Deg2Rad))
            { Reset(); return default; }
            var contactGap = float.PositiveInfinity;
            foreach (var collider in _own)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger)
                    contactGap = Mathf.Min(contactGap, Vector3.Distance(hit.point, collider.ClosestPoint(hit.point)));
            if (!floorPoint.HasValue && !distributed && contactGap > MaximumSupportGap + 1e-5f) { Reset(); return default; }
            if(!floorPoint.HasValue && !distributed && StandingReference.HasValue)
            {
                var footprint=hit.point+Vector3.ProjectOnPlane(StandingReference.Value-hit.point,hit.normal);
                if(!NearestExternalRay(footprint+Vector3.up*.1f,.2f,out var projected)
                    || projected.collider!=hit.collider || Vector3.Angle(projected.normal,hit.normal)>5f){Reset();return default;}
                hit=projected;
            }
            var supportBody = hit.rigidbody;
            var supportVelocity = supportBody == null ? Vector3.zero : supportBody.GetPointVelocity(_torso.worldCenterOfMass);
            var supportAngular = supportBody == null ? Vector3.zero : supportBody.angularVelocity;
            var quiet = (_torso.linearVelocity - supportVelocity).magnitude <= MaximumRelativeSpeed
                && (_torso.angularVelocity - supportAngular).magnitude <= MaximumRelativeAngularSpeed;
            var localPoint = hit.collider.transform.InverseTransformPoint(hit.point);
            var continuous = _support == hit.collider && Vector3.Angle(_normal, hit.normal) <= 5f
                && Vector3.Distance(hit.point, hit.collider.transform.TransformPoint(_localPoint)) <= .04f;
            _stableSeconds = quiet ? (continuous ? _stableSeconds : 0f) + seconds : 0f;
            _support = hit.collider; _normal = hit.normal; _localPoint = localPoint;
            // Keep the standing capsule world-upright, as the locomotion motor
            // does. Its lower sphere is offset along the actual support normal.
            var bottom = hit.point + hit.normal * (StandingRadius + .005f);
            var top = bottom + Vector3.up * (StandingHeight - 2f * StandingRadius);
            var overlaps = scene.OverlapCapsule(bottom, top, StandingRadius, _overlaps, ClearanceMask, QueryTriggerInteraction.Ignore);
            var clear = overlaps < _overlaps.Length;
            for (var i = 0; i < overlaps; i++) if (!_own.Contains(_overlaps[i])) clear = false;
            return new RecoverySupportSample(true, quiet && _stableSeconds + 1e-6f >= RequiredStableSeconds, clear,
                hit.point, hit.normal, bottom - Vector3.up * StandingRadius);
        }
    }
}
