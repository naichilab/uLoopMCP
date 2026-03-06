using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// ドメインリロード時に UnityAssemblyBuilderCompilationServiceFactory を登録する
    /// </summary>
    internal static class UnityAssemblyBuilderRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            DynamicCompilationServiceRegistry.RegisterFactory(
                new UnityAssemblyBuilderCompilationServiceFactory());
        }
    }
}
