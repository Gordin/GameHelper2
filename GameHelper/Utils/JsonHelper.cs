// <copyright file="JsonHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>
    ///     Utility functions to help read/write to Json files.
    /// </summary>
    internal static class JsonHelper
    {
        /// <summary>
        ///     Creates new instance or loads from the file if it exists.
        ///     If the file is unreadable or unparseable, logs a warning,
        ///     renames the bad file aside (preserving it for diagnosis),
        ///     and falls back to a fresh default-constructed <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Class name to (De)serialize.</typeparam>
        /// <param name="file">file to load from.</param>
        /// <returns>class object containing the data (if data exists).</returns>
        public static T CreateOrLoadJsonFile<T>(FileInfo file)
            where T : new()
        {
            file.Refresh();
            file.Directory?.Create();
            if (file.Exists)
            {
                try
                {
                    var content = File.ReadAllText(file.FullName);
                    var loaded = JsonConvert.DeserializeObject<T>(content);
                    if (loaded != null)
                    {
                        return loaded;
                    }

                    Console.WriteLine($"[JsonHelper] {file.FullName} deserialized to null; falling back to defaults.");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[JsonHelper] {file.FullName} is corrupt or schema-mismatched: {ex.Message}. Falling back to defaults.");
                    QuarantineCorruptFile(file);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[JsonHelper] {file.FullName} could not be read: {ex.Message}. Falling back to defaults.");
                }
            }

            T obj = new();
            SafeToFile(obj, file);
            return obj;
        }

        /// <summary>
        ///     Saves the class object into the file atomically.
        ///     Writes to a `.tmp` sibling first, then renames over the target —
        ///     so a crash mid-write leaves either the old file intact or the
        ///     new file complete, never a half-written truncation.
        /// </summary>
        /// <param name="classObject">class object to save in the file.</param>
        /// <param name="file">file to save in.</param>
        public static void SafeToFile(object classObject, FileInfo file)
        {
            var content = JsonConvert.SerializeObject(classObject, Formatting.Indented);
            var tempPath = file.FullName + ".tmp";
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, file.FullName, overwrite: true);
        }

        private static void QuarantineCorruptFile(FileInfo file)
        {
            try
            {
                var corruptName = $"{file.FullName}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                File.Move(file.FullName, corruptName, overwrite: true);
                Console.WriteLine($"[JsonHelper] Renamed corrupt file to {corruptName} for inspection.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[JsonHelper] Failed to quarantine corrupt file: {ex.Message}");
            }
        }
    }
}
