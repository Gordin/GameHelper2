// <copyright file="PManager.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Coroutine;
    using CoroutineEvents;
    using CTOUtils = ClickableTransparentOverlay.Win32.Utils;
    using Settings;
    using Ui;
    using Utils;

    internal record PluginWithName(string Name, IPCore Plugin, PluginAssemblyLoadContext Alc);

    internal record PluginContainer(string Name, IPCore Plugin, PluginMetadata Metadata, PluginAssemblyLoadContext Alc);

    /// <summary>
    ///     Finds, loads and unloads the plugins.
    /// </summary>
    internal static class PManager
    {
        private const int MaxUnloadGcAttempts = 10;
        private static bool disableRendering = false;
        private static readonly Dictionary<string, PluginMetadata> PluginMetadataByName =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PendingPluginUnload> PendingPluginUnloads = new();
#if DEBUG
        internal static readonly List<string> PluginNames = new();
#endif
        internal static readonly List<PluginContainer> Plugins = new();

        /// <summary>
        ///     Initlizes the plugin manager by loading all the plugins and their Metadata.
        /// </summary>
        internal static void InitializePlugins()
        {
            State.PluginsDirectory.Create(); // doesn't do anything if already exists.
            LoadPluginMetadata(LoadPlugins());
#if DEBUG
            GetAllPluginNames();
#endif
            // F-079: replaced Parallel.ForEach with foreach. Plugin OnEnable is rare
            // (once at startup), should be single-threaded for plugin authors who
            // assume ImGui / coroutine-registration semantics work on the render thread.
            PluginContainer[] snapshot;
            lock (Plugins)
            {
                snapshot = Plugins.ToArray();
            }

            ResolveStartupConflicts(snapshot);

            foreach (var container in snapshot)
            {
                EnablePluginIfRequired(container);
            }
            CoroutineHandler.Start(SavePluginSettingsCoroutine());
            CoroutineHandler.Start(SavePluginMetadataCoroutine());
            CoroutineHandler.Start(CollectUnloadedPluginAssembliesCoroutine());
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                DrawPluginUiRenderCoroutine(), "[PManager] Draw Plugins UI"));
        }

        private static List<PluginWithName> LoadPlugins()
        {
            return GetPluginsDirectories()
                  .AsParallel()
                  .Select(LoadPlugin)
                  .Where(x => x != null)
                  .Select(x => x!)
                  .OrderBy(x => x.Name)
                  .ToList();
        }

