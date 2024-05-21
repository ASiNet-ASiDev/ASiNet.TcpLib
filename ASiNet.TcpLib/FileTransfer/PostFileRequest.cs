namespace ASiNet.TcpLib.FileTransfer;
public class PostFileRequest
{
    public string FileName { get; set; } = null!;

    public long FileSize { get; set; }
}


public class PostFileResponse
{
    public string FileName { get; set; } = null!;
    
    public string Address { get; set; } = null!;

    public bool IsDone { get; set; }
}
