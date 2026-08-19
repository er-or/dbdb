using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace dbdb {

    // Writes mysqldump-style SQL for `export table` (deliberately not shelling out
    // to the real mysqldump binary, so the output format stays stable enough for a
    // future `import` command to read back) and plain JSON for `export view ... as json`.
    internal static class Exporter {

        internal static void ExportTable(DbSession session, DbType type, string tableName, string filePath) {
            string qTable = QuoteIdent(type, tableName);
            string? ddl = TryGetCreateTableDdl(session, type, tableName);
            var (columns, rows) = session.ExecuteQuery($"SELECT * FROM {qTable};");

            var sb = new StringBuilder();
            sb.AppendLine("-- dbdb export");
            sb.AppendLine($"-- Table: {tableName}");
            sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            if (ddl != null) {
                sb.AppendLine($"DROP TABLE IF EXISTS {qTable};");
                sb.AppendLine(ddl.TrimEnd().TrimEnd(';') + ";");
            } else {
                sb.AppendLine("-- Schema DDL not available for this engine; data only. Table must already exist to import.");
            }
            sb.AppendLine();

            if (rows.Count > 0) {
                string colList = string.Join(", ", Array.ConvertAll(columns, c => QuoteIdent(type, c)));
                const int batchSize = 100;
                for (int i = 0; i < rows.Count; i += batchSize) {
                    int end = Math.Min(i + batchSize, rows.Count);
                    sb.Append("INSERT INTO ").Append(qTable).Append(" (").Append(colList).Append(") VALUES\n");
                    for (int r = i; r < end; r++) {
                        string vals = string.Join(", ", Array.ConvertAll(rows[r], v => FormatValue(type, v)));
                        sb.Append('(').Append(vals).Append(')');
                        sb.Append(r < end - 1 ? ",\n" : ";\n");
                    }
                }
            }

            File.WriteAllText(filePath, sb.ToString());
            ConsoleHelper.WriteSuccess($"Exported {rows.Count} row(s) from '{tableName}' to '{filePath}'.");
        }

        internal static void ExportViewAsJson(DbSession session, DbType type, string viewName, string filePath) {
            string qView = QuoteIdent(type, viewName);
            var (columns, rows) = session.ExecuteQuery($"SELECT * FROM {qView};");

            var list = new List<Dictionary<string, object?>>(rows.Count);
            foreach (var row in rows) {
                var obj = new Dictionary<string, object?>();
                for (int c = 0; c < columns.Length; c++) obj[columns[c]] = row[c];
                list.Add(obj);
            }

            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            ConsoleHelper.WriteSuccess($"Exported {rows.Count} row(s) from view '{viewName}' to '{filePath}'.");
        }

        private static string? TryGetCreateTableDdl(DbSession session, DbType type, string tableName) {
            try {
                if (type == DbType.MySQL) {
                    var (cols, rows) = session.ExecuteQuery($"SHOW CREATE TABLE {QuoteIdent(type, tableName)};");
                    if (rows.Count > 0 && cols.Length > 1) return Convert.ToString(rows[0][1]);
                } else if (type == DbType.Sqlite) {
                    string safe = tableName.Replace("'", "''");
                    var (cols, rows) = session.ExecuteQuery($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{safe}';");
                    if (rows.Count > 0) return Convert.ToString(rows[0][0]);
                }
                // MsSql: no single-statement way to get exact CREATE TABLE DDL - skip.
            } catch {
                // Fall back to data-only export if the DDL lookup itself fails.
            }
            return null;
        }

        private static string QuoteIdent(DbType type, string name) => type switch {
            DbType.MySQL  => "`" + name.Replace("`", "``") + "`",
            DbType.MsSql  => "[" + name.Replace("]", "]]") + "]",
            DbType.Sqlite => "\"" + name.Replace("\"", "\"\"") + "\"",
            _             => name
        };

        private static string FormatValue(DbType type, object? val) {
            if (val == null) return "NULL";
            switch (val) {
                case bool b:
                    return b ? "1" : "0";
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToString(val, CultureInfo.InvariantCulture) ?? "NULL";
                case DateTime dt:
                    return "'" + dt.ToString("yyyy-MM-dd HH:mm:ss") + "'";
                case byte[] bytes:
                    string hex = Convert.ToHexString(bytes);
                    return type == DbType.Sqlite ? $"X'{hex}'" : $"0x{hex}";
                default:
                    string s = val.ToString() ?? "";
                    return "'" + s.Replace("'", "''") + "'";
            }
        }

    }

}
