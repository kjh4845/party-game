using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Sparse points of one character's visible skin (one vertex per Cell-sized cube), skinned every tick from its
    // fixed-step pose, so two bodies can be kept apart by where their skins really are rather than by collider
    // shapes plus a margin (2026-10-22 review: a 5 cm margin over capsules read as a wall between the bodies, and
    // capsules fit the skin differently per part, so the gap changed with the pose and the side pushed from).
    public sealed class SkinContactCloud
    {
        public const float Cell=.02f;
        private readonly FixedReviewPose _pose;
        private readonly string[] _boneNames;
        private readonly Matrix4x4[] _bind,_palette;
        private readonly Vector3[] _local,_localNormals;
        private readonly BoneWeight[] _weights;
        public readonly Transform[] Bones;
        // Per point: the skin bone with the largest weight (what the point rides on).
        public readonly int[] Bone;
        public readonly Vector3[] Points,Normals;
        // How far each point moved over the last Update (its skin speed times the step, legs swinging included).
        public readonly Vector3[] Motion;
        private bool _hasPrevious;private ulong _lastTick;
        public int Count=>Points.Length;
        public Bounds Bounds{get;private set;}
        private readonly Dictionary<Vector3Int,List<int>> _grid=new Dictionary<Vector3Int,List<int>>();
        private readonly Stack<List<int>> _spare=new Stack<List<int>>();
        public SkinContactCloud(PhysicsJumpReview actor)
        {
            _pose=actor.FixedPose;
            var skins=actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>();
            var skin=skins.FirstOrDefault(s=>s.name.EndsWith("SkinnedBody"))??skins[0];
            var mesh=skin.sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var weights=mesh.boneWeights;
            Bones=skin.bones;_boneNames=Bones.Select(b=>b.name).ToArray();_bind=mesh.bindposes;_palette=new Matrix4x4[Bones.Length];
            var cells=new HashSet<Vector3Int>();var picked=new List<int>();
            for(var i=0;i<vertices.Length;i++){var v=vertices[i]/Cell;if(cells.Add(new Vector3Int(Mathf.FloorToInt(v.x),Mathf.FloorToInt(v.y),Mathf.FloorToInt(v.z))))picked.Add(i);}
            _local=picked.Select(i=>vertices[i]).ToArray();_localNormals=picked.Select(i=>normals[i]).ToArray();_weights=picked.Select(i=>weights[i]).ToArray();
            Bone=_weights.Select(w=>w.boneIndex0).ToArray();
            Points=new Vector3[picked.Count];Normals=new Vector3[picked.Count];Motion=new Vector3[picked.Count];
        }
        // Skin the points from the pose captured after the last physics step (pose correctives are left out: <=5 mm).
        // Motion is only kept between consecutive ticks.
        public void Update(ulong tick)
        {
            _hasPrevious=_hasPrevious&&_lastTick+1==tick;_lastTick=tick;
            for(var i=0;i<_palette.Length;i++)_palette[i]=Matrix4x4.TRS(_pose.WorldPosition(_boneNames[i]),_pose.WorldRotation(_boneNames[i]),Vector3.one)*_bind[i];
            var min=Vector3.positiveInfinity;var max=Vector3.negativeInfinity;
            for(var i=0;i<Points.Length;i++)
            {
                var w=_weights[i];var v=_local[i];var n=_localNormals[i];
                var p=_palette[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+_palette[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+_palette[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+_palette[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                Motion[i]=_hasPrevious?p-Points[i]:Vector3.zero;Points[i]=p;min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                Normals[i]=(_palette[w.boneIndex0].MultiplyVector(n)*w.weight0+_palette[w.boneIndex1].MultiplyVector(n)*w.weight1+_palette[w.boneIndex2].MultiplyVector(n)*w.weight2+_palette[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
            }
            var bounds=new Bounds();bounds.SetMinMax(min,max);Bounds=bounds;_hasPrevious=true;
        }

        // Buckets the points (call after Update) so Nearest only looks at the cubes around a query.
        public void BuildGrid()
        {
            foreach(var list in _grid.Values){list.Clear();_spare.Push(list);}_grid.Clear();
            for(var i=0;i<Points.Length;i++)
            {
                var key=Key(Points[i]);
                if(!_grid.TryGetValue(key,out var list)){list=_spare.Count>0?_spare.Pop():new List<int>();_grid[key]=list;}
                list.Add(i);
            }
        }
        private const float GridCell=.04f;
        private static Vector3Int Key(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x/GridCell),Mathf.FloorToInt(p.y/GridCell),Mathf.FloorToInt(p.z/GridCell));
        // The skin point nearest to `query` within one grid cube's reach, the skin's outward normal there (the points
        // within NormalReach averaged: one point's normal faces the wrong way across a crease such as the neck or an
        // armpit) and the query's height over the skin along it (negative = inside).
        public const float NormalReach=.03f;
        public bool Nearest(Vector3 query,out int index,out float height,out Vector3 normal)
        {
            index=-1;height=0;normal=Vector3.up;var best=GridCell*GridCell;var key=Key(query);
            for(var x=-1;x<=1;x++)for(var y=-1;y<=1;y++)for(var z=-1;z<=1;z++)
            {
                if(!_grid.TryGetValue(new Vector3Int(key.x+x,key.y+y,key.z+z),out var list))continue;
                foreach(var i in list){var d=(Points[i]-query).sqrMagnitude;if(d<best){best=d;index=i;}}
            }
            if(index<0)return false;
            var sum=Vector3.zero;var near=Points[index];key=Key(near);
            for(var x=-1;x<=1;x++)for(var y=-1;y<=1;y++)for(var z=-1;z<=1;z++)
            {
                if(!_grid.TryGetValue(new Vector3Int(key.x+x,key.y+y,key.z+z),out var list))continue;
                foreach(var i in list)if((Points[i]-near).sqrMagnitude<NormalReach*NormalReach)sum+=Normals[i];
            }
            normal=sum.sqrMagnitude>1e-8f?sum.normalized:Normals[index];
            height=Vector3.Dot(query-near,normal);return true;
        }
    }
}
