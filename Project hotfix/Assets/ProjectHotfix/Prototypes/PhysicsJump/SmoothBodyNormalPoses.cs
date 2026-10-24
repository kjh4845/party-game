using System;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Presentation-only normal correctives. Every blend-shape position delta
    // is zero: physics, skin contact positions, and punch poses stay unchanged.
    [DefaultExecutionOrder(200)]
    public sealed class SmoothBodyNormalPoses : MonoBehaviour
    {
        [Serializable] private sealed class Reference
        {
            public int Side,BlendIndex;
            public Quaternion Rotation;
            [NonSerialized] public float Desired;
        }
        [SerializeField] private SkinnedMeshRenderer _skin;
        [SerializeField] private Transform _chest,_left,_right;
        [SerializeField] private Reference[] _references=Array.Empty<Reference>();

        public void Bind(SkinnedMeshRenderer skin,Transform[] bones)
        {
            _skin=skin;
            _chest=Array.Find(bones,b=>b.name=="Chest");
            _left=Array.Find(bones,b=>b.name=="UpperArm_L");
            _right=Array.Find(bones,b=>b.name=="UpperArm_R");
        }
        public void AddReference(int side,Quaternion rotation,int blendIndex)
        {
            Array.Resize(ref _references,_references.Length+1);
            _references[_references.Length-1]=new Reference{Side=side,Rotation=rotation,BlendIndex=blendIndex};
        }
        public void ApplyCurrentPose()
        {
            if(_skin==null||!_skin.enabled||_chest==null||_left==null||_right==null)return;
            foreach(var reference in _references)reference.Desired=0;
            for(var side=0;side<2;side++)
            {
                var current=Quaternion.Inverse(_chest.rotation)*(side==0?_left.rotation:_right.rotation);
                var total=0f;var nearest=float.PositiveInfinity;
                foreach(var reference in _references)
                {
                    if(reference.Side!=side)continue;
                    var distance=Quaternion.Angle(current,reference.Rotation);
                    nearest=Mathf.Min(nearest,distance);
                    var squared=Mathf.Max(.0025f,distance*distance);
                    reference.Desired=1/(squared*squared);total+=reference.Desired;
                }
                if(total<=0)continue;
                // All references contribute continuously; switching the two
                // nearest examples would make lighting pop at their boundary.
                var coverage=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(45,85,nearest));
                foreach(var reference in _references)if(reference.Side==side)
                {
                    reference.Desired*=100/total;
                    if(reference.BlendIndex>=0)reference.Desired*=coverage;
                }
            }
            foreach(var reference in _references)
                if(reference.BlendIndex>=0&&Mathf.Abs(_skin.GetBlendShapeWeight(reference.BlendIndex)-reference.Desired)>.001f)
                    _skin.SetBlendShapeWeight(reference.BlendIndex,reference.Desired);
        }
        private void LateUpdate()=>ApplyCurrentPose();
    }
}
