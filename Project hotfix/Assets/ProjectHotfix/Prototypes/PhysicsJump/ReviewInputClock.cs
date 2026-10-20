using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProjectHotfix.PhysicsJump
{
    public static class ReviewInputClock
    {
        public readonly struct MouseEdge
        {
            public readonly int Device,Side;public readonly bool Down;public readonly double HeldSeconds;
            public MouseEdge(int device,int side,bool down,double held){Device=device;Side=side;Down=down;HeldSeconds=held;}
        }
        private static readonly List<MouseEdge> Pending=new List<MouseEdge>();
        private static readonly List<MouseEdge> PendingKeyboard=new List<MouseEdge>();
        private static readonly Dictionary<int,double[]> PressTimes=new Dictionary<int,double[]>();
        public static IReadOnlyList<MouseEdge> MouseEdges { get; private set; }=Array.Empty<MouseEdge>();
        public static IReadOnlyList<MouseEdge> KeyboardEdges { get; private set; }=Array.Empty<MouseEdge>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            InputSystem.onEvent-=OnEvent;InputSystem.onEvent+=OnEvent;InputSystem.onAfterUpdate-=Publish;InputSystem.onAfterUpdate+=Publish;
            Pending.Clear();PendingKeyboard.Clear();PressTimes.Clear();MouseEdges=KeyboardEdges=Array.Empty<MouseEdge>();
        }
        private static void OnEvent(InputEventPtr input,InputDevice device)
        {
            if(InputState.currentUpdateType!=InputUpdateType.Fixed||(!input.IsA<StateEvent>()&&!input.IsA<DeltaStateEvent>()))return;
            if(!(device is Mouse)&&!(device is Keyboard))return;
            if(!PressTimes.TryGetValue(device.deviceId,out var times)){times=new[]{double.NaN,double.NaN};PressTimes.Add(device.deviceId,times);}
            for(var side=0;side<2;side++)
            {
                UnityEngine.InputSystem.Controls.ButtonControl control;
                if(device is Mouse mouse)control=side==0?mouse.leftButton:mouse.rightButton;
                else {var keys=(Keyboard)device;control=side==0?keys.nKey:keys.mKey;}
                if(!control.ReadValueFromEvent(input,out var value))continue;
                var down=value>=control.pressPointOrDefault;if(down==control.isPressed)continue;
                var held=double.NaN;
                if(down)times[side]=input.time;
                else{if(!double.IsNaN(times[side]))held=Math.Max(0,input.time-times[side]);times[side]=double.NaN;}
                (device is Mouse?Pending:PendingKeyboard).Add(new MouseEdge(device.deviceId,side,down,held));
            }
        }
        private static void Publish()
        {
            if(InputState.currentUpdateType!=InputUpdateType.Fixed)return;
            MouseEdges=Pending.ToArray();KeyboardEdges=PendingKeyboard.ToArray();Pending.Clear();PendingKeyboard.Clear();
        }
    }
}