#if DEBUG
        private static void GetAllPluginNames()
        {
            PluginContainer[] snapshot;
            lock (Plugins)
            {
                snapshot = Plugins.ToArray();
            }

            foreach (var plugin in snapshot)
            {
                PluginNames.Add(plugin.Name);
            }
        }

        /// <summary>
        ///     Cleans up the already loaded plugins.
        /// </summary>
        internal static bool UnloadPlugin(string name)
        {
            PluginContainer? target;
            lock (Plugins)
            {
                target = Plugins.FirstOrDefault(p => p.Name == name);
            }

            if (target == null)
            {
                return false;
            }

            if (target.Metadata.Enable)
            {
                DisablePlugin(target);
                SavePluginMetadata();
            }

            lock (Plugins)
            {
                Plugins.Remove(target);
            }

            // F-075: actually unload the assembly via the collectible ALC tracked
            // in the PluginContainer (F-074 made the ALC collectible). Collection is
            // verified on later frames, after render/settings snapshots release it.
            QueuePluginAssemblyUnload(target);
            target = null;

            return true;
        }

        internal static bool LoadPlugin(string name)
        {
            try
            {
                var container = GetPluginsDirectories()
                                .Where(x => x.Name.Contains(name))
                                .Select(LoadPlugin)
                                .Where(y => y != null)
                                .Select(y => y!)
                                .ToList();
                if (container.Count > 0)
                {
                    LoadPluginMetadata(container);
                    PluginContainer? loaded;
                    lock (Plugins)
                    {
                        loaded = Plugins.LastOrDefault(plugin => plugin.Name == container[0].Name);
                    }

                    if (loaded?.Metadata.Enable == true)
                    {
                        // The assembly is newly loaded but its persisted metadata already says true.
                        // Temporarily clear it so the normal transition performs conflict cleanup
                        // and invokes OnEnable exactly once.
                        loaded.Metadata.Enable = false;
                        SetPluginEnabled(loaded, true);
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
#endif

        private static List<DirectoryInfo> GetPluginsDirectories()
        {
            return State.PluginsDirectory.GetDirectories().Where(
                x => (x.Attributes & FileAttributes.Hidden) == 0).ToList();
        }

        private static (Assembly assembly, PluginAssemblyLoadContext alc)? ReadPluginFiles(DirectoryInfo pluginDirectory)
        {
            try
            {
                var dllFile = pluginDirectory.GetFiles(
                    $"{pluginDirectory.Name}*.dll",
                    SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (dllFile == null)
                {
                    Console.WriteLine($"Couldn't find plugin dll with name {pluginDirectory.Name}" +
                                      $" in directory {pluginDirectory.FullName}." +
                                      " Please make sure DLL & the plugin got same name.");
                    return null;
                }

                var alc = new PluginAssemblyLoadContext(dllFile.FullName);
                var assembly = alc.LoadFromAssemblyPath(dllFile.FullName);
                return (assembly, alc);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load plugin {pluginDirectory.FullName} due to {e}");
                return null;
            }
        }


        private static PluginWithName? LoadPlugin(DirectoryInfo pluginDirectory)
        {
            var loaded = ReadPluginFiles(pluginDirectory);
            if (loaded != null)
            {
                var relativePluginDir = pluginDirectory.FullName.Replace(
                    State.PluginsDirectory.FullName, State.PluginsDirectory.Name);
                return LoadPlugin(loaded.Value.assembly, loaded.Value.alc, relativePluginDir);
            }

            return null;
        }

        private static PluginWithName? LoadPlugin(Assembly assembly, PluginAssemblyLoadContext alc, string pluginRootDirectory)
        {
            try
            {
                var types = assembly.GetTypes();
                if (types.Length <= 0)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} doesn't " +
                                      "contain any types (i.e. classes/stuctures).");
                    return null;
                }

                var iPluginClasses = types.Where(
                    type => typeof(IPCore).IsAssignableFrom(type) &&
                            type.IsSealed).ToList();
                if (iPluginClasses.Count != 1)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} contains" +
                                      $" {iPluginClasses.Count} sealed classes derived from CoreBase<TSettings>." +
                                      " It should have one sealed class derived from IPlugin.");
                    return null;
                }

                var pluginCore = Activator.CreateInstance(iPluginClasses[0]) as IPCore;
                if (pluginCore == null)
                {
                    Console.WriteLine($"Plugin (in {pluginRootDirectory}) {assembly} failed to instantiate IPCore-derived class.");
                    return null;
                }

                pluginCore.SetPluginDllLocation(pluginRootDirectory);
                return new PluginWithName(assembly.GetName().Name ?? string.Empty, pluginCore, alc);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading plugin {assembly.FullName} due to {e}");
                return null;
            }
        }

        private static void LoadPluginMetadata(IEnumerable<PluginWithName> plugins)
        {
            var savedMetadata = JsonHelper.CreateOrLoadJsonFile<Dictionary<string, PluginMetadata>>(State.PluginsMetadataFile);

            lock (Plugins)
            {
                foreach (var (name, metadata) in savedMetadata)
                {
                    PluginMetadataByName.TryAdd(name, metadata);
                }

                var newContainers = plugins.Select(x =>
                {
                    if (!PluginMetadataByName.TryGetValue(x.Name, out var metadata))
                    {
                        metadata = new PluginMetadata();
                        PluginMetadataByName[x.Name] = metadata;
                    }

                    return new PluginContainer(x.Name, x.Plugin, metadata, x.Alc);
                }).ToList();

                Plugins.AddRange(newContainers);
            }

            SavePluginMetadata();
        }

        private static void EnablePluginIfRequired(PluginContainer container)
        {
            if (container.Metadata.Enable)
            {
                container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
            }
        }

        /// <summary>
        ///     Changes a plugin's enabled state. Enabling a plugin first disables every active
        ///     conflicting plugin, ensuring its cleanup completes before the selected plugin loads.
        /// </summary>
        internal static void SetPluginEnabled(PluginContainer container, bool enabled)
        {
            if (container.Metadata.Enable == enabled)
            {
                return;
            }

            try
            {
                if (enabled)
                {
                    PluginContainer[] conflicts;
                    lock (Plugins)
                    {
                        conflicts = Plugins.Where(other =>
                            other != container &&
                            other.Metadata.Enable &&
                            PluginsConflict(container, other)).ToArray();
                    }

                    foreach (var conflict in conflicts)
                    {
                        DisablePlugin(conflict);
                    }

                    container.Metadata.Enable = true;
                    container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
                }
                else
                {
                    DisablePlugin(container);
                }
            }
            catch
            {
                if (enabled)
                {
                    container.Metadata.Enable = false;
                }

                throw;
            }
            finally
            {
                SavePluginMetadata();
            }
        }

        /// <summary>
        ///     Reloads all plugin assemblies from disk and restores their enabled state.
        /// </summary>
        internal static int ReloadAllPlugins()
        {
            PluginContainer[] oldPlugins;
            Dictionary<string, PluginMetadata> desiredMetadata;
            lock (Plugins)
            {
                oldPlugins = Plugins.ToArray();
                desiredMetadata = oldPlugins.ToDictionary(
                    container => container.Name,
                    container => new PluginMetadata { Enable = container.Metadata.Enable },
                    StringComparer.OrdinalIgnoreCase);

                foreach (var (name, metadata) in desiredMetadata)
                {
                    PluginMetadataByName[name] = metadata;
                }

                // Stop render dispatch immediately while cleanup is in progress.
                foreach (var container in oldPlugins)
                {
                    container.Metadata.Enable = false;
                }

                Plugins.Clear();
            }

            foreach (var container in oldPlugins)
            {
                if (desiredMetadata[container.Name].Enable)
                {
                    try
                    {
                        SaveAndDisablePlugin(container);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PManager.ReloadAllPlugins] {container.Name} threw during cleanup: {ex}");
                    }
                }

                QueuePluginAssemblyUnload(container);
            }

            // Release this method's references. Other frame-local snapshots are allowed
            // to disappear naturally before the deferred unload collector runs.
            Array.Clear(oldPlugins);

            LoadPluginMetadata(LoadPlugins());

            PluginContainer[] newPlugins;
            lock (Plugins)
            {
                newPlugins = Plugins.ToArray();
            }

            ResolveStartupConflicts(newPlugins);

            foreach (var container in newPlugins)
            {
                try
                {
                    EnablePluginIfRequired(container);
                }
                catch (Exception ex)
                {
                    container.Metadata.Enable = false;
                    Console.WriteLine($"[PManager.ReloadAllPlugins] {container.Name} threw on enable: {ex}");
                }
            }

