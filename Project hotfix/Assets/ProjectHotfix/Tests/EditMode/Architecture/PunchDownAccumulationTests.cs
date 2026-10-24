using System;
using NUnit.Framework;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.Architecture.Tests
{
    public sealed class PunchDownAccumulationTests
    {
        [TestCase(PunchHitRegion.Body,10)] [TestCase(PunchHitRegion.Head,5)]
        public void FirstTenPointEpisodeCountsBodyAndHeadSeparately(PunchHitRegion region,int count)
        {
            var state=new PunchDownAccumulation(new PunchDownProfile(10,1,2,1,6),1);
            for(ulong i=1;i<=(ulong)count;i++)Assert.That(state.Receive(1,1,i,false,region),Is.EqualTo(i==(ulong)count ? PunchDownResult.ThresholdReached : PunchDownResult.Accumulated));
            Assert.That(state.Points,Is.EqualTo(10));Assert.That(state.Hits,Is.EqualTo(count));
        }
        [TestCase(0,10)] [TestCase(1,9)] [TestCase(4,6)] [TestCase(100,6)]
        public void ExplicitPointProfileUsesRegionAndBoundedEpisodeThreshold(int downs,int expected)
        {
            var profile=new PunchDownProfile(10,1,2,1,6);var state=new PunchDownAccumulation(profile,1);
            Assert.That(profile.RequiredPoints((uint)downs),Is.EqualTo(expected));
            ulong action=0;var points=0;
            while(points<expected)
            {
                var region=action%2==0 ? PunchHitRegion.Head : PunchHitRegion.Body;points+=region==PunchHitRegion.Head ? 2 : 1;
                Assert.That(state.Receive(1,1,++action,false,region,(uint)downs),Is.EqualTo(points>=expected ? PunchDownResult.ThresholdReached : PunchDownResult.Accumulated));
                Assert.That(state.Points,Is.EqualTo(Math.Min(points,expected)));
                Assert.That(state.Receive(1,1,action,false,PunchHitRegion.Head,(uint)downs),Is.EqualTo(PunchDownResult.Rejected));
            }
            state.ConsumeDown();Assert.That(state.Points,Is.Zero);
            state.Receive(1,1,++action,true,PunchHitRegion.Head,(uint)downs);Assert.That(state.Points,Is.Zero);
            state.Reset(2);Assert.That(state.Points,Is.Zero);Assert.That(profile.RequiredPoints(0),Is.EqualTo(10));
        }
        [Test] public void EachDownKeepsRequiredHitsFixedAndConsumesOnlyCurrentHits()
        {
            var state = new PunchDownAccumulation(new PunchDownProfile(5),1); ulong action = 0;
            for(uint downs=0;downs<6;downs++)
            {
                const int expected = 5; Assert.That(state.Profile.RequiredHits,Is.EqualTo(expected));
                for(var i=1;i<=expected;i++) Assert.That(state.Receive(1,1,++action,false),Is.EqualTo(i==expected ? PunchDownResult.ThresholdReached : PunchDownResult.Accumulated));
                state.ConsumeDown(); Assert.That(state.Hits,Is.Zero);
            }
            Assert.That(state.Profile.RequiredHits,Is.EqualTo(5));
        }
        [Test] public void DownHitsDoNotAccumulateAndCannotReplayAfterRecovery()
        {
            var state = new PunchDownAccumulation(new PunchDownProfile(5),1);
            Assert.That(state.Receive(1,1,1,true),Is.EqualTo(PunchDownResult.DownImpulseOnly)); Assert.That(state.Hits,Is.Zero);
            Assert.That(state.Receive(1,1,1,false),Is.EqualTo(PunchDownResult.Rejected)); Assert.That(state.Hits,Is.Zero);
        }
        [Test] public void IndependentAttackersAndOverlappingHandsDedupeWithoutDiscardingReverseOrder()
        {
            var state = new PunchDownAccumulation(new PunchDownProfile(5),1);
            state.Receive(1,1,2,false); state.Receive(1,1,1,false); state.Receive(1,2,1,false);
            Assert.That(state.Hits,Is.EqualTo(3));
            Assert.That(state.Receive(1,1,2,false),Is.EqualTo(PunchDownResult.Rejected));
            state.Receive(1,1,100,false);
            Assert.That(state.Receive(1,1,3,false),Is.EqualTo(PunchDownResult.Rejected));
        }
        [Test] public void ResetRejectsOldGenerationAndRestartsAtFirstThreshold()
        {
            var state = new PunchDownAccumulation(new PunchDownProfile(5),1);state.Receive(1,1,1,false);state.Reset(2);
            Assert.That(state.Hits,Is.Zero);Assert.That(state.Receive(1,1,2,false),Is.EqualTo(PunchDownResult.Rejected));
            Assert.That(state.Receive(2,1,2,false),Is.EqualTo(PunchDownResult.Accumulated));Assert.That(state.Profile.RequiredHits,Is.EqualTo(5));
        }
        [Test] public void InvalidProfilesAndIdsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PunchDownProfile(0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PunchDownProfile(-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PunchDownAccumulation(default,1));
            var state=new PunchDownAccumulation(new PunchDownProfile(5),1);
            Assert.That(state.Receive(1,0,1,false),Is.EqualTo(PunchDownResult.Rejected));Assert.That(state.Receive(1,1,0,false),Is.EqualTo(PunchDownResult.Rejected));
        }
    }
}
