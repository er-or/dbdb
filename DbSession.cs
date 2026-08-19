using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using MySqlConnector;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace dbdb {

    internal class DbSession : IDisposable {

        private readonly DbConnection _conn;
        private readonly DbType _type;

        internal DbSession(Config cfg) {
            _type = cfg.Type;
            _conn = cfg.Type switch {
                DbType.MySQL  => BuildMySql(cfg),
                DbType.MsSql  => BuildMsSql(cfg),
                DbType.Sqlite => BuildSqlite(cfg),
                _             => throw new ArgumentException("Unknown DbType")
            };
        }

        private static DbConnection BuildMySql(Config cfg) {
            var b = new MySqlConnectionStringBuilder {
                Server   = cfg.Host,
                Port     = (uint)cfg.EffectivePort,
                UserID   = cfg.User ?? "root",
                Password = cfg.Password ?? "",
                AllowUserVariables = true,
                CharacterSet = "utf8mb4",
            };
            if (!string.IsNullOrEmpty(cfg.Database)) b.Database = cfg.Database;
            return new MySqlConnection(b.ConnectionString);
        }

        private static DbConnection BuildMsSql(Config cfg) {
            var b = new SqlConnectionStringBuilder {
                DataSource = cfg.InstanceName != null && cfg.Port == 0
                    ? $"{cfg.Host}\\{cfg.InstanceName}"           // let Browser service resolve port
                    : cfg.InstanceName != null
                        ? $"{cfg.Host}\\{cfg.InstanceName},{cfg.Port}"
                        : $"{cfg.Host},{cfg.EffectivePort}",
                TrustServerCertificate = true, // default for CLI use; override with --no-trust-cert if needed
            };
            if (cfg.TrustedConnection) {
                b.IntegratedSecurity = true;
            } else {
                b.UserID   = cfg.User ?? "";
                b.Password = cfg.Password ?? "";
            }
            if (!string.IsNullOrEmpty(cfg.Database)) b.InitialCatalog = cfg.Database;
            return new SqlConnection(b.ConnectionString);
        }

        private static DbConnection BuildSqlite(Config cfg) {
            if (string.IsNullOrWhiteSpace(cfg.Database))
                throw new ArgumentException("SQLite requires a file path via -d.");
            return new SqliteConnection($"Data Source={cfg.Database}");
        }

        internal void Open() => _conn.Open();

        internal bool SupportsUse => _type != DbType.Sqlite;

        internal void UseDatabase(string db) {
            _conn.ChangeDatabase(db);
        }

        // Execute a statement. Returns true if it produced a result set.
        internal bool Execute(string sql, out int rowsAffected) {
            rowsAffected = 0;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 300;

            using var reader = cmd.ExecuteReader();
            bool hadResults = false;

            do {
                if (reader.FieldCount > 0) {
                    hadResults = true;
                    ResultRenderer.RenderReader(reader, out int rc);
                    if (rc == 0) ResultRenderer.PrintEmpty();
                    else         ResultRenderer.PrintRowCount(rc);
                } else {
                    rowsAffected += reader.RecordsAffected;
                }
            } while (reader.NextResult());

            return hadResults;
        }

        // Runs a query and returns its first column/row only (no console rendering) -
        // used for existence checks that drive app-side branching.
        internal object? ExecuteScalar(string sql) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 300;
            return cmd.ExecuteScalar();
        }

        // Runs a query and returns column names plus raw row values (no console
        // rendering) - used by Exporter to pull data for table/view export.
        internal (string[] columns, List<object?[]> rows) ExecuteQuery(string sql) {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 300;
            using var reader = cmd.ExecuteReader();

            int n = reader.FieldCount;
            var cols = new string[n];
            for (int i = 0; i < n; i++) cols[i] = reader.GetName(i);

            var rows = new List<object?[]>();
            while (reader.Read()) {
                var row = new object?[n];
                for (int i = 0; i < n; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return (cols, rows);
        }

        internal string? CurrentDatabase() {
            try { return _conn.Database; } catch { return null; }
        }

        public void Dispose() => _conn.Dispose();

    }

}
