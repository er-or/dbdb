using System;
using System.Runtime.InteropServices;

namespace dbdb {

    internal static class ConsoleHelper {

        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        const int STD_OUTPUT_HANDLE = -11;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        internal static void EnableVirtualTerminal() {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(handle, out uint mode)) {
                SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            }
        }

        internal static void Write(string text, ConsoleColor color) {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }

        internal static void WriteLine(string text, ConsoleColor color) {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        internal static void WriteError(string text) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(text);
            Console.ResetColor();
        }

        internal static void WriteSuccess(string text) => WriteLine(text, ConsoleColor.Green);
        internal static void WriteInfo(string text)    => WriteLine(text, ConsoleColor.Cyan);
        internal static void WriteWarn(string text)    => WriteLine(text, ConsoleColor.Yellow);
        internal static void WriteDim(string text)     => WriteLine(text, ConsoleColor.DarkGray);

    }

}
