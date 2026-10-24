using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class GetUpClipTests
    {
        [TestCase("FaceUp")] [TestCase("FaceDown")]
        public void GetUpHasAuthoredLimbAndTorsoCurvesWithoutRootMotionOrEvents(string direction)
        {
            var clip = Resources.Load<AnimationClip>("ReviewGetUp/GetUp_" + direction);
            Assert.That(clip, Is.Not.Null); Assert.That(clip.length, Is.EqualTo(2.35f).Within(.0001f)); Assert.That(clip.events, Is.Empty);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.EqualTo(87));
            Assert.That(bindings.All(b => b.path.StartsWith("Root/Pelvis") && (b.propertyName.StartsWith("m_LocalRotation.") || (b.path == "Root/Pelvis" && b.propertyName.StartsWith("m_LocalPosition.")))), Is.True);
            var spine = bindings.Single(b => b.path.EndsWith("/Spine") && b.propertyName == "m_LocalRotation.x");
            Assert.That(AnimationUtility.GetEditorCurve(clip, spine).Evaluate(.45f) * (direction == "FaceUp" ? 1 : -1), Is.GreaterThan(.03f), "Distinct front/back torso preparation");
            foreach (var bone in new[] { "Pelvis", "Spine", "Chest", "Head", "Clavicle_L", "Clavicle_R", "UpperArm_L", "UpperArm_R", "Forearm_L", "Forearm_R", "Thigh_L", "Thigh_R", "Calf_L", "Calf_R" })
            {
                var paths = bindings.Where(b => b.path.EndsWith("/" + bone)).ToArray();
                Assert.That(paths.Any(b => { var keys = AnimationUtility.GetEditorCurve(clip,b).keys; return keys.Max(k => k.value) - keys.Min(k => k.value) > .005f; }), Is.True, bone + " must actually animate");
            }
        }
        [TestCase("FaceUp")] [TestCase("FaceDown")]
        public void GetUpFinishesInNeutralBeforeLocomotionHandoff(string direction)
        {
            var clip = Resources.Load<AnimationClip>("ReviewGetUp/GetUp_" + direction);
            Assert.That(clip, Is.Not.Null);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var neutral = curve.Evaluate(0);
                Assert.That(curve.Evaluate(clip.length), Is.EqualTo(neutral).Within(.00001f), binding.path + " " + binding.propertyName);
                Assert.That(curve.Evaluate(clip.length - .15f), Is.EqualTo(neutral).Within(.00001f), "Settle before releasing input: " + binding.path);
            }
        }
    }
}
