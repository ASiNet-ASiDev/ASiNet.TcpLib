using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ASiNet.TcpLib.FileTransfer;

public enum FTStatus
{
    Starting,
    Working,
    Finishing,
    Failed,
}

public class FTClient : IDisposable
{
    public enum FTAction : byte
    {
        Read,
        Write,
    }

    public FTClient(TcpClient client, string endPointPath, FTAction action)
    {
        _client = client;
        _stream = client.GetStream();
        _endPointPath = endPointPath;
        _action = action;

        _ = Worker();
    }

    private const ushort FILE_HASHSUM_CODE = 0xAA55;
    private const ushort FILE_HASHSUM_COMPARE_FAILED_CODE = 0x5588;
    private const ushort FILE_HASHSUM_COMPARE_DONE_CODE = 0x8855;
    private const ushort FILE_FRAGMENT_CODE = 0x55AA;

    private const string FT_EXE = ".ft_download";

    public event Action<FTClient, FTStatus, long, long>? Changed;

    public FTStatus Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed?.Invoke(this, value, TotalSize, ProccesedSize);
        }
    }

    public int BufferSize { get; set; } = 60_000;

    public long ProccesedSize
    { 
        get => _procSize;
        private set
        {
            _procSize = value;
            Changed?.Invoke(this, Status, TotalSize, value);
        }    
    }

    public FileInfo EndPointFileInfo => new FileInfo(_endPointPath);

    public long TotalSize { get; private set; }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly string _endPointPath;
    private readonly FTAction _action;

    private long _procSize;
    private FTStatus _status = FTStatus.Starting;

    private async Task Worker(CancellationToken token = default)
    {
        try
        {
            await Task.Run(() =>
            {
                switch (_action)
                {
                    case FTAction.Read:
                        ReadFile(token);
                        break;
                    case FTAction.Write:
                        WriteFile(token);
                        break;
                }
            }, token);
        }
        catch (Exception ex)
        {
            Status = FTStatus.Failed;
#if DEBUG
            Debug.WriteLine(ex.Message, "FTC_ERROR");
#endif
        }
    }

    private void ReadFile(CancellationToken token)
    {
        Status = FTStatus.Working;
        WriteFileInfo(_endPointPath);

        using var fs = File.OpenRead(_endPointPath);
        TotalSize = fs.Length;
        var buffer = BufferSize > ushort.MaxValue ? new byte[BufferSize] : stackalloc byte[BufferSize];

        var readed = 0;

        while (!token.IsCancellationRequested && (readed = fs.Read(buffer)) > 0)
        {
            Write(FILE_FRAGMENT_CODE, buffer[..readed]);
            ProccesedSize += readed;
        }

        var (rds, code) = Read(buffer, token);

        if(code == FILE_HASHSUM_COMPARE_DONE_CODE)
        {
            Status = FTStatus.Finishing;
        }
        else
        {
            Status = FTStatus.Failed;
        }
    }

    private void WriteFile(CancellationToken token)
    {
        var (totalSize, Hash) = ReadFileInfo();
        TotalSize = totalSize;

        Status = FTStatus.Working;

        var tempPath = _endPointPath + FT_EXE;

        using var fs = File.Create(tempPath);
        
        var buffer = BufferSize > ushort.MaxValue ? new byte[BufferSize] : stackalloc byte[BufferSize];

        while (!token.IsCancellationRequested && ProccesedSize < TotalSize)
        {
            var (size, code) = Read(buffer, token);
            if (code == FILE_FRAGMENT_CODE)
            {
                fs.Write(buffer[..size]);
                ProccesedSize += size;
            }
        }
        
        var hc = GetFileHashCode(fs);
        if (hc != Hash)
        {
            Write(FILE_HASHSUM_COMPARE_FAILED_CODE, []);
            fs.Close();
            fs.Dispose();
            Status = FTStatus.Failed;
            File.Delete(tempPath);
        }
        else
        {
            Write(FILE_HASHSUM_COMPARE_DONE_CODE, []);
            fs.Close();
            fs.Dispose();
            Status = FTStatus.Finishing;
            if(File.Exists(_endPointPath))
                File.Delete(_endPointPath);
            File.Move(tempPath, _endPointPath);
        }
    }

    private void WriteFileInfo(string path)
    {
        using var file = File.OpenRead(path);
        var size = file.Length;
        var hash = SHA256.HashData(file);

        _stream.Write(BitConverter.GetBytes(FILE_HASHSUM_CODE));
        _stream.Write(BitConverter.GetBytes(size));
        _stream.Write(hash);
    }

    private (long Size, string Hash) ReadFileInfo()
    {
        while (_stream.Socket.Available < SHA256.HashSizeInBytes)
        {
            Task.Delay(50).Wait();
        }
        var buffer = (stackalloc byte[SHA256.HashSizeInBytes]);
        _stream.Read(buffer[..sizeof(ushort)]);
        var code = BitConverter.ToUInt16(buffer);

        if (code != FILE_HASHSUM_CODE)
            throw new IOException("File hash code failed accept.");

        _stream.Read(buffer[..sizeof(long)]);
        var size = BitConverter.ToInt64(buffer);
        _stream.Read(buffer);
        var hash = Convert.ToHexString(buffer);

        return (size, hash);

    }

    private string GetFileHashCode(FileStream file)
    {
        file.Position = 0;
        file.Flush();
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private void Write(ushort code, Span<byte> data)
    {
        var buff = (stackalloc byte[sizeof(int)]);
        BitConverter.TryWriteBytes(buff, code);
        _stream.Write(buff[..sizeof(ushort)]);
        BitConverter.TryWriteBytes(buff, data.Length);
        _stream.Write(buff);

        if (data.Length > 0)
            _stream.Write(data);
    }

    private (int ReadedDataSize, ushort Code) Read(Span<byte> data, CancellationToken token)
    {
        while (_stream.Socket.Available < sizeof(int) + sizeof(ushort) && !token.IsCancellationRequested)
        {
            Task.Delay(50, token).Wait(token);
        }
        if (token.IsCancellationRequested)
            return (0, 0);
        var buff = (stackalloc byte[sizeof(int)]);
        _stream.Read(buff[..sizeof(ushort)]);
        var code = BitConverter.ToUInt16(buff);
        _stream.Read(buff);
        var ds = BitConverter.ToInt32(buff);
        while (_stream.Socket.Available < ds && !token.IsCancellationRequested)
        {
            Task.Delay(50, token).Wait(token);
        }
        if (ds == 0)
            return (0, code);
        _stream.Read(data[..ds]);
        return (ds, code);
    }

    public void Dispose()
    {
        _client?.Dispose();
        Changed = null;
        GC.SuppressFinalize(this);
    }
}
