using UnityEditor;

namespace ProjectHotfix.Editor.C1B005
{
    public sealed class C1B005ModelPostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder()
        {
            return -1000;
        }

        private void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, C1B005ImportContract.ModelPath, System.StringComparison.Ordinal))
            {
                return;
            }

            C1B005ImportContract.ApplyExactImporterSettings((ModelImporter)assetImporter);
        }
    }
}
