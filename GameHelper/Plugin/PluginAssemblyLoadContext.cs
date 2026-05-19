namespace GameHelper.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.Loader;
    using GameOffsets;

    internal class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private static readonly IReadOnlyDictionary<string, Assembly> SharedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase)
            {
                [typeof(IPCore).Assembly.GetName().Name!] = typeof(IPCore).Assembly,
                [typeof(GameProcessDetails).Assembly.GetName().Name!] = typeof(GameProcessDetails).Assembly,
            };

        private readonly AssemblyDependencyResolver resolver;

        public PluginAssemblyLoadContext(string assemblyLocation)
            : base(isCollectible: true)
        {
            this.resolver = new AssemblyDependencyResolver(assemblyLocation);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null &&
                SharedAssemblies.TryGetValue(assemblyName.Name, out var sharedAssembly))
            {
                return sharedAssembly;
            }

            var path = this.resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
            {
                return this.LoadFromAssemblyPath(path);
            }

            return null;
        }
    }
}
