using CommandLine;
using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace App
    {
        public class Program
        {
            protected static readonly Logger log = LogManager.GetCurrentClassLogger();

            private static void InitLogging()
            {
                var config = new NLog.Config.LoggingConfiguration();
                var logconsole = new NLog.Targets.ConsoleTarget("console");
                logconsole.Layout = "[${level:uppercase=true}] ${message}";
                logconsole.Error = true;
                config.AddRule(LogLevel.Warn, LogLevel.Fatal, logconsole);
                LogManager.Configuration = config;
            }

            protected class Options
            {
                [Option('v', "verbose", Required = false, Default = false, HelpText = "Be more verbose")]
                public bool Verbose { get; set; }

                [Option('b', "buffer-size", Default = 16 * 1024, Required = false, HelpText = "Buffer size")]
                public int BufferSize { get; set; }

                [Value(0, MetaName = "PipeName", Required = true, HelpText = "Pipe name")]
                public string PipeName { get; set; }

                public string FullPipeName
                {
                    get
                    {
                        return string.Format(@"\\.\pipe\{0}", PipeName);
                    }
                }
            }

            [Verb("receive", HelpText = "Open pipe (write mode) and transfer data from stdin or file to it")]
            protected sealed class ReceiverOpttions : Options
            {
                [Option('f', "file", Default = "stdout", Required = false, HelpText = "File to read data from")]
                public string File { get; set; }
            }

            [Verb("send", HelpText = "Open pipe (read mode) and write data from it to stdout or file")]
            protected sealed class SenderOptions : Options
            {
                [Option('f', "file", Default = "stdin", Required = false, HelpText = "File to write data from pipe")]
                public string File { get; set; }
            }

            protected static Stream GetStream(SenderOptions options)
            {
                if (string.IsNullOrWhiteSpace(options.File) || string.Compare(options.File, "stdin", true) == 0)
                {
                    log.Debug("Get standard input stream");
                    return Console.OpenStandardInput(options.BufferSize);
                }
                else
                {
                    log.Info("Open {File} file (read mode)", options.File);
                    return File.Open(options.File, FileMode.Open, FileAccess.Read);
                }
            }

            protected static Stream GetStream(ReceiverOpttions options)
            {
                if (string.IsNullOrWhiteSpace(options.File) || string.Compare(options.File, "stdout", true) == 0)
                {
                    log.Debug("Get standard output stream");
                    return Console.OpenStandardOutput(options.BufferSize);
                }
                else
                {
                    log.Info("Create {File} file (write mode)", options.File);
                    return File.Open(options.File, FileMode.Create, FileAccess.Write);
                }
            }

            private static async Task VerbHandler<T>(T options, Func<T, CancellationToken, Task> handler) where T : Options
            {
                if (options.Verbose)
                {
                    LogManager.Configuration.LoggingRules[0].SetLoggingLevels(LogLevel.Info, LogLevel.Fatal);
                    LogManager.ReconfigExistingLoggers();
                }

                using (var cancellationToken = new CancellationTokenSource())
                {
                    ConsoleCancelEventHandler cancelEventHandler = (object sender, ConsoleCancelEventArgs e) =>
                    {
                        log.Warn("Cancel request");
                        cancellationToken.Cancel();
                    };

                    log.Debug("Initialize cancel event handler");
                    Console.CancelKeyPress += cancelEventHandler;

                    try
                    {
                        await handler(options, cancellationToken.Token);
                    }
                    finally
                    {
                        log.Debug("Uninitialize cancel event handler");
                        Console.CancelKeyPress -= cancelEventHandler;
                    }
                }
            }

            protected static async Task<int> Main(
                string[] args,
                Func<SenderOptions, CancellationToken, Task> senderHandler,
                Func<ReceiverOpttions, CancellationToken, Task> receiverHandler
            )
            {
                InitLogging();

                log.Debug("Parse arguments");
                var options = Parser.Default.ParseArguments<SenderOptions, ReceiverOpttions>(args);

                try
                {
                    await options.WithParsedAsync((SenderOptions o) => VerbHandler(o, senderHandler));
                    await options.WithParsedAsync((ReceiverOpttions o) => VerbHandler(o, receiverHandler));
                    log.Info("Done");
                    return 0;
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                    return 1;
                }
            }
        }
    }
}
