using System.IO;
using System.Reflection;

namespace DDEN.LoongNova
{
    internal static class ContentResources
    {
        internal static Stream Open(Assembly assembly, string path)
        {
            string name = typeof(ContentResources).Namespace + "." + path.Replace('/', '.').Replace('\\', '.');
            return assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException("Missing embedded resource: " + name, name);
        }
    }
}
