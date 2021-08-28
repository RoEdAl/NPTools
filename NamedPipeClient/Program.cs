using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools
{
    namespace Client
    {
        class Program : App.Program
        {
            static private NamedPipeClientStream GetPipeStream(Options options)
            {
                log.Info("Open named pipe {PipeName} client stream (direction {Direction})", options.PipeFullName, options.PipeDirection);
                return new NamedPipeClientStream(".", options.PipeName, options.PipeDirection, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.None, System.IO.HandleInheritability.None);
            }

            private static async Task Receiver(ReceiverOpttions options, CancellationToken cancellationToken)
            {
                using (var pipeStream = GetPipeStream(options))
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
                using (var pipeStream = GetPipeStream(options))
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

            public static async Task<int> Main(string[] args)
            {
                return await Main(args, Sender, Receiver);
            }
        }
    }
}
