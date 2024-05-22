using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static ASiNet.TcpLib.FileTransfer.FTClient;

namespace ASiNet.TcpLib.FileTransfer;
public class FTManager : IDisposable
{
    public FTManager(ExtensionTcpClient client, int mediaPort)
    {
        _port = mediaPort;
        _client = client;
        client.Subscribe<PostFileRequest>(OnPostFileRequest);
        _postHandler = client.OpenHandler<PostFileRequest, PostFileResponse>() ??
            throw new NotImplementedException();
    }

    public event Action<FTClient, FTStatus, long, long>? Chandged;

    public event Func<PostFileRequest, string?>? AcceptNewFile;

    private ExtensionTcpClient _client;

    private List<FTClient> _activeClients = [];

    private PackageHandler<PostFileRequest, PostFileResponse> _postHandler;

    private int _port;

    private readonly object _locker = new();

    private TcpListener? _listener;

    public async Task<bool> PostFile(string filePath, CancellationToken token = default)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new FileNotFoundException();
            var response = await _postHandler.SendAndWaitAccept(new() { FileName = fi.Name, FileSize = fi.Length }, token);
            if (response is not null && response.FileName == fi.Name && response.IsDone)
            {
                var client = Connect(response.Address, filePath, FTAction.Read);
                if (client is null)
                    return false;
                client.Changed += OnChandged;
                _activeClients.Add(client);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex.Message, "FTM_POST");
#endif
            return false;
        }
    }

    private async void OnPostFileRequest(PostFileRequest request)
    {
        await Task.Run(() =>
        {
            try
            {
            
                var localPath = AcceptNewFile?.Invoke(request);
                if (localPath is null)
                {
                    _client.Send(new PostFileResponse()
                    {
                        IsDone = false,
                        Address = _client.Address!,
                        FileName = request.FileName
                    });
                    return;
                }
                _client.Send(new PostFileResponse()
                {
                    IsDone = true,
                    Address = _client.Address!,
                    FileName = request.FileName
                });
                var client = Wait(localPath, FTAction.Write);
                if(client is not null)
                {
                    client.Changed += OnChandged;
                    _activeClients.Add(client);
                    return;
                }
            }
            catch (Exception ex)
            {
                _client.Send(new PostFileResponse()
                {
                    IsDone = false,
                    Address = _client.Address!,
                    FileName = request.FileName
                });
#if DEBUG
                Debug.WriteLine(ex.Message, "FTM_ON_POST");
#endif
            }
        });
    }


    private FTClient? Connect(string address, string filePath, FTAction action)
    {
        try
        {
            var tcp = new TcpClient(address, _port);
            return new(tcp, filePath, action);
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex.Message, "FTM_CONNECT");
#endif
            return null;
        }
    }

    private FTClient? Wait(string filePath, FTAction action)
    {
        _listener ??= new(IPAddress.Any, _port);
        lock (_locker)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            try
            {
                _listener.Start();
                var tcp = _listener.AcceptTcpClientAsync(cts.Token).Result;
                _listener.Stop();

                return new(tcp, filePath, action);
            }
            catch(Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex.Message, "FTM_WAIT");
#endif
                return null;
            }
            finally
            {
                if(!cts.IsCancellationRequested)
                    cts.Cancel();
                cts.Dispose();
            }
        }
    }

    private void OnChandged(FTClient client, FTStatus status, long arg3, long arg4)
    {
        Chandged?.Invoke(client, status, arg3, arg4);
        if(status is FTStatus.Finishing or FTStatus.Failed)
        {
            client.Dispose();
            client.Changed -= OnChandged;
            _activeClients.Remove(client);
#if DEBUG
            Debug.WriteLine($"client_closed: {client.EndPointFileInfo.Name}.", "FTC_EVENT");
#endif
        }
    }

    public void Dispose()
    {
        _client?.Unsubscribe<PostFileRequest>(OnPostFileRequest);

        _client?.CloseHandler(_postHandler);

        _listener?.Dispose();
        foreach (var client in _activeClients)
        {
            client.Dispose();
        }
        AcceptNewFile = null;
        Chandged = null;
        GC.SuppressFinalize(this);
    }
}
