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
    public sealed partial class PhysicalPunchCandidateTests
    {
        [Serializable] private sealed class ContactMeta {public int actor,tick;public bool[] active,hit;public string[] targets;public string[] regions={"palm_L","forearm_L","palm_R","forearm_R","head"};}
        private void ExportContact(string label,int tick)
        {
            var mesh=new Mesh();
            for(var actor=0;actor<2;actor++)
            {
                var name=$"Contact_{label}_{tick}_{actor}";var owner=_pair.Players[actor].Rig;var skins=_pair.Players[1-actor].Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>();
                using(var writer=new BinaryWriter(File.Create(Path.Combine(Output,name+".bin"))))
                {
                    writer.Write(skins.Length);
                    foreach(var skin in skins)
                    {
                        skin.BakeMesh(mesh);var vertices=mesh.vertices;var tri=mesh.triangles;writer.Write(vertices.Length);writer.Write(tri.Length);
                        foreach(var v in vertices){var p=skin.transform.TransformPoint(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}foreach(var t in tri)writer.Write(t);
                    }
                    var ownSkin=owner.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));ownSkin.BakeMesh(mesh);var points=mesh.vertices;var weights=ownSkin.sharedMesh.boneWeights;
                    for(var side=0;side<2;side++)for(var region=0;region<2;region++)
                    {
                        var suffix=side==0?"L":"R";var forearm=Array.FindIndex(ownSkin.bones,b=>b.name=="Forearm_"+suffix);var hand=Array.FindIndex(ownSkin.bones,b=>b.name=="HandLogical_"+suffix);var selected=new List<Vector3>();
                        for(var i=0;i<points.Length;i++)
                        {
                            var w=weights[i];var sum=0f;if(w.boneIndex0==forearm||w.boneIndex0==hand)sum+=w.weight0;if(w.boneIndex1==forearm||w.boneIndex1==hand)sum+=w.weight1;if(w.boneIndex2==forearm||w.boneIndex2==hand)sum+=w.weight2;if(w.boneIndex3==forearm||w.boneIndex3==hand)sum+=w.weight3;
                            if(sum<.75f)continue;var point=ownSkin.transform.TransformPoint(points[i]);if(region==1||Vector3.Distance(point,owner.PalmCenter(side))<.07f)selected.Add(point);
                        }
                        writer.Write(selected.Count);foreach(var p in selected){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
                    }
                    var head=owner.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>!s.name.EndsWith("SkinnedBody"));head.BakeMesh(mesh);var heads=mesh.vertices;writer.Write(heads.Length);foreach(var v in heads){var p=head.transform.TransformPoint(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
                }
                var hands=_pair.Players[actor].Hands;File.WriteAllText(Path.Combine(Output,name+".json"),JsonUtility.ToJson(new ContactMeta{actor=actor,tick=tick,active=new[]{_pair.PhysicalPunches.IsPunching(actor,0),_pair.PhysicalPunches.IsPunching(actor,1)},hit=new[]{_pair.PhysicalPunches.Hits.Any(h=>h.attacker==actor&&h.side==0&&h.tick==_pair.Tick),_pair.PhysicalPunches.Hits.Any(h=>h.attacker==actor&&h.side==1&&h.tick==_pair.Tick)},targets=hands.Select(h=>h.TargetBody!=null?h.TargetBody.name:"").ToArray()},true));
            }
            UnityEngine.Object.Destroy(mesh);
        }
        [UnityTest] public IEnumerator ExportPhysicalImpactSkin()
        {
            foreach(var mode in new[]{"LeftBody","RightBody","Head"})
            {
                Create(mode=="Head"?.42f:.38f,mode=="Head"?.22f:0);Tap(0,mode=="RightBody"?1:0);
                for(var tick=0;tick<30;tick++){Tick();if(tick==3||tick==16||_pair.PhysicalPunches.Hits.Any(h=>h.tick==_pair.Tick))ExportContact(mode,tick);}
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
