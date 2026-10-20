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
    public sealed partial class ShoulderReachCandidateTests
    {
        [Serializable] private sealed class MeshHeader { public string name;public int vertices,triangleIndices; }
        [Serializable] private sealed class TransportHeader { public MeshHeader[] meshes; }
        [Serializable] private sealed class VideoFrame {public int tick,grips;public float skin,otherSkin,low,force;}
        [Serializable] private sealed class VideoCase {public string name;public bool low;public float skin,otherSkin;public VideoFrame[] frames;}
        [Serializable] private sealed class VideoReport {public VideoCase[] cases;}
        private BoneWeight[] BaselineWeights()
        {
            using(var reader=new BinaryReader(File.OpenRead(PhysicsJumpReview.LatWeightTransportPath)))
            {
                var header=JsonUtility.FromJson<TransportHeader>(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())));var count=header.meshes[0].vertices;
                reader.BaseStream.Position+=count*24;var weights=new BoneWeight[count];
                for(var i=0;i<count;i++)weights[i]=new BoneWeight{boneIndex0=reader.ReadInt32(),boneIndex1=reader.ReadInt32(),boneIndex2=reader.ReadInt32(),boneIndex3=reader.ReadInt32()};
                for(var i=0;i<count;i++){weights[i].weight0=reader.ReadSingle();weights[i].weight1=reader.ReadSingle();weights[i].weight2=reader.ReadSingle();weights[i].weight3=reader.ReadSingle();}return weights;
            }
        }
        private void SaveFrame(RenderTexture rt,Texture2D image,string folder,int tick)
        {
            _pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=old;
            File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
        }
        [UnityTest] public IEnumerator CaptureRepairedShoulderComparison()
        {
            var results=new List<VideoCase>();var originalWeights=BaselineWeights();
            foreach(var mode in new[]{"ReachHeight","OneHand","TwoHands","Mutual"})
            {
                Create(true,mode=="ReachHeight"?1.2f:.5f);foreach(var p in _pair.Players)p.Rig.MatchHandSkinEnvelope();for(var i=0;i<60;i++)Tick();
                Buttons(0,mode=="OneHand"?2:3);if(mode=="Mutual")Buttons(1,3);
                var folder=Path.Combine(Output,"Frames",mode);var before=Path.Combine(folder,"Before");var after=Path.Combine(folder,"After");Directory.CreateDirectory(before);Directory.CreateDirectory(after);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.76f;
                var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var repaired=skins.Select(s=>s.sharedMesh).ToArray();var oldMeshes=repaired.Select(m=>UnityEngine.Object.Instantiate(m)).ToArray();foreach(var mesh in oldMeshes)mesh.boneWeights=originalWeights;
                var baked=new Mesh();var tri=repaired[0].triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var result=new VideoCase{name=mode,frames=new VideoFrame[240]};
                for(var tick=0;tick<240;tick++)
                {
                    Input(mode,tick);Tick();_pair.Present(1);
                    for(var i=0;i<2;i++)skins[i].sharedMesh=oldMeshes[i];yield return null;
                    var beforeSkin=0f;foreach(var skin in skins){skin.BakeMesh(baked);beforeSkin=Mathf.Max(beforeSkin,CameraGaitReviewTests.Surface(baked.vertices,tri,pairs,out _));}SaveFrame(rt,image,before,tick);
                    for(var i=0;i<2;i++)skins[i].sharedMesh=repaired[i];yield return null;
                    var afterSkin=0f;foreach(var skin in skins){skin.BakeMesh(baked);afterSkin=Mathf.Max(afterSkin,CameraGaitReviewTests.Surface(baked.vertices,tri,pairs,out _));}SaveFrame(rt,image,after,tick);
                    result.skin=Mathf.Max(result.skin,afterSkin);result.otherSkin=Mathf.Max(result.otherSkin,beforeSkin);result.frames[tick]=new VideoFrame{tick=tick,grips=_pair.LogicalGrips,skin=afterSkin,otherSkin=beforeSkin,force=_pair.MaximumGripForce};
                }
                results.Add(result);File.WriteAllText(Path.Combine(Output,"RepairVideo.json"),JsonUtility.ToJson(new VideoReport{cases=results.ToArray()},true));
                Assert.That(result.skin,Is.LessThan(60));Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(baked);foreach(var mesh in oldMeshes)UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator CaptureLimpPickupFourDirections()
        {
            var results=new List<VideoCase>();
            foreach(LimpGrabTargetReview.Orientation orientation in Enum.GetValues(typeof(LimpGrabTargetReview.Orientation)))
            {
                CreateLow(orientation);Buttons(0,3);var target=_pair.ExternalTarget;
                var folder=Path.Combine(Output,"Frames","Low_"+orientation);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.85f;
                var skins=new[]{_pair.Players[0].Rig.Actor.Pose,target.Actor.Pose}.Select(p=>p.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var baked=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var result=new VideoCase{name=orientation.ToString(),low=true,frames=new VideoFrame[360]};var maxGrips=0;
                for(var tick=0;tick<360;tick++)
                {
                    if(tick<150)_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;
                    if(tick==180)_pair.Players[0].Move=Vector2.down*.35f;if(tick==250)_pair.Players[0].Move=Vector2.zero;if(tick==280)Buttons(0,0);
                    Tick();maxGrips=Math.Max(maxGrips,_pair.LogicalGrips);var angles=new float[2];for(var i=0;i<2;i++){skins[i].BakeMesh(baked);angles[i]=CameraGaitReviewTests.Surface(baked.vertices,tri,pairs,out _);}
                    result.skin=Mathf.Max(result.skin,angles[0]);result.otherSkin=Mathf.Max(result.otherSkin,angles[1]);result.frames[tick]=new VideoFrame{tick=tick,grips=_pair.LogicalGrips,skin=angles[0],otherSkin=angles[1],low=_pair.Players[0].Rig.LowReachWeight,force=_pair.MaximumGripForce};
                    if(tick==140||tick==230)ExportContact("Contact_"+orientation+(tick==140?"":"_Drag"));
                    _pair.Present(1);yield return null;SaveFrame(rt,image,folder,tick);
                }
                results.Add(result);File.WriteAllText(Path.Combine(Output,"LowVideo.json"),JsonUtility.ToJson(new VideoReport{cases=results.ToArray()},true));
                Assert.That(result.skin,Is.LessThan(60));Assert.That(result.otherSkin,Is.LessThan(60));Assert.That(maxGrips,Is.EqualTo(2));Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(baked);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
