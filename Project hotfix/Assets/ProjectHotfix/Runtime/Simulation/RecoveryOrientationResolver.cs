using UnityEngine;

namespace ProjectHotfix.Simulation
{
    /// <summary>
    /// Classifies a supported authority torso relative to the support plane.
    /// This selects a recovery entry orientation; it does not select a clip,
    /// move a body, or infer a fall from the attack direction.
    /// </summary>
    public static class RecoveryOrientationResolver
    {
        public static bool TryClassify(
            Quaternion torsoRotation,
            Vector3 supportNormal,
            out RecoveryOrientation orientation)
        {
            orientation = default;
            var rotationLengthSquared = torsoRotation.x * torsoRotation.x
                + torsoRotation.y * torsoRotation.y + torsoRotation.z * torsoRotation.z
                + torsoRotation.w * torsoRotation.w;
            var normalLengthSquared = supportNormal.sqrMagnitude;
            if (!IsPositiveFinite(rotationLengthSquared) || !IsPositiveFinite(normalLengthSquared))
            {
                return false;
            }

            var inverseLength = 1f / Mathf.Sqrt(rotationLengthSquared);
            torsoRotation = new Quaternion(torsoRotation.x * inverseLength,
                torsoRotation.y * inverseLength, torsoRotation.z * inverseLength,
                torsoRotation.w * inverseLength);
            supportNormal /= Mathf.Sqrt(normalLengthSquared);

            var forwardDot = Vector3.Dot(torsoRotation * Vector3.forward, supportNormal);
            var rightDot = Vector3.Dot(torsoRotation * Vector3.right, supportNormal);
            var upDot = Vector3.Dot(torsoRotation * Vector3.up, supportNormal);
            if (Mathf.Abs(upDot) >= Mathf.Max(Mathf.Abs(forwardDot), Mathf.Abs(rightDot)))
            {
                // Standing, sitting vertically, or head-down is not a lying entry.
                // Authority must settle/reassess or use its upright return path.
                return false;
            }

            if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot))
            {
                orientation = forwardDot >= 0f ? RecoveryOrientation.FaceUp : RecoveryOrientation.FaceDown;
            }
            else
            {
                // Right pointing upward means the character's left side is down.
                orientation = rightDot >= 0f ? RecoveryOrientation.LeftSide : RecoveryOrientation.RightSide;
            }
            return true;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryBuildSideAlignment(Quaternion torsoRotation, Vector3 supportNormal,
            out RecoveryOrientation sharedGetUp, out Quaternion targetRotation)
        {
            sharedGetUp = default;
            targetRotation = Quaternion.identity;
            if (!TryClassify(torsoRotation, supportNormal, out var entry)
                || (entry != RecoveryOrientation.LeftSide && entry != RecoveryOrientation.RightSide))
            {
                return false;
            }

            var inverseLength = 1f / Mathf.Sqrt(torsoRotation.x * torsoRotation.x
                + torsoRotation.y * torsoRotation.y + torsoRotation.z * torsoRotation.z
                + torsoRotation.w * torsoRotation.w);
            torsoRotation = new Quaternion(torsoRotation.x * inverseLength,
                torsoRotation.y * inverseLength, torsoRotation.z * inverseLength,
                torsoRotation.w * inverseLength);
            supportNormal /= Mathf.Sqrt(supportNormal.sqrMagnitude);
            var heading = Vector3.ProjectOnPlane(torsoRotation * Vector3.up, supportNormal).normalized;
            var forwardDot = Vector3.Dot(torsoRotation * Vector3.forward, supportNormal);
            // Resolve an exact side tie consistently despite float roundoff.
            sharedGetUp = forwardDot >= -0.000001f ? RecoveryOrientation.FaceUp : RecoveryOrientation.FaceDown;
            var targetForward = sharedGetUp == RecoveryOrientation.FaceUp ? supportNormal : -supportNormal;
            targetRotation = Quaternion.LookRotation(targetForward, heading);
            return true;
        }
    }
}
