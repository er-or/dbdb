using System;
using System.IO;
using System.Text.Json;

namespace dbdb {

    // Persists connection params (no password) to a small JSON file so a
    // session can be restored later with `dbdb restore`.
    internal static class SessionStore {

        private const string FileName = ".dbdb_autosave";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        internal static void Save(Config cfg, string? currentDatabase) {
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

            foreach (string path in CandidatePaths()) {
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

        internal static Config? Load() {
            foreach (string path in CandidatePaths()) {
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

        private static string[] CandidatePaths() {
            string? profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cwd = Path.Combine(Directory.GetCurrentDirectory(), FileName);
            return string.IsNullOrEmpty(profile)
                ? new[] { cwd }
                : new[] { Path.Combine(profile, FileName), cwd };
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
