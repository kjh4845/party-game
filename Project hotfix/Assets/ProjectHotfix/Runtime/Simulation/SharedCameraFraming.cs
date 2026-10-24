using System;
using UnityEngine;

namespace ProjectHotfix.Simulation
{
    public static class SharedCameraFraming
    {
        private static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
        public static Vector3 ScreenMove(Vector2 input, Quaternion orientation)
        {
            if(!Finite(input.x)||!Finite(input.y))throw new ArgumentException("Nonfinite movement input.");
            input=Vector2.ClampMagnitude(input,1);
            var forward=Vector3.ProjectOnPlane(orientation*Vector3.forward,Vector3.up);
            if(forward.sqrMagnitude<1e-8f)throw new ArgumentException("Camera forward must define a ground direction.");
            forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
            return right*input.x+forward*input.y;
        }
        // Distance along the fixed view axis. Includes depth, not just AABB width/height.
        public static float RequiredDistance(Bounds bounds,Vector3 focus,Quaternion orientation,float verticalFov,float aspect,float margin,float near)
        {
            if(!Finite(aspect)||!Finite(verticalFov)||!Finite(margin)||!Finite(near)||aspect<=0||verticalFov<=0||verticalFov>=170||margin<0||margin>=.5f||near<=0)
                throw new ArgumentOutOfRangeException(nameof(aspect));
            var vertical=Mathf.Tan(verticalFov*Mathf.Deg2Rad*.5f)*(1-2*margin);var horizontal=vertical*aspect;
            var inverse=Quaternion.Inverse(orientation);var required=0f;
            for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2)
            {
                var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z));var local=inverse*(point-focus);
                required=Mathf.Max(required,Mathf.Abs(local.x)/horizontal-local.z,Mathf.Abs(local.y)/vertical-local.z,near+.05f-local.z);
            }
            return required;
        }
    }
}
