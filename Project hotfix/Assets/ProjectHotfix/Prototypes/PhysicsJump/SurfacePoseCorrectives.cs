using System;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // The same pose weights drive rendered skin and fixed-step contact skin.
    // Each mode is an independent interpolation group: Pose (shoulder, elbow and
    // torso), Shoulder (upper arm in the chest frame only) and Elbow (forearm in
    // the upper-arm frame only). Shoulder and elbow correctives touch separate
    // skin regions, so their groups add without double counting.
    [DefaultExecutionOrder(200)]
    public sealed class SurfacePoseCorrectives : MonoBehaviour
    {
        public enum ReferenceMode { Pose = 0, Shoulder = 1, Elbow = 2 }
        [Serializable] private sealed class Reference
        {
            public int Side, BlendIndex;
            public ReferenceMode Mode;
            public Quaternion Shoulder, Elbow, Torso;
        }
        [SerializeField] private SkinnedMeshRenderer _skin;
        [SerializeField] private Transform _spine, _chest, _left, _right, _leftElbow, _rightElbow;
        [SerializeField] private Reference[] _references = Array.Empty<Reference>();
        private float[] _renderWeights;

        public void Bind(SkinnedMeshRenderer skin, Transform[] bones)
        {
            _skin = skin;
            _spine = Array.Find(bones, b => b.name == "Spine");
            _chest = Array.Find(bones, b => b.name == "Chest");
            _left = Array.Find(bones, b => b.name == "UpperArm_L");
            _right = Array.Find(bones, b => b.name == "UpperArm_R");
            _leftElbow = Array.Find(bones, b => b.name == "Forearm_L");
            _rightElbow = Array.Find(bones, b => b.name == "Forearm_R");
        }

        public void AddReference(int side, Quaternion shoulder, Quaternion elbow, Quaternion torso, int blendIndex,
            ReferenceMode mode = ReferenceMode.Pose)
        {
            Array.Resize(ref _references, _references.Length + 1);
            _references[_references.Length - 1] = new Reference
                { Side = side, Mode = mode, Shoulder = shoulder, Elbow = elbow, Torso = torso, BlendIndex = blendIndex };
        }

        private static float DistanceSquared(Reference reference, Quaternion shoulder, Quaternion elbow, Quaternion torso)
        {
            var a = Quaternion.Angle(shoulder, reference.Shoulder);
            var b = Quaternion.Angle(elbow, reference.Elbow);
            switch (reference.Mode)
            {
                case ReferenceMode.Shoulder: return a * a;
                case ReferenceMode.Elbow: return b * b;
            }
            var c = Quaternion.Angle(torso, reference.Torso);
            return a * a + .5f * b * b + 4f * c * c;
        }

        private void EvaluateSide(int side, Quaternion shoulder, Quaternion elbow, Quaternion torso, float[] weights)
        {
            for (var mode = ReferenceMode.Pose; mode <= ReferenceMode.Elbow; mode++)
            {
                var total = 0f;
                var nearest = float.PositiveInfinity;
                foreach (var reference in _references)
                {
                    if (reference.Side != side || reference.Mode != mode) continue;
                    var distance = DistanceSquared(reference, shoulder, elbow, torso);
                    nearest = Mathf.Min(nearest, distance);
                    distance = Mathf.Max(.0025f, distance);
                    total += 1 / (distance * distance);
                }
                if (total <= 0) continue;
                var coverage = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(45, 85, Mathf.Sqrt(nearest)));
                foreach (var reference in _references)
                {
                    if (reference.Side != side || reference.Mode != mode || reference.BlendIndex < 0) continue;
                    var distance = Mathf.Max(.0025f, DistanceSquared(reference, shoulder, elbow, torso));
                    weights[reference.BlendIndex] = coverage / (distance * distance * total);
                }
            }
        }

        public void EvaluateFixed(FixedReviewPose pose, float[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            var chest = pose.WorldRotation("Chest");
            var torso = Quaternion.Inverse(pose.WorldRotation("Spine")) * chest;
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R";
                var upper = pose.WorldRotation("UpperArm_" + suffix);
                var forearm = pose.WorldRotation("Forearm_" + suffix);
                EvaluateSide(side, Quaternion.Inverse(chest) * upper, Quaternion.Inverse(upper) * forearm, torso, weights);
            }
        }

        public void ApplyCurrentPose()
        {
            if (_skin == null || !_skin.enabled || _chest == null) return;
            if (_renderWeights == null || _renderWeights.Length != _skin.sharedMesh.blendShapeCount)
                _renderWeights = new float[_skin.sharedMesh.blendShapeCount];
            Array.Clear(_renderWeights, 0, _renderWeights.Length);
            var torso = Quaternion.Inverse(_spine.rotation) * _chest.rotation;
            EvaluateSide(0, Quaternion.Inverse(_chest.rotation) * _left.rotation,
                Quaternion.Inverse(_left.rotation) * _leftElbow.rotation, torso, _renderWeights);
            EvaluateSide(1, Quaternion.Inverse(_chest.rotation) * _right.rotation,
                Quaternion.Inverse(_right.rotation) * _rightElbow.rotation, torso, _renderWeights);
            for (var i = 0; i < _renderWeights.Length; i++)
                _skin.SetBlendShapeWeight(i, _renderWeights[i] * 100);
        }

        private void LateUpdate() => ApplyCurrentPose();
    }
}
