using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectHotfix.Presentation
{
    // Authored clips plus visual contact IK. No physics writes or gameplay completion.
    public sealed class GetUpAnimationPose : MonoBehaviour
    {
        public const float Duration = 2.35f, PreparationSeconds = .4f, RiseSeconds = 1.75f;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Vector3> _positions = new Dictionary<string, Vector3>();
        private readonly Quaternion[] _clavicle = new Quaternion[2];
        private readonly Quaternion[] _footWorld = new Quaternion[2];
        private readonly Vector3[] _anchors = new Vector3[2], _entryHands = new Vector3[2];
        private readonly Vector3[] _footLocal = new Vector3[2], _footGoals = new Vector3[2];
        private Quaternion _standingRotation;
        public float MaximumFootPlantError { get; private set; }
        public int FootPlantSamples { get; private set; }
        public float MaximumFootCorrectionWeight { get; private set; }
        private readonly Dictionary<string, Quaternion> _entry = new Dictionary<string, Quaternion>();
        private AnimationClip _faceUp, _faceDown;
        private bool _up;
        private bool _fading;
        private float _fadeAge;
        private float _resumeAge = -1;
        private Vector3 _entryPelvisPosition;
        public int Starts { get; private set; }
        public int FadeSamples { get; private set; }
        public bool Playing { get; private set; }
        public float MaximumPlantError { get; private set; }
        public int PlantedSamples { get; private set; }
        public IEnumerable<Transform> AnimatedBones => _bones.Values.Where(t => t != transform && t.name != "Root" && !t.name.StartsWith("CHR_"));
        // Evaluate AuthorPose live instead of sampling the baked clips (explicit opt-in; a missing clip
        // still throws otherwise).
        public bool Procedural;
        // Short-legged bodies (r43/r46, ~15 cm legs): the feet stay planted where the get-up starts and the legs
        // follow from IK alone, knees toward the body's front. The authored 70-76 deg knee fold crumpled
        // these thick legs and the late, gated foot plant popped them (2026-10-20 review: "무릎이 기괴하게").
        public bool PlantedFeet;
        public void Bind(AnimationClip faceUp, AnimationClip faceDown, Quaternion leftClavicle, Quaternion rightClavicle)
        {
            _faceUp = faceUp; _faceDown = faceDown; _clavicle[0] = leftClavicle; _clavicle[1] = rightClavicle;
            foreach (var bone in GetComponentsInChildren<Transform>()) { _bones[bone.name] = bone; _rest[bone.name] = bone.localRotation; _positions[bone.name] = bone.localPosition; }
            for (var side = 0; side < 2; side++)
            {
                var foot = _bones["Foot_" + (side == 0 ? "L" : "R")];
                _footWorld[side] = Quaternion.Inverse(transform.rotation) * foot.rotation; _footLocal[side] = transform.InverseTransformPoint(foot.position);
            }
        }
        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        public static float RotationProgress(float age)
        {
            if (age < 1.12f) return .78f * Smooth(.4f, 1.12f, age);
            if (age < 1.65f) return .78f + .14f * Smooth(1.12f, 1.65f, age);
            return .92f + .08f * Smooth(1.65f, 2.15f, age);
        }
        public static float WeightTransfer(float age) => Smooth(.55f,1.5f,age);
        // Face-up sit-up (PlantedFeet): the trunk curls up first while the pelvis stays down, then the pelvis rolls
        // forward over the drawn-in feet. Pelvis (physics root) rotation and weight transfer follow these.
        public static float SitUpRotationProgress(float age) => .15f * Smooth(.4f, 1.1f, age) + .75f * Smooth(1.1f, 1.7f, age) + .1f * Smooth(1.7f, 2.15f, age);
        public static float SitUpWeightTransfer(float age) => Smooth(1f, 1.8f, age);
        private static float SitUpTrunkPitch(float age) => 60f * Smooth(.35f, .95f, age) + 20f * Smooth(.95f, 1.7f, age) + 10f * Smooth(1.7f, 2.15f, age);
        private static float Envelope(float age, int side) => Smooth(side == 0 ? .03f : .13f, side == 0 ? .35f : .45f, age) * (1 - Smooth(side == 0 ? .95f : .85f, 1.65f, age));
        public Vector3 AuthoredHandTarget(int side, bool faceUp)
        {
            // Two direction-specific braces: hands behind the hips when supine,
            // under the chest when prone. These positions are clip authoring inputs.
            return transform.TransformPoint(AuthoredHandLocalTarget(side,faceUp));
        }
        public static Vector3 AuthoredHandLocalTarget(int side,bool faceUp) => new Vector3(side==0?-.23f:.23f,faceUp?.38f:.43f,faceUp?-.13f:.13f);
        public Vector3 Terminal(int side) => _bones["HandLogical_" + (side == 0 ? "L" : "R")].TransformPoint(Vector3.back * .06f);
        public void AuthorPose(bool faceUp, float age)
        {
            foreach (var name in _bones.Keys) { _bones[name].localPosition = _positions[name]; _bones[name].localRotation = _rest[name]; }
            var curl = Smooth(.05f, .5f, age) * (1 - Smooth(1.2f, 2.15f, age));
            var load = Smooth(.45f,1.1f,age) * (1-Smooth(1.4f,2.15f,age));
            var hipHinge = 12f * load;
            _bones["Pelvis"].localRotation = _rest["Pelvis"] * Quaternion.AngleAxis(hipHinge, Vector3.right);
            // The pelvis leads the loaded posture; the spine finishes its
            // direction-specific preparation before the legs push to standing.
            var torso = (faceUp ? 12f : -10f) * curl * (1-Smooth(.7f,1.2f,age)) + 3f * load;
            _bones["Spine"].localRotation = _rest["Spine"] * Quaternion.AngleAxis(torso, Vector3.right);
            _bones["Chest"].localRotation = _rest["Chest"] * Quaternion.AngleAxis((faceUp ? 4f : -4f) * curl, Vector3.right);
            _bones["Head"].localRotation = _rest["Head"] * Quaternion.AngleAxis((faceUp ? -4f : 4f) * curl, Vector3.right);
            if (PlantedFeet && faceUp)
            {
                // The trunk's own curl ahead of the pelvis: lying 0 deg, upright 90 deg.
                var flex = Mathf.Max(0, SitUpTrunkPitch(age) - 90f * SitUpRotationProgress(age));
                _bones["Spine"].localRotation = _rest["Spine"] * Quaternion.AngleAxis(flex * .55f, Vector3.right);
                _bones["Chest"].localRotation = _rest["Chest"] * Quaternion.AngleAxis(flex * .45f, Vector3.right);
                _bones["Head"].localRotation = _rest["Head"] * Quaternion.AngleAxis(-flex * .2f, Vector3.right);
            }
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R";
                var leg = PlantedFeet ? 0 : Smooth(side == 0 ? .25f : .4f,side == 0 ? .9f : 1.05f,age) * (1-Smooth(side == 0 ? 1.3f : 1.45f,2.15f,age));
                var knee = (side == 0 ? 76f : 68f) * leg;
                var thigh = -knee * Mathf.Lerp(.62f,.5f,Smooth(.85f,1.4f,age)) - hipHinge;
                _bones["Thigh_" + suffix].localRotation = _rest["Thigh_" + suffix] * Quaternion.AngleAxis(thigh, Vector3.right);
                _bones["Calf_" + suffix].localRotation = _rest["Calf_" + suffix] * Quaternion.AngleAxis(knee, Vector3.right);
                _bones["HipDeform_" + suffix].localRotation = _rest["HipDeform_" + suffix] * Quaternion.AngleAxis(thigh * .25f, Vector3.right);
                _bones["Foot_" + suffix].rotation = transform.rotation * _footWorld[side];
            }
            // Derive the low/back pelvis position from both bent legs, then
            // bring it over the feet before extension. This preserves their
            // mean support position without inventing gameplay root motion.
            var supportDelta = Vector3.zero;
            for (var side=0;side<2;side++) supportDelta += (_bones["Foot_"+(side==0 ? "L" : "R")].position-transform.TransformPoint(_footLocal[side]))*.5f;
            _bones["Pelvis"].position -= supportDelta;
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R";
                var brace = Envelope(age,side);
                var clav = _bones["Clavicle_" + suffix];
                clav.localRotation = Quaternion.SlerpUnclamped(_rest[clav.name], _clavicle[side], (faceUp ? -9f : 9f) / 7f * brace);
                var upper = _bones["UpperArm_" + suffix]; var forearm = _bones["Forearm_" + suffix]; var hand = _bones["HandLogical_" + suffix];
                var u = upper.localRotation; var f = forearm.localRotation; var h = hand.localRotation;
                SolveArm(side, AuthoredHandTarget(side, faceUp));
                upper.localRotation = Quaternion.Slerp(u, upper.localRotation, brace);
                forearm.localRotation = Quaternion.Slerp(f, forearm.localRotation, brace);
                hand.localRotation = Quaternion.Slerp(h, hand.localRotation, brace);
            }
        }
        public void Begin(bool faceUp, Vector3 leftAnchor, Vector3 rightAnchor, Vector3 standingRoot, Quaternion standingRotation)
        {
            if (!Procedural && (_faceUp == null || _faceDown == null)) throw new InvalidOperationException("Generate the two GetUp clips before playing recovery.");
            _standingRotation = standingRotation; for (var side=0;side<2;side++) _footGoals[side] = standingRoot + standingRotation * _footLocal[side];
            _up = faceUp; _anchors[0] = leftAnchor; _anchors[1] = rightAnchor; _entry.Clear();
            for (var side = 0; side < 2; side++) _entryHands[side] = Terminal(side);
            foreach (var bone in AnimatedBones) _entry[bone.name] = bone.localRotation;
            _entryPelvisPosition = _bones["Pelvis"].localPosition;
            Playing = true; _fading = false; _resumeAge=-1; Starts++;
        }
        public void ReblendFromCurrentPose()
        {
            if(!Playing)return;
            foreach(var bone in AnimatedBones)_entry[bone.name]=bone.localRotation;
            _entryPelvisPosition=_bones["Pelvis"].localPosition;_resumeAge=0;
        }
        public void Apply(float age, float deltaTime)
        {
            if (!Playing)
            {
                if (!_fading) return;
                _fadeAge += deltaTime; var blend = Smooth(0,.18f,_fadeAge);
                foreach (var bone in AnimatedBones) bone.localRotation = Quaternion.Slerp(_entry[bone.name],bone.localRotation,blend);
                _bones["Pelvis"].localPosition = Vector3.Lerp(_entryPelvisPosition,_bones["Pelvis"].localPosition,blend);
                FadeSamples++; if (blend >= 1) _fading = false; return;
            }
            // Without a clip the same authored motion is evaluated live on this rig (its own rest pose
            // and bone lengths), e.g. for the r43 body whose bind differs from the clips' r20 rig.
            var clip = _up ? _faceUp : _faceDown;
            if (clip != null) clip.SampleAnimation(gameObject, Mathf.Clamp(age, 0, Duration));
            else AuthorPose(_up, Mathf.Clamp(age, 0, Duration));
            var resumeWeight=1f;
            if(_resumeAge>=0) { _resumeAge+=deltaTime;resumeWeight=Smooth(0,.18f,_resumeAge);if(resumeWeight>=1)_resumeAge=-1; }
            var entryWeight = Smooth(0, .35f, age)*resumeWeight;
            foreach (var bone in AnimatedBones) bone.localRotation = Quaternion.Slerp(_entry[bone.name], bone.localRotation, entryWeight);
            _bones["Pelvis"].localPosition = Vector3.Lerp(_entryPelvisPosition,_bones["Pelvis"].localPosition,entryWeight);
            // Planted: each hand slides from where it lay to where it will push, instead of the arm's joint angles
            // blending from the limp pose, which swung the arm over the back on the way (2026-10-20 review).
            if (PlantedFeet && age < .36f)
                for (var side = 0; side < 2; side++)
                {
                    var suffix = side == 0 ? "L" : "R"; var upper = _bones["UpperArm_" + suffix]; var forearm = _bones["Forearm_" + suffix];
                    var u = upper.localRotation; var f = forearm.localRotation;
                    SolveArm(side, Vector3.Lerp(_entryHands[side], _anchors[side], Smooth(0, .36f, age)));
                    upper.localRotation = Quaternion.Slerp(u, upper.localRotation, resumeWeight); forearm.localRotation = Quaternion.Slerp(f, forearm.localRotation, resumeWeight);
                }
            for (var side = 0; side < 2; side++)
            {
                var suffix = side == 0 ? "L" : "R"; var upper = _bones["UpperArm_" + suffix]; var forearm = _bones["Forearm_" + suffix];
                var reach = Vector3.Distance(upper.position, forearm.position) + Vector3.Distance(forearm.position, _bones["HandLogical_" + suffix].position) + .06f;
                var weight = Smooth(side == 0 ? .12f : .22f, side == 0 ? .36f : .46f, age) * (1-Smooth(side == 0 ? .95f : .85f,1.3f,age));
                var distanceToAnchor = Vector3.Distance(upper.position, _anchors[side]);
                var contactWeight = weight * Smooth(.18f,.21f,distanceToAnchor) * (1 - Smooth(reach - .05f, reach - .005f, distanceToAnchor))*resumeWeight;
                var u = upper.localRotation; var f = forearm.localRotation;
                SolveArm(side, _anchors[side]);
                upper.localRotation = Quaternion.Slerp(u, upper.localRotation, contactWeight); forearm.localRotation = Quaternion.Slerp(f, forearm.localRotation, contactWeight);
                if (contactWeight > .999f) { MaximumPlantError = Mathf.Max(MaximumPlantError, Vector3.Distance(Terminal(side), _anchors[side])); PlantedSamples++; }
            }
            for (var side=0;side<2;side++) PlantFoot(side,age,resumeWeight);
        }
        private void PlantFoot(int side, float age, float resumeWeight)
        {
            var suffix=side==0 ? "L" : "R"; var thigh=_bones["Thigh_"+suffix]; var calf=_bones["Calf_"+suffix]; var foot=_bones["Foot_"+suffix];
            var a=Vector3.Distance(thigh.position,calf.position); var b=Vector3.Distance(calf.position,foot.position);
            var delta=_footGoals[side]-thigh.position; var distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.0001f,a+b-.0001f);
            var weight=(PlantedFeet ? Smooth(0f,.3f,age) : Smooth(.8f,1.15f,age)*(1-Smooth(a+b-.015f,a+b-.001f,delta.magnitude)))*(1-Smooth(2.15f,Duration,age))*resumeWeight;
            // Planted: the knee points the way the body faces (down onto the floor when prone, up when supine, forward
            // standing); the standing forward alone is degenerate for a supine body whose feet lie ahead.
            var pole=PlantedFeet ? transform.forward+_standingRotation*Vector3.forward*.3f : _standingRotation*Vector3.forward;
            var direction=delta.normalized; var bend=Vector3.ProjectOnPlane(pole,direction).normalized;
            var along=(a*a+distance*distance-b*b)/(2*distance); var knee=thigh.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            var t=thigh.localRotation; var c=calf.localRotation; var f=foot.rotation;
            thigh.rotation=Quaternion.FromToRotation(calf.position-thigh.position,knee-thigh.position)*thigh.rotation;
            calf.rotation=Quaternion.FromToRotation(foot.position-calf.position,thigh.position+direction*distance-calf.position)*calf.rotation;
            var correction=Mathf.Max(Quaternion.Angle(t,thigh.localRotation),Quaternion.Angle(c,calf.localRotation));
            if (!PlantedFeet) weight *= 1-Smooth(8,18,correction);
            MaximumFootCorrectionWeight=Mathf.Max(MaximumFootCorrectionWeight,weight);
            thigh.localRotation=Quaternion.Slerp(t,thigh.localRotation,weight); calf.localRotation=Quaternion.Slerp(c,calf.localRotation,weight);
            foot.rotation=Quaternion.Slerp(f,_standingRotation*_footWorld[side],weight);
            var localDelta=Quaternion.Inverse(_rest[thigh.name])*thigh.localRotation;
            _bones["HipDeform_"+suffix].localRotation=_rest["HipDeform_"+suffix]*Quaternion.Slerp(Quaternion.identity,localDelta,.25f);
            if(weight>.999f) { MaximumFootPlantError=Mathf.Max(MaximumFootPlantError,Vector3.Distance(foot.position,_footGoals[side])); FootPlantSamples++; }
        }
        public void Cancel(bool immediate = false)
        {
            if (Playing && !immediate) { _entry.Clear(); foreach (var bone in AnimatedBones) _entry[bone.name] = bone.localRotation; _entryPelvisPosition = _bones["Pelvis"].localPosition; _fading = true; _fadeAge = 0; }
            if (immediate) _fading = false;
            _resumeAge=-1;
            Playing = false;
        }
        private void SolveArm(int side, Vector3 target)
        {
            var suffix = side == 0 ? "L" : "R"; var upper = _bones["UpperArm_" + suffix]; var forearm = _bones["Forearm_" + suffix]; var hand = _bones["HandLogical_" + suffix];
            var a = Vector3.Distance(upper.position, forearm.position); var b = Vector3.Distance(forearm.position, hand.position) + .06f;
            var delta = target - upper.position; var distance = Mathf.Clamp(delta.magnitude, Mathf.Max(.20f,Mathf.Abs(a-b)+.0001f), a+b-.0001f); var direction = delta.normalized;
            var pole = -transform.up + transform.right * (side == 0 ? -.5f : .5f);
            var bend = Vector3.ProjectOnPlane(pole, direction).normalized; if (bend.sqrMagnitude < .01f) bend = transform.right;
            var along = (a*a+distance*distance-b*b)/(2*distance); var elbow = upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation = Quaternion.FromToRotation(forearm.position-upper.position, elbow-upper.position) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(Terminal(side)-forearm.position, upper.position+direction*distance-forearm.position) * forearm.rotation;
        }
    }
}
