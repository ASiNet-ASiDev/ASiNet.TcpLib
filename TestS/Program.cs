
using System.Net.Sockets;
using ASiNet.TcpLib;
using ASiNet.TcpLib.FileTransfer;

var listener = new TcpListener(System.Net.IPAddress.Any, 55334);

listener.Start();
var client = new ExtensionTcpClient(listener.AcceptTcpClient(), x => { });
listener.Stop();

var ft = client.InitFT(55335);
ft.Chandged += OnFtChandged;

ft.AcceptNewFile += OnFile;

Console.WriteLine("OK");

Console.ReadLine();



string? OnFile(PostFileRequest request)
{
    Console.WriteLine($"File name: {request.FileName}, size: {request.FileSize}");

    return Path.Join("C:\\Users\\Alexa\\Downloads\\ftmt", request.FileName);
}

void OnFtChandged(FTClient client, FTStatus status, long t, long p)
{
    if(status == FTStatus.Finishing)
        Console.WriteLine($"{client.EndPointFileInfo.Name} - OK, {p}/{t}");
    if (status == FTStatus.Failed)
        Console.WriteLine($"{client.EndPointFileInfo.Name} - Failed, {p}/{t}");
}
