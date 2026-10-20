using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace ProjectHotfix.PhysicsJump
{
    public static class ReviewFixedLoop
    {
        private sealed class PostPhysicsReview { }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var loop=PlayerLoop.GetCurrentPlayerLoop();Insert(ref loop);PlayerLoop.SetPlayerLoop(loop);
        }
        private static void Insert(ref PlayerLoopSystem parent)
        {
            if(parent.subSystemList==null)return;
            var children=new List<PlayerLoopSystem>();
            foreach(var original in parent.subSystemList)
            {
                if(original.type==typeof(PostPhysicsReview))continue;
                var child=original;Insert(ref child);children.Add(child);
                if(child.type==typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate))
                    children.Add(new PlayerLoopSystem{type=typeof(PostPhysicsReview),updateDelegate=Tick});
            }
            parent.subSystemList=children.ToArray();
        }
        private static void Tick()
        {
            foreach(var pair in ActiveGrabPairReview.Live)
                if(pair!=null&&pair.isActiveAndEnabled&&pair.Ready&&!pair.ManualStep)pair.CapturePhysics();
            foreach(var lab in ActiveUpperBodyReview.Live)
                if(lab!=null&&lab.isActiveAndEnabled&&lab.Ready&&!lab.ManualStep)lab.CapturePhysics();
            foreach(var ledge in LedgeGrabReview.Live)
                if(ledge!=null&&ledge.isActiveAndEnabled&&ledge.Grabs!=null&&!ledge.Grabs.Hands.Actor.Motor.ManualStep)ledge.AfterPhysics();
            foreach(var arena in ArenaBoundaryReview.Live)
                if(arena!=null&&arena.isActiveAndEnabled&&!arena.ManualStep)arena.StepAfterPhysics();
            foreach(var pose in FixedReviewPose.Live)
                if(pose!=null&&pose.isActiveAndEnabled&&pose.Actor!=null&&!pose.Actor.Motor.ManualStep)pose.Advance(Time.fixedDeltaTime);
            foreach(var camera in SharedCameraReview.Live)
                if(camera!=null&&camera.isActiveAndEnabled&&!camera.ManualStep&&camera.View!=null)camera.AdvanceFixed(Time.fixedDeltaTime);
        }
        public static float RenderAlpha => Mathf.Clamp01((float)((Time.timeAsDouble-Time.fixedTimeAsDouble)/Time.fixedDeltaTime));
    }
}
