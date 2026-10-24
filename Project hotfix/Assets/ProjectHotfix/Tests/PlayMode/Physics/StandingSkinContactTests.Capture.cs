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
    public sealed partial class StandingSkinContactTests
    {
        [Serializable] private sealed class Frame {public int tick,a,b,physical,throws;public bool mutual;public float skin,stepMs;}
        [Serializable] private sealed class Clip {public string name;public bool candidate,lift;public float maxSkin;public int maxGrips;public Frame[] frames;}
        [Serializable] private sealed class VideoReport {public Clip[] clips;}
        [UnityTest, Timeout(600000)] public IEnumerator CaptureStandingComparisonAndCheckSkin()
        {
            var results=new List<Clip>();var failures=new List<string>();
            foreach(var mode in new[]{"LeftHand","RightHand","TwoHands","MutualOne","MutualBoth","MutualLift","Carry"})foreach(var candidate in new[]{false,true})
            {
                var lift=mode=="MutualLift"||mode=="Carry";Create(candidate,lift);var name=mode+(candidate?"_After":"_Before");var folder=Path.Combine(Output,"Frames",name);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.76f;
                var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var clip=new Clip{name=name,candidate=candidate,lift=lift,frames=new Frame[300]};
                for(var tick=0;tick<300;tick++)
                {
                    Input(mode,tick);Tick();var angle=0f;foreach(var skin in skins){skin.BakeMesh(mesh);angle=Mathf.Max(angle,CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _));}
                    clip.maxSkin=Mathf.Max(clip.maxSkin,angle);clip.maxGrips=Math.Max(clip.maxGrips,_pair.LogicalGrips);
                    clip.frames[tick]=new Frame{tick=tick,a=_pair.Players[0].Hands.Count(h=>h.Connection!=null),b=_pair.Players[1].Hands.Count(h=>h.Connection!=null),physical=_pair.PhysicalConnections,throws=_pair.Throws,mutual=_pair.Players[0].Mutual,skin=angle,stepMs=_pair.StandingContacts?.LastStepMilliseconds??0};
                    if(tick==110||tick==200)ExportContact(name,tick);
                    _pair.Present(1);yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                results.Add(clip);File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new VideoReport{clips=results.ToArray()},true));
                var expected=mode=="LeftHand"||mode=="RightHand"?1:mode=="TwoHands"||mode=="MutualOne"||mode=="Carry"?2:4;
                if(clip.maxGrips<expected)failures.Add(name+" missing contacts");if(clip.maxSkin>=60)failures.Add(name+" skin "+clip.maxSkin);
                if(_pair.Throws!=(mode=="Carry"?1:mode=="MutualLift"?2:0))failures.Add(name+" throw "+_pair.Throws);
                Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
