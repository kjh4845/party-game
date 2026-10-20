using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    public static class LedgeRecoveryReview
    {
        public static ArenaBoundaryReview Create(Scene scene,int count,bool hangingPose=true,bool raisedGrab=true)
        {
            var boundary=ArenaBoundaryReview.Create(scene,count);var arena=boundary.Arena;var roof=arena.transform.Find("ReviewRoof");
            roof.gameObject.AddComponent<ReviewGrabSurface>().RoofTop=roof;
            for(var side=0;side<4;side++)
            {
                var eastWest=side>=2;var sign=side%2==0?1:-1;
                var facade=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(facade,scene);facade.transform.SetParent(arena.transform,false);facade.name="FacadeGrab_"+side;
                facade.transform.position=new Vector3(eastWest?sign*(boundary.WidthCU*.5f-.06f):0,-.965f,eastWest?0:sign*(boundary.DepthCU*.5f-.06f));
                facade.transform.localScale=eastWest?new Vector3(.12f,1.37f,boundary.DepthCU):new Vector3(boundary.WidthCU,1.37f,.12f);
                facade.GetComponent<Renderer>().material.SetColor("_BaseColor",new Color(.24f,.30f,.35f));facade.AddComponent<ReviewGrabSurface>().RoofTop=roof;
            }
            foreach(var actor in arena.Actors)
            {
                var ledges=actor.gameObject.AddComponent<LedgeGrabReview>();ledges.Grabs=actor.Hands.Grabs;ledges.RaisedReach=raisedGrab;
                if(raisedGrab)actor.Hands.Grabs.EnableRaisedGrab();actor.Hands.Grabs.Ledges=ledges;
                if(hangingPose)actor.Pose.gameObject.AddComponent<HangingBodyPose>().Bind();
            }
            return boundary;
        }
    }
}
