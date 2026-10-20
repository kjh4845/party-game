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
    public sealed partial class LimpLiftCandidateTests
    {
        [Serializable] private sealed class Frame {public int tick,grips,throws;public float support,centerHeight,skin,targetSkin,lowestCollider;}
        [Serializable] private sealed class Clip {public string name;public float maximumSkin,maximumTargetSkin,maximumRise;public Frame[] frames;}
        [Serializable] private sealed class CaptureReport {public Clip[] clips;}
        [UnityTest] public IEnumerator CaptureLiftReleaseAndValidateSkin()
        {
            var results=new List<Clip>();var failures=new List<string>();
            foreach(var mode in new[]{"FaceUp","FaceDown","LeftSide","RightSide","LeftHand","RightHand"})
            {
                var orientation=mode.EndsWith("Hand")?LimpGrabTargetReview.Orientation.FaceUp:(LimpGrabTargetReview.Orientation)Enum.Parse(typeof(LimpGrabTargetReview.Orientation),mode);
                Create(orientation);var mask=mode=="LeftHand"?1:mode=="RightHand"?2:3;Buttons(mask);var start=_pair.ExternalTarget.Center.y;
                var folder=Path.Combine(Output,"Frames",mode);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=1f;
                var skins=new[]{_pair.Players[0].Rig.Actor.Pose,_pair.ExternalTarget.Actor.Pose}.Select(p=>p.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var clip=new Clip{name=mode,frames=new Frame[360]};
                var colliders=_pair.ExternalTarget.Bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger).Distinct().ToArray();
                for(var tick=0;tick<360;tick++)
                {
                    if(tick<150)Approach(mask==3?2:1);else _pair.Players[0].Move=Vector2.zero;
                    if(tick==230&&mask==3)Buttons(2);if(tick==270)Buttons(0);
                    Tick();AssertLimp();var angles=new float[2];
                    for(var i=0;i<2;i++){skins[i].BakeMesh(mesh);angles[i]=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _);}
                    clip.maximumSkin=Mathf.Max(clip.maximumSkin,angles[0]);clip.maximumTargetSkin=Mathf.Max(clip.maximumTargetSkin,angles[1]);if(tick<270)clip.maximumRise=Mathf.Max(clip.maximumRise,_pair.ExternalTarget.Center.y-start);
                    clip.frames[tick]=new Frame{tick=tick,grips=_pair.LogicalGrips,throws=_pair.Throws,support=_pair.LiftSupportForce,centerHeight=_pair.ExternalTarget.Center.y,skin=angles[0],targetSkin=angles[1],lowestCollider=colliders.Min(c=>c.bounds.min.y)};
                    _pair.Present(1);yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                results.Add(clip);File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new CaptureReport{clips=results.ToArray()},true));
                if(clip.maximumSkin>=60||clip.maximumTargetSkin>=60)failures.Add($"{mode} skin={clip.maximumSkin}/{clip.maximumTargetSkin}");
                Assert.That(_pair.Throws,Is.EqualTo(1));Assert.That(_pair.LiftSupportForce,Is.Zero);Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips,Is.Zero);
                _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
