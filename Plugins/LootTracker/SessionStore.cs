// <copyright file="SessionStore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootTracker
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>Atomic UTF-8 persistence and portable exports; never expires archived sessions.</summary>
    public static class SessionStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static TrackingSession Read(string path)
        {
            var state = JsonSerializer.Deserialize<TrackingSession>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty session.");
            _ = new TrackingLedger(state); // Validate persisted numbers before displaying/reusing them.
            return state;
        }

        public static void Write(string path, TrackingSession session)
        {
            AtomicWrite(path, JsonSerializer.Serialize(session, Options));
        }

        public static void AtomicWrite(string path, string contents)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(contents);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static void ExportCsv(string path, TrackingSession session, string header)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(true));
            writer.WriteLine(header);
            foreach (var map in session.Maps)
            {
                if (map.Currency.Count == 0)
                    WriteRow(writer, session, map, string.Empty, string.Empty, 0);
                else
                    foreach (var (key, value) in map.Currency.OrderBy(pair => pair.Value.Name, StringComparer.CurrentCulture))
                        WriteRow(writer, session, map, key, value.Name, value.Count);
            }
        }

        private static void WriteRow(TextWriter writer, TrackingSession session, MapRun map, string key, string name, long count)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Cell(session.StartedUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                Cell(map.Name), Cell(map.AreaId), Cell(map.AreaHash),
                map.ActiveSeconds.ToString("F2", CultureInfo.InvariantCulture), Cell(name), Cell(key),
                count.ToString(CultureInfo.InvariantCulture),
            }));
        }

        private static string Cell(string value)
        {
            // Item names are data; spreadsheet applications must not interpret them as formulas.
            if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
