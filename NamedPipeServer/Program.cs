﻿using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace Server
    {
        class Program : App.Program
        {
            private static NamedPipeServerStream GetPipeStream(Options options, PipeDirection pipeDirection)
            {
                log.Info("Create named pipe {PipeName} server stream", options.FullPipeName);
                return new NamedPipeServerStream(options.PipeName, pipeDirection, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }

            private static async Task Receiver(ReceiverOpttions options, CancellationToken cancellationToken)
            {
                using (var pipeStream = GetPipeStream(options, PipeDirection.In))
                {
                    log.Info("Wait for connection");
                    await pipeStream.WaitForConnectionAsync(cancellationToken);

                    using (var outStream = GetStream(options))
                    {
                        log.Info("Receive data");
                        await pipeStream.CopyToAsync(outStream, options.BufferSize, cancellationToken);
                    }
                }
            }

            private static async Task Sender(SenderOptions options, CancellationToken cancellationToken)
            {
                using (var pipeStream = GetPipeStream(options, PipeDirection.Out))
                {
                    log.Info("Wait for connection");
                    await pipeStream.WaitForConnectionAsync(cancellationToken);

                    using (var inStream = GetStream(options))
                    {
                        log.Info("Send data");
                        await inStream.CopyToAsync(pipeStream, options.BufferSize, cancellationToken);
                    }
                }
            }

            static async Task<int> Main(string[] args)
            {
                return await Main(args, Sender, Receiver);
            }
        }
    }
}