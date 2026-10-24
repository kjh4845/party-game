using System;
using UnityEngine;

namespace ProjectHotfix.Simulation
{
    public enum CharacterRecoveryKind
    {
        None = 0,
        DropkickRecovery = 1,
        DownEpisode = 2,
    }

    public enum RecoveryPresentationPhase
    {
        None = 0,
        PhysicsTumble = 1,
        SharedGetUp = 2,
        AligningToGetUp = 3,
        UprightReturn = 4,
    }

    public enum RecoveryOrientation
    {
        FaceUp = 0,
        FaceDown = 1,
        LeftSide = 2,
        RightSide = 3,
    }

    /// <summary>
    /// Keeps dropkick recovery separate from a scored Down episode while allowing
    /// both paths to use the same orientation-matched get-up presentation.
    /// </summary>
    public sealed class CharacterRecoveryStateMachine
    {
        public CharacterRecoveryKind Kind { get; private set; }

        public RecoveryPresentationPhase PresentationPhase { get; private set; }

        public RecoveryOrientation? GetUpOrientation { get; private set; }

        public RecoveryOrientation? EntryOrientation { get; private set; }

        public Quaternion? AlignmentTargetRotation { get; private set; }

        public uint DownCount { get; private set; }

        public bool BlocksNewAttacks => Kind != CharacterRecoveryKind.None;

        public bool IsDownEpisode => Kind == CharacterRecoveryKind.DownEpisode;

        public void BeginDropkickRecovery()
        {
            if (Kind == CharacterRecoveryKind.DownEpisode)
            {
                throw new InvalidOperationException(
                    "Dropkick recovery cannot replace an active Down episode.");
            }

            Kind = CharacterRecoveryKind.DropkickRecovery;
            PresentationPhase = RecoveryPresentationPhase.PhysicsTumble;
            GetUpOrientation = null;
            EntryOrientation = null;
            AlignmentTargetRotation = null;
        }

        public bool TryBeginDownEpisode(bool countForMatchRound = true)
        {
            if (Kind == CharacterRecoveryKind.DownEpisode)
            {
                return false;
            }

            Kind = CharacterRecoveryKind.DownEpisode;
            PresentationPhase = RecoveryPresentationPhase.PhysicsTumble;
            GetUpOrientation = null;
            EntryOrientation = null;
            AlignmentTargetRotation = null;
            if (countForMatchRound) DownCount = checked(DownCount + 1U);
            return true;
        }

        public void BeginSharedGetUp(RecoveryOrientation orientation)
        {
            if (Kind == CharacterRecoveryKind.None)
            {
                throw new InvalidOperationException(
                    "A recovery state is required before starting the shared get-up presentation.");
            }

            if (orientation != RecoveryOrientation.FaceUp && orientation != RecoveryOrientation.FaceDown)
            {
                throw new ArgumentOutOfRangeException(nameof(orientation));
            }

            PresentationPhase = RecoveryPresentationPhase.SharedGetUp;
            GetUpOrientation = orientation;
            EntryOrientation = orientation;
            AlignmentTargetRotation = null;
        }

        /// <summary>
        /// Authority supplies support stability and clearance from its physics
        /// checks. Missing support/space and repeated starts leave state intact.
        /// </summary>
        public bool TryBeginSharedGetUp(Quaternion torsoRotation, Vector3 supportNormal,
            bool hasStableSupport, bool hasClearance)
        {
            if (Kind == CharacterRecoveryKind.None
                || PresentationPhase != RecoveryPresentationPhase.PhysicsTumble
                || !hasStableSupport || !hasClearance
                || !RecoveryOrientationResolver.TryClassify(torsoRotation, supportNormal, out var orientation))
            {
                return false;
            }

            if (orientation == RecoveryOrientation.LeftSide || orientation == RecoveryOrientation.RightSide)
            {
                if (!RecoveryOrientationResolver.TryBuildSideAlignment(torsoRotation, supportNormal,
                    out var targetOrientation, out var targetRotation))
                {
                    return false;
                }
                PresentationPhase = RecoveryPresentationPhase.AligningToGetUp;
                EntryOrientation = orientation;
                GetUpOrientation = targetOrientation;
                AlignmentTargetRotation = targetRotation;
            }
            else
            {
                BeginSharedGetUp(orientation);
            }
            return true;
        }

        public bool TryFinishAlignment(bool hasStableSupport, bool hasClearance, bool alignmentReached)
        {
            if (PresentationPhase != RecoveryPresentationPhase.AligningToGetUp
                || !hasStableSupport || !hasClearance || !alignmentReached)
            {
                return false;
            }
            PresentationPhase = RecoveryPresentationPhase.SharedGetUp;
            return true;
        }

        public bool TryBeginUprightReturn(bool supported, bool clear, bool upright)
        {
            if(Kind!=CharacterRecoveryKind.DownEpisode || PresentationPhase!=RecoveryPresentationPhase.PhysicsTumble
                || !supported || !clear || !upright) return false;
            PresentationPhase=RecoveryPresentationPhase.UprightReturn;
            GetUpOrientation=null;EntryOrientation=null;AlignmentTargetRotation=null;
            return true;
        }

        public void InterruptRecoveryPresentation()
        {
            if (Kind == CharacterRecoveryKind.None)
            {
                return;
            }
            PresentationPhase = RecoveryPresentationPhase.PhysicsTumble;
            GetUpOrientation = null;
            EntryOrientation = null;
            AlignmentTargetRotation = null;
        }

        public void CompleteRecovery()
        {
            Kind = CharacterRecoveryKind.None;
            PresentationPhase = RecoveryPresentationPhase.None;
            GetUpOrientation = null;
            EntryOrientation = null;
            AlignmentTargetRotation = null;
        }

        public void ResetRound()
        {
            CompleteRecovery();
            DownCount = 0U;
        }
    }
}
