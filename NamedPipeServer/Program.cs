using CommandLine;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace Server
    {
        internal abstract class Options : App.Options
        {
            [Option('s', "security-mode", Default = SecurityMode.CurrentSession, Required = false, HelpText = "Security mode: SystemDefault, CurrentUser, CurrentSession, LocalUsers, Everyone")]
            public SecurityMode SecurityMode { get; set; }
        }

        [Verb("receive", HelpText = "Open pipe (write mode) and transfer data from stdin or file to it")]
        internal sealed class ReceiverOpttions : Options
        {
            [Option('f', "file", Default = "stdout", Required = false, HelpText = "File to read data from")]
            public override string File { get; set; }
        }

        [Verb("send", HelpText = "Open pipe (read mode) and write data from it to stdout or file")]
        internal sealed class SenderOptions : Options
        {
            [Option('f', "file", Default = "stdin", Required = false, HelpText = "File to write data from pipe")]
            public override string File { get; set; }
        }

        class Program : App.Program<ReceiverOpttions, SenderOptions>
        {
            private static NamedPipeServerStream GetPipeStream(Options options)
            {
                PipeDirection pipeDirection = GetPipeDirection(options);
                log.Info("Create named pipe {PipeName} server stream (direction: {Direction}, security: {Security})", options.PipeFullName, pipeDirection, options.SecurityMode);
                var pipeSecurity = Security.Get(options.SecurityMode, pipeDirection);
                return  new NamedPipeServerStream(options.PipeName, pipeDirection, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, options.BufferSize, options.BufferSize, pipeSecurity);
            }

            private static async Task Receiver(ReceiverOpttions options, CancellationToken cancellationToken, Func<Task, Task> timeoutHandler)
            {
                using (var pipeStream = GetPipeStream(options))
                {
                    log.Info("Wait for connection");
                    await timeoutHandler(pipeStream.WaitForConnectionAsync(cancellationToken));

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
                    log.Info("Wait for connection");
                    await timeoutHandler(pipeStream.WaitForConnectionAsync(cancellationToken));

                    using (var inStream = GetStream(options))
                    {
                        log.Info("Send data");
                        await inStream.CopyToAsync(pipeStream, options.BufferSize, cancellationToken);
                    }
                }
            }

            public static async Task<int> Main(string[] args) => await Main(args, Sender, Receiver);
        }
    }
}
