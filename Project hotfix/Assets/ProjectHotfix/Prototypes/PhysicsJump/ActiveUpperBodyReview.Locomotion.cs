using System;
using System.Globalization;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Walk and run read differently (2026-10-20 review: both were the same upright shuffle with the arms
    // hanging still). The walk swings straight arms a little; the run leans the trunk forward, bends the
    // elbows and pumps the arms opposite the legs, with a longer, higher stride (UseDistinctRunGait).
    // Only free arms swing: a reaching, holding, punching or guarding arm keeps its own goal.
    public sealed partial class ActiveUpperBodyReview
    {
        public float WalkArmSwing = 24f, RunArmSwing = 40f, WalkElbow = 12f, RunElbow = 85f, RunLean = 12f;
        // Shoulder extension while running (deg behind the bind; the standing limit is 25): the pumping arm
        // must get behind the body, or both bent arms hover in front (2026-10-20: "팔의 앞뒤 움직임이 어색").
        public float RunArmBackLimit = 55f;
        private float _runLean, _armBackLimit = 25f;
        // Free-arm drive gain while running: at the standing drive the arms followed the 3.4 Hz pump with half
        // the amplitude and a quarter-cycle lag, so both hovered in front (2026-10-20 measurement).
        public float RunArmDriveGain = 4f;
        private float _runArmDrive;
        // Walk arm swing at fighting range (set by the pair from the opponent's distance, 0..1). A punch thrown
        // from an arm swung back arrives late (2026-10-20: one of three head strikes missed its window), so
        // near the opponent the walking arms stay ready. The run's pumping arms are unaffected.
        public float WalkArmSwingScale = 1f;
        private float _walkSwingScale = 1f;

        // r43 interaction rig only. Run: half the walk-candidate cadence at the same speed, so each stride is
        // longer, with a high knee (lift .05 m inside a .07 m leg-shortening budget) and a bouncier pelvis.
        // PARTY_RUN_GAIT=cadence,lift,limit,lean,swing,elbow overrides for review comparisons.
        private void UseDistinctRunGait()
        {
            var pose = Actor.Pose;
            // The walk: longer, calmer steps (3 cycles/s instead of 4.8) that the physical arms can swing with;
            // at 4.8 the arm drives could not follow and the arms hung still.
            pose.WalkCadence = 3f;
            pose.SprintCadence = 3.4f; pose.FootPlantSprintLift = .05f; pose.SprintShorteningLimit = .07f;
            pose.FootPlantSprintPelvisDrop = .016f; pose.FootPlantSprintPelvisBob = .02f;
            var tuning = Environment.GetEnvironmentVariable("PARTY_RUN_GAIT");
            if (string.IsNullOrEmpty(tuning)) return;
            var v = Array.ConvertAll(tuning.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
            pose.SprintCadence = v[0]; pose.FootPlantSprintLift = v[1]; pose.SprintShorteningLimit = v[2];
            if (v.Length > 3) RunLean = v[3]; if (v.Length > 4) RunArmSwing = v[4]; if (v.Length > 5) RunElbow = v[5];
        }

        // After the reference arms are solved, before the guard and punch goals.
        private void ApplyLocomotionReference(float seconds)
        {
            if (!LocomotionPresentation) return;
            var pose = Actor.Pose; var gait = pose.GaitWeight; var run = pose.SprintBlend;
            var right = _goalRoot.right; var forward = _goalRoot.forward;
            var busy = IsStriking || _handActive[0] || _handActive[1] || _gripPosture[0] || _gripPosture[1];
            _runLean = Mathf.MoveTowards(_runLean, busy || !Supported ? 0 : RunLean * run * gait, seconds * 60);
            if (_runLean > .01f)
            {
                Find("Spine").Goal.rotation = Quaternion.AngleAxis(_runLean * .6f, right) * Find("Spine").Goal.rotation;
                Find("Chest").Goal.rotation = Quaternion.AngleAxis(_runLean * .4f, right) * Find("Chest").Goal.rotation;
                // The head keeps looking ahead instead of at the floor.
                Find("Head").Goal.rotation = Quaternion.AngleAxis(-_runLean * .7f, right) * Find("Head").Goal.rotation;
                if (LowReachWeight <= 0) pose.AdditionalPelvisPitch = _runLean * .4f;
            }
            else if (LowReachWeight <= 0) pose.AdditionalPelvisPitch = 0;
            _walkSwingScale = Mathf.MoveTowards(_walkSwingScale, Mathf.Clamp01(WalkArmSwingScale), seconds * 4);
            var back = Mathf.Lerp(25f, RunArmBackLimit, run * gait);
            if (Mathf.Abs(back - _armBackLimit) > .5f)
            {
                _armBackLimit = back;
                foreach (var name in new[] { "UpperArm_L", "UpperArm_R" }) Find(name).Joint.lowAngularXLimit = new SoftJointLimit { limit = -back };
            }
            _runArmDrive = run * gait;
            if (gait < .01f) return;
            var swing = Mathf.Lerp(WalkArmSwing * _walkSwingScale, RunArmSwing, run) * gait;
            var elbow = Mathf.Lerp(WalkElbow * _walkSwingScale, RunElbow, run) * gait;
            for (var side = 0; side < 2; side++)
            {
                if (_punchReference[side] || _gripPosture[side] || _guardWeight[side] > 0) continue;
                var free = 1 - Mathf.SmoothStep(0, 1, _handWeights[side]);
                if (free <= 0) continue;
                // Opposite the same-side foot: forward while that foot is back (its stride ~ cos of the contact phase).
                // The run leads the arm phase a little: even the stronger drive trails the pump by ~45 deg.
                var phase = Mathf.Repeat(pose.GaitPhase - .25f + side * .5f + .1f * run, 1f);
                var forwardSwing = -Mathf.Cos(phase * Mathf.PI * 2f) * swing * free;
                var suffix = side == 0 ? "L" : "R";
                var upper = Find("UpperArm_" + suffix); var forearm = Find("Forearm_" + suffix);
                // Symmetric about the hanging arm: the hand comes up to the chest in front and passes the hip behind.
                SwingForward(upper.Goal, upper.Goal.rotation * upper.Joint.axis, forearm.Goal.position, forward, forwardSwing);
                SwingForward(forearm.Goal, forearm.Goal.rotation * forearm.Joint.axis, GoalHand(side), forward, elbow * free);
            }
        }

        private void ApplyRunArmDrive(int side, ref JointDrive drive)
        {
            if (_runArmDrive <= 0 || _punchReference[side] || _handActive[side] || _gripPosture[side] || _guardWeight[side] > 0) return;
            var gain = Mathf.Lerp(1, RunArmDriveGain, _runArmDrive);
            drive.positionSpring *= gain; drive.maximumForce *= gain; drive.positionDamper *= Mathf.Sqrt(gain);
        }

        // Rotates a goal about its hinge so that its tip moves forward by degrees (backward when negative).
        private static void SwingForward(Transform goal, Vector3 axis, Vector3 tip, Vector3 forward, float degrees)
        {
            axis.Normalize();
            var sign = Vector3.Dot(Vector3.Cross(axis, tip - goal.position), forward) >= 0 ? 1 : -1;
            goal.rotation = Quaternion.AngleAxis(degrees * sign, axis) * goal.rotation;
        }
    }
}
