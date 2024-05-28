using System.Diagnostics;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Helpers;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;

namespace RdtClient.Service.Services;

public class UnpackClient(Download download, String destinationPath)
{
    public Boolean Finished { get; private set; }
        
    public String? Error { get; private set; }
        
    public Int32 Progess { get; private set; }

    private readonly Torrent _torrent = download.Torrent ?? throw new($"Torrent is null");
    
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public void Start()
    {
        Progess = 0;

        try
        {
            var filePath = DownloadHelper.GetDownloadPath(destinationPath, _torrent, download) ?? throw new("Invalid download path");

            Task.Run(async delegate
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Error = $"Compressed file: {_torrent.RdName}";
                    Finished = true;
                }
            });
        }
        catch (Exception ex)
        {
            Error = $"Compressed file: {_torrent.RdName}";
            Finished = true;
        }
    }

    public void Cancel()
    {
        _cancellationTokenSource.Cancel();
    }
}