using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace dbdb {

    // Powers `SHOW COMMANDS [LIKE 'pattern']` - a client-side listing of dbdb's own
    // command syntax (as opposed to querying the connected database), so the list
    // itself is identical no matter which engine you're connected to. A handful of
    // entries (views, procs) report "not supported" when actually run against
    // SQLite, but the command syntax is still recognized there.
    internal static class CommandCatalog {

        private static readonly (string Command, string Description)[] Commands = {
            ("SHOW DATABASES / SHOW SCHEMAS",            "List all databases on the server"),
            ("SHOW TABLES [LIKE 'pattern']",             "List tables in the current database"),
            ("SHOW FULL TABLES [LIKE 'pattern']",        "List tables with their type (table/view)"),
            ("SHOW VIEWS [LIKE 'pattern']",              "List views in the current database"),
            ("SHOW PROCS / PROCEDURES [LIKE 'pattern']", "List stored procedures"),
            ("SHOW COLUMNS FROM <table>",                "List a table's columns"),
            ("SHOW CREATE TABLE <table>",                "Show a table's definition"),
            ("SHOW CREATE PROCEDURE <proc>",             "Show a procedure's definition"),
            ("SHOW CREATE FUNCTION <func>",              "Show a function's definition"),
            ("SHOW INDEX|INDEXES|KEYS FROM <table>",     "List indexes on a table"),
            ("SHOW VARIABLES [LIKE 'pattern']",          "List server variables"),
            ("SHOW STATUS",                              "Show server/version status"),
            ("SHOW PROCEDURE STATUS [LIKE 'pattern']",   "List procedures with metadata"),
            ("SHOW FUNCTION STATUS [LIKE 'pattern']",    "List functions with metadata"),
            ("SHOW PROCESSLIST",                         "List running server processes"),
            ("SHOW COMMANDS [LIKE 'pattern']",           "List dbdb's built-in commands (this command)"),
            ("DESCRIBE / DESC <name>",                   "Describe a table's columns, or a procedure's parameters"),
            ("DESCRIBE PROC / PROCEDURE <name>",         "Describe a stored procedure's parameters"),
            ("USE <database>",                           "Switch the active database"),
            ("EXPORT TABLE <table> TO <file>",           "Export a table's schema + data as mysqldump-style SQL"),
            ("EXPORT VIEW <view> AS JSON TO <file>",     "Export a view's data as JSON"),
            (@"\s / status",                             "Show the current connection status"),
            (@"\c / clear buffer",                       "Clear the multi-line input buffer"),
            (@"quit / exit / \q",                        "End the session"),
        };

        internal static void Print(string? likePattern) {
            var rows = new List<string[]>();
            foreach (var (command, description) in Commands) {
                if (likePattern != null && !LikeMatch(command, likePattern) && !LikeMatch(description, likePattern))
                    continue;
                rows.Add(new[] { command, description });
            }

            if (rows.Count == 0) {
                ResultRenderer.PrintEmpty();
                return;
            }

            ResultRenderer.PrintStringTable(new[] { "COMMAND", "DESCRIPTION" }, rows);
            ResultRenderer.PrintRowCount(rows.Count);
        }

        private static bool LikeMatch(string text, string pattern) {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$";
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
        }

    }

}
