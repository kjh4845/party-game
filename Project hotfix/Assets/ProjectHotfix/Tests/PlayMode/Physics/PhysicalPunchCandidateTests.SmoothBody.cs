using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        [UnityTest] public IEnumerator SmoothNormalCorrectivesNeverMoveSkinVertices()
        {
            Create(.7f);
            var skin=_pair.Players[0].Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
            var driver=skin.GetComponent<SmoothBodyNormalPoses>();
            if(driver==null)Assert.Ignore("This assertion applies to a smooth-body transport with normal-only correctives.");
            _pair.Present(1);
            var count=skin.sharedMesh.blendShapeCount;Assert.That(count,Is.GreaterThan(0));
            for(var i=0;i<count;i++)skin.SetBlendShapeWeight(i,0);
            var before=new Mesh();var after=new Mesh();skin.BakeMesh(before);
            var positions=before.vertices;var normals=before.normals;var maximumDisplacement=0f;var maximumNormalChange=0f;
            for(var shape=0;shape<count;shape++)
            {
                skin.SetBlendShapeWeight(shape,100);skin.BakeMesh(after);
                var current=after.vertices;var currentNormals=after.normals;var changed=0f;
                for(var vertex=0;vertex<positions.Length;vertex++)
                {
                    maximumDisplacement=Mathf.Max(maximumDisplacement,Vector3.Distance(positions[vertex],current[vertex]));
                    changed=Mathf.Max(changed,Vector3.Distance(normals[vertex],currentNormals[vertex]));
                }
                Assert.That(changed,Is.GreaterThan(.01f),"The normal corrective must actually reach Unity's skinning path.");
                maximumNormalChange=Mathf.Max(maximumNormalChange,changed);
                skin.SetBlendShapeWeight(shape,0);
            }
            Assert.That(maximumDisplacement,Is.LessThan(.000001f),"Normal correction must not modify hit/grab surface positions.");
            driver.ApplyCurrentPose();
            File.WriteAllText(Path.Combine(Output,"NormalOnlyVerification.txt"),$"frames={count}\nmaximumVertexDisplacement={maximumDisplacement:R}\nmaximumNormalChange={maximumNormalChange:R}\n");
            Object.Destroy(before);Object.Destroy(after);yield return null;
        }
    }
}
