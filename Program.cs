using System;
using System.Text;

namespace dbdb {

    internal class Program {

        static int Main(string[] args) {
            Console.OutputEncoding = Encoding.UTF8;
            ConsoleHelper.EnableVirtualTerminal();

            if (args.Length == 0 || IsHelpFlag(args[0])) {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            bool isRestore = args[0].Equals("restore", StringComparison.OrdinalIgnoreCase);

            Config cfg;
            string? parseError;
            if (isRestore) {
                var restored = SessionStore.Load();
                if (restored == null) {
                    ConsoleHelper.WriteError("Error: No saved session found. Connect normally first to create one.");
                    return 1;
                }
                (cfg, parseError) = Config.Parse(args[1..], restored);
            } else {
                (cfg, parseError) = Config.Parse(args);
            }

            if (parseError != null) {
                ConsoleHelper.WriteError("Error: " + parseError);
                Console.Error.WriteLine();
                PrintUsage();
                return 1;
            }

            // Restored sessions never carry a saved password - prompt for one
            // if the connection will actually need it.
            if (isRestore && !cfg.PromptPassword && string.IsNullOrEmpty(cfg.Password)
                    && !cfg.TrustedConnection && cfg.Type != DbType.Sqlite) {
                cfg.PromptPassword = true;
            }

            if (cfg.PromptPassword) {
                cfg.Password = ReadPassword("Enter password: ");
            }

            PrintBanner(cfg);

            DbSession? session = null;
            try {
                session = new DbSession(cfg);
                session.Open();
                ConsoleHelper.WriteSuccess("Connected.");
                Console.WriteLine();

                SessionStore.Save(cfg, session.CurrentDatabase());

                var repl = new Repl(session, cfg);
                repl.Run();

                ConsoleHelper.WriteDim("Bye.");
                return 0;
            } catch (Exception ex) {
                ConsoleHelper.WriteError("Connection error: " + ex.Message);
                return 1;
            } finally {
                session?.Dispose();
            }
        }

        static string ReadPassword(string prompt) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(prompt);
            Console.ResetColor();
            var pw = new System.Text.StringBuilder();
            while (true) {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) {
                    if (pw.Length > 0) pw.Remove(pw.Length - 1, 1);
                } else if (k.KeyChar != '\0') {
                    pw.Append(k.KeyChar);
                }
            }
            Console.WriteLine();
            return pw.ToString();
        }

        static bool IsHelpFlag(string s) =>
            s == "-?" || s == "--help" || s == "-help" || s == "--h";

        static void PrintBanner(Config cfg) {
            string typeLabel = cfg.Type switch {
                DbType.MySQL  => "MySQL",
                DbType.MsSql  => "MS SQL Server",
                DbType.Sqlite => "SQLite",
                _             => "Unknown"
            };
            int w = 60;
            string title = $"dbdb - database client ({typeLabel})";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("//" + new string('=', w + 2) + "//");
            Console.WriteLine($"// {title.PadRight(w)} //");
            Console.WriteLine("//" + new string('=', w + 2) + "//");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void PrintUsage() {
            int w = 60;
            string title = "dbdb - multi-engine database client";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("//" + new string('=', w + 2) + "//");
            Console.WriteLine($"// {title.PadRight(w)} //");
            Console.WriteLine("//" + new string('=', w + 2) + "//");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine("USAGE:");
            WriteRow("restore",             "Reconnect using the last saved session (prompts for password if needed)");
            WriteRow("-t, --type <type>",   "Engine: mysql | mssql | sqlite  (required)");
            WriteRow("-h, --host <host>",   "Server hostname  (default: localhost)");
            WriteRow("-u, --user <user>",   "Username");
            WriteRow("-p [password]",       "Password  (omit value to prompt)");
            WriteRow("--port <port>",       "Port  (default: 3306 / 1433)");
            WriteRow("-d, --database <db>", "Database name, or file path for sqlite");
            WriteRow("-e, --trusted",       "Windows auth  (mssql only)");
            WriteRow("--trust-cert",        "Trust server certificate  (mssql only)");
            WriteRow("--instance <name>",   "Named instance  (mssql only)");

            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  dbdb -t mysql  -h 127.0.0.1 -u root -p");
            Console.WriteLine("  dbdb -t mssql  -h myserver  -e -d MyDb");
            Console.WriteLine("  dbdb -t mysql  -h db.example.com -u app -pSecret mydb");
            Console.WriteLine("  dbdb -t sqlite -d C:\\path\\to\\myfile.db");
            Console.WriteLine("  dbdb restore");
            Console.ResetColor();

            Console.WriteLine();
            Console.WriteLine("IN-SESSION COMMANDS:");
            WriteRow(@"quit / exit / \q", "End the session");
            WriteRow(@"\c",               "Clear multi-line buffer");
            WriteRow(@"\s",               "Show connection status");
            WriteRow("USE <db>",          "Switch database");
            WriteRow("EXPORT TABLE <t> TO <file>",           "Dump table schema + data as mysqldump-style SQL");
            WriteRow("EXPORT VIEW <v> AS JSON TO <file>",    "Dump a view's data as JSON");
            WriteRow("SHOW COMMANDS [LIKE 'pattern']",       "List dbdb's built-in commands");

            Console.WriteLine();
            Console.WriteLine("STATEMENT TERMINATOR:");
            WriteRow(";",  "Terminates a statement (both engines)");
            WriteRow("GO", "Terminates a batch  (mssql only)");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("``'-.,_,.-'``'-.,_,.='``'-.,_,.-'``'-.,_,.='``'-.,_,.='``'-.,_,.-'");
            Console.ResetColor();
        }

        static void WriteRow(string label, string desc) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  {label.PadRight(26)}");
            Console.ResetColor();
            Console.WriteLine(desc);
        }

    }

}
