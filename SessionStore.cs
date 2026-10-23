using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace dbdb {

    // Persists connection params (no password) to a small JSON file so a
    // session can be restored later with `dbdb restore`, or `dbdb restore
    // <name>` for one saved under a name via `SAVE SESSION <name>`.
    internal static class SessionStore {

        private const string BaseFileName = ".dbdb_autosave";

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+$");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        // Session names are embedded directly into a file name, so they're
        // restricted to characters that are always safe there and can't
        // escape the target directory (e.g. "../../x").
        internal static bool IsValidName(string name) => NamePattern.IsMatch(name);

        internal static void Save(Config cfg, string? currentDatabase, string? name = null) {
            // SQLite's "current database" is a logical connection name ("main"),
            // not the file path - always persist the actual file path for it.
            string? db = cfg.Type == DbType.Sqlite ? cfg.Database : (currentDatabase ?? cfg.Database);

            var data = new SessionData {
                Type              = cfg.Type.ToString(),
                Host              = cfg.Host,
                Port              = cfg.Port,
                User              = cfg.User,
                Database          = db,
                TrustedConnection = cfg.TrustedConnection,
                TrustServerCert   = cfg.TrustServerCert,
                InstanceName      = cfg.InstanceName
            };
            string json = JsonSerializer.Serialize(data, JsonOptions);

            foreach (string path in CandidatePaths(name)) {
                try {
                    // Windows refuses to CREATE_ALWAYS-overwrite an existing hidden
                    // file unless the hidden attribute is passed again, so clear it
                    // first or every save after the first silently fails here.
                    if (File.Exists(path)) TryClearHidden(path);
                    File.WriteAllText(path, json);
                    TryMarkHidden(path);
                    return;
                } catch {
                    // Not writable here - try the next candidate location.
                }
            }
        }

        internal static Config? Load(string? name = null) {
            foreach (string path in CandidatePaths(name)) {
                try {
                    if (!File.Exists(path)) continue;
                    string json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<SessionData>(json);
                    if (data == null) continue;

                    return new Config {
                        Type              = Enum.TryParse<DbType>(data.Type, true, out var t) ? t : DbType.MySQL,
                        Host              = data.Host ?? "localhost",
                        Port              = data.Port,
                        User              = data.User,
                        Database          = data.Database,
                        TrustedConnection = data.TrustedConnection,
                        TrustServerCert   = data.TrustServerCert,
                        InstanceName      = data.InstanceName
                    };
                } catch {
                    // Not readable/valid here - try the next candidate location.
                }
            }
            return null;
        }

        // Lists the names of all saved sessions (i.e. ones saved via
        // `SAVE SESSION <name>`), for reporting back when `dbdb restore <name>`
        // is given a name that doesn't match anything saved.
        internal static string[] ListNames() {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string prefix = BaseFileName + ".";

            foreach (string dir in CandidateDirs()) {
                try {
                    foreach (string file in Directory.GetFiles(dir, prefix + "*")) {
                        string suffix = Path.GetFileName(file).Substring(prefix.Length);
                        if (suffix.Length > 0) names.Add(suffix);
                    }
                } catch {
                    // Directory not readable - skip it.
                }
            }

            var result = new string[names.Count];
            names.CopyTo(result);
            return result;
        }

        private static string FileNameFor(string? name) =>
            string.IsNullOrEmpty(name) ? BaseFileName : BaseFileName + "." + name;

        private static string[] CandidateDirs() {
            string? profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cwd = Directory.GetCurrentDirectory();
            return string.IsNullOrEmpty(profile) ? new[] { cwd } : new[] { profile, cwd };
        }

        private static string[] CandidatePaths(string? name) {
            string fileName = FileNameFor(name);
            string[] dirs = CandidateDirs();
            var paths = new string[dirs.Length];
            for (int i = 0; i < dirs.Length; i++) paths[i] = Path.Combine(dirs[i], fileName);
            return paths;
        }

        private static void TryClearHidden(string path) {
            try {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Hidden) != 0)
                    File.SetAttributes(path, attrs & ~FileAttributes.Hidden);
            } catch {
                // Not critical if this fails - the write below will just throw
                // and fall through to the next candidate location instead.
            }
        }

        private static void TryMarkHidden(string path) {
            try {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Hidden) == 0)
                    File.SetAttributes(path, attrs | FileAttributes.Hidden);
            } catch {
                // Not critical if this fails.
            }
        }

        private class SessionData {
            public string? Type { get; set; }
            public string? Host { get; set; }
            public int Port { get; set; }
            public string? User { get; set; }
            public string? Database { get; set; }
            public bool TrustedConnection { get; set; }
            public bool TrustServerCert { get; set; }
            public string? InstanceName { get; set; }
        }

    }

}
