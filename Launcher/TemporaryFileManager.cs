// <copyright file="TemporaryFileManager.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace Launcher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Newtonsoft.Json;

    /// <summary>
    ///     A helper class to manage the temporary executables created by the <see cref="Launcher"/>.
    /// </summary>
    public static class TemporaryFileManager
    {
        private const string StoreFileName = "tempFileLocations.dat";

        /// <summary>
        ///     Adds the file to the manager.
        /// </summary>
        /// <param name="path">file pathname.</param>
        public static void AddFile(string path)
        {
            var fileList = GetFileList();
            WriteFileList(fileList.Append(Path.GetRelativePath(GetDirectoryPath(), path)));
        }

        /// <summary>
        ///     Purge all the files added into the <see cref="TemporaryFileManager"/>.
        ///     This includes the files added in the previous executions of <see cref="Launcher"/>.
        /// </summary>
        public static void Purge()
        {
            var fileList = GetFileList();
            var directoryPath = GetDirectoryPath();
            foreach (var file in fileList.Select(x => Path.Join(directoryPath, x)))
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            WriteFileList(Array.Empty<string>());
        }

        private static IReadOnlyList<string> GetFileList()
        {
            try
            {
                var tempFileName = GetFullTemporaryFileName();
                if (!File.Exists(tempFileName))
                {
                    return Array.Empty<string>();
                }

                var fileContent = File.ReadAllText(tempFileName);
                return JsonConvert.DeserializeObject<List<string>>(fileContent) ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return Array.Empty<string>();
            }
        }

        private static void WriteFileList(IEnumerable<string> list)
        {
            File.WriteAllText(GetFullTemporaryFileName(), JsonConvert.SerializeObject(list.Distinct()));
        }

        private static string GetFullTemporaryFileName()
        {
            return Path.Join(GetDirectoryPath(), StoreFileName);
        }

        private static string GetDirectoryPath()
        {
            var currentProcessPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(currentProcessPath) ?? string.Empty;
        }
    }
}
