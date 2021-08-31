using CommandLine;
using NLog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools.App
{
    internal enum ExitCode
    {
        NO_ERROR = 0,
        EXCEPTION_OCCURED = 1,
        CANCELLED = 2
    }

    internal class HandledException : Exception
    {
        public HandledException(string message, Exception innerException) : base(message, innerException) { }
        public HandledException(Exception innerException) : base("Exception has been handled", innerException) { }
    }

    public class Options
    {
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Be more verbose")]
        public bool Verbose { get; set; }

        [Option('b', "buffer-size", Default = 32 * 1024, Required = false, HelpText = "Buffer size")]
        public int BufferSize { get; set; }

        [Value(0, MetaName = "PipeName", Required = true, HelpText = "Pipe name")]
        public string PipeName { get; set; }

        [Option('t', "connect-timeout", Default = 60, Required = false, HelpText = "Connection timeout in seconds")]
        public int ConnectTimeout { get; set; }

        public string PipeFullName
        {
            get
            {
                return string.Format(@"\\.\pipe\{0}", PipeName);
            }
        }
    }

    public interface IFilePath
    {
        string File { get; set; }
    }

    public interface IReceiverOptions
    {
        bool Overwrite { get; set; }
    }

    public class Program<R,S> where R : Options, IReceiverOptions, IFilePath where S : Options, IFilePath
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

        protected static Stream GetStream(S options)
        {
            if (string.IsNullOrWhiteSpace(options.File) || string.Compare(options.File, "stdin", true) == 0 || string.Compare(options.File, "-") == 0)
            {
                log.Debug("Get standard input stream");
                return Console.OpenStandardInput(options.BufferSize);
            }
            else
            {
                log.Info("Open {File} file (Mode:{FileMode}, Access:{FileAccess})", options.File, FileMode.Open, FileAccess.Read);
                return new FileStream(options.File, FileMode.Open, FileAccess.Read, FileShare.Read, options.BufferSize, FileOptions.SequentialScan);
            }
        }

        protected static Stream GetStream(R options)
        {
            if (string.IsNullOrWhiteSpace(options.File) || string.Compare(options.File, "stdout", true) == 0 || string.Compare(options.File, "-") == 0)
            {
                log.Debug("Get standard output stream");
                return Console.OpenStandardOutput(options.BufferSize);
            }
            else
            {
                var fileMode = options.Overwrite ? FileMode.Create : FileMode.CreateNew;
                log.Info("Create {File} file (Mode:{FileMode}, Access:{FileAccess})", options.File, fileMode, FileAccess.Write);
                return new FileStream(options.File, fileMode, FileAccess.Write, FileShare.None, options.BufferSize);
            }
        }

        protected static PipeDirection GetPipeDirection<T>(T options) where T : Options
        {
            if (options is R)
            {
                return PipeDirection.In;
            }
            else if (options is S)
            {
                return PipeDirection.Out;
            }

            throw new ArgumentException("Unsupported options class");
        }

        private static async Task GetCancellationTask(int timeoutInSeconds, CancellationToken timeoutCancellationToken, CancellationTokenSource taskCancellationTokenSource )
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutInSeconds), timeoutCancellationToken);

            log.Warn("Timeout");
            taskCancellationTokenSource.Cancel();
        }
        private static async Task TimeoutHandler(Options options, Task task, CancellationTokenSource taskCancellationTokenSource)
        {
            if (options.ConnectTimeout <= 0)
            {
                await task;
                return;
            }

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var cancellationTask = GetCancellationTask(options.ConnectTimeout, cancellationTokenSource.Token, taskCancellationTokenSource);
                var completedTask = await Task.WhenAny(cancellationTask, task);
                if (completedTask == task)
                {
                    log.Debug("Cancel cancellation task");
                    cancellationTokenSource.Cancel();
                }
                else
                {
                    await task; // task cancelled, raise exception
                }
            }
        }

        private static async Task<T> TimeoutHandler<T>(Options options, Task<T> task, CancellationTokenSource taskCancellationTokenSource)
        {
            if (options.ConnectTimeout <= 0)
            {
                return await task;
            }

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var cancellationTask = GetCancellationTask(options.ConnectTimeout, cancellationTokenSource.Token, taskCancellationTokenSource);
                var completedTask = await Task.WhenAny(cancellationTask, task);
                if (completedTask == task)
                {
                    log.Debug("Cancel cancellation task");
                    cancellationTokenSource.Cancel();
                    return task.Result;
                }
                else
                {
                    return await task; // task cancelled, raise exception
                }
            }

        }

        private static async Task VerbHandler<T>(T options, Func<T, CancellationToken, Func<Task, Task>, Task> handler) where T : Options
        {
            if (options.Verbose)
            {
                LogManager.Configuration.LoggingRules[0].SetLoggingLevels(LogLevel.Info, LogLevel.Fatal);
                LogManager.ReconfigExistingLoggers();
            }

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancelEventHandler = (object sender, ConsoleCancelEventArgs e) =>
                {
                    log.Warn("Cancel request");
                    cancellationTokenSource.Cancel();
                };

                log.Debug("Initialize cancel event handler");
                Console.CancelKeyPress += cancelEventHandler;

                try
                {
                    Func<Task, Task> timeoutHandler = (t) => TimeoutHandler(options, t, cancellationTokenSource);
                    await handler(options, cancellationTokenSource.Token, timeoutHandler);
                }
                catch(OperationCanceledException)
                {
                    throw;
                }
                catch(Exception ex)
                {
                    if (options.Verbose)
                    {
                        log.Error(ex);
                    }
                    else
                    {
                        log.Error(ex.Message);
                    }

                    throw new HandledException(ex);
                }
                finally
                {
                    log.Debug("Uninitialize cancel event handler");
                    Console.CancelKeyPress -= cancelEventHandler;
                }
            }
        }


        private static async Task<ExitCode> Run(
            string[] args,
            Func<S, CancellationToken, Func<Task, Task>, Task> senderHandler,
            Func<R, CancellationToken, Func<Task, Task>, Task> receiverHandler
        )
        {
            log.Debug("Parse arguments");
            var options = Parser.Default.ParseArguments<S, R>(args);

            try
            {
                await options.WithParsedAsync((S o) => VerbHandler(o, senderHandler));
                await options.WithParsedAsync((R o) => VerbHandler(o, receiverHandler));
                log.Info("Done");
                return ExitCode.NO_ERROR;
            }
            catch(HandledException)
            {
                return ExitCode.EXCEPTION_OCCURED;
            }
            catch(OperationCanceledException)
            {
                return ExitCode.CANCELLED;
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                return ExitCode.EXCEPTION_OCCURED;
            }
        }

        protected static async Task<int> Main(
            string[] args,
            Func<S, CancellationToken, Func<Task, Task>, Task> senderHandler,
            Func<R, CancellationToken, Func<Task, Task>, Task> receiverHandler
        )
        {
            InitLogging();

            return (int)(await Run(args, senderHandler, receiverHandler));
        }
    }
}
