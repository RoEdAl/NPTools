using CommandLine;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace Client
    {
        [Verb("receive", HelpText = "Open pipe (write mode) and transfer data from stdin or file to it")]
        internal sealed class ReceiverOpttions : App.Options
        {
            [Option('f', "file", Default = "stdout", Required = false, HelpText = "File to read data from")]
            public override string File { get; set; }
        }

        [Verb("send", HelpText = "Open pipe (read mode) and write data from it to stdout or file")]
        internal sealed class SenderOptions : App.Options
        {
            [Option('f', "file", Default = "stdin", Required = false, HelpText = "File to write data from pipe")]
            public override string File { get; set; }
        }

        class Program : App.Program<ReceiverOpttions, SenderOptions>
        {
            static private NamedPipeClientStream GetPipeStream(App.Options options)
            {
                PipeDirection pipeDirection = GetPipeDirection(options);
                log.Info("Open named pipe {PipeName} client stream (direction {Direction})", options.PipeFullName, pipeDirection);
                return new NamedPipeClientStream(".", options.PipeName, pipeDirection, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.None, System.IO.HandleInheritability.None);
            }

            private static async Task Receiver(ReceiverOpttions options, CancellationToken cancellationToken, Func<Task, Task> timeoutHandler)
            {
                using (var pipeStream = GetPipeStream(options))
                {
                    log.Info("Connect to server");
                    await timeoutHandler(pipeStream.ConnectAsync(cancellationToken));

                    using (var outStream = GetStream(options))
                    {
                        log.Info("Receive data");
                        await pipeStream.CopyToAsync(outStream, options.BufferSize, cancellationToken);
                    }
                }
            }

            private static async Task Sender(SenderOptions options, CancellationToken cancellationToken, Func<Task, Task> timeoutHandler)
            {
                using (var pipeStream = GetPipeStream(options))
                {
                    log.Info("Connect to server");
                    await timeoutHandler(pipeStream.ConnectAsync(cancellationToken));

                    using (var inStream = GetStream(options))
                    {
                        log.Info("Send data");
                        await inStream.CopyToAsync(pipeStream, options.BufferSize, cancellationToken);
                    }
                }
            }

            public static async Task<int> Main(string[] args)
            {
                return await Main(args, Sender, Receiver);
            }
        }
    }
}
