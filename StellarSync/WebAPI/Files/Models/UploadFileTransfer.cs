using StellarSync.API.Dto.Files;

namespace StellarSync.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto)
    {
    }

    public string LocalFile { get; set; } = string.Empty;
    private long _total;
    public override long Total
    {
        get => Interlocked.Read(ref _total);
        set => Interlocked.Exchange(ref _total, value);
    }
}
