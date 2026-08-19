using System;

namespace dbdb {

    internal enum DbType { MySQL, MsSql, Sqlite }

    internal class Config {

        internal DbType Type      { get; set; } = DbType.MySQL;
        internal string Host      { get; set; } = "localhost";
        internal int    Port      { get; set; } = 0;           // 0 = use driver default
        internal string? User     { get; set; }
        internal string? Password { get; set; }
        internal string? Database { get; set; }
        internal bool   PromptPassword { get; set; } = false;

        // MS SQL only
        internal bool   TrustedConnection    { get; set; } = false;
        internal bool   TrustServerCert      { get; set; } = false;
        internal string? InstanceName        { get; set; }

        // Derived
        internal int EffectivePort => Port != 0 ? Port : (Type == DbType.MySQL ? 3306 : 1433);

        // For SQLite, Database is the file path
        internal bool IsSqlite => Type == DbType.Sqlite;

        internal static (Config cfg, string? error) Parse(string[] args, Config? baseCfg = null) {
            var cfg = baseCfg ?? new Config();
            for (int i = 0; i < args.Length; i++) {
                string a = args[i];

                string? next() => (i + 1 < args.Length) ? args[++i] : null;

                switch (a.ToLowerInvariant()) {
                    case "-t":
                    case "--type":
                        var t = next();
                        if (t == null) return (cfg, "Missing value for " + a);
                        if (t.Equals("mysql",  StringComparison.OrdinalIgnoreCase)) cfg.Type = DbType.MySQL;
                        else if (t.Equals("mssql",  StringComparison.OrdinalIgnoreCase)) cfg.Type = DbType.MsSql;
                        else if (t.Equals("sqlite", StringComparison.OrdinalIgnoreCase)) cfg.Type = DbType.Sqlite;
                        else return (cfg, $"Unknown type '{t}'. Use 'mysql', 'mssql', or 'sqlite'.");
                        break;

                    case "-h":
                    case "--host":
                        cfg.Host = next() ?? cfg.Host;
                        break;

                    case "-p":
                    case "--password":
                        // mysql style: -p alone means prompt; -pVALUE or -p VALUE means use value
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) {
                            cfg.Password = args[++i];
                        } else {
                            cfg.PromptPassword = true;
                        }
                        break;

                    case "--port":
                        var portStr = next();
                        if (portStr == null || !int.TryParse(portStr, out int port))
                            return (cfg, "Invalid port value.");
                        cfg.Port = port;
                        break;

                    case "-u":
                    case "--user":
                    case "--username":
                        cfg.User = next();
                        break;

                    case "-d":
                    case "--database":
                        cfg.Database = next();
                        break;

                    case "-e":
                    case "--trusted":
                    case "--trusted-connection":
                        cfg.TrustedConnection = true;
                        break;

                    case "--trust-server-certificate":
                    case "--trust-cert":
                        cfg.TrustServerCert = true;
                        break;

                    case "--instance":
                        cfg.InstanceName = next();
                        break;

                    default:
                        // mysql-style: positional database name (last bare arg)
                        if (!a.StartsWith("-")) {
                            cfg.Database = a;
                        } else {
                            // Handle -uUSER / -pPASS (mysql style, value attached)
                            if (a.Length > 2 && a[0] == '-' && a[1] != '-') {
                                char flag = a[1];
                                string value = a.Substring(2);
                                if (flag == 'u') { cfg.User = value; break; }
                                if (flag == 'p') { cfg.Password = value; break; }
                                if (flag == 'h') { cfg.Host = value; break; }
                                if (flag == 'D') { cfg.Database = value; break; }
                            }
                            return (cfg, $"Unknown argument: {a}");
                        }
                        break;
                }
            }
            return (cfg, null);
        }

    }

}
