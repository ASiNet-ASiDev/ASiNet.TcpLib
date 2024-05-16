using System.Threading.Channels;

namespace ASiNet.TcpLib;
public class EventHandler<TAccept> : IDisposable
{
    public EventHandler(ExtensionTcpClient client)
    {
        BaseClient = client;
        var options = new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        };
        _channel = Channel.CreateBounded<TAccept>(options);
    }

    public ExtensionTcpClient BaseClient { get; }

    private Channel<TAccept> _channel;

    public async Task<TAccept?> WaitPackage(CancellationToken token = default)
    {
        try
        {
            if (await _channel.Reader.WaitToReadAsync(token))
            {
                return await _channel.Reader.ReadAsync(token);
            }
            return default;
        }
        catch
        {
            return default;
        }
    }

    internal void OnAccept(TAccept accept)
    {
        _channel.Writer.TryWrite(accept);
    }


    public void Dispose()
    {
        BaseClient.CloseHandler(this);
        GC.SuppressFinalize(this);
    }

    ~EventHandler()
    {
        Dispose();
    }
}
