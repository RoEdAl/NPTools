# Named pipes command line tools - server and client

The idea is taken from [psmay/windows-named-pipe-utils](https://github.com/psmay/windows-named-pipe-utils). Code rewriten from scratch in *C#* language.

## The Server - `NamedPipeServer.exe`

Server creates named pipe and sets its direction.
Server can work in two modes:

* Receiver - copies data from pipe to `StdOut` or file

    ```.bat
    NamedPipeServer.exe receive MySweetPipe
    NamedPipeServer.exe receive -f ReceivedData.bin MyBinaryPipe
    ```

* Sender - copies data from `StdIn` or file to pipe

    ```.bat
    NamedPipeServer.exe send MySweetPipe
    NamedPipeServer.exe send -f FileToSend.bin MyBinaryPipe
    ```

    Use `-o` option if you want to overwrite existing file.

## The Client

Ppipe created by server may be - in many situations - used as regular file when using full pipe path:

```.bat
REM Send data to named pipe
DIR > \\.\pipe\MySweetPipe

REM Receive data from named pipe
SORT < \\.\pipe\MySweetPipe
```

## The Client - `NamedPipeClient.exe`

`NamedPipeClient` is very similar to `NamedPipeServer`.
It just connects to pipe created by `NamedPipeServer`.
Like server, client can also work in two modes:

* Receiver - copies data from pipe to `StdOut` or file

    ```.bat
    NamedPipeClient.exe receive MySweetPipe
    NamedPipeClient.exe receive -f ReceivedData.bin MyBinaryPipe
    ```

* Sender - copies data from `StdIn` or file to pipe

    ```.bat
    NamedPipeClient.exe send MySweetPipe
    NamedPipeClient.exe send -f FileToSend.bin MyBinaryPipe
    ```

    Additional option `-r` tries to open pipe as regular file (file stream).