namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UnityAssemblyBuilderCompilationService のファクトリー実装
    /// </summary>
    internal class UnityAssemblyBuilderCompilationServiceFactory : IDynamicCompilationServiceFactory
    {
        public IDynamicCompilationService Create(DynamicCodeSecurityLevel securityLevel)
        {
            return new UnityAssemblyBuilderCompilationService(securityLevel);
        }
    }
}
