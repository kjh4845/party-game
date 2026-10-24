using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;
using UnityEngine;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class SharedCameraFramingTests
    {
        [TestCase(0)] [TestCase(45)] [TestCase(90)] [TestCase(180)] [TestCase(270)]
        public void ScreenMovementKeepsProjectedDirectionsAndDiagonalSpeed(float yaw)
        {
            var rotation=Quaternion.Euler(34,yaw,0);
            foreach(var input in new[]{Vector2.up,Vector2.down,Vector2.left,Vector2.right,Vector2.one})
            {
                var move=SharedCameraFraming.ScreenMove(input,rotation);var view=Quaternion.Inverse(rotation)*move;
                Assert.That(move.y,Is.EqualTo(0).Within(.00001));Assert.That(move.magnitude,Is.EqualTo(1).Within(.00001));
                if(input.x!=0)Assert.That(view.x*input.x,Is.GreaterThan(0));if(input.y!=0)Assert.That(view.y*input.y,Is.GreaterThan(0));
            }
        }
        [TestCase(16f/9)] [TestCase(16f/10)] [TestCase(21f/9)]
        public void PerspectiveDistanceIncludesEveryCornerAndDepth(float aspect)
        {
            var bounds=new Bounds(new Vector3(1,.6f,-.5f),new Vector3(8,1.5f,6));var focus=new Vector3(.7f,.8f,-.4f);var rotation=Quaternion.Euler(34,25,0);
            var d=SharedCameraFraming.RequiredDistance(bounds,focus,rotation,42,aspect,.15f,.05f);
            for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2)
            {
                var p=Quaternion.Inverse(rotation)*(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))-focus);p.z+=d;
                var vertical=Mathf.Tan(21*Mathf.Deg2Rad)*p.z;
                Assert.That(p.z,Is.GreaterThan(.05));Assert.That(Mathf.Abs(p.x)/(vertical*aspect),Is.LessThanOrEqualTo(.70001));Assert.That(Mathf.Abs(p.y)/vertical,Is.LessThanOrEqualTo(.70001));
            }
        }
        [Test] public void InvalidProjectionDoesNotEmitNonfiniteCameraTargets()
        {
            foreach(var aspect in new[]{0f,-1,float.NaN,float.PositiveInfinity})
                Assert.Throws<ArgumentOutOfRangeException>(()=>SharedCameraFraming.RequiredDistance(new Bounds(Vector3.zero,Vector3.one),Vector3.zero,Quaternion.identity,42,aspect,.15f,.05f));
            Assert.Throws<ArgumentException>(()=>SharedCameraFraming.ScreenMove(new Vector2(float.NaN,0),Quaternion.identity));
        }
    }
}
