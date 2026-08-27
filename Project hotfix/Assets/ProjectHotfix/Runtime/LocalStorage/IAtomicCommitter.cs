using System.IO;

namespace ProjectHotfix.LocalStorage
{
    internal interface IAtomicCommitter
    {
        void Move(string pendingPath, string currentPath);

        void Replace(string pendingPath, string currentPath, string lastGoodPath);
    }

    internal sealed class SystemAtomicCommitter : IAtomicCommitter
    {
        public void Move(string pendingPath, string currentPath)
        {
            File.Move(pendingPath, currentPath);
        }

        public void Replace(string pendingPath, string currentPath, string lastGoodPath)
        {
            File.Replace(pendingPath, currentPath, lastGoodPath, true);
        }
    }
}
