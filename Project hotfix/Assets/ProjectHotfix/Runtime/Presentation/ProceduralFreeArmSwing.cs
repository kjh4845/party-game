using UnityEngine;

namespace ProjectHotfix.Presentation
{
    // A separate optional locomotion layer. Occupied/attacking arms are never written.
    public sealed class ProceduralFreeArmSwing : MonoBehaviour
    {
        public bool LeftAvailable = true;
        public bool RightAvailable = true;
        private readonly Transform[] _arms = new Transform[2];
        private readonly Quaternion[] _idle = new Quaternion[2];
        private uint _resetVersion;

        private void Start() { if (_arms[0] == null) Bind(); }
        public void Bind()
        {
            foreach (var bone in GetComponentsInChildren<Transform>())
            {
                var index = bone.name == "UpperArm_L" ? 0 : bone.name == "UpperArm_R" ? 1 : -1;
                if (index < 0) continue;
                _arms[index] = bone;
                _idle[index] = bone.localRotation;
            }
        }

        public void Apply(ProceduralJumpPose legs, float deltaTime, uint resetVersion, bool suppressLeft = false, bool suppressRight = false)
        {
            var reset = _resetVersion != resetVersion; _resetVersion = resetVersion;
            var travel = legs.TravelVelocity;
            var axis = travel.sqrMagnitude > .0001f ? Vector3.Cross(Vector3.up, travel.normalized) : transform.right;
            for (var i = 0; i < 2; i++)
            {
                if (_arms[i] == null || (i == 0 ? !LeftAvailable || suppressLeft : !RightAvailable || suppressRight)) continue;
                var swing = Mathf.Sin(legs.GaitPhase * Mathf.PI * 2f + i * Mathf.PI)
                    * Mathf.Lerp(10f, 25f, legs.SprintBlend) * legs.GaitWeight * legs.FreeArmSwingScale;
                var parentRotation = _arms[i].parent != null ? _arms[i].parent.rotation : transform.rotation;
                var target = Quaternion.AngleAxis(swing, axis) * parentRotation * _idle[i];
                _arms[i].rotation = reset ? target : Quaternion.Slerp(_arms[i].rotation, target,
                    1f - Mathf.Exp(-18f * deltaTime));
            }
        }
    }
}
