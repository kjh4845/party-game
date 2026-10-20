using System;
using UnityEngine;
using ProjectHotfix.Contracts;

namespace ProjectHotfix.Presentation
{
    [Serializable] public sealed class PunchPoseFrame
    {
        public float time;
        public Quaternion[] rotations;
        public Vector3[] heads, tails;
    }

    [Serializable] public sealed class PunchPoseClip
    {
        public string side;
        public float duration;
        public string[] bones;
        public PunchPoseFrame[] frames;

        public Quaternion Sample(float time, int bone)
        {
            var scaled = Mathf.Clamp01(time / duration) * (frames.Length - 1);
            var first = Mathf.Min(Mathf.FloorToInt(scaled), frames.Length - 2);
            return Quaternion.Slerp(frames[first].rotations[bone], frames[first + 1].rotations[bone], scaled - first);
        }
    }

    [Serializable] public sealed class PunchPoseLibrary
    {
        public int version, sampleRate;
        public string sourceSha256;
        public PunchPoseClip[] clips;
    }

    // Review presentation only: no input arbitration, hit, force or root writes.
    // The four-bone hand mask is sampled from the user-approved Blender Action.
    public sealed class ApprovedPunchPose : MonoBehaviour
    {
        private PunchPoseLibrary _library;
        private readonly Transform[][] _bones = { new Transform[4], new Transform[4] };
        private readonly Quaternion[][] _idle = { new Quaternion[4], new Quaternion[4] };
        private readonly Quaternion[][] _entry = { new Quaternion[4], new Quaternion[4] };
        private readonly float[] _time = new float[2];
        private readonly bool[] _active = new bool[2];
        private float _playbackRate = 1f;
        private uint _resetVersion;
        public bool IsActive(int side) => _active[side];
        public float PlaybackTime(int side) => _time[side];

        public void Bind(PunchPoseLibrary library)
        {
            if (library == null || library.version != 1 || library.clips == null || library.clips.Length != 2)
                throw new ArgumentException("Expected the approved two-hand punch library.");
            _library = library;
            var transforms = GetComponentsInChildren<Transform>();
            for (var side = 0; side < 2; side++)
            {
                var clip = library.clips[side];
                if (clip.side != (side == 0 ? "L" : "R") || clip.bones.Length != 4
                    || clip.frames.Length != 85 || Mathf.Abs(clip.duration - .7f) > .000001f)
                    throw new ArgumentException("Unexpected approved punch timing or mask.");
                for (var bone = 0; bone < 4; bone++)
                {
                    _bones[side][bone] = Array.Find(transforms, t => t.name == clip.bones[bone]);
                    if (_bones[side][bone] == null) throw new ArgumentException("Missing punch bone: " + clip.bones[bone]);
                    _idle[side][bone] = _bones[side][bone].localRotation;
                }
                _active[side] = false; _time[side] = 0f;
            }
        }

        public bool TryStart(int side, bool handAvailable)
        {
            if (side < 0 || side > 1 || _library == null || !handAvailable || _active[side]) return false;
            for (var bone = 0; bone < 4; bone++) _entry[side][bone] = _bones[side][bone].localRotation;
            _time[side] = 0f; _active[side] = true;
            return true;
        }

        public void ApplyAuthority(HandActionPresentationState state, bool leftAvailable, bool rightAvailable)
        {
            _playbackRate = state.PunchPlaybackRate > 0 ? state.PunchPlaybackRate : 1f;
            if (state.ResetVersion != _resetVersion) Apply(0f, state.ResetVersion, leftAvailable, rightAvailable);
            for (var side = 0; side < 2; side++)
            {
                var available = side == 0 ? leftAvailable : rightAvailable;
                var active = side == 0 ? state.LeftPunch : state.RightPunch;
                if (!active && _active[side])
                { if (available) RestoreIdle(side); _active[side] = false; }
                if (active && available)
                {
                    if (!_active[side]) TryStart(side, true);
                    _time[side] = side == 0 ? state.LeftAge : state.RightAge;
                }
            }
            Apply(0f, state.ResetVersion, leftAvailable, rightAvailable);
        }

        public void Apply(float deltaTime, uint resetVersion, bool leftAvailable, bool rightAvailable)
        {
            var reset = resetVersion != _resetVersion; _resetVersion = resetVersion;
            for (var side = 0; side < 2; side++)
            {
                var available = side == 0 ? leftAvailable : rightAvailable;
                if (!available) { _active[side] = false; _time[side] = 0f; continue; }
                if (reset)
                {
                    if (_active[side]) RestoreIdle(side);
                    _active[side] = false; _time[side] = 0f;
                    continue;
                }
                if (!_active[side]) continue;
                var clip = _library.clips[side];
                var duration = clip.duration / _playbackRate;
                _time[side] = Mathf.Min(duration, _time[side] + Mathf.Max(0f, deltaTime));
                if (_time[side] >= duration)
                {
                    RestoreIdle(side); _active[side] = false;
                    continue;
                }
                // Authority and presentation share one rate; source poses stay unchanged.
                var sourceTime = _time[side] * _playbackRate;
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sourceTime / .04f));
                for (var bone = 0; bone < 4; bone++)
                    _bones[side][bone].localRotation = Quaternion.Slerp(_entry[side][bone], clip.Sample(sourceTime, bone), blend);
            }
        }

        private void RestoreIdle(int side)
        {
            for (var bone = 0; bone < 4; bone++) _bones[side][bone].localRotation = _idle[side][bone];
        }
    }
}
