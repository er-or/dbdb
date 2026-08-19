using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace dbdb {

    internal static class ResultRenderer {

        internal static void RenderReader(DbDataReader reader, out int rowCount) {
            rowCount = 0;

            int cols = reader.FieldCount;
            string[] headers = new string[cols];
            int[] widths = new int[cols];

            for (int c = 0; c < cols; c++) {
                headers[c] = reader.GetName(c);
                widths[c] = headers[c].Length;
            }

            // sp_helptext and similar return a single "Text" column meant to be read
            // as a continuous script - render as plain text, not a table.
            bool plainText = cols == 1 && headers[0].Equals("Text", StringComparison.OrdinalIgnoreCase);

            int maxWidth = GetMaxColumnWidth();

            var rows = new List<string[]>();
            while (reader.Read()) {
                var row = new string[cols];
                for (int c = 0; c < cols; c++) {
                    string val = reader.IsDBNull(c) ? "NULL" : Convert.ToString(reader.GetValue(c)) ?? "";
                    row[c] = val;
                    if (val.Length > widths[c]) widths[c] = Math.Min(val.Length, maxWidth);
                }
                rows.Add(row);
                rowCount++;
            }

            if (plainText) {
                foreach (var row in rows) Console.WriteLine(row[0]);
            } else {
                PrintTable(headers, widths, rows);
            }
        }

        // Renders a table from plain in-memory string rows (no DbDataReader involved) -
        // used by client-side commands like SHOW COMMANDS that don't query the database.
        internal static void PrintStringTable(string[] headers, List<string[]> rows) {
            int cols = headers.Length;
            int[] widths = new int[cols];
            for (int c = 0; c < cols; c++) widths[c] = headers[c].Length;

            int maxWidth = GetMaxColumnWidth();
            foreach (var row in rows) {
                for (int c = 0; c < cols; c++) {
                    if (row[c].Length > widths[c]) widths[c] = Math.Min(row[c].Length, maxWidth);
                }
            }

            PrintTable(headers, widths, rows);
        }

        private static void PrintTable(string[] headers, int[] widths, List<string[]> rows) {
            string sep = BuildSeparator(widths);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sep);
            Console.ResetColor();

            // Header row
            Console.Write("|");
            for (int c = 0; c < headers.Length; c++) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(" " + headers[c].PadRight(widths[c]) + " ");
                Console.ResetColor();
                Console.Write("|");
            }
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sep);
            Console.ResetColor();

            // Data rows
            foreach (var row in rows) {
                Console.Write("|");
                for (int c = 0; c < row.Length; c++) {
                    string val = row[c];
                    bool isNull = val == "NULL";
                    string cell = val.Length > widths[c]
                        ? val.Substring(0, widths[c] - 1) + "…"
                        : val.PadRight(widths[c]);
                    if (isNull) {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    } else {
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    Console.Write(" " + cell + " ");
                    Console.ResetColor();
                    Console.Write("|");
                }
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(sep);
            Console.ResetColor();
        }

        // Console.WindowWidth throws when stdout isn't a real console (piped/redirected),
        // so fall back to a fixed width in that case instead of crashing.
        private static int GetMaxColumnWidth() {
            try {
                return Math.Max(80, Console.WindowWidth - 4);
            } catch {
                return 80;
            }
        }

        private static string BuildSeparator(int[] widths) {
            var sb = new System.Text.StringBuilder("+");
            foreach (int w in widths) {
                sb.Append(new string('-', w + 2));
                sb.Append('+');
            }
            return sb.ToString();
        }

        internal static void PrintRowsAffected(int count) {
            ConsoleHelper.WriteSuccess($"Query OK, {count} row(s) affected.");
        }

        internal static void PrintEmpty() {
            ConsoleHelper.WriteDim("Empty set.");
        }

        internal static void PrintRowCount(int count) {
            ConsoleHelper.WriteDim($"{count} row(s) in set.");
        }

    }

}