#if DEBUG
            PluginNames.Clear();
            GetAllPluginNames();
#endif
            SavePluginMetadata();
            return newPlugins.Length;
        }

        private static bool shutDown;

        /// <summary>Finish enabled plugins before Core closes its process handle.</summary>
        internal static void Shutdown()
        {
            if (shutDown) return;
            shutDown = true;
            disableRendering = true;
            PluginContainer[] snapshot;
            lock (Plugins) { snapshot = Plugins.Where(p => p.Metadata.Enable).ToArray(); }
            foreach (var container in snapshot)
            {
                try { SaveAndDisablePlugin(container); }
                catch (Exception ex) { Console.WriteLine($"[PManager.Shutdown] {container.Name}: {ex}"); }
            }
            // Preserve enabled metadata so the next launch uses the user's configuration.
        }

        private static void DisablePlugin(PluginContainer container)
        {
            if (!container.Metadata.Enable)
            {
                return;
            }

            // Stop render dispatch immediately. SaveSettings and OnDisable then finish before a
            // conflicting plugin's OnEnable is called.
            container.Metadata.Enable = false;
            SaveAndDisablePlugin(container);
        }

        private static void SaveAndDisablePlugin(PluginContainer container)
        {
            Exception? saveException = null;
            try
            {
                container.Plugin.SaveSettings();
            }
            catch (Exception ex)
            {
                saveException = ex;
            }

            try
            {
                container.Plugin.OnDisable();
            }
            catch (Exception disableException) when (saveException != null)
            {
                throw new AggregateException(saveException, disableException);
            }

            if (saveException != null)
            {
                throw saveException;
            }
        }

        private static void QueuePluginAssemblyUnload(PluginContainer container)
        {
            var pendingUnload = new PendingPluginUnload(container.Name, new WeakReference(container.Alc));
            container.Alc.Unload();
            lock (PendingPluginUnloads)
            {
                PendingPluginUnloads.Add(pendingUnload);
            }
        }

        private static void ResolveStartupConflicts(IReadOnlyList<PluginContainer> plugins)
        {
            var accepted = new List<PluginContainer>();
            foreach (var candidate in plugins.Where(plugin => plugin.Metadata.Enable)
                         .OrderByDescending(plugin => plugin.Plugin.ConflictPriority)
                         .ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
            {
                var winner = accepted.FirstOrDefault(active => PluginsConflict(active, candidate));
                if (winner == null)
                {
                    accepted.Add(candidate);
                    continue;
                }

                candidate.Metadata.Enable = false;
                Console.WriteLine(
                    $"[PManager] Disabled {candidate.Name} because it conflicts with enabled plugin {winner.Name}.");
            }

            SavePluginMetadata();
        }

        private static bool PluginsConflict(PluginContainer first, PluginContainer second) =>
            DeclaresConflict(first.Plugin, second.Name) || DeclaresConflict(second.Plugin, first.Name);

        private static bool DeclaresConflict(IPCore plugin, string otherName) =>
            plugin.ConflictsWith.Any(name =>
                string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase));

        private static void SavePluginMetadata()
        {
            Dictionary<string, PluginMetadata> snapshot;
            lock (Plugins)
            {
                foreach (var container in Plugins)
                {
                    PluginMetadataByName[container.Name] = container.Metadata;
                }

                snapshot = PluginMetadataByName.ToDictionary(
                    pair => pair.Key,
                    pair => new PluginMetadata { Enable = pair.Value.Enable },
                    StringComparer.OrdinalIgnoreCase);
            }

            JsonHelper.SafeToFile(snapshot, State.PluginsMetadataFile);
        }

        private static IEnumerator<Wait> SavePluginMetadataCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                SavePluginMetadata();
            }
        }

        private static IEnumerator<Wait> SavePluginSettingsCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                PluginContainer[] snapshot;
                lock (Plugins)
                {
                    snapshot = Plugins.ToArray();
                }

                foreach (var container in snapshot)
                {
                    // Only save enabled plugins. Disabled plugins never had OnEnable
                    // called, so they never loaded their settings file from disk - their
                    // in-memory Settings is still the empty `new TSettings()` default.
                    // Saving that would overwrite (wipe) the user's saved config on disk.
                    if (!container.Metadata.Enable)
                    {
                        continue;
                    }

                    try
                    {
                        container.Plugin.SaveSettings();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PManager.SavePluginSettingsCoroutine] {container.Name} threw on save: {ex}");
                    }
                }
            }
        }

        private static IEnumerator<Wait> CollectUnloadedPluginAssembliesCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnPostRender);
                lock (PendingPluginUnloads)
                {
                    if (PendingPluginUnloads.Count == 0)
                    {
                        continue;
                    }
                }

                // Perform at most one full collection per frame for the entire batch,
                // rather than blocking the render thread with ten collections per plugin.
                GC.Collect();
                GC.WaitForPendingFinalizers();

                lock (PendingPluginUnloads)
                {
                    for (var i = PendingPluginUnloads.Count - 1; i >= 0; i--)
                    {
                        var pending = PendingPluginUnloads[i];
                        if (!pending.AlcReference.IsAlive)
                        {
                            PendingPluginUnloads.RemoveAt(i);
                            continue;
                        }

                        pending.Attempts++;
                        if (pending.Attempts < MaxUnloadGcAttempts)
                        {
                            continue;
                        }

                        Console.WriteLine(
                            $"[PManager] {pending.Name}: ALC still alive after {MaxUnloadGcAttempts} GC cycles - likely retained by plugin state or a static reflection cache.");
                        PendingPluginUnloads.RemoveAt(i);
                    }
                }
            }
        }

        private static IEnumerator<Wait> DrawPluginUiRenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (CTOUtils.IsKeyPressedAndNotTimeout(Core.GHSettings.DisableAllRenderingKey))
                {
                    disableRendering = !disableRendering;
                }

                if (disableRendering)
                {
                    continue;
                }

                PluginContainer[] snapshot;
                lock (Plugins)
                {
                    snapshot = Plugins.ToArray();
                }

                foreach (var container in snapshot)
                {
                    if (container.Metadata.Enable)
                    {
                        try
                        {
                            using var _ = PerformanceProfiler.Profile(container.Plugin.GetType().FullName ?? string.Empty, "DrawUI");
                            container.Plugin.DrawUI();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PManager.DrawPluginUiRenderCoroutine] {container.Name} threw: {ex}");
                        }
                    }
                }
            }
        }

        private sealed class PendingPluginUnload
        {
            internal PendingPluginUnload(string name, WeakReference alcReference)
            {
                this.Name = name;
                this.AlcReference = alcReference;
            }

            internal string Name { get; }

            internal WeakReference AlcReference { get; }

            internal int Attempts { get; set; }
        }
    }
}
