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
    public sealed partial class SkinContactCandidateTests
    {
        [Serializable] private sealed class Frame {public int tick,grips,throws;public float skin,targetSkin,stepMs;}
        [Serializable] private sealed class Clip {public string name;public bool contacts;public float maxSkin,maxTargetSkin;public Frame[] frames;}
        [Serializable] private sealed class CaptureReport {public Clip[] clips;}
        [UnityTest, Timeout(600000)] public IEnumerator CaptureSkinContactComparison()
        {
            var results=new List<Clip>();var failures=new List<string>();
            foreach(var mode in new[]{"FaceUp","FaceDown","LeftSide","RightSide","LeftHand","RightHand","Drag"})foreach(var candidate in new[]{false,true})
            {
                var orientation=mode.EndsWith("Hand")||mode=="Drag"?LimpGrabTargetReview.Orientation.FaceUp:(LimpGrabTargetReview.Orientation)Enum.Parse(typeof(LimpGrabTargetReview.Orientation),mode);
                Create(orientation,contacts:candidate);_pair.EnableLiftThrow=mode!="Drag";var mask=mode=="LeftHand"?1:mode=="RightHand"?2:3;Buttons(mask);
                var name=mode+(candidate?"_After":"_Before");var folder=Path.Combine(Output,"Frames",name);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.85f;
                var skins=new[]{_pair.Players[0].Rig.Actor.Pose,_pair.ExternalTarget.Actor.Pose}.Select(p=>p.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var clip=new Clip{name=name,contacts=candidate,frames=new Frame[360]};
                for(var tick=0;tick<360;tick++)
                {
                    if(tick<150)Approach(mask==3?2:1);else _pair.Players[0].Move=mode=="Drag"&&tick<240?Vector2.down*.35f:Vector2.zero;
                    if(tick==230&&mask==3&&mode!="Drag")Buttons(2);if(tick==270)Buttons(0);
                    Tick();AssertLimp();var angles=new float[2];for(var i=0;i<2;i++){skins[i].BakeMesh(mesh);angles[i]=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _);}
                    clip.maxSkin=Mathf.Max(clip.maxSkin,angles[0]);clip.maxTargetSkin=Mathf.Max(clip.maxTargetSkin,angles[1]);clip.frames[tick]=new Frame{tick=tick,grips=_pair.LogicalGrips,throws=_pair.Throws,skin=angles[0],targetSkin=angles[1],stepMs=_pair.SkinContacts?.LastStepMilliseconds??0};
                    if(tick==140||tick==220)ExportContact("Contact_"+name+"_"+tick);
                    _pair.Present(1);yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                results.Add(clip);File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new CaptureReport{clips=results.ToArray()},true));
                if(clip.maxSkin>=60||clip.maxTargetSkin>=60)failures.Add($"{name} skin={clip.maxSkin}/{clip.maxTargetSkin}");
                if(_pair.Throws!=(mode=="Drag"?0:1))failures.Add(name+" throw "+_pair.Throws);
                Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
