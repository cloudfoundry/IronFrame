﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NDesk.Options;

namespace IronFoundry.Warden.TestHelper
{
    class Program
    {
        const int SUCCEEDED = 0;
        const int FAILED = 1;
        const int FATAL = -1;

        static void Abort(string message, params object[] args)
        {
            Console.Error.WriteLine(message, args);
            Environment.Exit(FATAL);
        }

        static int AllocateMemory(IEnumerable<string> args)
        {
            int bytes = 0;

            var options = new OptionSet()
            {
                { "bytes=", v => bytes = Int32.Parse(v) },
            };

            options.Parse(args);

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = Marshal.AllocHGlobal(bytes);
                return handle != IntPtr.Zero ? SUCCEEDED : FAILED;
            }
            catch (OutOfMemoryException)
            {
                return FAILED;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    Marshal.FreeHGlobal(handle);
            }
        }

        static int ConsumeCpu(IEnumerable<string> args)
        {
            int duration = 0;

            var options = new OptionSet()
            {
                {"duration=", v => duration = Int32.Parse(v)},
            };

            options.Parse(args);

            Stopwatch stopwatch = Stopwatch.StartNew();
            var threads = new List<Thread>();
            for (var i = 0; i < 10; i++)
            {
                var thread = new Thread(() =>
                {
                    while (duration > stopwatch.ElapsedMilliseconds)
                    {
                        var sieve = new PrimeSieve(500);
                        sieve.ComputePrimes();
                    }
                });
                threads.Add(thread);
            }
            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();

            return SUCCEEDED;
        }

        static int CreateChildProcess(IEnumerable<string> args)
        {
            var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/K";
            process.StartInfo.UseShellExecute = false;

            if (!process.Start())
                return FAILED;

            Console.Out.WriteLine(process.Id);

            Console.ReadLine();

            process.Kill();

            return SUCCEEDED;
        }

        static int ForkBomb()
        {
           Console.WriteLine("FORK");
            try
            {
                var process = new Process();
                process.StartInfo.FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                process.StartInfo.Arguments = "fork-bomb";
                process.StartInfo.UseShellExecute = false;
                if (!process.Start())
                {
                    Console.WriteLine("Failed to start new fork-bomb process.");
                    return FAILED;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
            catch
            {
                Console.WriteLine("Caught Failed to start new fork-bomb process.");
                return FAILED;
            }
        }

        static int WriteClipboard(List<string> commandArgs)
        {
            try
            {
                if (commandArgs.Count > 0)
                {
                    System.Windows.Forms.Clipboard.SetText(String.Join(" ", commandArgs));
                }
                else
                {
                    System.Windows.Forms.Clipboard.Clear();
                }
                Console.WriteLine("Wrote to clipboard!");
                return SUCCEEDED;
            }
            catch (ExternalException)
            {
                Console.WriteLine("Could not write to clipboard");
                return FAILED;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0)
                Abort("No command specified.");

            string command = args[0].ToLowerInvariant();
            var commandArgs = args.Skip(1).ToList();

            bool debug = false;
            int repeatCount = 0;
            int repeatDelay = 0;
            bool waitForInput = false;

            var globalOptions = new OptionSet()
            {
                { "debug", v => debug = v != null },
                { "repeat=", v => repeatCount = Int32.Parse(v) },
                { "repeat-delay=", v => repeatDelay = Int32.Parse(v) },
                { "wait|w", v => waitForInput = v != null },
            };

            commandArgs = globalOptions.Parse(commandArgs);

            if (debug)
                Debugger.Launch();

            if (waitForInput)
                Console.ReadLine();

            try
            {
                int exitCode = SUCCEEDED;

                do
                {
                    switch (command)
                    {
                    case "allocate-memory":
                        exitCode = AllocateMemory(commandArgs);
                        break;

                    case "consume-cpu":
                        exitCode = ConsumeCpu(commandArgs);
                        break;

                    case "create-child":
                        exitCode = CreateChildProcess(commandArgs);
                        break;

                    case "nop":
                        exitCode = SUCCEEDED;
                        break;

                    case "fork-bomb":
                        exitCode = ForkBomb();
                        break;

                    case "write-clipboard":
                        exitCode = WriteClipboard(commandArgs);
                        break;

                    case "read-clipboard":
                        Console.WriteLine(System.Windows.Forms.Clipboard.GetText());
                        exitCode = SUCCEEDED;
                        break;

                    default:
                        Abort("Unknown command '{0}'.", command);
                        break;
                    }

                    if (repeatCount > 0)
                        Thread.Sleep(repeatDelay);
                }
                while (repeatCount-- > 0);

                Environment.Exit(exitCode);
            }
            catch (Exception ex)
            {
                Abort("Unexpected exception:\n{0}", ex);
            }
        }
    }
}
