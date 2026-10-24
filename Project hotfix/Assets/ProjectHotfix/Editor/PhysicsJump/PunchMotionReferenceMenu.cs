using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump.Editor
{
    public static class PunchMotionReferenceMenu
    {
        [MenuItem("Project Hotfix/Prototypes/Open Punch Motion Reference - No Physics")]
        public static void Open()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=PunchMotionReferenceReview.Create(SceneManager.GetActiveScene());Selection.activeGameObject=review.gameObject;
        }
    }
}
