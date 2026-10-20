using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class PunchMotionReferenceTests
    {
        private Scene _scene;private PunchMotionReferenceReview _review;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/punch-reference-20260918"));
        [Serializable] private sealed class PoseFrame {public float time;public string phase;public Vector3 tip;public Vector3[] localPositions;public Quaternion[] localRotations;}
        [Serializable] private sealed class PoseClip {public int side;public PoseFrame[] frames;}
        [Serializable] private sealed class PoseLibrary {public int version=1,sampleRate=120;public bool referenceOnly=true;public float duration=.4f;public string[] bones;public PoseClip[] clips;}
        [Serializable] private sealed class CaptureFrame {public int frame,side;public float age,footError,surface;public string phase;public Vector3 tip;}
        [Serializable] private sealed class CaptureReport {public bool referenceOnly=true;public int fps=60;public CaptureFrame[] frames;}
        private void Create()
        {
            Directory.CreateDirectory(Output);_scene=SceneManager.CreateScene("Punch reference - no physics",new CreateSceneParameters(LocalPhysicsMode.None));
            _review=PunchMotionReferenceReview.Create(_scene);_review.ManualPlayback=true;_review.View.enabled=false;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}}
        private static string Mirror(string name)=>name.EndsWith("_L")?name.Substring(0,name.Length-2)+"_R":name.EndsWith("_R")?name.Substring(0,name.Length-2)+"_L":name;
        [UnityTest] public IEnumerator ReferenceIsIsolatedMirroredAndFeetStayPlanted()
        {
            Create();var bones=_review.Bones;var neutral=bones.Select(b=>b.localRotation).ToArray();var lengths=bones.Select(b=>b.localPosition.magnitude).ToArray();
            var maxMirror=0f;var maxFoot=0f;var maxSurface=0f;var minSurface=float.PositiveInfinity;var maxLength=0f;var maxStep=0f;
            var skins=_review.Skins;var body=skins.Single(s=>s.name.EndsWith("SkinnedBody"));var triangles=body.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var mesh=new Mesh();var frames=new List<PoseClip>();
            var left=new List<Vector3[]>();
            foreach(var side in new[]{0,1})
            {
                var clip=new PoseClip{side=side,frames=new PoseFrame[49]};var previous=_review.Tip(side);
                for(var i=0;i<=48;i++)
                {
                    var t=i/120f;_review.Evaluate(side,t);var positions=bones.Select(b=>b.position).ToArray();var tip=_review.Tip(side);if(i>0)maxStep=Mathf.Max(maxStep,Vector3.Distance(previous,tip));previous=tip;
                    if(side==0)left.Add(positions);else for(var n=0;n<bones.Length;n++)
                    {var index=Array.FindIndex(bones,b=>b.name==Mirror(bones[n].name));var mirrored=left[i][index];mirrored.x=-mirrored.x;maxMirror=Mathf.Max(maxMirror,Vector3.Distance(mirrored,positions[n]));}
                    foreach(var skin in skins){skin.BakeMesh(mesh);foreach(var v in mesh.vertices)minSurface=Mathf.Min(minSurface,skin.transform.TransformPoint(v).y);}
                    body.BakeMesh(mesh);maxSurface=Mathf.Max(maxSurface,CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _));maxFoot=Mathf.Max(maxFoot,_review.FootError);
                    for(var n=0;n<bones.Length;n++)if(bones[n].name.StartsWith("UpperArm")||bones[n].name.StartsWith("Forearm")||bones[n].name.StartsWith("HandLogical"))maxLength=Mathf.Max(maxLength,Mathf.Abs(bones[n].localPosition.magnitude-lengths[n]));
                    clip.frames[i]=new PoseFrame{time=t,phase=PunchMotionReferenceReview.Phase(t),tip=tip,localPositions=bones.Select(b=>b.localPosition).ToArray(),localRotations=bones.Select(b=>b.localRotation).ToArray()};
                }
                frames.Add(clip);
            }
            _review.Evaluate(0,-1);var restDelta=bones.Select((b,i)=>Quaternion.Angle(b.localRotation,neutral[i])).Max();
            _review.Evaluate(0,.13f);var first=bones.Select(b=>b.position).ToArray();_review.Evaluate(1,.27f);_review.Evaluate(0,.13f);
            var repeat=bones.Select((b,i)=>Vector3.Distance(b.position,first[i])).Max();
            var objects=_scene.GetRootGameObjects();var rigidbodies=objects.Sum(o=>o.GetComponentsInChildren<Rigidbody>(true).Length);var colliders=objects.Sum(o=>o.GetComponentsInChildren<Collider>(true).Length);
            File.WriteAllText(Path.Combine(Output,"Quality.txt"),$"mirror={maxMirror:R}\nfootTargetError={maxFoot:R}\narmLengthChange={maxLength:R}\nrestRotationDelta={restDelta:R}\nrepeatPositionDelta={repeat:R}\nmaxSurfaceAngle={maxSurface:R}\nfloorSkinDelta={minSurface-_review.FloorHeight:R}\nmaxTipStep120Hz={maxStep:R}\nrigidbodies={rigidbodies}\ncolliders={colliders}\n");
            File.WriteAllText(Path.Combine(Output,"ReferencePose.json"),JsonUtility.ToJson(new PoseLibrary{bones=bones.Select(b=>b.name).ToArray(),clips=frames.ToArray()},true));
            Assert.That(rigidbodies+colliders,Is.Zero);Assert.That(maxMirror,Is.LessThan(.0003f));Assert.That(maxFoot,Is.LessThan(.001f));Assert.That(maxLength,Is.LessThan(.00001f));Assert.That(restDelta,Is.LessThan(.06f));Assert.That(repeat,Is.LessThan(.000001f));Assert.That(maxSurface,Is.LessThan(60));Assert.That(minSurface-_review.FloorHeight,Is.GreaterThan(-.004f));
            UnityEngine.Object.Destroy(mesh);yield return null;
        }
        [UnityTest,Timeout(600000)] public IEnumerator CaptureReferenceFrontAndSide()
        {
            Create();var camera=_review.View;var rt=new RenderTexture(700,700,24);rt.Create();camera.targetTexture=rt;camera.aspect=1;var image=new Texture2D(700,700,TextureFormat.RGB24,false);var rows=new List<CaptureFrame>();
            foreach(var side in new[]{0,1})foreach(var view in new[]{PunchMotionReferenceReview.ViewAngle.Front,PunchMotionReferenceReview.ViewAngle.Side})Directory.CreateDirectory(Path.Combine(Output,"Frames",side.ToString(),view.ToString()));
            for(var i=0;i<180;i++)
            {
                var side=i<90?0:1;var t=(i%90-20)/60f;_review.Evaluate(side,t);rows.Add(new CaptureFrame{frame=i,side=side,age=t,phase=PunchMotionReferenceReview.Phase(t),footError=_review.FootError,tip=_review.Tip(side)});
                yield return null;
                foreach(var view in new[]{PunchMotionReferenceReview.ViewAngle.Front,PunchMotionReferenceReview.ViewAngle.Side})
                {
                    _review.SetView(view,side);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,700,700),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(Output,"Frames",side.ToString(),view.ToString(),$"Frame_{i%90:D4}.png"),image.EncodeToPNG());
                }
            }
            File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new CaptureReport{frames=rows.ToArray()},true));camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator SharedCharacterFactoryRetainsItsPhysicsPath()
        {
            _scene=SceneManager.CreateScene("Shared factory regression",new CreateSceneParameters(LocalPhysicsMode.Physics3D));var actor=PhysicsJumpReview.Create(Vector3.zero,false,PunchMotionReferenceReview.SkinPath);SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);actor.ReadKeyboard=false;actor.Motor.ManualStep=true;
            Assert.That(actor.Motor.Body.mass,Is.EqualTo(20));Assert.That(actor.GetComponent<CapsuleCollider>(),Is.Not.Null);Assert.That(actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>()[0].bones.Length,Is.EqualTo(24));Assert.That(actor.GetComponent<PunchMotionReferenceReview>(),Is.Null);yield return null;
        }
    }
}
