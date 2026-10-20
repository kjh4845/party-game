using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        private void Tap(int player,int side)
        {Buttons(player,1<<side);Tick();Tick();Buttons(player,0);}
        [UnityTest] public IEnumerator ProbeHeadHeightAndSides()
        {
            var rows=new List<string>();
            foreach(var drop in new[]{0f,.10f,.15f,.20f,.25f})foreach(var side in new[]{0,1})
            {
                Create(.38f,drop);Tap(0,side);for(var tick=0;tick<50;tick++)Tick();
                rows.Add($"drop={drop} side={side} hits={_pair.PhysicalPunches.Hits.Count} points={_pair.PhysicalPunches.Score(1).Points} contacts={_pair.PhysicalPunches.PhysicalContactsInWindow} radius={_pair.PhysicalPunches.RejectedOutsideTip} motion={_pair.PhysicalPunches.RejectedMotion}");foreach(var h in _pair.PhysicalPunches.Hits)rows.Add(JsonUtility.ToJson(h));File.WriteAllLines(Path.Combine(Output,"Regions.txt"),rows);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
