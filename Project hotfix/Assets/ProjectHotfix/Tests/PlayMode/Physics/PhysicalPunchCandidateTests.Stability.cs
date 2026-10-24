using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        private string StabilityOutput=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/punch-stability-20260917"));
        [Serializable] private sealed class StabilityFrame
        {
            public int tick,hits,contacts;public float age,upperError,forearmError;
            public Vector3 tip,goal,upperVelocity,forearmVelocity;
            public Quaternion upperTarget,forearmTarget,upperRotation,forearmRotation;
        }
        [Serializable] private sealed class StabilityRun {public string name;public StabilityFrame[] frames;public string[] contacts;}
        [Serializable] private sealed class StabilityReport {public StabilityRun[] runs;}
        [UnityTest,Timeout(600000)] public IEnumerator DiagnosePunchOscillation()=>DiagnosePunchOscillationCases(new[]{"Baseline","NoSelfContacts","NoLimits","Damping3","Damping5","Spring4Damping2","Spring4Damping4","Spring2Damping2"},"Ablations");
        [UnityTest,Timeout(600000)] public IEnumerator DiagnosePunchJointLimits()=>DiagnosePunchOscillationCases(new[]{"Baseline","NoUpperLimits","NoForearmLimits","NoUpperX","NoUpperY","NoUpperZ","NoForearmX","NoForearmY","NoForearmZ","ForearmSwing15","ForearmSwing30"},"JointAblations");
        private IEnumerator DiagnosePunchOscillationCases(string[] modes,string output)
        {
            var runs=new List<StabilityRun>();Directory.CreateDirectory(StabilityOutput);
            foreach(var mode in modes)
            {
                Create(.7f);var rig=_pair.Players[0].Rig;var upper=rig.Find("UpperArm_L");var forearm=rig.Find("Forearm_L");
                var contactRows=new List<string>();var currentTick=0;var contacts=0;
                rig.PhysicalContact+=(own,c)=>{if(own!=upper.Body&&own!=forearm.Body)return;contacts++;if(contactRows.Count<200)contactRows.Add($"{currentTick}: {own.name} / {c.collider.name} impulse={c.impulse.magnitude:R}");};
                if(mode=="NoSelfContacts")foreach(var a in rig.Shapes)foreach(var b in rig.Shapes)if(a!=b)UnityEngine.Physics.IgnoreCollision(a,b,true);
                var run=new StabilityRun{name=mode,frames=new StabilityFrame[100]};
                for(var t=0;t<100;t++)
                {
                    currentTick=t;contacts=0;if(t==10)Buttons(0,1);if(t==12)Buttons(0,0);_pair.Step(Dt);
                    if(_pair.PhysicalPunches.IsPunching(0,0))
                    {
                        if(mode=="NoLimits")upper.Joint.angularXMotion=upper.Joint.angularYMotion=upper.Joint.angularZMotion=forearm.Joint.angularXMotion=forearm.Joint.angularYMotion=forearm.Joint.angularZMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoUpperLimits")upper.Joint.angularXMotion=upper.Joint.angularYMotion=upper.Joint.angularZMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoForearmLimits")forearm.Joint.angularXMotion=forearm.Joint.angularYMotion=forearm.Joint.angularZMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoUpperX")upper.Joint.angularXMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoUpperY")upper.Joint.angularYMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoUpperZ")upper.Joint.angularZMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoForearmX")forearm.Joint.angularXMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoForearmY")forearm.Joint.angularYMotion=ConfigurableJointMotion.Free;
                        if(mode=="NoForearmZ")forearm.Joint.angularZMotion=ConfigurableJointMotion.Free;
                        if(mode.StartsWith("ForearmSwing"))forearm.Joint.angularYLimit=forearm.Joint.angularZLimit=new SoftJointLimit{limit=mode=="ForearmSwing15"?15:30};
                        if(mode.StartsWith("Damping")||mode.StartsWith("Spring"))foreach(var link in new[]{upper,forearm})
                        {
                            var spring=mode.StartsWith("Spring4")?4:mode.StartsWith("Spring2")?2:8;var damper=mode=="Damping3"?3:mode=="Damping5"?5:mode=="Spring4Damping4"?4:2;
                            var d=link.Drive;d.positionSpring*=spring;d.positionDamper*=damper;d.maximumForce*=3;link.Joint.slerpDrive=d;
                        }
                    }
                    _scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();
                    run.frames[t]=new StabilityFrame{tick=t,hits=_pair.PhysicalPunches.Hits.Count,contacts=contacts,age=(float)_pair.PhysicalPunches.State(0).SourcePunchAge(0),tip=rig.HandPosition(0),goal=rig.GoalHand(0),upperVelocity=upper.Body.angularVelocity,forearmVelocity=forearm.Body.angularVelocity,upperTarget=upper.Joint.targetRotation,forearmTarget=forearm.Joint.targetRotation,upperRotation=upper.Body.rotation,forearmRotation=forearm.Body.rotation,upperError=rig.TrackingError("UpperArm_L"),forearmError=rig.TrackingError("Forearm_L")};
                }
                run.contacts=contactRows.ToArray();runs.Add(run);File.WriteAllText(Path.Combine(StabilityOutput,output+".json"),JsonUtility.ToJson(new StabilityReport{runs=runs.ToArray()},true));
                Assert.That(_pair.PhysicalPunches.Hits.Count,Is.Zero);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
