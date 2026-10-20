using System;
using ProjectHotfix.Contracts;
using UnityEngine;

namespace ProjectHotfix.Presentation
{
    // Read-only physics contact follower. No Rigidbody, collider or joint writes.
    public sealed class GrabArmPose : MonoBehaviour
    {
        private readonly Transform[][] _bones = { new Transform[4], new Transform[4] };
        private readonly Quaternion[][] _idle = { new Quaternion[4], new Quaternion[4] };
        private readonly Quaternion[][] _bind = { new Quaternion[4], new Quaternion[4] };
        private readonly Quaternion[][] _entry = { new Quaternion[4], new Quaternion[4] };
        private readonly bool[] _controlling = new bool[2];
        private float _terminalLength;
        private readonly Vector3[][] _handSupport = new Vector3[2][];
        private readonly float[] _fitOffset = new float[2], _fitAngle = new float[2];
        public float MaximumContactOffset { get; private set; }
        public float MaximumBoneCorrection { get; private set; }
        public float RemainingContactPenetration { get; private set; }
        public const float ContactOffsetLimit = .035f;
        public const float UpperCorrectionLimit = 12f, ForearmCorrectionLimit = 18f;
        private readonly Quaternion[][] _releasePrevious = { new Quaternion[4], new Quaternion[4] };
        private readonly Quaternion[] _clavicleForward = new Quaternion[2];
        private uint _resetVersion;
        public float MaximumHeldTargetError { get; private set; }
        public float MaximumReachCorrection { get; private set; }
        public bool IsControlling(int side) => _controlling[side];
        public Vector3 Terminal(int side) => _bones[side][3].TransformPoint(Vector3.back * _terminalLength);

