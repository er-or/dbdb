using System;
using System.Text.RegularExpressions;

namespace dbdb {

    // Translates MySQL-style commands to T-SQL equivalents and vice-versa,
    // so users can type familiar MySQL commands against either engine.
    internal static class QueryTranslator {

        // Returns null if no translation needed (pass through as-is).
        internal static string? ToMsSql(string input) {
            string trimmed = input.Trim();
            string upper = trimmed.ToUpperInvariant();

            // SHOW DATABASES  ->  SELECT name FROM sys.databases ORDER BY name
            if (Regex.IsMatch(upper, @"^SHOW\s+DATABASES\s*;?$"))
                return "SELECT name FROM sys.databases ORDER BY name;";

            // SHOW SCHEMAS  -> same
            if (Regex.IsMatch(upper, @"^SHOW\s+SCHEMAS\s*;?$"))
                return "SELECT name FROM sys.databases ORDER BY name;";

            // SHOW TABLES [LIKE 'pattern']  ->  SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ...
            var showTables = Regex.Match(trimmed, @"^SHOW\s+TABLES(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showTables.Success) {
                string like = showTables.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_NAME LIKE '{escaped}' ORDER BY TABLE_NAME;";
            }

            // SHOW FULL TABLES [LIKE 'pattern']
            var showFullTables = Regex.Match(trimmed, @"^SHOW\s+FULL\s+TABLES(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showFullTables.Success) {
                string like = showFullTables.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '{escaped}' ORDER BY TABLE_NAME;";
            }

            // SHOW VIEWS [LIKE 'pattern']
            var showViews = Regex.Match(trimmed, @"^SHOW\s+VIEWS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showViews.Success) {
                string like = showViews.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS ORDER BY TABLE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME LIKE '{escaped}' ORDER BY TABLE_NAME;";
            }

            // DESCRIBE PROC / PROCEDURE proc  ->  parameter list
            // (SQL Server's INFORMATION_SCHEMA.PARAMETERS has no ROUTINE_TYPE column,
            // unlike MySQL's, so filter to procedures via a join against ROUTINES)
            var descProc = Regex.Match(trimmed, @"^(?:DESCRIBE|DESC)\s+PROC(?:EDURE)?\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (descProc.Success) {
                string proc = descProc.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                int dot = proc.LastIndexOf('.');
                if (dot >= 0) proc = proc.Substring(dot + 1).Trim('[', ']', '`', '"');
                string safe = proc.Replace("'", "''");
                return "SELECT p.PARAMETER_NAME, p.DATA_TYPE, p.PARAMETER_MODE\n" +
                       "FROM INFORMATION_SCHEMA.PARAMETERS p\n" +
                       "JOIN INFORMATION_SCHEMA.ROUTINES r ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA\n" +
                       $"WHERE p.SPECIFIC_NAME = '{safe}' AND r.ROUTINE_TYPE = 'PROCEDURE'\n" +
                       "ORDER BY p.ORDINAL_POSITION;";
            }

            // SHOW COLUMNS FROM table  ->  table only
            var showColumns = Regex.Match(trimmed, @"^SHOW\s+COLUMNS\s+FROM\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showColumns.Success) {
                string tbl = showColumns.Groups[1].Value.Trim('[', ']', '`', '"');
                string safe = tbl.Replace("'", "''");
                return $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{safe}')\n" +
                       $"    RAISERROR('Unknown table ''{safe}''', 16, 1)\n" +
                       $"ELSE\n" +
                       $"    SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT\n" +
                       $"    FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{safe}' ORDER BY ORDINAL_POSITION;";
            }

            // DESCRIBE / DESC name  ->  table if it exists, else procedure, else error
            var descMatch = Regex.Match(trimmed, @"^(?:DESCRIBE|DESC)\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (descMatch.Success) {
                string name = descMatch.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                int dot = name.LastIndexOf('.');
                string specificName = dot >= 0 ? name.Substring(dot + 1).Trim('[', ']', '`', '"') : name;
                string safeTbl  = name.Replace("'", "''");
                string safeProc = specificName.Replace("'", "''");
                return "IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '" + safeTbl + "')\n" +
                       "    SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT\n" +
                       "    FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '" + safeTbl + "' ORDER BY ORDINAL_POSITION;\n" +
                       "ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.ROUTINES WHERE SPECIFIC_NAME = '" + safeProc + "' AND ROUTINE_TYPE = 'PROCEDURE')\n" +
                       "    SELECT p.PARAMETER_NAME, p.DATA_TYPE, p.PARAMETER_MODE\n" +
                       "    FROM INFORMATION_SCHEMA.PARAMETERS p\n" +
                       "    JOIN INFORMATION_SCHEMA.ROUTINES r ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA\n" +
                       "    WHERE p.SPECIFIC_NAME = '" + safeProc + "' AND r.ROUTINE_TYPE = 'PROCEDURE' ORDER BY p.ORDINAL_POSITION;\n" +
                       "ELSE\n" +
                       "    RAISERROR('Unknown table or procedure ''" + safeTbl + "''', 16, 1);";
            }

            // SHOW CREATE TABLE table  ->  sp_help
            var showCreate = Regex.Match(trimmed, @"^SHOW\s+CREATE\s+TABLE\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showCreate.Success) {
                string tbl = showCreate.Groups[1].Value.Trim('[', ']', '`', '"');
                return $"EXEC sp_help '{tbl.Replace("'", "''")}';";
            }

            // SHOW INDEX FROM table  ->  sp_helpindex
            var showIdx = Regex.Match(trimmed, @"^SHOW\s+(?:INDEX|INDEXES|KEYS)\s+FROM\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showIdx.Success) {
                string tbl = showIdx.Groups[1].Value.Trim('[', ']', '`', '"');
                return $"EXEC sp_helpindex '{tbl.Replace("'", "''")}';";
            }

            // SHOW VARIABLES [LIKE 'pattern']
            var showVars = Regex.Match(trimmed, @"^SHOW\s+(?:GLOBAL\s+|SESSION\s+)?VARIABLES(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showVars.Success)
                return "SELECT name, value_in_use AS value FROM sys.configurations ORDER BY name;";

            // SHOW STATUS
            if (Regex.IsMatch(upper, @"^SHOW\s+(?:GLOBAL\s+|SESSION\s+)?STATUS\s*;?$"))
                return "SELECT @@VERSION AS server_version, @@SERVERNAME AS server_name, DB_NAME() AS current_db;";

            // SHOW CREATE PROCEDURE proc  ->  sp_helptext
            var showCreateProc = Regex.Match(trimmed, @"^SHOW\s+CREATE\s+PROCEDURE\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showCreateProc.Success) {
                string proc = showCreateProc.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                return $"EXEC sp_helptext '{proc.Replace("'", "''")}';";
            }

            // SHOW CREATE FUNCTION func  ->  sp_helptext
            var showCreateFunc = Regex.Match(trimmed, @"^SHOW\s+CREATE\s+FUNCTION\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showCreateFunc.Success) {
                string func = showCreateFunc.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                return $"EXEC sp_helptext '{func.Replace("'", "''")}';";
            }

            // SHOW PROCEDURE STATUS [LIKE 'pattern']
            var showProcStatus = Regex.Match(trimmed, @"^SHOW\s+PROCEDURE\s+STATUS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showProcStatus.Success) {
                string like = showProcStatus.Groups[1].Value;
                string filter = string.IsNullOrEmpty(like) ? "" : $" AND ROUTINE_NAME LIKE '{like.Replace("'", "''")}'";
                return $"SELECT ROUTINE_NAME, ROUTINE_TYPE, CREATED, LAST_ALTERED " +
                       $"FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE'{filter} ORDER BY ROUTINE_NAME;";
            }

            // SHOW FUNCTION STATUS [LIKE 'pattern']
            var showFuncStatus = Regex.Match(trimmed, @"^SHOW\s+FUNCTION\s+STATUS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showFuncStatus.Success) {
                string like = showFuncStatus.Groups[1].Value;
                string filter = string.IsNullOrEmpty(like) ? "" : $" AND ROUTINE_NAME LIKE '{like.Replace("'", "''")}'";
                return $"SELECT ROUTINE_NAME, ROUTINE_TYPE, CREATED, LAST_ALTERED " +
                       $"FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='FUNCTION'{filter} ORDER BY ROUTINE_NAME;";
            }

            // SHOW PROCS / SHOW PROCEDURES [LIKE 'pattern']
            var showProcs = Regex.Match(trimmed, @"^SHOW\s+PROC(?:S|EDURES)(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showProcs.Success) {
                string like = showProcs.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE' ORDER BY ROUTINE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE' AND ROUTINE_NAME LIKE '{escaped}' ORDER BY ROUTINE_NAME;";
            }

            // SHOW PROCESSLIST
            if (Regex.IsMatch(upper, @"^SHOW\s+(?:FULL\s+)?PROCESSLIST\s*;?$"))
                return "SELECT session_id, login_name, status, command, cpu_time, total_elapsed_time, text " +
                       "FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(sql_handle) t;";

            // USE database  ->  pass through (both engines support USE)
            // SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, EXEC, CALL, etc. -> pass through

            return null;
        }

        // Translates MySQL-style commands to SQLite equivalents.
        internal static string? ToSqlite(string input) {
            string trimmed = input.Trim();
            string upper = trimmed.ToUpperInvariant();

            // SHOW TABLES [LIKE 'pattern']
            var showTables = Regex.Match(trimmed, @"^SHOW\s+(?:FULL\s+)?TABLES(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showTables.Success) {
                string like = showTables.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                string escaped = like.Replace("'", "''");
                return $"SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '{escaped}' ORDER BY name;";
            }

            // SHOW DATABASES / SHOW SCHEMAS
            if (Regex.IsMatch(upper, @"^SHOW\s+(DATABASES|SCHEMAS)\s*;?$"))
                return "SELECT 'Only one database per file in SQLite.' AS note;";

            // SHOW VIEWS [LIKE 'pattern']
            var showViews = Regex.Match(trimmed, @"^SHOW\s+VIEWS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showViews.Success) {
                string like = showViews.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT name FROM sqlite_master WHERE type='view' ORDER BY name;";
                string escaped = like.Replace("'", "''");
                return $"SELECT name FROM sqlite_master WHERE type='view' AND name LIKE '{escaped}' ORDER BY name;";
            }

            // SHOW PROCS / SHOW PROCEDURES [LIKE 'pattern'] - SQLite has no stored procedures
            if (Regex.IsMatch(upper, @"^SHOW\s+PROC(?:S|EDURES)(?:\s+LIKE\s+'[^']*')?\s*;?$"))
                return "SELECT 'SQLite does not support stored procedures.' AS note;";

            // DESCRIBE PROC / PROCEDURE proc - SQLite has no stored procedures
            if (Regex.IsMatch(upper, @"^(?:DESCRIBE|DESC)\s+PROC(?:EDURE)?\s+\S+\s*;?$"))
                return "SELECT 'SQLite does not support stored procedures.' AS note;";

            // DESCRIBE / DESC / SHOW COLUMNS FROM table
            var descMatch = Regex.Match(trimmed, @"^(?:DESCRIBE|DESC|SHOW\s+COLUMNS\s+FROM)\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (descMatch.Success) {
                string tbl = descMatch.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                string safe = tbl.Replace("'", "''");
                // Return columns if table or view exists, otherwise an error row
                return $"SELECT cid, name, type, \"notnull\", dflt_value, pk FROM pragma_table_info('{safe}')\n" +
                       $"UNION ALL\n" +
                       $"SELECT -1, 'ERROR: Unknown table ''{safe}''', '', 0, '', 0\n" +
                       $"WHERE NOT EXISTS (SELECT 1 FROM sqlite_master WHERE type IN ('table','view') AND name='{safe}');";
            }

            // SHOW CREATE TABLE table
            var showCreate = Regex.Match(trimmed, @"^SHOW\s+CREATE\s+TABLE\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showCreate.Success) {
                string tbl = showCreate.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                return $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tbl.Replace("'", "''")}';";
            }

            // SHOW INDEX / INDEXES / KEYS FROM table
            var showIdx = Regex.Match(trimmed, @"^SHOW\s+(?:INDEX|INDEXES|KEYS)\s+FROM\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (showIdx.Success) {
                string tbl = showIdx.Groups[1].Value.Trim('[', ']', '`', '"', ';');
                return $"PRAGMA index_list('{tbl.Replace("'", "''")}');";
            }

