﻿using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ASiNet.Data.Serialization.V2;
using ASiNet.Data.Serialization.V2.Extensions;
using ASiNet.Data.Serialization.V2.IO;
using ASiNet.TcpLib.FileTransfer;

namespace ASiNet.TcpLib;

public class ExtensionTcpClient : IDisposable
{
    public ExtensionTcpClient(Action<ISerializerBuilder<ushort>> registerTypes)
    {
        var builder = new SerializerBuilder<ushort>()
            .AllowRecursiveTypeDeconstruction()
            .SetIndexer(new SerializerIndexer())
            .RegisterBaseTypes()
            .RegisterType<PostFileRequest>()
            .RegisterType<PostFileResponse>();
        registerTypes.Invoke(builder);
        _serializer = (Serializer<ushort>)builder.Build();
        _serializer.SubscribeTypeNotFound(OnTypeNotFound);
    }

    public ExtensionTcpClient(TcpClient client, Action<ISerializerBuilder<ushort>> registerTypes)
    {
        var builder = new SerializerBuilder<ushort>()
            .AllowRecursiveTypeDeconstruction()
            .SetIndexer(new SerializerIndexer())
            .RegisterBaseTypes()
            .RegisterType<PostFileRequest>()
            .RegisterType<PostFileResponse>();
        registerTypes.Invoke(builder);
        _serializer = (Serializer<ushort>)builder.Build();
        _client = client;
        _stream = client.GetStream();
        _ = Acceptor();
    }

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<ushort>? AcceptNotRegisterType;
    public event Action<Type>? SendNotRegisterType;
    public event Action<object?>? TypeNotRegister;

    public string? Address => (_client?.Client.LocalEndPoint as IPEndPoint)?.Address.ToString();
    public int? Port => (_client?.Client.LocalEndPoint as IPEndPoint)?.Port;
    public IPEndPoint? LocalEndPoint => _client?.Client.LocalEndPoint as IPEndPoint;
    public IPEndPoint? RemoteEndPoint => _client?.Client.RemoteEndPoint as IPEndPoint;

    public int UpdateDelay { get; set; } = 50;

    public bool Connected => IsConnectedCheck();

    public Serializer<ushort>? CurrentSerializer => _serializer;

    private Serializer<ushort>? _serializer;
    private TcpClient? _client;
    private NetworkStream? _stream;

    private readonly object _lock = new();

    public async Task<bool> Connect(string address, int port)
    {
        try
        {
            if (Connected)
                return true;
            lock (_lock)
            {
                _client = new TcpClient();
            }
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);
            await _client.ConnectAsync(address, port, cts.Token);
            _stream = _client.GetStream();
            _ = Acceptor();
            return _client?.Connected ?? false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if(Connected)
                ClientConnected?.Invoke();
            else
                ClientDisconnected?.Invoke();
        }
    }

    public void Disconnect()
    {
        try
        {
            lock (_lock)
            {
                _client?.Dispose();
            }
            _client = null;
        }
        catch (Exception)
        {

        }
        finally
        {
            if (Connected)
                ClientConnected?.Invoke();
            else
                ClientDisconnected?.Invoke();
        }
    }


    public PackageHandler<TSend, TAccept>? OpenHandler<TSend, TAccept>()
    {
        var h = new PackageHandler<TSend, TAccept>(this);
        if(_serializer!.Subscribe<TAccept>(h.OnAccept))
            return h;
        return null;
    }

    public bool CloseHandler<TSend, TAccept>(PackageHandler<TSend, TAccept> handler)
    {
        return _serializer!.Unsubscribe<TAccept>(handler.OnAccept);
    }

    public EventHandler<TAccept>? OpenHandler<TAccept>()
    {
        var h = new EventHandler<TAccept>(this);
        if(_serializer!.Subscribe<TAccept>(h.OnAccept))
            return h;
        return null;
    }

    public bool Subscribe<T>(Action<T> action) =>
        _serializer!.Subscribe(action);
    public bool Unsubscribe<T>(Action<T> action) =>
        _serializer!.Unsubscribe(action);

    public bool CloseHandler<TAccept>(EventHandler<TAccept> handler)
    {
        return _serializer!.Unsubscribe<TAccept>(handler.OnAccept);
    }

    public bool Send<T>(T message)
    {
        try
        {
            if (!Connected)
            {
                ClientDisconnected?.Invoke();
                return false;
            }
            lock (_lock)
            {
                _serializer!.Serialize(message, (SerializerNetworkStreamIO)_stream!);
#if DEBUG
                Debug.WriteLine($"Sended Package", "ETcpClient");
#endif
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task Acceptor()
    {
        try
        {
            while (Connected)
            {
                if (_stream!.DataAvailable)
                {
                    var res =_serializer!.DeserializeToEvent((SerializerNetworkStreamIO)_stream!);

#if DEBUG
                    Debug.WriteLine($"Accept Package[{res}]", "ETcpClient");
#endif
                }
                else
                    await Task.Delay(UpdateDelay);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            if (Connected)
                ClientConnected?.Invoke();
            else
                ClientDisconnected?.Invoke();
        }
    }


    public FTManager InitFT(int port)
    {
        var ftm = new FTManager(this, port);
        return ftm;
    }

    private bool IsConnectedCheck()
    {
        try
        {
            if (_client != null && _client.Client != null && _client.Client.Connected)
            {
                if (_client.Client.Poll(0, SelectMode.SelectRead))
                {
                    byte[] buff = new byte[1];
                    if (_client.Client.Receive(buff, SocketFlags.Peek) == 0)
                        return false;
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void OnTypeNotFound(object obj)
    {
        if(obj is Type type)
            SendNotRegisterType?.Invoke(type);
        else if(obj is ushort index)
            AcceptNotRegisterType?.Invoke(index);
        TypeNotRegister?.Invoke(obj);
    }

    public void Dispose()
    {
        _serializer?.UnsubscribeTypeNotFound(OnTypeNotFound);
        _serializer = null;
        _client?.Dispose();
        ClientDisconnected?.Invoke();
        ClientDisconnected = null;
        ClientConnected = null;
        SendNotRegisterType = null;
        AcceptNotRegisterType = null;
        TypeNotRegister = null;
        GC.SuppressFinalize(this);
    }
}
