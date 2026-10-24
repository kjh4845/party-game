using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Local review feedback, emitted by a confirmed Hit event only. It never owns
    // timing, contacts or damage. Everything here is presentation:
    // - sound (the same PCM is used in the review capture)
    // - hit stop: attacker and victim hold the impact frame for a few ticks
    // - a short emissive flash on the victim and a burst at the contact point
    // - a small camera shake for head hits only (the camera is shared)
    // Effect ages advance with physics ticks so captures are frame-rate independent.
    public sealed class PhysicalPunchFeedbackReview : MonoBehaviour
    {
        public const int SampleRate=48000;
        public const float Volume=.55f;
        public bool HitStop=true,Flash=true,Burst=true,Shake=true;
        public int HitStopTicks=4,HitStopCatchUpTicks=3,FlashTicks=6,BurstTicks=8,ShakeTicks=8;
        public float FlashIntensity=.5f,BurstSize=.15f,HeadShake=.018f;
        private AudioSource _source;
        private readonly AudioClip[] _clips=new AudioClip[2];
        private sealed class FlashState {public Material Material;public Color Emission;public bool HadKeyword;public int Age;public float Scale=1;}
        private sealed class BurstState {public Transform Transform;public Vector3 Point;public float Roll,Scale;public int Age;}
        private readonly List<FlashState> _flashes=new List<FlashState>();
        private readonly List<BurstState> _bursts=new List<BurstState>();
        private Mesh _burstMesh;private Material _burstMaterial;
        private int _shakeAge=-1;private float _shakeAmplitude;private Vector3 _shakeOffset;private Camera _shakeView;
        public int ConfirmedCues {get;private set;}
        public int HitStops {get;private set;}
        public static float[] Samples(bool head)
        {
            var samples=new float[(int)(SampleRate*.11)];uint noise=0x5d1f3a;
            for(var i=0;i<samples.Length;i++)
            {
                var t=i/(double)SampleRate;noise=1664525u*noise+1013904223u;
                var n=(noise>>8)/8388607.5-1;
                var attack=Math.Min(1,t/.0015);var end=Math.Min(1,(samples.Length-1-i)/(SampleRate*.008));
                var thump=Math.Sin(2*Math.PI*((head?185:135)*t-220*t*t))*Math.Exp(-t*38);
                var tap=n*Math.Exp(-t*135)*(head?.34:.23);
                samples[i]=(float)((thump*.65+tap)*attack*end);
            }
            return samples;
        }
        public void Confirm(bool head,bool audible,Camera view)=>Confirm(head,audible,view,null,null,Vector3.zero,Vector3.forward,1);
        // An uncounted hit on a body getting up gets only a small burst and a quiet thud: no hit-stop, flash or shake.
        // One on a body lying knocked out (lying) lands like a hit, hit-stop, burst and thud, with a softer flash and no
        // shake, though it still counts for nothing (2026-10-24 review: the soft cue had no punch to it).
        public float LyingFlashScale=.5f;
        public void Confirm(bool head,bool audible,Camera view,ActiveUpperBodyReview attacker,ActiveUpperBodyReview target,Vector3 point,Vector3 direction,float strength,bool counted=true,bool lying=false)
        {
            ConfirmedCues++;
            if(!counted&&!lying){if(Burst&&target!=null)StartBurst(point,.35f);if(audible)Play(false,.5f,view);return;}
            if(lying)
            {
                if(HitStop&&attacker!=null&&target!=null){attacker.Actor.FixedPose.HoldPresentation(HitStopTicks,HitStopCatchUpTicks);target.Actor.FixedPose.HoldPresentation(HitStopTicks,HitStopCatchUpTicks);HitStops++;}
                if(Flash&&target!=null)foreach(var skin in target.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())StartFlash(skin.sharedMaterial,LyingFlashScale);
                if(Burst&&target!=null)StartBurst(point,Mathf.Clamp(strength,.5f,1.3f));
                if(audible)Play(head,Mathf.Lerp(.8f,1.15f,Mathf.InverseLerp(.65f,1.3f,strength)),view);
                return;
            }
            if(HitStop&&attacker!=null&&target!=null)
            {
                attacker.Actor.FixedPose.HoldPresentation(HitStopTicks,HitStopCatchUpTicks);
                target.Actor.FixedPose.HoldPresentation(HitStopTicks,HitStopCatchUpTicks);
                HitStops++;
            }
            if(Flash&&target!=null)
                foreach(var skin in target.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())StartFlash(skin.sharedMaterial);
            if(Burst&&target!=null)StartBurst(point,Mathf.Clamp(strength,.5f,1.3f));
            if(Shake&&head&&view!=null){_shakeAge=0;_shakeAmplitude=HeadShake*Mathf.Clamp(strength,.5f,1.3f);_shakeView=view;}
            if(audible)Play(head,Mathf.Lerp(.8f,1.15f,Mathf.InverseLerp(.65f,1.3f,strength)),view);
        }
        private void Play(bool head,float loudness,Camera view)
        {
            if(_source==null)
            {
                _source=gameObject.AddComponent<AudioSource>();_source.playOnAwake=false;_source.spatialBlend=0;
                if(UnityEngine.Object.FindFirstObjectByType<AudioListener>()==null&&view!=null)view.gameObject.AddComponent<AudioListener>();
            }
            var index=head?1:0;
            if(_clips[index]==null)
            {
                var pcm=Samples(head);_clips[index]=AudioClip.Create(head?"Review head contact":"Review body contact",pcm.Length,1,SampleRate,false);_clips[index].SetData(pcm,0);
            }
            _source.PlayOneShot(_clips[index],Volume*loudness);
        }
        private void StartFlash(Material material,float scale=1)
        {
            if(material==null)return;
            var existing=_flashes.Find(f=>f.Material==material);
            if(existing!=null){existing.Age=0;existing.Scale=scale;return;}
            _flashes.Add(new FlashState{Material=material,HadKeyword=material.IsKeywordEnabled("_EMISSION"),Scale=scale,
                Emission=material.HasProperty("_EmissionColor")?material.GetColor("_EmissionColor"):Color.black});
            material.EnableKeyword("_EMISSION");
        }
        private void StartBurst(Vector3 point,float strength)
        {
            if(_burstMesh==null)
            {
                _burstMesh=StarMesh(8,.28f);
                _burstMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));_burstMaterial.SetColor("_BaseColor",new Color(1f,.93f,.62f));
            }
            var go=new GameObject("Hit burst");go.transform.SetParent(transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=_burstMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_burstMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            go.SetActive(false);
            _bursts.Add(new BurstState{Transform=go.transform,Point=point,Roll=(ConfirmedCues*47)%360,Scale=strength});
        }
        private static Mesh StarMesh(int points,float inner)
        {
            var vertices=new Vector3[points*2+1];var triangles=new int[points*2*3];vertices[0]=Vector3.zero;
            for(var i=0;i<points*2;i++)
            {
                var angle=Mathf.PI*i/points;var radius=i%2==0?1:inner;
                vertices[i+1]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);
            }
            for(var i=0;i<points*2;i++){triangles[i*3]=0;triangles[i*3+1]=1+(i+1)%(points*2);triangles[i*3+2]=1+i;}
            var mesh=new Mesh{name="Hit burst star",vertices=vertices,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
        // Called once per physics tick by the punch review, before this tick's hits.
        public void AdvanceTick()
        {
            foreach(var f in _flashes)f.Age++;
            foreach(var b in _bursts)b.Age++;
            if(_shakeAge>=0&&++_shakeAge>ShakeTicks)_shakeAge=-1;
        }
        // Called after the characters' fixed poses are presented and the camera framed.
        private Camera _lastView;
        // Bursts face whichever camera renders this frame (a capture may move it after Present).
        private void LateUpdate()
        {
            if(_lastView==null)return;
            foreach(var b in _bursts)if(b.Transform!=null&&b.Transform.gameObject.activeSelf)
                b.Transform.SetPositionAndRotation(b.Point-_lastView.transform.forward*.06f,_lastView.transform.rotation*Quaternion.AngleAxis(b.Roll,Vector3.forward));
        }
        public void Present(Camera view)
        {
            _lastView=view;
            for(var i=_flashes.Count-1;i>=0;i--)
            {
                var f=_flashes[i];
                if(f.Material==null){_flashes.RemoveAt(i);continue;}
                if(f.Age>=FlashTicks)
                {
                    f.Material.SetColor("_EmissionColor",f.Emission);if(!f.HadKeyword)f.Material.DisableKeyword("_EMISSION");
                    _flashes.RemoveAt(i);continue;
                }
                var k=1-f.Age/(float)FlashTicks;
                f.Material.SetColor("_EmissionColor",f.Emission+Color.white*(FlashIntensity*f.Scale*k*k));
            }
            for(var i=_bursts.Count-1;i>=0;i--)
            {
                var b=_bursts[i];
                if(b.Age>=BurstTicks){if(b.Transform!=null)Destroy(b.Transform.gameObject);_bursts.RemoveAt(i);continue;}
                var grow=Mathf.SmoothStep(0,1,(b.Age+1)/2.5f);var fade=1-Mathf.SmoothStep(0,1,(b.Age-2)/(float)(BurstTicks-2));
                var size=BurstSize*b.Scale*grow*fade;
                b.Transform.gameObject.SetActive(size>.002f);
                var facing=view!=null?view.transform.rotation:Quaternion.identity;
                // Pushed slightly toward the camera so the burst is not hidden inside the skin.
                var toward=view!=null?-view.transform.forward*.06f:Vector3.zero;
                b.Transform.SetPositionAndRotation(b.Point+toward,facing*Quaternion.AngleAxis(b.Roll,Vector3.forward));
                b.Transform.localScale=Vector3.one*size;
            }
            if(_shakeView!=null&&_shakeView==view)
            {
                _shakeOffset=Vector3.zero;
                if(_shakeAge>=0)
                {
                    var k=1-_shakeAge/(float)ShakeTicks;var phase=_shakeAge*2.1f;
                    _shakeOffset=(view.transform.right*Mathf.Sin(phase)+view.transform.up*Mathf.Cos(phase*1.3f))*_shakeAmplitude*k*k;
                }
                view.transform.position+=_shakeOffset;
            }
        }
        public void Clear()
        {
            ConfirmedCues=HitStops=0;if(_source!=null)_source.Stop();
            foreach(var f in _flashes)if(f.Material!=null){f.Material.SetColor("_EmissionColor",f.Emission);if(!f.HadKeyword)f.Material.DisableKeyword("_EMISSION");}
            _flashes.Clear();
            foreach(var b in _bursts)if(b.Transform!=null)Destroy(b.Transform.gameObject);
            _bursts.Clear();_shakeAge=-1;
        }
        private void OnDisable(){if(_source!=null)_source.Stop();}
        private void OnDestroy()
        {
            foreach(var clip in _clips)if(clip!=null)Destroy(clip);
            if(_burstMesh!=null)Destroy(_burstMesh);if(_burstMaterial!=null)Destroy(_burstMaterial);
        }
    }
}
