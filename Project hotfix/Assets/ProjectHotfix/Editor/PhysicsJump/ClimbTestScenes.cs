using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump.Editor
{
    // Playable test scenes (2026-10-26; third person and press-to-punch since 2026-10-28): the fight setup (slim body,
    // BodyStrike), on open ground or with a wall between the two players, a .55 m ledge or a 1.6 m wall as in the
    // WallClimb / WallClimbHigh captures. Player 1: mouse to look, WASD relative to the view, click to punch, hold to grab,
    // Space to jump; player 2: arrows (relative to player 1's view), N/M, right Ctrl. R resets, Esc frees the cursor.
    public static class ClimbTestScenes
    {
        private const string Folder="Assets/ProjectHotfix/Scenes";
        [MenuItem("Project Hotfix/Prototypes/Climb Test/Create Low And High Wall Scenes")]
        public static void CreateBoth()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/ProjectHotfix","Scenes");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/ClimbWall.mat");
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.55f,.6f,.66f));AssetDatabase.CreateAsset(material,Folder+"/ClimbWall.mat");}
            Create("FightTest",0,1.5f,material);
            Create("ClimbTest_LowWall",.55f,3f,material);
            Create("ClimbTest_HighWall",1.6f,3f,material);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Folder+"/FightTest.unity");
        }
        private static void Create(string name,float height,float separation,Material material)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // Relative to the repository root (resolved at Play), so the saved scene works on any clone.
            const string skin="BlenderSource/Characters/C1B-RW-046-slim-body/R43Character.bytes";
            var review=ActiveGrabPairReview.Create(scene,separation,ActiveGrabPairReview.GrabMotion.AdaptiveReach,skin,matchHandEnvelope:true);
            review.EnableStandingSkinContacts=review.EnableLiftThrow=review.EnablePhysicalPunch=true;
            review.UseStrikeStyle(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);review.UseFightThrows();
            review.ThirdPerson=review.PunchOnPress=true;
            if(height>0)
            {
                // Faces .8 m in front of each player (spawned at z -1.5 and 1.5), 3 m wide.
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name=height<1?"Low wall":"High wall";wall.isStatic=true;
                wall.transform.position=new Vector3(0,height*.5f,0);wall.transform.localScale=new Vector3(3f,height,1.4f);
                wall.GetComponent<Renderer>().sharedMaterial=material;
            }
            EditorSceneManager.SaveScene(scene,Folder+"/"+name+".unity");
        }
    }
}
