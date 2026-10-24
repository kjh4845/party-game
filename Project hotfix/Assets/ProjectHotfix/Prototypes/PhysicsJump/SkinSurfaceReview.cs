using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Sparse search + local triangle walk over the authored, pose-corrected mesh.
    // Matrices come only from fixed pose snapshots, never interpolated render bones.
    public sealed class SkinSurfaceReview
    {
        public sealed class Anchor
        {
            internal SkinSurfaceReview Surface;
            internal int Triangle;
            internal Vector3 Barycentric;
            public Rigidbody Body;
            public Vector3 Position=>Surface.Point(Triangle,Barycentric);
            public Vector3 Normal=>Surface.Normal(Triangle,Barycentric);
        }
        private sealed class Part
        {
            public Vector3[] Vertices,Normals,World,WorldNormals;
            public BoneWeight[] Weights;
            public Matrix4x4[] Bind,Palette;
            public string[] Bones;
            public int[] Triangles,Offsets,Adjacent,Stamp,Samples,Visit;
            public int TriangleStart;
            public SurfacePoseCorrectives Correctives;
            public Vector3[][] PositionDeltas,NormalDeltas;
            public float[] PoseWeights;
        }
        private readonly FixedReviewPose _pose;
        private readonly Part[] _parts;
        private readonly Rigidbody[] _bodies;
        private readonly Dictionary<string,int> _bodyByBone=new Dictionary<string,int>();
        private readonly float[] _bodyWeights;
        private int _stamp;
        public int SampleCount {get;private set;}
        public int EvaluatedVertices {get;private set;}
        public SkinSurfaceReview(PhysicsJumpReview actor,Rigidbody[] bodies)
        {
            _pose=actor.FixedPose;_bodies=bodies;_bodyWeights=new float[bodies.Length];
            for(var i=0;i<bodies.Length;i++)
            {
                var name=bodies[i].name;var end=name.IndexOf("_Physics",StringComparison.Ordinal);if(end<0)end=name.IndexOf("_ActivePhysics",StringComparison.Ordinal);
                if(end>=0)_bodyByBone[name.Substring(0,end)]=i;
            }
            _bodyByBone["Pelvis"]=Array.IndexOf(bodies,actor.Motor.Body);
            _parts=actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>MakePart(s)).ToArray();
            var profile=actor.Pose.GetComponentInChildren<SkinTransportProfile>()??actor.GetComponentInChildren<SkinTransportProfile>();
            if(profile!=null){SeedCount=Mathf.Max(1,profile.ContactSearchSeeds);EscapeRings=Mathf.Max(0,profile.ContactEscapeRings);}
            _seeds=new int[SeedCount];_seedDistances=new float[SeedCount];
            var count=0;foreach(var p in _parts){p.TriangleStart=count;count+=p.Triangles.Length/3;SampleCount+=p.Samples.Length;}
            BeginFixedFrame();
        }
        private static Part MakePart(SkinnedMeshRenderer skin)
        {
            var mesh=skin.sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;var n=vertices.Length;
            var offsets=new int[n+1];foreach(var v in triangles)offsets[v+1]++;for(var i=1;i<=n;i++)offsets[i]+=offsets[i-1];
            var cursor=(int[])offsets.Clone();var adjacent=new int[triangles.Length];for(var t=0;t<triangles.Length;t++)adjacent[cursor[triangles[t]]++]=t/3;
            var cells=new HashSet<Vector3Int>();var samples=new List<int>();
            for(var i=0;i<n;i++){var v=vertices[i]/.018f;if(cells.Add(new Vector3Int(Mathf.FloorToInt(v.x),Mathf.FloorToInt(v.y),Mathf.FloorToInt(v.z))))samples.Add(i);}
            var part=new Part{Vertices=vertices,Normals=mesh.normals,Weights=mesh.boneWeights,Bind=mesh.bindposes,Palette=new Matrix4x4[skin.bones.Length],Bones=skin.bones.Select(b=>b.name).ToArray(),Triangles=triangles,Offsets=offsets,Adjacent=adjacent,Stamp=new int[n],World=new Vector3[n],WorldNormals=new Vector3[n],Samples=samples.ToArray()};
            part.Correctives=skin.GetComponent<SurfacePoseCorrectives>();
            if(part.Correctives!=null)
            {
                var count=mesh.blendShapeCount;part.PositionDeltas=new Vector3[count][];part.NormalDeltas=new Vector3[count][];part.PoseWeights=new float[count];
                for(var i=0;i<count;i++)
                {
                    part.PositionDeltas[i]=new Vector3[n];part.NormalDeltas[i]=new Vector3[n];
                    mesh.GetBlendShapeFrameVertices(i,0,part.PositionDeltas[i],part.NormalDeltas[i],null);
                }
            }
            return part;
        }
        public void BeginFixedFrame()
        {
            _stamp++;EvaluatedVertices=0;
            foreach(var p in _parts)
            {
                for(var i=0;i<p.Bones.Length;i++)p.Palette[i]=Matrix4x4.TRS(_pose.WorldPosition(p.Bones[i]),_pose.WorldRotation(p.Bones[i]),Vector3.one)*p.Bind[i];
                p.Correctives?.EvaluateFixed(_pose,p.PoseWeights);
            }
        }
        private Vector3 Vertex(Part p,int i)
        {
            if(p.Stamp[i]==_stamp)return p.World[i];var w=p.Weights[i];var v=p.Vertices[i];var n=p.Normals[i];
            if(p.PoseWeights!=null)for(var shape=0;shape<p.PoseWeights.Length;shape++)
            {
                var weight=p.PoseWeights[shape];v+=p.PositionDeltas[shape][i]*weight;n+=p.NormalDeltas[shape][i]*weight;
            }
            p.World[i]=p.Palette[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+p.Palette[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+p.Palette[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+p.Palette[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            p.WorldNormals[i]=(p.Palette[w.boneIndex0].MultiplyVector(n)*w.weight0+p.Palette[w.boneIndex1].MultiplyVector(n)*w.weight1+p.Palette[w.boneIndex2].MultiplyVector(n)*w.weight2+p.Palette[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
            p.Stamp[i]=_stamp;EvaluatedVertices++;return p.World[i];
        }
        private Part PartFor(ref int triangle)
        {for(var i=_parts.Length-1;i>=0;i--)if(triangle>=_parts[i].TriangleStart){triangle-=_parts[i].TriangleStart;return _parts[i];}throw new ArgumentOutOfRangeException(nameof(triangle));}
        private Vector3 Point(int triangle,Vector3 bary)
        {var p=PartFor(ref triangle);var k=triangle*3;return Vertex(p,p.Triangles[k])*bary.x+Vertex(p,p.Triangles[k+1])*bary.y+Vertex(p,p.Triangles[k+2])*bary.z;}
        private Vector3 Normal(int triangle,Vector3 bary)
        {var p=PartFor(ref triangle);var k=triangle*3;for(var j=0;j<3;j++)Vertex(p,p.Triangles[k+j]);return (p.WorldNormals[p.Triangles[k]]*bary.x+p.WorldNormals[p.Triangles[k+1]]*bary.y+p.WorldNormals[p.Triangles[k+2]]*bary.z).normalized;}
        public Anchor Closest(Vector3 query,Anchor reuse=null)
        {
            Part bestPart=null;var bestTriangle=-1;var bestBary=Vector3.zero;var best=float.PositiveInfinity;
            foreach(var p in _parts)
            {
                // Several nearest samples seed the walk: on dense, irregular skin a single seed can
                // sit on the facing sheet of a fold (armpit) and the walk stops there.
                Array.Fill(_seedDistances,float.PositiveInfinity);
                foreach(var index in p.Samples)
                {
                    var d=(Vertex(p,index)-query).sqrMagnitude;if(d>=_seedDistances[SeedCount-1])continue;
                    var slot=SeedCount-1;while(slot>0&&_seedDistances[slot-1]>d){_seedDistances[slot]=_seedDistances[slot-1];_seeds[slot]=_seeds[slot-1];slot--;}
                    _seedDistances[slot]=d;_seeds[slot]=index;
                }
                for(var s=0;s<SeedCount&&!float.IsPositiveInfinity(_seedDistances[s]);s++)
                {
                    var triangle=p.Adjacent[p.Offsets[_seeds[s]]];var bary=Vector3.zero;var localBest=float.PositiveInfinity;
                    // Walk triangle neighbours until the closest point stops changing; when stuck,
                    // look once over a wider ring before giving up (folds make local minima).
                    for(var step=0;step<24;step++)
                    {
                        var previous=triangle;
                        Neighbourhood(p,previous,1,query,ref localBest,ref triangle,ref bary);
                        if(triangle==previous&&EscapeRings>0)Neighbourhood(p,previous,EscapeRings,query,ref localBest,ref triangle,ref bary);
                        if(triangle==previous)break;
                    }
                    if(localBest>=best)continue;best=localBest;bestPart=p;bestTriangle=triangle;bestBary=bary;
                }
            }
            Array.Clear(_bodyWeights,0,_bodyWeights.Length);var offset=bestTriangle*3;
            AddWeight(bestPart,bestPart.Triangles[offset],bestBary.x);AddWeight(bestPart,bestPart.Triangles[offset+1],bestBary.y);AddWeight(bestPart,bestPart.Triangles[offset+2],bestBary.z);
            var bodyIndex=0;for(var i=1;i<_bodyWeights.Length;i++)if(_bodyWeights[i]>_bodyWeights[bodyIndex])bodyIndex=i;
            var result=reuse??new Anchor();result.Surface=this;result.Triangle=bestPart.TriangleStart+bestTriangle;result.Barycentric=bestBary;result.Body=_bodies[bodyIndex];return result;
        }
        // Older transports keep the original single-seed walk (their contact behaviour was tuned on it);
        // transports that ask for it (dense, irregular skin) get extra seeds and an escape ring.
        private readonly int SeedCount=1,EscapeRings;
        private readonly int[] _seeds;
        private readonly float[] _seedDistances;
        private readonly List<int> _ring=new List<int>(),_nextRing=new List<int>();
        private int _visitStamp;
        // Closest point over the triangles within `rings` vertex-rings of `start`.
        private void Neighbourhood(Part p,int start,int rings,Vector3 query,ref float localBest,ref int triangle,ref Vector3 bary)
        {
            if(p.Visit==null)p.Visit=new int[p.Triangles.Length/3];
            _visitStamp++;_ring.Clear();_ring.Add(start);p.Visit[start]=_visitStamp;
            for(var r=0;r<rings;r++)
            {
                _nextRing.Clear();
                foreach(var from in _ring)for(var corner=0;corner<3;corner++)
                {
                    var vertex=p.Triangles[from*3+corner];
                    for(var j=p.Offsets[vertex];j<p.Offsets[vertex+1];j++)
                    {
                        var t=p.Adjacent[j];if(p.Visit[t]==_visitStamp)continue;p.Visit[t]=_visitStamp;_nextRing.Add(t);
                        var c=t*3;var point=ClosestTriangle(query,Vertex(p,p.Triangles[c]),Vertex(p,p.Triangles[c+1]),Vertex(p,p.Triangles[c+2]),out var weights);var d=(point-query).sqrMagnitude;
                        if(d+1e-12f>=localBest)continue;localBest=d;triangle=t;bary=weights;
                    }
                }
                _ring.Clear();_ring.AddRange(_nextRing);
            }
        }
        private void AddWeight(Part p,int vertex,float bary)
        {
            var w=p.Weights[vertex];AddBone(p.Bones[w.boneIndex0],w.weight0*bary);AddBone(p.Bones[w.boneIndex1],w.weight1*bary);AddBone(p.Bones[w.boneIndex2],w.weight2*bary);AddBone(p.Bones[w.boneIndex3],w.weight3*bary);
        }
        private void AddBone(string name,float weight)
        {
            if(weight<=0)return;
            if(ShoulderHelperRig.IsHelper(name))
            {
                var fraction=ShoulderHelperRig.FractionOf(name);
                AddBone("Chest",weight*(1-fraction));AddBone("UpperArm_"+name.Substring(name.Length-1),weight*fraction);return;
            }
            if(name=="Neck"||name.StartsWith("Clavicle"))name="Chest";
            if(name.StartsWith("HandLogical_"))name=name.Replace("HandLogical_","Forearm_");
            if(name.StartsWith("Foot_"))name=name.Replace("Foot_","Calf_");
            if(!_bodyByBone.TryGetValue(name,out var i))i=_bodyByBone["Pelvis"];
            _bodyWeights[i]+=weight;
        }
        // Closest point and material coordinates, including edge/vertex regions.
        private static Vector3 ClosestTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c,out Vector3 w)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;var d1=Vector3.Dot(ab,ap);var d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0){w=Vector3.right;return a;}var bp=p-b;var d3=Vector3.Dot(ab,bp);var d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3){w=Vector3.up;return b;}var vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0){var v=d1/(d1-d3);w=new Vector3(1-v,v,0);return a+ab*v;}
            var cp=p-c;var d5=Vector3.Dot(ab,cp);var d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6){w=Vector3.forward;return c;}var vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0){var v=d2/(d2-d6);w=new Vector3(1-v,0,v);return a+ac*v;}var va=d3*d6-d5*d4;
            if(va<=0&&d4-d3>=0&&d5-d6>=0){var v=(d4-d3)/(d4-d3+d5-d6);w=new Vector3(0,1-v,v);return b+(c-b)*v;}
            var sum=va+vb+vc;if(Mathf.Abs(sum)<1e-16f){w=Vector3.right;return a;}w=new Vector3(va,vb,vc)/sum;return a*w.x+b*w.y+c*w.z;
        }
    }
}
