﻿using System.Diagnostics;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Helpers;
using SharpCompress.Archives;
using SharpCompress.Archives.IArchiveExtensions;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System;

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
                if (!_cancellationTokenSource.IsCancellationRequested && _torrent.DownloadClient != Data.Enums.DownloadClient.Symlink)
                {
                    Error = $"Compressed file: {_torrent.RdName}";
                    Finished = true;
                }
            });
        }
        catch (Exception ex)
        {
            Error = $"Compressed file: {_torrent.RdName} Error: {ex}";
            Finished = true;
        }
    }

    public void Cancel()
    {
        _cancellationTokenSource.Cancel();
    }

    private async Task Unpack(String filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath) || _torrent.DownloadClient == Data.Enums.DownloadClient.Symlink)
            {
                return;
            }

            var extractPath = destinationPath;
            String? extractPathTemp = null;

            var archiveEntries = await GetArchiveFiles(filePath);

            if (!archiveEntries.Any(m => m.StartsWith(_torrent.RdName + @"\")) && !archiveEntries.Any(m => m.StartsWith(_torrent.RdName + "/")))
            {
                extractPath = Path.Combine(destinationPath, _torrent.RdName!);
            }

            if (archiveEntries.Any(m => m.Contains(".r00")))
            {
                extractPathTemp = Path.Combine(extractPath, Guid.NewGuid().ToString());
                
                if (!Directory.Exists(extractPathTemp))
                {
                    Directory.CreateDirectory(extractPathTemp);
                }
            }
            
            if (extractPathTemp != null)
            {
                Extract(filePath, extractPathTemp, cancellationToken);

                await FileHelper.Delete(filePath);

                var rarFiles = Directory.GetFiles(extractPathTemp, "*.r00", SearchOption.TopDirectoryOnly);

                foreach (var rarFile in rarFiles)
                {
                    var mainRarFile = Path.ChangeExtension(rarFile, ".rar");

                    if (File.Exists(mainRarFile))
                    {
                        Extract(mainRarFile, extractPath, cancellationToken);
                    }

                    await FileHelper.DeleteDirectory(extractPathTemp);
                }
            }
            else
            {
                Extract(filePath, extractPath, cancellationToken);

                await FileHelper.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Error = $"An unexpected error occurred unpacking {download.Link} for torrent {_torrent.RdName}: {ex.Message}";
        }
        finally
        {
            Finished = true;
        }
    }

    private static async Task<IList<String>> GetArchiveFiles(String filePath)
    {
        await using Stream stream = File.OpenRead(filePath);

        var extension = Path.GetExtension(filePath);

        IArchive archive;
        if (extension == ".zip")
        {
            archive = ZipArchive.Open(stream);
        }
        else
        {
            archive = RarArchive.Open(stream);
        }

        var entries = archive.Entries
                             .Where(entry => !entry.IsDirectory)
                             .Select(m => m.Key)
                             .ToList();

        archive.Dispose();

        return entries;
    }

    private void Extract(String filePath, String extractPath, CancellationToken cancellationToken)
    {
        var parts = ArchiveFactory.GetFileParts(filePath);

        var fi = parts.Select(m => new FileInfo(m));

        var extension = Path.GetExtension(filePath);

        IArchive archive;

        archive.Dispose();

        GC.Collect();
    }
}