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
        [Serializable] private sealed class ContactExport
        {public Vector3[] palms;public float[] radii;public bool[] holding;public string[] targets;public Vector3[] bodyPositions,shapeCenters;public Quaternion[] bodyRotations;}
        private void ExportContact(string name)
        {
            var skins=_pair.ExternalTarget.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
            using(var writer=new BinaryWriter(File.Create(Path.Combine(Output,name+".bin"))))
            {
                writer.Write(skins.Length);
                foreach(var skin in skins)
                {
                    skin.BakeMesh(mesh);var vertices=mesh.vertices;var triangles=mesh.triangles;writer.Write(vertices.Length);writer.Write(triangles.Length);
                    foreach(var vertex in vertices){var p=skin.transform.TransformPoint(vertex);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}foreach(var index in triangles)writer.Write(index);
                }
                var owner=_pair.Players[0].Rig;var ownerSkin=owner.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));ownerSkin.BakeMesh(mesh);
                var points=mesh.vertices;var weights=ownerSkin.sharedMesh.boneWeights;
                for(var side=0;side<2;side++)
                {
                    var suffix=side==0?"L":"R";var forearm=Array.FindIndex(ownerSkin.bones,b=>b.name=="Forearm_"+suffix);var hand=Array.FindIndex(ownerSkin.bones,b=>b.name=="HandLogical_"+suffix);var selected=new List<Vector3>();
                    for(var i=0;i<points.Length;i++)
                    {
                        var w=weights[i];var sum=0f;
                        if(w.boneIndex0==forearm||w.boneIndex0==hand)sum+=w.weight0;if(w.boneIndex1==forearm||w.boneIndex1==hand)sum+=w.weight1;if(w.boneIndex2==forearm||w.boneIndex2==hand)sum+=w.weight2;if(w.boneIndex3==forearm||w.boneIndex3==hand)sum+=w.weight3;
                        if(sum<.75f)continue;var point=ownerSkin.transform.TransformPoint(points[i]);if(Vector3.Distance(point,owner.PalmCenter(side))<.07f)selected.Add(point);
                    }
                    writer.Write(selected.Count);foreach(var point in selected){writer.Write(point.x);writer.Write(point.y);writer.Write(point.z);}
                }
            }
            var rig=_pair.Players[0].Rig;var hands=_pair.Players[0].Hands;
            File.WriteAllText(Path.Combine(Output,name+".json"),JsonUtility.ToJson(new ContactExport{palms=new[]{rig.PalmCenter(0),rig.PalmCenter(1)},radii=new[]{.037f,.037f},holding=hands.Select(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding).ToArray(),targets=hands.Select(h=>h.TargetBody!=null?h.TargetBody.name:"").ToArray(),bodyPositions=hands.Select(h=>h.TargetBody!=null?h.TargetBody.position:Vector3.zero).ToArray(),bodyRotations=hands.Select(h=>h.TargetBody!=null?h.TargetBody.rotation:Quaternion.identity).ToArray(),shapeCenters=hands.Select(h=>h.TargetBody!=null?Quaternion.Inverse(h.TargetBody.rotation)*(h.TargetCollider.bounds.center-h.TargetBody.position):Vector3.zero).ToArray()},true));UnityEngine.Object.Destroy(mesh);
        }
        private void CreateLow(LimpGrabTargetReview.Orientation orientation)
        {
            Create(true);_pair.Players[1].Rig.gameObject.SetActive(false);
            _pair.Players[0].Rig.MatchHandSkinEnvelope();
            _pair.ExternalTarget=LimpGrabTargetReview.Create(_scene,Candidate,orientation);
            Assert.That(_pair.ExternalTarget.Ragdoll.Active,Is.True);Assert.That(_pair.ExternalTarget.Bodies.Length,Is.EqualTo(12));
            for(var i=0;i<150;i++)Tick();
        }
        private IEnumerator PhotoLow(string file)
        {
            _pair.Present(1);yield return null;
            var rt=new RenderTexture(960,640,24);rt.Create();var image=new Texture2D(960,640,TextureFormat.RGB24,false);
            _pair.View.targetTexture=rt;_pair.View.aspect=1.5f;_pair.View.orthographicSize=.85f;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(Output,file),image.EncodeToPNG());_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator InspectLimpTargetReach()
        {
            var rows=new List<string>();var failures=new List<string>();
            foreach(LimpGrabTargetReview.Orientation orientation in Enum.GetValues(typeof(LimpGrabTargetReview.Orientation)))
            {
                CreateLow(orientation);var target=_pair.ExternalTarget;var start=target.Center;Buttons(0,3);
                var skins=new[]{_pair.Players[0].Rig.Actor.Pose,target.Actor.Pose}.Select(p=>p.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var maxGrips=0;var surface=new float[2];
                for(var tick=0;tick<360;tick++)
                {
                    if(tick<150)_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;
                    if(tick==180)_pair.Players[0].Move=Vector2.down*.35f;
                    if(tick==250)_pair.Players[0].Move=Vector2.zero;
                    if(tick==280)Buttons(0,0);
                    Tick();maxGrips=Math.Max(maxGrips,_pair.LogicalGrips);
                    if(tick%5==0)for(var i=0;i<2;i++){skins[i].BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out var where);if(angle>surface[i]){surface[i]=angle;rows.Add($"SKIN {orientation} tick={tick} actor={i} angle={angle:R} point={where:R}");}}
                    if(tick%20==0)rows.Add($"{orientation} tick={tick} grips={_pair.LogicalGrips} root={_pair.Players[0].Rig.Actor.Motor.Body.position:R} center={target.Center:R} low={_pair.Players[0].Rig.LowReachWeight:R} hands={_pair.Players[0].Rig.HandPosition(0):R}/{_pair.Players[0].Rig.HandPosition(1):R} goals={_pair.Players[0].Hands[0].MotionTargetLocal:R}/{_pair.Players[0].Hands[1].MotionTargetLocal:R} skin={surface[0]:R}/{surface[1]:R} gap={_pair.MaximumGripGap:R}");
                    if(tick==140||tick==230){ExportContact("Contact_"+orientation+(tick==140?"":"_Drag"));}
                    if(tick==140||tick==230||tick==330)yield return PhotoLow($"Low_{orientation}_{tick}.png");
                    foreach(var body in target.Bodies)foreach(var joint in body.GetComponents<ConfigurableJoint>()){Assert.That(joint.rotationDriveMode,Is.EqualTo(RotationDriveMode.XYAndZ));Assert.That(joint.angularXDrive.positionSpring+joint.angularXDrive.positionDamper+joint.angularXDrive.maximumForce+joint.angularYZDrive.positionSpring+joint.angularYZDrive.positionDamper+joint.angularYZDrive.maximumForce,Is.Zero);}
                }
                rows.Add($"SUMMARY {orientation} maxGrips={maxGrips} moved={Vector3.Distance(start,target.Center):R} surfaces={surface[0]:R}/{surface[1]:R}");File.WriteAllLines(Path.Combine(Output,"LowReach.txt"),rows);
                if(maxGrips<1)failures.Add(orientation+" no acquisition");if(surface.Any(a=>a>=60))failures.Add(orientation+" skin gate");Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips,Is.Zero);Assert.That(target.Ragdoll.Recoveries,Is.Zero);Assert.That(target.Ragdoll.TotalMass,Is.EqualTo(20));
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
