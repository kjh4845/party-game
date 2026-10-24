using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    // Walking or running into a knocked-out body from every side, for several lying poses (2026-10-22 review: the
    // push must feel the same from any direction and in any pose; skins touch, never pass through, and the body is
    // still shoved). Measured on the full baked skins of both characters, independent of the runtime's skin samples.
    public sealed partial class PhysicalPunchCandidateTests
    {
        private static readonly (string name,Vector3 shove)[] MatrixPoses={("fwd",new Vector3(0,0,1.2f)),("back",new Vector3(0,0,-1.2f)),("right",new Vector3(1.2f,0,0)),("left",new Vector3(-1.2f,0,0)),("diag",new Vector3(1.2f,0,1.2f))};
        // Where the walker comes from, in the lying body's frame: 0 = beyond its head, 180 = beyond its feet.
        private static readonly int[] MatrixAngles={0,45,90,135,180,225,270,315};
        private const float MatrixPenetration=.01f,MatrixGap=.02f,MatrixMinPush=.02f;
        // One character's baked skin: vertices bucketed for nearest-vertex distance, and the mesh cooked into a collider
        // alone in its own query scene, so a point is inside when rays from it first hit the back of a face (a
        // nearest-vertex normal gets the side wrong in creases: neck, armpit, between the shoulders).
        private sealed class SkinSample
        {
            public Vector3[] Points;public Bounds Bounds;public string[] Part;
            private readonly Dictionary<Vector3Int,List<int>> _grid=new Dictionary<Vector3Int,List<int>>();
            private readonly PhysicsScene _query;
            private const float Cell=.03f;
            private static readonly Vector3[] Rays={new Vector3(0,1,0),new Vector3(.8f,.3f,.52f).normalized,new Vector3(-.5f,-.2f,.84f).normalized};
            public SkinSample(SkinnedMeshRenderer skin,MeshCollider query)
            {
                var mesh=new Mesh();skin.BakeMesh(mesh);var rotation=skin.transform.rotation;var position=skin.transform.position;
                var bones=skin.bones;Part=skin.sharedMesh.boneWeights.Select(w=>bones[w.boneIndex0].name).ToArray();
                Points=mesh.vertices.Select(v=>position+rotation*v).ToArray();
                var old=query.sharedMesh;query.transform.SetPositionAndRotation(position,rotation);query.transform.localScale=Vector3.one;query.sharedMesh=mesh;if(old!=null)UnityEngine.Object.Destroy(old);
                _query=query.gameObject.scene.GetPhysicsScene();UnityEngine.Physics.SyncTransforms();
                var min=Vector3.positiveInfinity;var max=Vector3.negativeInfinity;
                for(var i=0;i<Points.Length;i++)
                {
                    min=Vector3.Min(min,Points[i]);max=Vector3.Max(max,Points[i]);var key=Key(Points[i]);
                    if(!_grid.TryGetValue(key,out var list))_grid[key]=list=new List<int>();list.Add(i);
                }
                Bounds=new Bounds();Bounds.SetMinMax(min,max);
            }
            private static Vector3Int Key(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.y/Cell),Mathf.FloorToInt(p.z/Cell));
            public bool Inside(Vector3 query){var votes=0;foreach(var ray in Rays)if(_query.Raycast(query,ray,out var hit,3f)&&Vector3.Dot(hit.normal,ray)>0)votes++;return votes>=2;}
            // Distance from `query` to this skin (negative = inside), if a vertex is within a cell.
            public bool Height(Vector3 query,out float height,out int index)
            {
                height=0;var best=Cell*Cell;index=-1;var key=Key(query);
                for(var x=-1;x<=1;x++)for(var y=-1;y<=1;y++)for(var z=-1;z<=1;z++)
                    if(_grid.TryGetValue(new Vector3Int(key.x+x,key.y+y,key.z+z),out var list))foreach(var i in list){var d=(Points[i]-query).sqrMagnitude;if(d<best){best=d;index=i;}}
                if(index<0)return false;
                var votes=0;foreach(var ray in Rays)if(_query.Raycast(query,ray,out var hit,3f)&&Vector3.Dot(hit.normal,ray)>0)votes++;
                height=Mathf.Sqrt(best)*(votes>=2?-1:1);return true;
            }
        }
        // Closest skin-to-skin distance (negative = one inside the other) and how many vertices of either skin are
        // more than MatrixPenetration inside the other.
        private static (float gap,int inside,float deepest,string where) SkinGap(SkinSample a,SkinSample b,Func<string,bool> skipA=null)
        {
            var gap=float.PositiveInfinity;var inside=0;var deepest=0f;var where="";
            // Backface hits only while measuring: the game's own rays (ground probes) must not see them.
            var backfaces=UnityEngine.Physics.queriesHitBackfaces;UnityEngine.Physics.queriesHitBackfaces=true;
            foreach(var (from,to,label) in new[]{(a,b,"walker in downed"),(b,a,"downed in walker")})
            {
                var reach=to.Bounds;reach.Expand(.06f);
                for(var i=0;i<from.Points.Length;i++)
                {
                    var p=from.Points[i];if(skipA!=null&&from==a&&skipA(a.Part[i]))continue;
                    if(!reach.Contains(p)||!to.Height(p,out var h,out var j)||skipA!=null&&to==a&&skipA(a.Part[j]))continue;
                    gap=Mathf.Min(gap,h);if(h<-MatrixPenetration)inside++;if(-h>deepest){deepest=-h;where=$"{label}: {from.Part[i]} vs {to.Part[j]} at {p.x:F2},{p.y:F2},{p.z:F2} (from mesh bounds {from.Bounds.center:F2} size {from.Bounds.size:F2})";}
                }
            }
            UnityEngine.Physics.queriesHitBackfaces=backfaces;
            return (gap,inside,deepest,where);
        }
        // How far each collider of a character pokes out past its own skin (max and 90th percentile over points on the
        // collider's surface): a collider outside the skin is a wall the other body hits before the skins touch.
        private static string Protrusion(SkinSample skin,IEnumerable<Collider> colliders)
        {
            var lines=new List<string>();
            var backfaces=UnityEngine.Physics.queriesHitBackfaces;UnityEngine.Physics.queriesHitBackfaces=true;
            foreach(var c in colliders)
            {
                if(c==null||!c.enabled||c.isTrigger)continue;var outs=new List<float>();var b=c.bounds;
                for(var i=0;i<400;i++)
                {
                    var dir=UnityEngine.Random.onUnitSphere;var far=b.center+dir*2;var p=c.ClosestPoint(far);
                    if(skin.Height(p,out var h,out _))outs.Add(h);else outs.Add(skin.Inside(p)?-.05f:.05f);
                }
                outs.Sort();lines.Add($"{c.attachedRigidbody?.name}/{c.GetType().Name}: max {outs[outs.Count-1]*100:F1} p90 {outs[(int)(outs.Count*.9f)]*100:F1} cm");
            }
            UnityEngine.Physics.queriesHitBackfaces=backfaces;return string.Join(" | ",lines);
        }
        // Query scenes holding the two baked skins (SkinSample), made on first use and unloaded in Cleanup.
        private MeshCollider[] _skinQueries;private UnityEngine.SceneManagement.Scene[] _skinQueryScenes;
        private MeshCollider[] SkinQueries()
        {
            if(_skinQueries!=null&&_skinQueries[0]!=null)return _skinQueries;
            _skinQueries=new MeshCollider[2];_skinQueryScenes=new UnityEngine.SceneManagement.Scene[2];
            for(var q=0;q<2;q++){_skinQueryScenes[q]=UnityEngine.SceneManagement.SceneManager.CreateScene("Skin query "+q+" "+Guid.NewGuid().ToString("N"),new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D));var go=new GameObject("Skin query");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,_skinQueryScenes[q]);_skinQueries[q]=go.AddComponent<MeshCollider>();}
            return _skinQueries;
        }
        private IEnumerator UnloadSkinQueries()
        {
            if(_skinQueryScenes!=null)foreach(var scene in _skinQueryScenes)if(scene.IsValid()&&scene.isLoaded){var op=UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);while(!op.isDone)yield return null;}
            _skinQueries=null;_skinQueryScenes=null;
        }
        // The two players' skins as they are drawn now: closest distance, vertices of either more than `tolerance`
        // inside the other, deepest, and where.
        private (float gap,int inside,float deepest,string where) SkinGapNow(SkinnedMeshRenderer[] renderers)
        // A punching arm is left out, as the runtime separation leaves it out (a punch has to land).
        {
            _pair.Present(1);var q=SkinQueries();var punches=_pair.PhysicalPunches;
            bool Punching(string bone){var side=bone.EndsWith("_L")?0:bone.EndsWith("_R")?1:-1;return side>=0&&punches!=null&&punches.IsPunching(0,side)&&(bone.StartsWith("Forearm")||bone.StartsWith("UpperArm")||bone.StartsWith("Hand"));}
            return SkinGap(new SkinSample(renderers[0],q[0]),new SkinSample(renderers[1],q[1]),Punching);
        }
        private void PlaceWalker(Vector3 position,Vector3 facing)
        {
            var rig=_pair.Players[0].Rig;var root=rig.Actor.Motor.Body;var from=root.position;
            var turn=Quaternion.FromToRotation(Vector3.ProjectOnPlane(root.rotation*Vector3.forward,Vector3.up).normalized,facing);
            foreach(var body in rig.Links.Select(l=>l.Body).Append(root))
            {
                var p=position+turn*(body.position-from);p.y=body.position.y;
                body.transform.SetPositionAndRotation(p,turn*body.rotation);body.position=p;body.rotation=turn*body.rotation;body.linearVelocity=body.angularVelocity=Vector3.zero;
            }
            UnityEngine.Physics.SyncTransforms();
        }
        [UnityTest, Timeout(600000)] public IEnumerator PushDownedMatrix()
        {
            Create(3f);
            var poses=Environment.GetEnvironmentVariable("PARTY_MATRIX_POSES")?.Split(',')??MatrixPoses.Select(p=>p.name).ToArray();
            var gaits=Environment.GetEnvironmentVariable("PARTY_MATRIX_GAITS")?.Split(',')??new[]{"walk","run"};
            var angles=Environment.GetEnvironmentVariable("PARTY_MATRIX_ANGLES")?.Split(',').Select(int.Parse).ToArray()??MatrixAngles;
            var framesFor=new HashSet<string>(Environment.GetEnvironmentVariable("PARTY_MATRIX_FRAMES")?.Split(',')??new string[0]);
            var folder=Path.Combine(Output,"Matrix");Directory.CreateDirectory(folder);
            var renderers=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
            var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);
            var rows=new List<string>();var failures=new List<string>();var first=true;
            foreach(var poseName in poses)foreach(var gait in gaits)foreach(var angle in angles)
            {
                var name=$"{poseName}_{gait}_{angle:D3}";var shove=MatrixPoses.First(p=>p.name==poseName).shove;
                if(!first){_pair.ResetReview();for(var i=0;i<20;i++)Tick();}first=false;
                var target=_pair.Players[1].Rig;var walker=_pair.Players[0];walker.Move=Vector2.zero;walker.Sprint=false;
                // A long Down wait: this checks the push while lying, not the get-up.
                target.Knockdown.Down.Bind(target.Actor,new ProjectHotfix.Simulation.DownTimingProfile(6,.5,7),true);
                target.BeginKnockdown(shove);
                for(var i=0;i<130;i++)Tick();
                // The lying body's frame: along = pelvis to head.
                if(Environment.GetEnvironmentVariable("PARTY_MATRIX_PROTRUSION")=="1")
                {
                    _pair.Present(1);var bodies=new Rigidbody[32];var count=target.Knockdown.CopyDynamicBodies(bodies);
                    Debug.Log("PROTRUDE downed "+name+" "+Protrusion(new SkinSample(renderers[1],SkinQueries()[1]),bodies.Take(count).SelectMany(b=>b.GetComponents<Collider>())));
                    var w=_pair.Players[0].Rig;Debug.Log("PROTRUDE walker "+Protrusion(new SkinSample(renderers[0],SkinQueries()[0]),w.Actor.Motor.Body.GetComponents<Collider>().Concat(w.Links.Select(l=>(Collider)l.Shape))));
                }
                var pelvis=Bone(1,"Pelvis");var head=Bone(1,"Head");var center=(pelvis+Bone(1,"Chest"))*.5f;center.y=0;
                var along=Vector3.ProjectOnPlane(head-pelvis,Vector3.up).normalized;var up=target.Actor.Motor.Body.rotation*Vector3.forward;
                var lying=up.y>.5f?"faceUp":up.y<-.5f?"faceDown":"side";
                var from=Quaternion.AngleAxis(angle,Vector3.up)*along;var start=center+from*(gait=="run"?2.2f:1.4f);
                PlaceWalker(start,-from);for(var i=0;i<3;i++)Tick();
                var dir=-from;var contactTick=-1;var chestStart=Vector3.zero;var walkerStart=Vector3.zero;
                var deepWhere="";var riseTick=-1;var sep0=_pair.DownedSeparations;var push0=_pair.DownedPushTicks;var maxGap=float.NegativeInfinity;var minGap=float.PositiveInfinity;var gapSum=0f;var gapCount=0;var inside=0;var deepest=0f;var speedSum=0f;var speedCount=0;var leanSum=0f;var lostTicks=0;
                var keepFrames=framesFor.Contains(name)||framesFor.Contains("all");var caseFolder=Path.Combine(folder,name);if(keepFrames)Directory.CreateDirectory(caseFolder);
                const int ticks=150;
                for(var tick=0;tick<ticks;tick++)
                {
                    walker.Move=new Vector2(dir.x,dir.z);walker.Sprint=gait=="run";
                    Tick();
                    if(riseTick<0&&target.RagdollActive&&target.Knockdown.Rising)riseTick=tick;
                    if(tick%2==0||contactTick<0)
                    {
                        var (gap,deep,depth,where)=SkinGapNow(renderers);
                        if(contactTick<0&&gap<MatrixGap){contactTick=tick;chestStart=Bone(1,"Chest");walkerStart=walker.Rig.Actor.Motor.Body.position;}
                        if(contactTick>=0)
                        {
                            inside=Mathf.Max(inside,deep);if(depth>deepest){deepest=depth;deepWhere=$"t+{tick-contactTick} {where}";}minGap=Mathf.Min(minGap,gap);
                            // After the first touch settles, how far apart the skins are while the walker keeps pushing.
                            if(tick>=contactTick+10){var pose=walker.Rig.Actor.FixedPose;leanSum+=Vector3.Dot(pose.WorldPosition("Head")-pose.WorldPosition("Pelvis"),dir);maxGap=Mathf.Max(maxGap,gap);gapSum+=Mathf.Max(0,gap);gapCount++;if(gap>MatrixGap)lostTicks+=2;speedSum+=Vector3.Dot(walker.Rig.Actor.Motor.Body.linearVelocity,dir);speedCount++;}
                        }
                    }
                    var snap=contactTick>=0&&tick==Mathf.Min(ticks-1,contactTick+40);
                    if(snap||keepFrames&&tick%2==0)
                    {
                        _pair.Present(1);var across=new Vector3(dir.z,0,-dir.x);var look=(walker.Rig.Actor.Motor.Body.position+Bone(1,"Pelvis"))*.5f;look.y=.4f;var offset=across*2.6f+Vector3.up*.9f;
                        _pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.9f;_pair.View.transform.SetPositionAndRotation(look+offset,Quaternion.LookRotation(-offset));
                        yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;
                        var png=image.EncodeToPNG();if(snap)File.WriteAllBytes(Path.Combine(folder,name+".png"),png);if(keepFrames&&tick%2==0)File.WriteAllBytes(Path.Combine(caseFolder,$"Frame_{tick:D4}.png"),png);
                    }
                }
                var chest=Vector3.Dot(Bone(1,"Chest")-chestStart,dir);var walked=Vector3.Dot(walker.Rig.Actor.Motor.Body.position-walkerStart,dir);
                var meanGap=gapCount>0?gapSum/gapCount:float.NaN;var meanSpeed=speedCount>0?speedSum/speedCount:float.NaN;
                var row=string.Format(CultureInfo.InvariantCulture,"{{\"case\":\"{0}\",\"lying\":\"{1}\",\"contactTick\":{2},\"meanGapCm\":{3:F2},\"maxGapCm\":{4:F2},\"minGapCm\":{5:F2},\"insideVerts\":{6},\"deepestCm\":{7:F2},\"chestPushedCm\":{8:F1},\"walkerMovedCm\":{9:F1},\"pushSpeed\":{10:F2},\"lostTicks\":{11},\"deepest\":\"{12}\",\"riseTick\":{13},\"downAtEnd\":{14},\"separations\":{15},\"pushTicks\":{16},\"leanCm\":{17:F1}",
                    name,lying,contactTick,meanGap*100,maxGap*100,minGap*100,inside,deepest*100,contactTick>=0?chest*100:0,contactTick>=0?walked*100:0,meanSpeed,lostTicks,deepWhere,riseTick,target.RagdollActive?"true":"false",_pair.DownedSeparations-sep0,_pair.DownedPushTicks-push0,speedCount>0?leanSum/speedCount*100:float.NaN)+"}";
                rows.Add(row);Debug.Log("MATRIX "+row);
                if(contactTick<0)failures.Add(name+" never touched");
                else
                {
                    if(inside>0)failures.Add(name+$" skin inside: {inside} vertices, {deepest*100:F1} cm");
                    if(maxGap>MatrixGap)failures.Add(name+$" gap while pushing up to {maxGap*100:F1} cm");
                    if(chest<MatrixMinPush)failures.Add(name+$" body pushed only {chest*100:F1} cm");
                }
            }
            yield return UnloadSkinQueries();
            _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
            File.WriteAllText(Path.Combine(folder,"matrix.jsonl"),string.Join("\n",rows));
            Assert.That(failures,Is.Empty);
        }
    }
}
