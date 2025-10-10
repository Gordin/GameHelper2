namespace GameHelper.Plugin
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;

    internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        // Assemblies that must be shared with the host
        private static readonly string[] Shared =
        {
            "ClickableTransparentOverlay",
            "Coroutine",
            "GameHelper",
            "GameOffsets",
            "ImGui.NET",
            "Newtonsoft.Json",
            "SixLabors.ImageSharp",
        };

        public PluginAssemblyLoadContext(string assemblyLocation, bool isCollectible = true)
            : base(isCollectible)
        {
            resolver = new AssemblyDependencyResolver(assemblyLocation);
            Resolving += OnResolving;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Prefer the host's copy for shared assemblies
            if (Shared.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
            {
                var hostAsm = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
                if (hostAsm != null)
                    return hostAsm;
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        private Assembly OnResolving(AssemblyLoadContext context, AssemblyName name)
        {
            if (Shared.Contains(name.Name, StringComparer.OrdinalIgnoreCase))
            {
                return AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }
    }
}
