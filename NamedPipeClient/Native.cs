using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NamedPipeTools.Client
{
    internal class Native
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
           string lpFileName,
           EFileAccess dwDesiredAccess,
           EFileShare dwShareMode,
           IntPtr lpSecurityAttributes,
           ECreationDisposition dwCreationDisposition,
           EFileAttributes dwFlagsAndAttributes,
           IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool WaitNamedPipe(string lpNamedPipeName, uint nTimeOut);

        [Flags]
        private enum EFileAccess : uint
        {
            Delete = 0x10000,
            ReadControl = 0x20000,
            WriteDAC = 0x40000,
            WriteOwner = 0x80000,
            Synchronize = 0x100000,

            StandardRightsRequired = 0xF0000,
            StandardRightsRead = ReadControl,
            StandardRightsWrite = ReadControl,
            StandardRightsExecute = ReadControl,
            StandardRightsAll = 0x1F0000,
            SpecificRightsAll = 0xFFFF,

            FILE_READ_DATA = 0x0001,        // file & pipe
            FILE_LIST_DIRECTORY = 0x0001,       // directory
            FILE_WRITE_DATA = 0x0002,       // file & pipe
            FILE_ADD_FILE = 0x0002,         // directory
            FILE_APPEND_DATA = 0x0004,      // file
            FILE_ADD_SUBDIRECTORY = 0x0004,     // directory
            FILE_CREATE_PIPE_INSTANCE = 0x0004, // named pipe
            FILE_READ_EA = 0x0008,          // file & directory
            FILE_WRITE_EA = 0x0010,         // file & directory
            FILE_EXECUTE = 0x0020,          // file
            FILE_TRAVERSE = 0x0020,         // directory
            FILE_DELETE_CHILD = 0x0040,     // directory
            FILE_READ_ATTRIBUTES = 0x0080,      // all
            FILE_WRITE_ATTRIBUTES = 0x0100,     // all

            FILE_GENERIC_READ =
            StandardRightsRead |
            FILE_READ_DATA |
            FILE_READ_ATTRIBUTES |
            FILE_READ_EA,

            FILE_GENERIC_WRITE =
            StandardRightsWrite |
            FILE_WRITE_DATA |
            FILE_WRITE_ATTRIBUTES |
            FILE_WRITE_EA |
            FILE_APPEND_DATA
        }

        [Flags]
        private enum EFileShare : uint
        {
            None = 0x00000000,
            Read = 0x00000001,
            Write = 0x00000002,
            Delete = 0x00000004
        }

        private enum ECreationDisposition : uint
        {
            New = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5
        }

        [Flags]
        private enum EFileAttributes : uint
        {
            Readonly = 0x00000001,
            Hidden = 0x00000002,
            System = 0x00000004,
            Directory = 0x00000010,
            Archive = 0x00000020,
            Device = 0x00000040,
            Normal = 0x00000080,
            Temporary = 0x00000100,
            SparseFile = 0x00000200,
            ReparsePoint = 0x00000400,
            Compressed = 0x00000800,
            Offline = 0x00001000,
            NotContentIndexed = 0x00002000,
            Encrypted = 0x00004000,
            Write_Through = 0x80000000,
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            RandomAccess = 0x10000000,
            SequentialScan = 0x08000000,
            DeleteOnClose = 0x04000000,
            BackupSemantics = 0x02000000,
            PosixSemantics = 0x01000000,
            OpenReparsePoint = 0x00200000,
            OpenNoRecall = 0x00100000,
            FirstPipeInstance = 0x00080000
        }

        private static SafeFileHandle CreateFile(string pipeName, PipeDirection pipeDirection)
        {
            EFileAccess fileAccess = 0;
            switch(pipeDirection)
            {
                case PipeDirection.In:
                    fileAccess = EFileAccess.FILE_GENERIC_READ;
                    break;

                case PipeDirection.Out:
                    fileAccess = EFileAccess.FILE_GENERIC_WRITE;
                    break;
            }

            var res = CreateFile(pipeName, fileAccess, EFileShare.None, IntPtr.Zero, ECreationDisposition.OpenExisting, EFileAttributes.Overlapped | EFileAttributes.SequentialScan, IntPtr.Zero);
            if (res.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return res;
        }

        public static FileStream Open(string pipeName, PipeDirection pipeDirection, int bufferSize)
        {
            switch (pipeDirection)
            {
                case PipeDirection.In:
                    {
                        var pipeHandle = CreateFile(pipeName, pipeDirection);
                        return new FileStream(pipeHandle, FileAccess.Read, bufferSize, true);
                    }

                case PipeDirection.Out:
                    {
                        var pipeHandle = CreateFile(pipeName, pipeDirection);
                        return new FileStream(pipeHandle, FileAccess.Write, bufferSize, true);
                    }

                default:
                    throw new ArgumentException();
            }
        }

        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_SEM_TIMEOUT = 121;
        private const uint PIPE_WAIT_DELAY = 125;
        private static readonly TimeSpan PIPE_WAIT_DELAY_TS2 = TimeSpan.FromMilliseconds(2*PIPE_WAIT_DELAY);
        private static readonly TimeSpan PIPE_WAIT_DELAY_TS4 = TimeSpan.FromMilliseconds(4*PIPE_WAIT_DELAY);

        public static async Task WaitNamedPipeAsync(string pipeName, CancellationToken cancellationToken )
        {
            while(!cancellationToken.IsCancellationRequested)
            {
                if (WaitNamedPipe(pipeName, PIPE_WAIT_DELAY)) return;
                var lastError = Marshal.GetLastWin32Error();
                switch(lastError)
                {
                    case ERROR_FILE_NOT_FOUND:
                        await Task.Delay(PIPE_WAIT_DELAY_TS4, cancellationToken);
                        break;

                    case ERROR_SEM_TIMEOUT:
                        await Task.Delay(PIPE_WAIT_DELAY_TS2, cancellationToken);
                        break;

                    default:
                        throw new Win32Exception(lastError);
                }
            }
            
            if (cancellationToken.IsCancellationRequested)
            {
                await Task.FromCanceled(cancellationToken);
            }
        }
    }
}
