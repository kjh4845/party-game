using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.SimulationHarness.Tests.Unit
{
    [Category("Core")]
    public sealed class RecoveryOrientationTests
    {
        [TestCase(-90, 0, RecoveryOrientation.FaceUp)]
        [TestCase(90, 0, RecoveryOrientation.FaceDown)]
        [TestCase(0, 90, RecoveryOrientation.LeftSide)]
        [TestCase(0, -90, RecoveryOrientation.RightSide)]
        public void SupportedFall_IsClassifiedAcrossYawAndSlopedGround(
            float pitch, float roll, RecoveryOrientation expected)
        {
            foreach (var slope in new[] { Quaternion.identity, Quaternion.Euler(12, 0, 18) })
            {
                for (var yaw = 0; yaw < 360; yaw += 15)
                {
                    var fall = slope * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(pitch, 0, roll);
                    Assert.That(RecoveryOrientationResolver.TryClassify(fall, slope * Vector3.up, out var result), Is.True);
                    Assert.That(result, Is.EqualTo(expected), $"yaw={yaw}, slope={slope}");
                }
            }
        }

        [TestCase(35, RecoveryOrientation.FaceUp)]
        [TestCase(55, RecoveryOrientation.RightSide)]
        public void DiagonalFall_UsesTheDominantSupportedSide(float sideAngle, RecoveryOrientation expected)
        {
            var rotation = Quaternion.Euler(-90, 0, 0) * Quaternion.AngleAxis(sideAngle, Vector3.up);
            Assert.That(RecoveryOrientationResolver.TryClassify(rotation, Vector3.up, out var result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void AirborneUnsettledOrBlockedRecovery_DoesNotStart(bool supported, bool clear)
        {
            var state = new CharacterRecoveryStateMachine();
            state.TryBeginDownEpisode();
            Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(-90, 0, 0), Vector3.up, supported, clear), Is.False);
            Assert.That(state.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble));
            Assert.That(state.GetUpOrientation, Is.Null);
            Assert.That(state.DownCount, Is.EqualTo(1U));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AllRecoveryEntries_KeepEpisodeSemanticsAndLatchOrientation(bool down)
        {
            foreach (var rotation in new[] { Quaternion.Euler(-90, 0, 0), Quaternion.Euler(90, 0, 0),
                         Quaternion.Euler(0, 0, 90), Quaternion.Euler(0, 0, -90) })
            {
                var state = new CharacterRecoveryStateMachine();
                if (down) state.TryBeginDownEpisode();
                else state.BeginDropkickRecovery();
                Assert.That(state.TryBeginSharedGetUp(rotation, Vector3.up, true, true), Is.True);
                var entry = state.GetUpOrientation;
                Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(90, 0, 0), Vector3.up, true, true), Is.False);
                Assert.That(state.GetUpOrientation, Is.EqualTo(entry));
                Assert.That(state.IsDownEpisode, Is.EqualTo(down));
                Assert.That(state.DownCount, Is.EqualTo(down ? 1U : 0U));
                Assert.That(state.BlocksNewAttacks, Is.True);
                state.CompleteRecovery();
                Assert.That(state.GetUpOrientation, Is.Null);
                Assert.That(state.BlocksNewAttacks, Is.False);
            }
        }

        [Test]
        public void InvalidUnsupportedOrUprightOrientation_IsRejectedWithoutMutation()
        {
            var state = new CharacterRecoveryStateMachine();
            state.BeginDropkickRecovery();
            foreach (var rotation in new[] { Quaternion.identity, Quaternion.Euler(180, 0, 0),
                         new Quaternion(0, 0, 0, 0), new Quaternion(float.NaN, 0, 0, 1) })
            {
                Assert.That(state.TryBeginSharedGetUp(rotation, Vector3.up, true, true), Is.False);
            }
            Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(-90, 0, 0), Vector3.zero, true, true), Is.False);
            Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(-90, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), true, true), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginSharedGetUp((RecoveryOrientation)99));
            Assert.That(state.GetUpOrientation, Is.Null);
            Assert.That(state.DownCount, Is.Zero);
        }

        [Test]
        public void GroundQueryMayReturnANonUnitNormal()
        {
            Assert.That(RecoveryOrientationResolver.TryClassify(Quaternion.Euler(-90, 0, 0), Vector3.up * 3, out var result), Is.True);
            Assert.That(result, Is.EqualTo(RecoveryOrientation.FaceUp));
        }

        [Test]
        public void AlignmentNormalNormalization_MatchesEntryClassification()
        {
            var source = Quaternion.Euler(0, 0, 90);
            Assert.That(RecoveryOrientationResolver.TryBuildSideAlignment(source, Vector3.up * 0.00000001f,
                out var getUp, out var target), Is.True);
            Assert.That(getUp, Is.EqualTo(RecoveryOrientation.FaceUp));
            Assert.That(RecoveryOrientationResolver.TryClassify(target, Vector3.up, out var result), Is.True);
            Assert.That(result, Is.EqualTo(getUp));
        }

        [TestCase(-90, 70, RecoveryOrientation.FaceUp)]
        [TestCase(-90, -70, RecoveryOrientation.FaceUp)]
        [TestCase(90, 70, RecoveryOrientation.FaceDown)]
        [TestCase(90, -70, RecoveryOrientation.FaceDown)]
        public void SideAlignment_ChoosesNearestSharedGetUpAndPreservesPlanarHeading(
            float pitch, float rollAboutBody, RecoveryOrientation expected)
        {
            var slope = Quaternion.Euler(12, 0, 18);
            var normal = slope * Vector3.up;
            for (var yaw = 0; yaw < 360; yaw += 15)
            {
                var source = slope * Quaternion.AngleAxis(yaw, Vector3.up)
                    * Quaternion.Euler(pitch, 0, 0) * Quaternion.AngleAxis(rollAboutBody, Vector3.up);
                Assert.That(RecoveryOrientationResolver.TryBuildSideAlignment(source, normal, out var getUp, out var target), Is.True);
                Assert.That(getUp, Is.EqualTo(expected));
                Assert.That(RecoveryOrientationResolver.TryClassify(target, normal, out var aligned), Is.True);
                Assert.That(aligned, Is.EqualTo(expected));
                var sourceHeading = Vector3.ProjectOnPlane(source * Vector3.up, normal).normalized;
                Assert.That(Vector3.Dot(sourceHeading, target * Vector3.up), Is.GreaterThan(0.9999f));
                var previous = source;
                for (var step = 1; step <= 20; step++)
                {
                    var sample = Quaternion.Slerp(source, target, step / 20f);
                    Assert.That(Quaternion.Angle(previous, sample), Is.LessThan(5f));
                    previous = sample;
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SideAlignment_NeedsReachedTargetAndCurrentSupportBeforeSharedGetUp(bool down)
        {
            var state = new CharacterRecoveryStateMachine();
            if (down) state.TryBeginDownEpisode();
            else state.BeginDropkickRecovery();
            Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(0, 0, 90), Vector3.up, true, true), Is.True);
            Assert.That(state.EntryOrientation, Is.EqualTo(RecoveryOrientation.LeftSide));
            Assert.That(state.GetUpOrientation, Is.EqualTo(RecoveryOrientation.FaceUp));
            Assert.That(state.AlignmentTargetRotation.HasValue, Is.True);
            Assert.That(state.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.AligningToGetUp));
            Assert.That(state.TryFinishAlignment(false, true, true), Is.False);
            Assert.That(state.TryFinishAlignment(true, false, true), Is.False);
            Assert.That(state.TryFinishAlignment(true, true, false), Is.False);
            Assert.That(state.TryFinishAlignment(true, true, true), Is.True);
            Assert.That(state.TryFinishAlignment(true, true, true), Is.False);
            Assert.That(state.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.SharedGetUp));
            Assert.That(state.DownCount, Is.EqualTo(down ? 1U : 0U));
            Assert.That(state.IsDownEpisode, Is.EqualTo(down));
        }

        [Test]
        public void InterruptedAlignment_CanReassessWithoutAddingADownEpisode()
        {
            var state = new CharacterRecoveryStateMachine();
            state.TryBeginDownEpisode();
            state.TryBeginSharedGetUp(Quaternion.Euler(0, 0, -90), Vector3.up, true, true);
            state.InterruptRecoveryPresentation();
            Assert.That(state.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble));
            Assert.That(state.AlignmentTargetRotation, Is.Null);
            Assert.That(state.EntryOrientation, Is.Null);
            Assert.That(state.GetUpOrientation, Is.Null);
            Assert.That(state.DownCount, Is.EqualTo(1U));
            Assert.That(state.BlocksNewAttacks, Is.True);
            Assert.That(state.TryBeginSharedGetUp(Quaternion.Euler(90, 0, 0), Vector3.up, true, true), Is.True);
            Assert.That(state.GetUpOrientation, Is.EqualTo(RecoveryOrientation.FaceDown));
            state.ResetRound();
            Assert.That(state.AlignmentTargetRotation, Is.Null);
            Assert.That(state.EntryOrientation, Is.Null);
            Assert.That(state.DownCount, Is.Zero);
        }

        [Test]
        public void ActualDownDuringDropkickAlignment_ClosesTheOldPresentation()
        {
            var state = new CharacterRecoveryStateMachine();
            state.BeginDropkickRecovery();
            state.TryBeginSharedGetUp(Quaternion.Euler(0, 0, 90), Vector3.up, true, true);
            Assert.That(state.TryBeginDownEpisode(), Is.True);
            Assert.That(state.AlignmentTargetRotation, Is.Null);
            Assert.That(state.GetUpOrientation, Is.Null);
            Assert.That(state.EntryOrientation, Is.Null);
            Assert.That(state.PresentationPhase, Is.EqualTo(RecoveryPresentationPhase.PhysicsTumble));
            Assert.That(state.DownCount, Is.EqualTo(1U));
        }
    }
}
