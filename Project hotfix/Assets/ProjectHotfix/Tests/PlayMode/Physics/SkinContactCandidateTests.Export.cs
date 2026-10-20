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
                for(var side=0;side<2;side++)for(var region=0;region<2;region++)
                {
                    var suffix=side==0?"L":"R";var forearm=Array.FindIndex(ownerSkin.bones,b=>b.name=="Forearm_"+suffix);var hand=Array.FindIndex(ownerSkin.bones,b=>b.name=="HandLogical_"+suffix);var selected=new List<Vector3>();
                    for(var i=0;i<points.Length;i++)
                    {
                        var w=weights[i];var sum=0f;
                        if(w.boneIndex0==forearm||w.boneIndex0==hand)sum+=w.weight0;if(w.boneIndex1==forearm||w.boneIndex1==hand)sum+=w.weight1;if(w.boneIndex2==forearm||w.boneIndex2==hand)sum+=w.weight2;if(w.boneIndex3==forearm||w.boneIndex3==hand)sum+=w.weight3;
                        if(sum<.75f)continue;var point=ownerSkin.transform.TransformPoint(points[i]);if(region==1||Vector3.Distance(point,owner.PalmCenter(side))<.07f)selected.Add(point);
                    }
                    writer.Write(selected.Count);foreach(var point in selected){writer.Write(point.x);writer.Write(point.y);writer.Write(point.z);}
                }
            }
            var rig=_pair.Players[0].Rig;var hands=_pair.Players[0].Hands;
            File.WriteAllText(Path.Combine(Output,name+".json"),JsonUtility.ToJson(new ContactExport{palms=new[]{rig.PalmCenter(0),rig.PalmCenter(1)},radii=new[]{.037f,.037f},holding=hands.Select(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding).ToArray(),targets=hands.Select(h=>h.TargetBody!=null?h.TargetBody.name:"").ToArray(),bodyPositions=hands.Select(h=>h.TargetBody!=null?h.TargetBody.position:Vector3.zero).ToArray(),bodyRotations=hands.Select(h=>h.TargetBody!=null?h.TargetBody.rotation:Quaternion.identity).ToArray(),shapeCenters=hands.Select(h=>h.TargetBody!=null?Quaternion.Inverse(h.TargetBody.rotation)*(h.TargetCollider.bounds.center-h.TargetBody.position):Vector3.zero).ToArray()},true));UnityEngine.Object.Destroy(mesh);
        }
    }
}
