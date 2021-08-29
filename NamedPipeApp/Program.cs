using CommandLine;
using NLog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools.App
{
    enum ExitCodes
    {
        NO_ERROR = 0,
        EXCEPTION_OCCURED = 1
    }

    public class HandledException : Exception
    {
        public HandledException(string message, Exception innerException) : base(message, innerException) { }
        public HandledException(Exception innerException) : base("Exception was already handled", innerException) { }
    }

    public abstract class Options
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

        public abstract string File { get; set; }
    }

    public class Program<R,S> where R : Options where S : Options
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
                log.Info("Open {File} file (read mode)", options.File);
                return File.Open(options.File, FileMode.Open, FileAccess.Read);
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
                log.Info("Create {File} file (write mode)", options.File);
                return File.Open(options.File, FileMode.Create, FileAccess.Write);
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

        protected static PipeDirection GetPipeDirection<T>(T options) where T:Options
        {
            if (options is R)
            {
                return PipeDirection.In;
            }
            else if (options is S)
            {
                return PipeDirection.Out;
            }

            throw new ArgumentException("Wrong options class");
        }

        private static async Task TimeoutHandler(Options options, Task task, CancellationTokenSource taskCancellationTokenSource)
        {
            if (options.ConnectTimeout <= 0)
            {
                await task;
                return;
            }

            using (var timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(options.ConnectTimeout), timeoutCancellationTokenSource.Token).ContinueWith((t) => {
                    if (!t.IsCanceled)
                    {
                        log.Warn("Timeout");
                        taskCancellationTokenSource.Cancel();
                    }
                });

                Task waitAny = await Task.WhenAny(timeoutTask, task);
                if (waitAny == task)
                {
                    log.Debug("Cancel timeout task");
                    timeoutCancellationTokenSource.Cancel();
                }
                else
                {
                    await task;
                }
            }
        }

        protected static async Task<int> Main(
            string[] args,
            Func<S, CancellationToken, Func<Task, Task>, Task> senderHandler,
            Func<R, CancellationToken, Func<Task, Task>, Task> receiverHandler
        )
        {
            InitLogging();

            log.Debug("Parse arguments");
            var options = Parser.Default.ParseArguments<S, R>(args);

            try
            {
                await options.WithParsedAsync((S o) => VerbHandler(o, senderHandler));
                await options.WithParsedAsync((R o) => VerbHandler(o, receiverHandler));
                log.Info("Done");
                return (int)ExitCodes.NO_ERROR;
            }
            catch(HandledException)
            {
                return (int)ExitCodes.EXCEPTION_OCCURED;
            }
            catch(OperationCanceledException)
            {
                return (int)ExitCodes.NO_ERROR;
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                return (int)ExitCodes.EXCEPTION_OCCURED;
            }
        }
    }
}
