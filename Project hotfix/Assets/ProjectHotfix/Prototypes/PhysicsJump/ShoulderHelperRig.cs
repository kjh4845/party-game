using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Skin-only shoulder helpers. Each one sits on the upper-arm joint and carries
    // a fixed fraction of the chest -> upper-arm rotation, so the shoulder skin is
    // spread over several partial rotations instead of one seam. Helpers have no
    // Rigidbody or drive; every pose writer calls Apply after the arm bones are set.
    public sealed class ShoulderHelperRig : MonoBehaviour
    {
        [Serializable] private struct Helper
        {
            public Transform Bone, Upper;
            public Quaternion Rest;
            public float Fraction;
        }
        [SerializeField] private Transform _chest;
        [SerializeField] private Helper[] _helpers = Array.Empty<Helper>();

        public int Count => _helpers.Length;

        // Name-based fallback for transports without a helper table.
        public static float FractionOf(string name)
        {
            if (name.StartsWith("ShoulderBlendInner_")) return .25f;
            if (name.StartsWith("ShoulderBlendOuter_")) return .75f;
            return .5f;
        }

        public static bool IsHelper(string name) => name.StartsWith("ShoulderBlend");

        public static ShoulderHelperRig Bind(Transform root, Transform[] bones, IReadOnlyDictionary<string, float> fractions)
        {
            var chest = Array.Find(bones, b => b.name == "Chest");
            var helpers = new List<Helper>();
            foreach (var bone in bones)
            {
                if (!IsHelper(bone.name)) continue;
                var side = bone.name.Substring(bone.name.Length - 1);
                var upper = Array.Find(bones, b => b.name == "UpperArm_" + side);
                if (upper == null || chest == null) throw new InvalidOperationException("Shoulder helper without chest/upper arm: " + bone.name);
                var fraction = fractions != null && fractions.TryGetValue(bone.name, out var f) ? f : FractionOf(bone.name);
                helpers.Add(new Helper { Bone = bone, Upper = upper, Rest = bone.localRotation, Fraction = Mathf.Clamp01(fraction) });
            }
            if (helpers.Count == 0) return null;
            var rig = root.gameObject.AddComponent<ShoulderHelperRig>();
            rig._chest = chest; rig._helpers = helpers.ToArray();
            return rig;
        }

        public void Apply()
        {
            foreach (var helper in _helpers)
                helper.Bone.SetPositionAndRotation(helper.Upper.position,
                    Quaternion.Slerp(_chest.rotation * helper.Rest, helper.Upper.rotation, helper.Fraction));
        }

        public float FractionFor(Transform bone)
        {
            foreach (var helper in _helpers) if (helper.Bone == bone) return helper.Fraction;
            return FractionOf(bone.name);
        }
    }
}
