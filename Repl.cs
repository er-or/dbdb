using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace dbdb {

    internal class Repl {

        private static readonly Regex DescProcRegex =
            new Regex(@"^(?:DESCRIBE|DESC)\s+PROC(?:EDURE)?\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);

        private static readonly Regex DescMatchRegex =
            new Regex(@"^(?:DESCRIBE|DESC)\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);

        private static readonly Regex ExportTableRegex =
            new Regex(@"^EXPORT\s+TABLE\s+(\S+)\s+TO\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);

        private static readonly Regex ExportViewJsonRegex =
            new Regex(@"^EXPORT\s+VIEW\s+(\S+)\s+AS\s+JSON\s+TO\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);

        private static readonly Regex ShowCommandsRegex =
            new Regex(@"^SHOW\s+COMMANDS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);

        private readonly DbSession _session;
        private readonly Config _cfg;

        internal Repl(DbSession session, Config cfg) {
            _session = session;
            _cfg = cfg;
        }

        internal void Run() {
            var buffer = new StringBuilder();
            bool continuation = false;

            while (true) {
                string prompt = continuation
                    ? "    -> "
                    : BuildPrompt();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(prompt);
                Console.ResetColor();

                string? line = Console.ReadLine();
                if (line == null) break; // EOF / Ctrl+Z

                string trimmed = line.Trim();

                // Exit commands
                if (!continuation && IsExitCommand(trimmed)) break;

                // Clear buffer command
                if (!continuation && (trimmed == @"\c" || trimmed.Equals("clear buffer", StringComparison.OrdinalIgnoreCase))) {
                    buffer.Clear();
                    continuation = false;
                    ConsoleHelper.WriteDim("Buffer cleared.");
                    continue;
                }

                // Status command
                if (!continuation && (trimmed == @"\s" || trimmed.Equals("status", StringComparison.OrdinalIgnoreCase))) {
                    PrintStatus();
                    continue;
                }

                // SHOW COMMANDS [LIKE 'pattern'] - client-side, identical on every engine
                if (!continuation) {
                    var showCommandsMatch = ShowCommandsRegex.Match(trimmed);
                    if (showCommandsMatch.Success) {
                        string? like = showCommandsMatch.Groups[1].Success ? showCommandsMatch.Groups[1].Value : null;
                        CommandCatalog.Print(like);
                        continue;
                    }
                }

                // USE database
                if (!continuation) {
                    var useMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^USE\s+(\S+)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (useMatch.Success) {
                        if (!_session.SupportsUse) {
                            ConsoleHelper.WriteWarn("USE is not supported for SQLite - each file is its own database.");
                        } else {
                            string db = useMatch.Groups[1].Value.Trim('`', '[', ']', '"', ';');
                            try {
                                _session.UseDatabase(db);
                                ConsoleHelper.WriteSuccess($"Database changed to '{db}'.");
                                SessionStore.Save(_cfg, db);
                            } catch (Exception ex) {
                                ConsoleHelper.WriteError("ERROR: " + ex.Message);
                            }
                        }
                        continue;
                    }
                }

                // DESCRIBE / DESC name - auto-detect table vs procedure for MySQL.
                // (MsSql does this via a single T-SQL IF/ELSE query; MySQL has no
                // ad-hoc branching outside stored routines, so it's done here instead.)
                if (!continuation && _cfg.Type == DbType.MySQL) {
                    var mysqlDescMatch = DescMatchRegex.Match(trimmed);
                    if (mysqlDescMatch.Success) {
                        HandleMySqlDescribe(trimmed, mysqlDescMatch.Groups[1].Value);
                        continue;
                    }
                }

                // EXPORT VIEW name AS JSON TO file
                if (!continuation) {
                    var exportViewMatch = ExportViewJsonRegex.Match(trimmed);
                    if (exportViewMatch.Success) {
                        string viewName = exportViewMatch.Groups[1].Value.Trim('`', '[', ']', '"', ';');
                        string filePath = exportViewMatch.Groups[2].Value.Trim('\'', '"', ';');
                        try {
                            Exporter.ExportViewAsJson(_session, _cfg.Type, viewName, filePath);
                        } catch (Exception ex) {
                            ConsoleHelper.WriteError("ERROR: " + ex.Message);
                        }
                        continue;
                    }
                }

                // EXPORT TABLE name TO file
                if (!continuation) {
                    var exportTableMatch = ExportTableRegex.Match(trimmed);
                    if (exportTableMatch.Success) {
                        string tableName = exportTableMatch.Groups[1].Value.Trim('`', '[', ']', '"', ';');
                        string filePath = exportTableMatch.Groups[2].Value.Trim('\'', '"', ';');
                        try {
                            Exporter.ExportTable(_session, _cfg.Type, tableName, filePath);
                        } catch (Exception ex) {
                            ConsoleHelper.WriteError("ERROR: " + ex.Message);
                        }
                        continue;
                    }
                }

                if (buffer.Length > 0) buffer.AppendLine();
                buffer.Append(line);

                // Decide if the statement is complete.
                // For MsSql: ';' or 'GO' on its own line terminates.
                // For MySQL: ';' at end of the last non-empty line terminates.
                bool complete = IsStatementComplete(buffer.ToString(), trimmed);

                if (!complete) {
                    continuation = true;
                    continue;
                }

                string fullSql = buffer.ToString().Trim();
                buffer.Clear();
                continuation = false;

                // Strip GO / trailing semicolons for MsSql batch separator
                if (_cfg.Type == DbType.MsSql) {
                    fullSql = StripGo(fullSql).Trim();
                }

                if (string.IsNullOrWhiteSpace(fullSql)) continue;

                ExecuteSql(fullSql);
            }
        }

        private void ExecuteSql(string sql) {
            string toExecute = sql;
            string? translated = _cfg.Type switch {
                DbType.MsSql  => QueryTranslator.ToMsSql(sql),
                DbType.Sqlite => QueryTranslator.ToSqlite(sql),
                DbType.MySQL  => QueryTranslator.ToMySql(sql),
                _             => null
            };
            if (translated != null) toExecute = translated;

            try {
                bool hadResults = _session.Execute(toExecute, out int rowsAffected);
                if (!hadResults) {
                    ResultRenderer.PrintRowsAffected(rowsAffected >= 0 ? rowsAffected : 0);
                }

                var descProcMatch = DescProcRegex.Match(sql.Trim());
                if (descProcMatch.Success) {
                    string procName = descProcMatch.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                    ConsoleHelper.WriteDim($"Use SHOW CREATE PROCEDURE {procName} for more info.");
                }
            } catch (Exception ex) {
                ConsoleHelper.WriteError("ERROR: " + ex.Message);
            }
        }

        // MySQL has no ad-hoc IF/ELSE, so table-vs-procedure detection for plain
        // DESCRIBE/DESC happens here as two round trips instead of one query.
        private void HandleMySqlDescribe(string originalCommand, string rawName) {
            string name = rawName.Trim('`', '\'', '"', ';');
            int dot = name.LastIndexOf('.');
            string specificName = dot >= 0 ? name.Substring(dot + 1).Trim('`', '\'', '"') : name;
            string safeTbl = name.Replace("'", "''");
            string safeProc = specificName.Replace("'", "''");

            try {
                bool tableExists = ScalarBool(
                    $"SELECT EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{safeTbl}')");
                if (tableExists) {
                    ExecuteSql(originalCommand);
                    return;
                }

                bool procExists = ScalarBool(
                    $"SELECT EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = DATABASE() AND SPECIFIC_NAME = '{safeProc}' AND ROUTINE_TYPE = 'PROCEDURE')");
                if (procExists) {
                    ExecuteSql(
                        "SELECT PARAMETER_NAME, DATA_TYPE, PARAMETER_MODE " +
                        "FROM INFORMATION_SCHEMA.PARAMETERS " +
                        $"WHERE SPECIFIC_NAME = '{safeProc}' AND SPECIFIC_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'PROCEDURE' " +
                        "ORDER BY ORDINAL_POSITION;");
                    return;
                }

                ConsoleHelper.WriteError($"ERROR: Unknown table or procedure '{name}'");
            } catch (Exception ex) {
                ConsoleHelper.WriteError("ERROR: " + ex.Message);
            }
        }

        private bool ScalarBool(string sql) {
            object? result = _session.ExecuteScalar(sql);
            return result != null && Convert.ToInt64(result) != 0;
        }

        private bool IsStatementComplete(string buffer, string lastTrimmedLine) {
            if (_cfg.Type == DbType.MsSql) {
                if (lastTrimmedLine.Equals("go", StringComparison.OrdinalIgnoreCase)) return true;
                if (lastTrimmedLine == ";") return true;
            }
            // Trailing semicolon
            string trimBuf = buffer.TrimEnd();
            if (trimBuf.EndsWith(";")) return true;
            // Single-line commands that don't need a semicolon
            return IsSelfTerminating(trimBuf);
        }

        // Commands that are unambiguously complete on a single line without a semicolon.
        private static bool IsSelfTerminating(string sql) {
            string u = sql.Trim().ToUpperInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(u,
                @"^(SHOW\s+.+|DESCRIBE\s+.+|DESC\s+.+|USE\s+\S+|STATUS|HELP|\\[A-Z?]|EXEC\s+SP_HELP\w*\s+.+)$");
        }

        private static string StripGo(string sql) {
            // Remove trailing GO (case-insensitive) or trailing semicolon
            string s = sql.TrimEnd();
            if (s.EndsWith(";")) s = s.Substring(0, s.Length - 1).TrimEnd();
            if (s.EndsWith("GO", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).TrimEnd();
            return s;
        }

        private bool IsExitCommand(string s) =>
            s.Equals("quit",    StringComparison.OrdinalIgnoreCase) ||
            s.Equals("exit",    StringComparison.OrdinalIgnoreCase) ||
            s == @"\q" ||
            s.Equals("quit;",   StringComparison.OrdinalIgnoreCase) ||
            s.Equals("exit;",   StringComparison.OrdinalIgnoreCase);

        private string BuildPrompt() {
            string typeTag = _cfg.Type switch {
                DbType.MySQL  => "mysql",
                DbType.MsSql  => "mssql",
                DbType.Sqlite => "sqlite",
                _             => "db"
            };
            if (_cfg.Type == DbType.Sqlite) {
                string file = System.IO.Path.GetFileName(_cfg.Database ?? "");
                return $"sqlite [{file}]> ";
            }
            string? db = _session.CurrentDatabase();
            if (string.IsNullOrEmpty(db)) return $"{typeTag}> ";
            return $"{typeTag} [{db}]> ";
        }

        private void PrintStatus() {
            string typeLabel = _cfg.Type switch {
                DbType.MySQL  => "MySQL",
                DbType.MsSql  => "MS SQL Server",
                DbType.Sqlite => "SQLite",
                _             => "Unknown"
            };
            ConsoleHelper.WriteInfo($"Connection type : {typeLabel}");
            if (_cfg.Type == DbType.Sqlite) {
                ConsoleHelper.WriteInfo($"File            : {_cfg.Database}");
            } else {
                ConsoleHelper.WriteInfo($"Host            : {_cfg.Host}:{_cfg.EffectivePort}");
                string? db = _session.CurrentDatabase();
                ConsoleHelper.WriteInfo($"Current DB      : {(string.IsNullOrEmpty(db) ? "(none)" : db)}");
            }
        }

    }

}
