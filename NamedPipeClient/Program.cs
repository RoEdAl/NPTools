using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace Client
    {
        class Program : App.Program
        {
            static private NamedPipeClientStream GetPipeStream(Options options, PipeDirection pipeDirection)
            {
                log.Info("Open named pipe {PipeName} client stream", options.FullPipeName);
                return new NamedPipeClientStream(".", options.PipeName, pipeDirection, PipeOptions.Asynchronous);
            }

            private static async Task Receiver(ReceiverOpttions options, CancellationToken cancellationToken)
            {
                using (var pipeStream = GetPipeStream(options, PipeDirection.In))
                {
                    log.Info("Connect to server");
                    await pipeStream.ConnectAsync(cancellationToken);

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
                    log.Info("Connect to server");
                    await pipeStream.ConnectAsync(cancellationToken);

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
