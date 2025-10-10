namespace GameHelper.Plugin
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;

    internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public PluginAssemblyLoadContext(string assemblyLocation, bool isCollectible = true)
            : base(isCollectible)
        {
            resolver = new AssemblyDependencyResolver(assemblyLocation);
            Resolving += OnResolving;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var hostAsm = TryGetFromDefault(assemblyName);
            if (hostAsm != null)
                return hostAsm;

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        private Assembly OnResolving(AssemblyLoadContext context, AssemblyName name)
        {
            var hostAsm = TryGetFromDefault(name);
            if (hostAsm != null)
                return hostAsm;

            var path = resolver.ResolveAssemblyToPath(name);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        private static Assembly TryGetFromDefault(AssemblyName requested)
        {
            // Match by Name + Culture + PublicKeyToken; ignore Version to allow unification.
            foreach (var asm in AssemblyLoadContext.Default.Assemblies)
            {
                var an = asm.GetName();
                if (!an.Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!CultureEquals(an.CultureName, requested.CultureName))
                    continue;

                if (!PublicKeyTokenEquals(an.GetPublicKeyToken(), requested.GetPublicKeyToken()))
                    continue;

                return asm;
            }
            return null;
        }

        private static bool CultureEquals(string a, string b)
            => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static bool PublicKeyTokenEquals(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