        public void Bind(float terminalLength, Quaternion leftClavicle, Quaternion rightClavicle)
        {
            _terminalLength = terminalLength;
            _clavicleForward[0] = leftClavicle; _clavicleForward[1] = rightClavicle;
            var skin = GetComponentInChildren<SkinnedMeshRenderer>(); var bindposes = skin.sharedMesh.bindposes;
            var nodes = GetComponentsInChildren<Transform>(); var parts = new[] { "Clavicle", "UpperArm", "Forearm", "HandLogical" };
            for (var side = 0; side < 2; side++) for (var bone = 0; bone < 4; bone++)
            {
                _bones[side][bone] = Array.Find(nodes, t => t.name == parts[bone] + (side == 0 ? "_L" : "_R"));
                _idle[side][bone] = _bones[side][bone].localRotation;
                var index = Array.IndexOf(skin.bones, _bones[side][bone]);
                var parentIndex = Array.IndexOf(skin.bones, _bones[side][bone].parent);
                _bind[side][bone] = (bindposes[parentIndex] * bindposes[index].inverse).rotation;
            }
            var vertices = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights;
            for (var side = 0; side < 2; side++)
            {
                var handIndex = Array.IndexOf(skin.bones, _bones[side][3]);
                var forearmIndex = Array.IndexOf(skin.bones, _bones[side][2]);
                var cloud = new System.Collections.Generic.List<Vector3>();
                for (var v = 0; v < vertices.Length; v++)
                {
                    var w = weights[v]; var sum = 0f;
                    if (w.boneIndex0 == handIndex || w.boneIndex0 == forearmIndex) sum += w.weight0;
                    if (w.boneIndex1 == handIndex || w.boneIndex1 == forearmIndex) sum += w.weight1;
                    if (w.boneIndex2 == handIndex || w.boneIndex2 == forearmIndex) sum += w.weight2;
                    if (w.boneIndex3 == handIndex || w.boneIndex3 == forearmIndex) sum += w.weight3;
                    var local = bindposes[handIndex].MultiplyPoint3x4(vertices[v]);
                    if (sum >= .99f && local.z <= .025f) cloud.Add(local);
                }
                var support = new System.Collections.Generic.HashSet<Vector3>();
                for (var sample = 0; sample < 64 && cloud.Count > 0; sample++)
                {
                    var y = 1f - 2f * (sample + .5f) / 64; var radius = Mathf.Sqrt(1 - y * y); var angle = sample * 2.39996323f;
                    var direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    var best = float.NegativeInfinity; var selected = Vector3.zero;
                    foreach (var point in cloud) { var value = Vector3.Dot(point, direction); if (value > best) { best = value; selected = point; } }
                    support.Add(selected);
                }
                _handSupport[side] = new Vector3[support.Count]; support.CopyTo(_handSupport[side]);
            }
        }
        public void Apply(GrabPresentationState state, float deltaTime, bool leftAvailable, bool rightAvailable, bool leftPunch, bool rightPunch)
        {
            var reset = _resetVersion != state.ResetVersion; _resetVersion = state.ResetVersion;
            MaximumHeldTargetError = MaximumReachCorrection = 0;
            MaximumContactOffset = MaximumBoneCorrection = RemainingContactPenetration = 0;
            for (var side = 0; side < 2; side++)
            {
                var available = side == 0 ? leftAvailable : rightAvailable;
                var punch = side == 0 ? leftPunch : rightPunch;
                if (!available || punch) { _controlling[side] = false; _fitOffset[side] = _fitAngle[side] = 0; continue; }
                var hand = side == 0 ? state.Left : state.Right;
                if (reset)
                {
                    _fitOffset[side] = _fitAngle[side] = 0;
                    if (_controlling[side]) for (var i = 0; i < 4; i++) _bones[side][i].localRotation = _idle[side][i];
                    _controlling[side] = false;
                }
                if (!hand.Active)
                {
                    _fitOffset[side] = _fitAngle[side] = 0;
                    if (!_controlling[side]) continue;
                    var remaining = 0f;
                    for (var i = 0; i < 4; i++)
                    {
                        _releasePrevious[side][i] = _bones[side][i].localRotation;
                        _bones[side][i].localRotation = Quaternion.Slerp(_bones[side][i].localRotation, _idle[side][i], 1f - Mathf.Exp(-30f * deltaTime));
                        remaining = Mathf.Max(remaining, Quaternion.Angle(_bones[side][i].localRotation, _idle[side][i]));
                    }
                    // The grip is already released. Only the cosmetic arm return
                    // waits briefly for the rising body to clear the roof edge.
                    if (!float.IsNaN(hand.SurfaceTop) && !ExitPoseClearsRoof(side, hand))
                    { for (var i = 0; i < 4; i++) _bones[side][i].localRotation = _releasePrevious[side][i]; continue; }
                    if (remaining < .05f)
                    { for (var i = 0; i < 4; i++) _bones[side][i].localRotation = _idle[side][i]; _controlling[side] = false; }
                    continue;
                }
                if (!_controlling[side])
                    for (var i = 0; i < 4; i++) _entry[side][i] = _bones[side][i].localRotation;
                _controlling[side] = true;
                Solve(side, hand, false, deltaTime);
            }
        }
        // Resolve pinned hands after a body-only presentation offset, without
        // advancing entry/release blending a second time in the same fixed tick.
        public void ReapplyHeld(GrabPresentationState state)
        {
            MaximumHeldTargetError = MaximumReachCorrection = 0;
            MaximumContactOffset = MaximumBoneCorrection = RemainingContactPenetration = 0;
            if (_controlling[0] && state.Left.Holding) Solve(0, state.Left, true);
            if (_controlling[1] && state.Right.Holding) Solve(1, state.Right, true);
        }
        private void Solve(int side, GrabHandPresentationState hand, bool followBody = false, float seconds = 0f)
        {
            var surface = new Vector3(hand.X, hand.Y, hand.Z);
            var normal = new Vector3(hand.NormalX, hand.NormalY, hand.NormalZ).normalized;
            var fitting = normal.sqrMagnitude > .5f;
            SolveGeometry(side, surface, hand.LiftAmount, followBody, 0);
            var baseUpper = _bones[side][1].localRotation; var baseForearm = _bones[side][2].localRotation;
            if (fitting && seconds > 0)
            {
                var bestOffset = 0f; var bestAngle = 0f; var bestCost = float.PositiveInfinity;
                // Search only a small neighbourhood of the approved elbow direction.
                for (var offsetStep = 0; offsetStep <= 4; offsetStep++) for (var angleStep = -2; angleStep <= 2; angleStep++)
                {
                    var offset = offsetStep * ContactOffsetLimit / 4; var angle = angleStep * 7.5f;
                    SolveGeometry(side, surface + normal * offset, hand.LiftAmount, followBody, angle);
                    if (Quaternion.Angle(baseUpper, _bones[side][1].localRotation) > UpperCorrectionLimit ||
                        Quaternion.Angle(baseForearm, _bones[side][2].localRotation) > ForearmCorrectionLimit) continue;
                    var penetration = ContactPenetration(side, surface, normal, hand.Obstacles);
                    var cost = penetration * penetration + offset * offset * .03f + angle * angle * .00000003f;
                    if (cost < bestCost) { bestCost = cost; bestOffset = offset; bestAngle = angle; }
                }
                _fitOffset[side] = Mathf.MoveTowards(_fitOffset[side], bestOffset, .3f * seconds);
                _fitAngle[side] = Mathf.MoveTowards(_fitAngle[side], bestAngle, 180f * seconds);
            }
            if (!fitting) _fitOffset[side] = _fitAngle[side] = 0;
            var target = surface + normal * _fitOffset[side];
            var reach = SolveGeometry(side, target, hand.LiftAmount, followBody, _fitAngle[side]);
            // A changed body pose may invalidate a previously small correction.
            // Clamp the actual local rotations, never move the shoulder or scale a bone.
            _bones[side][1].localRotation = Quaternion.RotateTowards(baseUpper, _bones[side][1].localRotation, UpperCorrectionLimit);
            _bones[side][2].localRotation = Quaternion.RotateTowards(baseForearm, _bones[side][2].localRotation, ForearmCorrectionLimit);
            MaximumBoneCorrection = Mathf.Max(MaximumBoneCorrection, Quaternion.Angle(baseUpper, _bones[side][1].localRotation), Quaternion.Angle(baseForearm, _bones[side][2].localRotation));
            MaximumContactOffset = Mathf.Max(MaximumContactOffset, _fitOffset[side]);
            MaximumReachCorrection = Mathf.Max(MaximumReachCorrection, reach);
            for (var i = 0; i < 4; i++) _bones[side][i].localRotation = Quaternion.Slerp(_entry[side][i], _bones[side][i].localRotation, hand.Weight);
            if (fitting) RemainingContactPenetration = Mathf.Max(RemainingContactPenetration, ContactPenetration(side, surface, normal, hand.Obstacles));
            if (hand.Holding && hand.Weight >= .999f) MaximumHeldTargetError = Mathf.Max(MaximumHeldTargetError, Vector3.Distance(Terminal(side), target));
        }
        private float SolveGeometry(int side, Vector3 target, float lift, bool followBody, float bendAngle)
        {
            for (var i = 0; i < 4; i++) _bones[side][i].localRotation = _bind[side][i];
            _bones[side][0].localRotation = Quaternion.SlerpUnclamped(_bind[side][0], _clavicleForward[side], Mathf.Lerp(9f, 32f, lift) / 7f);
            var upper = _bones[side][1]; var forearm = _bones[side][2]; var wrist = _bones[side][3];
            var a = Vector3.Distance(upper.position, forearm.position); var b = Vector3.Distance(forearm.position, wrist.position) + _terminalLength;
            var delta = target - upper.position;
            var distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .00001f, a + b - .00001f);
            var reachError = Mathf.Abs(delta.magnitude - distance);
            var direction = delta.normalized;
            var up = followBody ? (_bones[side][0].parent.position - _bones[side][0].parent.parent.position).normalized : transform.up;
            var pole = -up + transform.right * ((side == 0 ? -1f : 1f) * .2f * lift);
            var bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = transform.right * (side == 0 ? -1 : 1);
            bend.Normalize(); bend = Quaternion.AngleAxis(bendAngle, direction) * bend;
            var projection = (a * a + distance * distance - b * b) / (2 * distance);
            var height = Mathf.Sqrt(Mathf.Max(0, a * a - projection * projection));
            var elbow = upper.position + direction * projection + bend * height;
            upper.rotation = Swing(forearm.position - upper.position, elbow - upper.position) * upper.rotation;
            var clamped = upper.position + direction * distance;
            forearm.rotation = Swing(Terminal(side) - forearm.position, clamped - forearm.position) * forearm.rotation;
            return reachError;
        }
        private float ContactPenetration(int side, Vector3 point, Vector3 normal, GrabArmObstacle[] obstacles)
        {
            var upper = _bones[side][1].position; var elbow = _bones[side][2].position; var tip = Terminal(side);
            var depth = Mathf.Max(0, .045f - Vector3.Dot(elbow - point, normal));
            foreach (var local in _handSupport[side]) depth = Mathf.Max(depth, .001f - Vector3.Dot(_bones[side][3].TransformPoint(local) - point, normal));
            if (obstacles != null) foreach (var obstacle in obstacles)
            {
                var a = new Vector3(obstacle.AX, obstacle.AY, obstacle.AZ); var b = new Vector3(obstacle.BX, obstacle.BY, obstacle.BZ);
                depth = Mathf.Max(depth, .035f + obstacle.Radius - SegmentDistance(elbow, tip, a, b), .045f + obstacle.Radius - SegmentDistance(upper, elbow, a, b));
            }
            return depth;
        }
        private bool ExitPoseClearsRoof(int side, GrabHandPresentationState hand)
        {
            var point = new Vector3(hand.X, hand.Y, hand.Z); var normal = new Vector3(hand.NormalX, hand.NormalY, hand.NormalZ);
            for (var bone = 1; bone <= 2; bone++)
            {
                var a = _bones[side][bone].position; var b = bone == 1 ? _bones[side][2].position : Terminal(side);
                var radius = bone == 1 ? .055f : .045f;
                for (var sample = 0; sample <= 4; sample++)
                {
                    var p = Vector3.Lerp(a, b, sample / 4f);
                    if (p.y - radius < hand.SurfaceTop + .003f && Vector3.Dot(p - point, normal) < radius + .003f) return false;
                }
            }
            return true;
        }
        private static float SegmentDistance(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            var d = q - p; var e = b - a; var r = p - a;
            var dd = Vector3.Dot(d, d); var ee = Vector3.Dot(e, e); var er = Vector3.Dot(e, r); float s, t;
            if (dd < 1e-9f && ee < 1e-9f) return r.magnitude;
            if (dd < 1e-9f) { s = 0; t = Mathf.Clamp01(er / ee); }
            else
            {
                var dr = Vector3.Dot(d, r);
                if (ee < 1e-9f) { t = 0; s = Mathf.Clamp01(-dr / dd); }
                else
                {
                    var de = Vector3.Dot(d, e); var denominator = dd * ee - de * de;
                    s = denominator > 1e-12f ? Mathf.Clamp01((de * er - dr * ee) / denominator) : 0;
                    t = (de * s + er) / ee;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-dr / dd); }
                    else if (t > 1) { t = 1; s = Mathf.Clamp01((de - dr) / dd); }
                }
            }
            return Vector3.Distance(p + d * s, a + e * t);
        }
        private static Quaternion Swing(Vector3 from, Vector3 to)
        {
            from.Normalize(); to.Normalize(); var dot = Vector3.Dot(from, to);
            if (dot < -.999999f) return Quaternion.AngleAxis(180, Vector3.Cross(from, Vector3.right).normalized);
            var cross = Vector3.Cross(from, to); return new Quaternion(cross.x, cross.y, cross.z, 1 + dot).normalized;
        }
    }
}
