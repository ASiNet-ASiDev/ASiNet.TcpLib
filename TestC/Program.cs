
using ASiNet.TcpLib;
using ASiNet.TcpLib.FileTransfer;

var client = new ExtensionTcpClient(x => { });
await client.Connect("127.0.0.1", 55334);

var ft = client.InitFT(55335);
ft.Chandged += OnFtChandged;

Console.WriteLine("OK");

var path = Console.ReadLine()!;

Console.WriteLine(await ft.PostFile(@"C:\Users\Alexa\Downloads\wcp_desktop (1).exe"));
Console.WriteLine(await ft.PostFile(@"C:\Users\Alexa\Downloads\Xamarin.YandexAds-master.zip"));
Console.WriteLine(await ft.PostFile(@"C:\Users\Alexa\Downloads\WcpPP_ru.html"));
Console.WriteLine(await ft.PostFile(@"C:\Users\Alexa\Downloads\ASiNet.Data.Serialization.V2.Common.0.0.5.nupkg"));

Console.ReadLine();

void OnFtChandged(FTClient client, FTStatus status, long t, long p)
{
    if (status == FTStatus.Finishing)
        Console.WriteLine($"{client.EndPointFileInfo.Name} - OK, {p}/{t}");
    if (status == FTStatus.Failed)
        Console.WriteLine($"{client.EndPointFileInfo.Name} - Failed, {p}/{t}");
}