            // SHOW CREATE PROCEDURE / FUNCTION / SHOW PROCEDURE STATUS / SHOW FUNCTION STATUS
            if (Regex.IsMatch(upper, @"^SHOW\s+(CREATE\s+)?(PROCEDURE|FUNCTION)"))
                return "SELECT 'SQLite does not support stored procedures or functions.' AS note;";

            // SHOW VARIABLES
            if (Regex.IsMatch(upper, @"^SHOW\s+(?:GLOBAL\s+|SESSION\s+)?VARIABLES\s*;?$"))
                return "PRAGMA compile_options;";

            // SHOW STATUS
            if (Regex.IsMatch(upper, @"^SHOW\s+(?:GLOBAL\s+|SESSION\s+)?STATUS\s*;?$"))
                return "SELECT sqlite_version() AS sqlite_version;";

            return null;
        }

        // Normalise a MySQL statement for execution against MySQL
        // (mostly pass-through; only SHOW VIEWS / SHOW PROCS need translation,
        // since MySQL has no such syntax natively)
        internal static string? ToMySql(string input) {
            string trimmed = input.Trim();

            // SHOW VIEWS [LIKE 'pattern']
            var showViews = Regex.Match(trimmed, @"^SHOW\s+VIEWS(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showViews.Success) {
                string like = showViews.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='VIEW' AND TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='VIEW' AND TABLE_SCHEMA=DATABASE() AND TABLE_NAME LIKE '{escaped}' ORDER BY TABLE_NAME;";
            }

            // SHOW PROCS / SHOW PROCEDURES [LIKE 'pattern']
            var showProcs = Regex.Match(trimmed, @"^SHOW\s+PROC(?:S|EDURES)(?:\s+LIKE\s+'([^']*)')?\s*;?$", RegexOptions.IgnoreCase);
            if (showProcs.Success) {
                string like = showProcs.Groups[1].Value;
                if (string.IsNullOrEmpty(like))
                    return "SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE' AND ROUTINE_SCHEMA=DATABASE() ORDER BY ROUTINE_NAME;";
                string escaped = like.Replace("'", "''");
                return $"SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE' AND ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME LIKE '{escaped}' ORDER BY ROUTINE_NAME;";
            }

            // DESCRIBE PROC / PROCEDURE proc  ->  parameter list
            var descProc = Regex.Match(trimmed, @"^(?:DESCRIBE|DESC)\s+PROC(?:EDURE)?\s+(\S+)\s*;?$", RegexOptions.IgnoreCase);
            if (descProc.Success) {
                string proc = descProc.Groups[1].Value.Trim('`', '\'', '"', ';');
                string safe = proc.Replace("'", "''");
                return "SELECT PARAMETER_NAME, DATA_TYPE, PARAMETER_MODE\n" +
                       "FROM INFORMATION_SCHEMA.PARAMETERS\n" +
                       $"WHERE SPECIFIC_NAME = '{safe}' AND SPECIFIC_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'PROCEDURE'\n" +
                       "ORDER BY ORDINAL_POSITION;";
            }

            // No translation needed for everything else - pass through as-is
            return null;
        }

        // Strip a trailing semicolon if present (for drivers that don't want it)
        internal static string StripSemicolon(string sql) {
            string s = sql.TrimEnd();
            if (s.EndsWith(";")) s = s.Substring(0, s.Length - 1).TrimEnd();
            return s;
        }

    }

}
