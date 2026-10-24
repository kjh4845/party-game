using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectHotfix.Presentation;
using UnityEditor;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump.Editor
{
    public static class GetUpClipAuthoring
    {
        public const string Folder = "Assets/ProjectHotfix/Prototypes/PhysicsJump/Resources/ReviewGetUp";
        [MenuItem("Project Hotfix/Prototypes/Generate GetUp Animation Candidates")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var actor = PhysicsJumpReview.Create(Vector3.zero); actor.ReadKeyboard = false;
            var author = actor.Pose.gameObject.AddComponent<GetUpAnimationPose>();
            author.Bind(null, null, actor.PunchLibrary.clips[0].Sample(7f/30, 0), actor.PunchLibrary.clips[1].Sample(7f/30, 0));
            foreach (var faceUp in new[] { true, false })
            {
                var path = Folder + (faceUp ? "/GetUp_FaceUp.anim" : "/GetUp_FaceDown.anim");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.name = faceUp ? "GetUp_FaceUp" : "GetUp_FaceDown"; clip.legacy = true; clip.frameRate = 30; clip.wrapMode = WrapMode.ClampForever;
                var curves = new Dictionary<string, AnimationCurve[]>();
                foreach (var bone in author.AnimatedBones) curves[AnimationUtility.CalculateTransformPath(bone, author.transform)] = Enumerable.Range(0,4).Select(_ => new AnimationCurve()).ToArray();
                var position = Enumerable.Range(0,3).Select(_ => new AnimationCurve()).ToArray();
                for (var frame = 0; frame <= 71; frame++)
                {
                    var age = Mathf.Min(frame / 30f, GetUpAnimationPose.Duration); author.AuthorPose(faceUp, age);
                    var pelvisPosition = author.AnimatedBones.Single(t=>t.name=="Pelvis").localPosition;
                    for (var axis=0;axis<3;axis++) position[axis].AddKey(new Keyframe(age,pelvisPosition[axis]));
                    foreach (var bone in author.AnimatedBones)
                    {
                        var keys = curves[AnimationUtility.CalculateTransformPath(bone, author.transform)]; var q = bone.localRotation;
                        if (frame > 0 && keys[0].keys[frame-1].value*q.x+keys[1].keys[frame-1].value*q.y+keys[2].keys[frame-1].value*q.z+keys[3].keys[frame-1].value*q.w < 0) q = new Quaternion(-q.x,-q.y,-q.z,-q.w);
                        for (var axis=0;axis<4;axis++) keys[axis].AddKey(new Keyframe(age,q[axis]));
                    }
                }
                foreach (var pair in curves) for (var axis=0;axis<4;axis++)
                {
                    for (var i=0;i<pair.Value[axis].length;i++) { AnimationUtility.SetKeyLeftTangentMode(pair.Value[axis],i,AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(pair.Value[axis],i,AnimationUtility.TangentMode.Linear); }
                    clip.SetCurve(pair.Key,typeof(Transform),"localRotation."+"xyzw"[axis],pair.Value[axis]);
                }
                for (var axis=0;axis<3;axis++)
                {
                    for (var i=0;i<position[axis].length;i++) { AnimationUtility.SetKeyLeftTangentMode(position[axis],i,AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(position[axis],i,AnimationUtility.TangentMode.Linear); }
                    clip.SetCurve("Root/Pelvis",typeof(Transform),"localPosition."+"xyz"[axis],position[axis]);
                }
                clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip);
            }
            Object.DestroyImmediate(actor.gameObject); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("GetUp candidates authored: two clips, 2.35 seconds each, bone rotations and visual pelvis transfer.");
        }
    }
}
