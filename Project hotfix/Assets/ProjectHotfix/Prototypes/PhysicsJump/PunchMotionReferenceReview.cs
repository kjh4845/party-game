using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Stage-one authoring reference only. This scene has no Rigidbody, Collider,
    // hit authority, motor, or live combat controller. Direct bone posing here
    // must not be mistaken for the pending physical-punch integration.
    public sealed class PunchMotionReferenceReview : MonoBehaviour
    {
        public const float Duration=.4f;
        public enum ViewAngle { Front, Side, ThreeQuarter }
        [Serializable] public struct Key
        {
            public float time,shoulder,elbow,yaw,pelvisYaw,spineYaw,chestYaw,lean,drop,shift;
            public Key(float t,float s,float e,float y,float p,float sp,float c,float l,float d,float z)
            {time=t;shoulder=s;elbow=e;yaw=y;pelvisYaw=p;spineYaw=sp;chestYaw=c;lean=l;drop=d;shift=z;}
        }
        public static readonly Key[] Keys={
            new Key(0,0,0,0,0,0,0,0,0,0),
            new Key(.055f,15,115,2,-1,-.5f,-2,-1,.003f,-.002f),
            new Key(.085f,42,115,5,-1.5f,-1,-3,-1,.004f,-.002f),
            new Key(.105f,65,105,8,1.5f,1,3,4,.004f,.004f),
            new Key(.15f,86,8,12,2,1.5f,4,5,.004f,.004f),
            new Key(.167f,86,8,12,2,1.5f,4,5,.004f,.004f),
            new Key(.19f,80,25,10,1.8f,1.3f,3.5f,4,.0038f,.002f),
            new Key(.25f,32,100,4,.8f,.7f,1.5f,2,.002f,0),
            new Key(.35f,0,0,0,0,0,0,0,0,0),
            new Key(.4f,0,0,0,0,0,0,0,0,0)};
        private readonly Dictionary<string,Transform> _bones=new Dictionary<string,Transform>();
        private readonly Dictionary<string,Vector3> _positions=new Dictionary<string,Vector3>();
        private readonly Dictionary<string,Quaternion> _rotations=new Dictionary<string,Quaternion>();
        private readonly Dictionary<string,Quaternion> _frames=new Dictionary<string,Quaternion>();
        private readonly Dictionary<string,Quaternion> _relative=new Dictionary<string,Quaternion>();
        private readonly HashSet<UnityEngine.Object> _owned=new HashSet<UnityEngine.Object>();
        private readonly Vector3[] _feet=new Vector3[2],_kneePoles=new Vector3[2];
        private readonly Quaternion[] _footRotations=new Quaternion[2];
        private ShoulderHelperRig _helpers;
        private readonly float[] _upperLeg=new float[2],_lowerLeg=new float[2];
        private Transform _visual;private float _startTime;
        public bool ManualPlayback;
        public ViewAngle Angle=ViewAngle.ThreeQuarter;
        public Camera View {get;private set;}
        public bool Ready {get;private set;}
        public float FootError {get;private set;}
        public float FloorHeight {get;private set;}
        public Transform[] Bones {get;private set;}
        public SkinnedMeshRenderer[] Skins {get;private set;}
        public static string SkinPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916/candidate-r02/ShoulderCharacter.bytes"));
        public static PunchMotionReferenceReview Create(Scene scene)
        {
            var root=new GameObject("Punch motion reference - NO PHYSICS - Press Play");SceneManager.MoveGameObjectToScene(root,scene);
            var review=root.AddComponent<PunchMotionReferenceReview>();if(Application.isPlaying)review.Build();return review;
        }
        private void Start(){if(!Ready)Build();}
        private void Update()
        {
            if(!Ready||ManualPlayback)return;var t=Mathf.Repeat(Time.unscaledTime-_startTime,3);var side=t<1.5f?0:1;
            Evaluate(side,t-side*1.5f-1f/3);SetView(Angle,side);
        }
        private void Build()
        {
            if(Ready)return;_visual=new GameObject("Reference skin only").transform;_visual.SetParent(transform,false);
            PhysicsJumpReview.LoadSkin(_visual,SkinPath);
            Skins=_visual.GetComponentsInChildren<SkinnedMeshRenderer>();Bones=Skins[0].bones;
            foreach(var bone in Bones){_bones.Add(bone.name,bone);_positions.Add(bone.name,bone.localPosition);_rotations.Add(bone.name,bone.localRotation);}
            for(var side=0;side<2;side++)
            {
                var suffix=side==0?"L":"R";var thigh=_bones["Thigh_"+suffix];var calf=_bones["Calf_"+suffix];var foot=_bones["Foot_"+suffix];
                _feet[side]=foot.position;_footRotations[side]=foot.rotation;
                _upperLeg[side]=Vector3.Distance(thigh.position,calf.position);_lowerLeg[side]=Vector3.Distance(calf.position,foot.position);
                _kneePoles[side]=Vector3.ProjectOnPlane(calf.position-thigh.position,foot.position-thigh.position).normalized;
            }
            foreach(var name in new[]{"Spine","Chest","UpperArm_L","UpperArm_R","Forearm_L","Forearm_R"})
            {
                var bone=_bones[name];var parent=Parent(name);_frames[name]=Quaternion.Inverse(bone.rotation);
                _relative[name]=Quaternion.Inverse(parent.rotation)*bone.rotation;
            }
            _helpers=_visual.GetComponent<ShoulderHelperRig>();
            UpdateShoulders();foreach(var bone in Bones)if(ShoulderHelperRig.IsHelper(bone.name))_rotations[bone.name]=bone.localRotation;
            foreach(var skin in Skins){_owned.Add(skin.sharedMesh);_owned.Add(skin.sharedMaterial);}
            var bake=new Mesh();FloorHeight=float.PositiveInfinity;
            foreach(var skin in Skins){skin.BakeMesh(bake);foreach(var v in bake.vertices)FloorHeight=Mathf.Min(FloorHeight,skin.transform.TransformPoint(v).y);}
            Destroy(bake);CreateFloor();
            var cameraObject=new GameObject("Reference Camera");cameraObject.transform.SetParent(transform,false);View=cameraObject.AddComponent<Camera>();View.orthographic=true;View.orthographicSize=.58f;View.nearClipPlane=.01f;View.farClipPlane=20;View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.12f,.17f,.23f);
            var lamp=new GameObject("Reference Light");lamp.transform.SetParent(transform,false);var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.shadows=LightShadows.Soft;light.shadowStrength=.65f;light.transform.rotation=Quaternion.Euler(38,140,0);
            Ready=true;_startTime=Time.unscaledTime;Evaluate(0,-1);SetView(Angle,0);
        }
        private Transform Parent(string name)=>_bones[name=="Spine"?"Pelvis":name=="Chest"?"Spine":name.StartsWith("UpperArm")?"Chest":"UpperArm_"+(name.EndsWith("L")?"L":"R")];
        private void SetJoint(string name,Quaternion target)
        {
            var frame=_frames[name];_bones[name].rotation=Parent(name).rotation*_relative[name]*frame*Quaternion.Inverse(target)*Quaternion.Inverse(frame);
        }
        public static Key Sample(float age)
        {
            if(!float.IsFinite(age))throw new ArgumentOutOfRangeException(nameof(age));
            if(age<=0||age>=Duration)return Keys[0];
            var i=0;while(i<Keys.Length-2&&age>Keys[i+1].time)i++;
            var a=Keys[i];var b=Keys[i+1];var u=Mathf.Clamp01((age-a.time)/(b.time-a.time));
            // Acceleration through the strike; a crisp peak and recoil instead
            // of easing to a stop before the nominal impact pose.
            u=i==3?u*u:(i==1||i==2||i==5||i==6||i==7)?1-Mathf.Pow(1-u,3):u*u*(3-2*u);
            return new Key(age,Mathf.Lerp(a.shoulder,b.shoulder,u),Mathf.Lerp(a.elbow,b.elbow,u),Mathf.Lerp(a.yaw,b.yaw,u),Mathf.Lerp(a.pelvisYaw,b.pelvisYaw,u),Mathf.Lerp(a.spineYaw,b.spineYaw,u),Mathf.Lerp(a.chestYaw,b.chestYaw,u),Mathf.Lerp(a.lean,b.lean,u),Mathf.Lerp(a.drop,b.drop,u),Mathf.Lerp(a.shift,b.shift,u));
        }
        public static string Phase(float age)=>age<0||age>=.35f?"Rest":age<.085f?"Prepare":age<.15f?"Strike":age<.19f?"Impact pose":"Recover";
        public void Evaluate(int side,float age)
        {
            if(!Ready)throw new InvalidOperationException("Reference not built");if(side<0||side>1)throw new ArgumentOutOfRangeException(nameof(side));
            foreach(var b in Bones){b.localPosition=_positions[b.name];b.localRotation=_rotations[b.name];}
            FootError=0;var pose=Sample(age);if(age<=0||age>=.35f){UpdateShoulders();return;}
            var sign=side==0?1f:-1f;var pelvis=_bones["Pelvis"];
            pelvis.position+=Vector3.down*pose.drop+Vector3.forward*pose.shift;
            pelvis.rotation=Quaternion.AngleAxis(sign*pose.pelvisYaw,Vector3.up)*pelvis.rotation;
            SetJoint("Spine",Quaternion.AngleAxis(-pose.lean,Vector3.right)*Quaternion.AngleAxis(-sign*pose.spineYaw,Vector3.up));
            SetJoint("Chest",Quaternion.AngleAxis(-sign*pose.chestYaw,Vector3.up));
            var suffix=side==0?"L":"R";
            SetJoint("UpperArm_"+suffix,Quaternion.AngleAxis(pose.shoulder,Vector3.right)*Quaternion.AngleAxis(-sign*pose.yaw,Vector3.up));
            SetJoint("Forearm_"+suffix,Quaternion.AngleAxis(pose.elbow,Vector3.right));
            SolveFeet(pose.drop);UpdateShoulders();
        }
        private void SolveFeet(float drop)
        {
            for(var side=0;side<2;side++)
            {
                var suffix=side==0?"L":"R";var thigh=_bones["Thigh_"+suffix];var calf=_bones["Calf_"+suffix];var foot=_bones["Foot_"+suffix];
                var a=_upperLeg[side];var b=_lowerLeg[side];var delta=_feet[side]-thigh.position;var d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.000001f,a+b-.000001f);var direction=delta.normalized;
                var pole=Vector3.Slerp(_kneePoles[side],Vector3.forward,Mathf.Clamp01(drop/.004f)*.65f);var bend=Vector3.ProjectOnPlane(pole,direction).normalized;
                var along=(a*a+d*d-b*b)/(2*d);var height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));var knee=thigh.position+direction*along+bend*height;
                thigh.rotation=Swing(calf.position-thigh.position,knee-thigh.position)*thigh.rotation;
                calf.rotation=Swing(foot.position-calf.position,_feet[side]-calf.position)*calf.rotation;foot.rotation=_footRotations[side];
                FootError=Mathf.Max(FootError,Vector3.Distance(foot.position,_feet[side]));
                var deltaRotation=Quaternion.Inverse(_rotations[thigh.name])*thigh.localRotation;var angle=Mathf.DeltaAngle(0,2*Mathf.Atan2(deltaRotation.x,deltaRotation.w)*Mathf.Rad2Deg);
                var helper=_bones["HipDeform_"+suffix];helper.localRotation=_rotations[helper.name]*Quaternion.AngleAxis(angle*.25f,Vector3.right);
                var pelvisDelta=_bones["Pelvis"].rotation*Quaternion.Inverse(_bones["Pelvis"].parent.rotation*_rotations["Pelvis"]);
                helper.position+=pelvisDelta*new Vector3(0,.0005f,.0025f)*(angle/-25f);
            }
        }
        private static Quaternion Swing(Vector3 from,Vector3 to)
        {from.Normalize();to.Normalize();var cross=Vector3.Cross(from,to);return new Quaternion(cross.x,cross.y,cross.z,1+Vector3.Dot(from,to)).normalized;}
        private void UpdateShoulders()=>_helpers?.Apply();
        public Vector3 Tip(int side)=>_bones["HandLogical_"+(side==0?"L":"R")].TransformPoint(Vector3.back*.06f);
        public void SetView(ViewAngle view,int side)
        {
            var center=new Vector3(0,.46f,.07f);var offset=view==ViewAngle.Front?new Vector3(0,.13f,2.2f):view==ViewAngle.Side?new Vector3(side==0?-2.2f:2.2f,.28f,0):new Vector3(side==0?-1.8f:1.8f,.55f,1.6f);
            View.transform.SetPositionAndRotation(center+offset,Quaternion.LookRotation(-offset));
        }
        private void CreateFloor()
        {
            var floor=new GameObject("Reference floor - visual only");floor.transform.SetParent(transform,false);var mesh=new Mesh();var y=FloorHeight-.001f;
            mesh.vertices=new[]{new Vector3(-3,y,-3),new Vector3(3,y,-3),new Vector3(3,y,3),new Vector3(-3,y,3)};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateNormals();mesh.RecalculateBounds();
            floor.AddComponent<MeshFilter>().sharedMesh=mesh;var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.24f,.28f,.33f));floor.AddComponent<MeshRenderer>().sharedMaterial=material;_owned.Add(mesh);_owned.Add(material);
        }
        private void OnDestroy(){foreach(var item in _owned)if(item!=null)Destroy(item);}
    }
}
