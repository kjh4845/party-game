using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class PhysicalPunchClockTests : InputTestFixture
    {
        [Test] public void KeyboardTapDurationsSurviveLowFpsAndCatchUp()
        {
            var keyboard=InputSystem.AddDevice<Keyboard>();
            foreach(var rate in new[]{1.4,1.75})foreach(var fps in new[]{15,60,144})
            {
                ReviewInputClock.Install();var state=new GroundHandActionStateMachine(.15,rate);var sequence=0UL;var offset=currentTime+10;var times=new[]{.0991,.2461,.8991,1.0521,1.7001,1.7101};
                for(var i=0;i<times.Length;i++)InputSystem.QueueStateEvent(keyboard,i%2==0?new KeyboardState(Key.N,Key.M):new KeyboardState(),offset+times[i]);
                var tick=0;var renderTime=0d;var edges=0;var durations=new List<double>();
                while(tick<150)
                {
                    renderTime=Math.Min(2.5,renderTime+1d/fps);var due=Math.Min(150,(int)Math.Floor(renderTime*60+1e-8));
                    while(tick<due)
                    {
                        tick++;var time=tick/60d;currentTime=offset+time;
                        var runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(this);runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime,offset+time);runtime.GetType().GetMethod("Update").Invoke(runtime,new object[]{InputUpdateType.Fixed});
                        foreach(var edge in ReviewInputClock.KeyboardEdges)if(!edge.Down&&!double.IsNaN(edge.HeldSeconds))state.PrepareTimedRelease(edge.Side,time,edge.HeldSeconds);
                        foreach(var edge in ReviewInputClock.KeyboardEdges)
                        {edges++;double? held=double.IsNaN(edge.HeldSeconds)?null:(double?)edge.HeldSeconds;state.Receive(++sequence,edge.Side,edge.Down,time,true,true,held);if(held.HasValue)durations.Add(held.Value);}
                        state.Advance(time);
                    }
                }
                Assert.That(edges,Is.EqualTo(12));Assert.That(state.PunchCount,Is.EqualTo(4));Assert.That(durations[0],Is.EqualTo(.147).Within(1e-8));Assert.That(durations[2],Is.EqualTo(.153).Within(1e-8));
            }
        }
    }
}
