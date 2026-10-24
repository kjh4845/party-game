using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Local P00 footprint/camera/OOB preflight, not the production map or Round system.
    public sealed class ArenaBoundaryReview : MonoBehaviour
    {
        internal static readonly HashSet<ArenaBoundaryReview> Live=new HashSet<ArenaBoundaryReview>();
        public SharedArenaReview Arena;
        public bool ManualStep;
        public float WidthCU=8.8f,DepthCU=6.2f,SideBufferCU=1.75f,BottomCU=-2.8f,CharacterUnit=1f;
        public bool[] Out { get; private set; }
        public string[] OutReason { get; private set; }
        public ulong[] OutTick { get; private set; }
        public ulong Tick { get; private set; }
        public uint ResetVersion { get; private set; }
        public int AliveCount=>Out.Count(value=>!value);
        public float SideX=>(WidthCU*.5f+SideBufferCU)*CharacterUnit;
        public float SideZ=>(DepthCU*.5f+SideBufferCU)*CharacterUnit;
        private void OnEnable(){Live.Add(this);}
        private void OnDisable(){Live.Remove(this);}
        private void Start(){Bind();}
        public void Bind()
        {
            if(Out!=null)return;
            if(Arena==null)Arena=GetComponent<SharedArenaReview>();
            var count=Arena.Actors.Length;Out=new bool[count];OutReason=new string[count];OutTick=new ulong[count];
        }
        public static Vector3[] SpawnPositions(int count,int layout=0)
        {
            if(count<2||count>4||layout<0)throw new ArgumentOutOfRangeException(nameof(count));
            var cardinal=new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left};
            var slots=count==2?new[]{layout%4,(layout+2)%4}:Enumerable.Range(0,count).Select(i=>(i+layout)%4).ToArray();
            return slots.Select(i=>cardinal[i]*1.85f+Vector3.up*.02f).ToArray();
        }
        public static ArenaBoundaryReview Create(Scene scene,int count,float maximumDolly=22f,int layout=0)
        {
            var arena=SharedArenaReview.Create(scene,count,spawnPositions:SpawnPositions(count,layout));
            arena.name="ArenaBoundaryReview";var review=arena.gameObject.AddComponent<ArenaBoundaryReview>();review.Arena=arena;review.Bind();
            var height=arena.Actors[0].GetComponent<CapsuleCollider>().height;review.CharacterUnit=height;
            if(Mathf.Abs(height-1f)>.0001f)throw new InvalidOperationException("Review spawns must be regenerated for a different character unit.");
            var floor=arena.transform.Find("ReviewFloor");floor.name="ReviewRoof";floor.position=Vector3.down*.14f*height;floor.localScale=new Vector3(review.WidthCU,.28f,review.DepthCU)*height;
            floor.GetComponent<Renderer>().material.SetTextureScale("_BaseMap",new Vector2(review.WidthCU,review.DepthCU));
            var border=new GameObject("OpenEdgeMarking");SceneManager.MoveGameObjectToScene(border,scene);border.transform.SetParent(arena.transform,false);
            var line=border.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.widthMultiplier=.02f*height;line.positionCount=4;
            line.SetPositions(new[]{new Vector3(-review.WidthCU*.5f,.004f,-review.DepthCU*.5f),new Vector3(review.WidthCU*.5f,.004f,-review.DepthCU*.5f),new Vector3(review.WidthCU*.5f,.004f,review.DepthCU*.5f),new Vector3(-review.WidthCU*.5f,.004f,review.DepthCU*.5f)}.Select(p=>p*height).ToArray());
            line.material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));line.material.SetColor("_BaseColor",new Color(1f,.7f,.15f));line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            arena.SharedCamera.MaximumDolly=maximumDolly*height;
            return review;
        }
        public bool TryCoreBounds(int index,out Bounds bounds)
        {
            bounds=default;var found=false;var actor=Arena.Actors[index];
            // Only colliders owned by the authority root body. Ragdoll limbs and query/visual bones cannot eliminate.
            foreach(var collider in actor.GetComponents<Collider>())
            {
                if(!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy||collider.attachedRigidbody!=actor.Motor.Body)continue;
                if(!found){bounds=collider.bounds;found=true;}else bounds.Encapsulate(collider.bounds);
            }
            return found;
        }
        public string ExitReason(Bounds core)
        {
            if(core.min.y<=BottomCU*CharacterUnit)return "Bottom";
            if(core.max.x>=SideX)return "East";if(core.min.x<=-SideX)return "West";
            if(core.max.z>=SideZ)return "North";if(core.min.z<=-SideZ)return "South";
            return null;
        }
        public void StepAfterPhysics()
        {
            Bind();Tick++;
            for(var i=0;i<Out.Length;i++)
            {
                if(Out[i]||!Arena.Actors[i].gameObject.activeInHierarchy||!TryCoreBounds(i,out var core))continue;
                var reason=ExitReason(core);if(reason==null)continue;
                Out[i]=true;OutReason[i]=reason;OutTick[i]=Tick;Arena.ExcludeReviewActor(i);
            }
        }
        public void PrepareReset()
        {
            Bind();ResetVersion++;
            for(var i=0;i<Out.Length;i++){Out[i]=false;OutReason[i]=null;OutTick[i]=0;Arena.Actors[i].gameObject.SetActive(true);Arena.SharedCamera.Included[i]=true;}
        }
        private void OnGUI()
        {
            if(Arena==null||!Arena.ShowHelp||Out==null)return;
            var ledge=Arena.Actors[Arena.ControlledIndex].GetComponent<LedgeGrabReview>();
            var stage=ledge==null?"Ledge/Climb, hazards, rounds and score are not connected.":$"Hold L/R on a marked wall; Space assist {ledge.AssistsUsed}/{ledge.MaximumAssists}. Hazards/rounds pending.";
            GUI.Label(new Rect(14,144,950,70),$"P00 footprint preflight {WidthCU:F1} x {DepthCU:F1} CU | alive {AliveCount}/{Out.Length}\nOpen edges, OOB only. {stage}\nR: reset this layout | camera max {Arena.SharedCamera.MaximumDolly:F1}m | selected player out: {Out[Arena.ControlledIndex]}");
        }
    }
}
