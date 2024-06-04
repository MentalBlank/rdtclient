﻿using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RDNET;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.TorrentClient;
using RdtClient.Service.Helpers;

namespace RdtClient.Service.Services.TorrentClients;

public class RealDebridTorrentClient(ILogger<RealDebridTorrentClient> logger, IHttpClientFactory httpClientFactory) : ITorrentClient
{
    private TimeSpan? _offset;

    private RdNetClient GetClient()
    {
        try
        {
            var apiKey = Settings.Get.Provider.ApiKey;

            if (String.IsNullOrWhiteSpace(apiKey))
            {
                throw new("Real-Debrid API Key not set in the settings");
            }

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(Settings.Get.Provider.Timeout);

            var rdtNetClient = new RdNetClient(null, httpClient, 5);
            rdtNetClient.UseApiAuthentication(apiKey);

            // Get the server time to fix up the timezones on results
            if (_offset == null)
            {
                var serverTime = rdtNetClient.Api.GetIsoTimeAsync().Result;
                _offset = serverTime.Offset;
            }

            return rdtNetClient;
        }
        catch (AggregateException ae)
        {
            foreach (var inner in ae.InnerExceptions)
            {
                logger.LogError(inner, $"The connection to RealDebrid has failed: {inner.Message}");
            }

            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogError(ex, $"The connection to RealDebrid has timed out: {ex.Message}");

            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, $"The connection to RealDebrid has timed out: {ex.Message}");

            throw; 
        }
    }

    private TorrentClientTorrent Map(Torrent torrent)
    {
        return new()
        {
            Id = torrent.Id,
            Filename = torrent.Filename,
            OriginalFilename = torrent.OriginalFilename,
            Hash = torrent.Hash,
            Bytes = torrent.Bytes,
            OriginalBytes = torrent.OriginalBytes,
            Host = torrent.Host,
            Split = torrent.Split,
            Progress = torrent.Progress,
            Status = torrent.Status,
            Added = ChangeTimeZone(torrent.Added)!.Value,
            Files = (torrent.Files ?? []).Select(m => new TorrentClientFile
            {
                Path = m.Path,
                Bytes = m.Bytes,
                Id = m.Id,
                Selected = m.Selected
            }).ToList(),
            Links = torrent.Links,
            Ended = ChangeTimeZone(torrent.Ended),
            Speed = torrent.Speed,
            Seeders = torrent.Seeders,
        };
    }

    public async Task<IList<TorrentClientTorrent>> GetTorrents()
    {
        var page = 0;
        var results = new List<Torrent>();

        while (true)
        {
            var pagedResults = await GetClient().Torrents.GetAsync(page, 100);

            results.AddRange(pagedResults);

            if (pagedResults.Count == 0)
            {
                break;
            }

            page += 100;
        }

        return results.Select(Map).ToList();
    }

    public async Task<TorrentClientUser> GetUser()
    {
        var user = await GetClient().User.GetAsync();
            
        return new()
        {
            Username = user.Username,
            Expiration = user.Premium > 0 ? user.Expiration : null
        };
    }

    public async Task<String> AddMagnet(String magnetLink)
    {
        var result = await GetClient().Torrents.AddMagnetAsync(magnetLink);
        if (result?.Id == null)
        {
            throw new("Unable to add magnet link");
        }
        var resultId = result.Id.ToString() ?? throw new($"Invalid responseID {result}");
        return result.Id;
    }

    public async Task<String> AddFile(Byte[] bytes)
    {
        var result = await GetClient().Torrents.AddFileAsync(bytes);
        if (result?.Id == null)
        {
            throw new("Unable to add torrent file");
        }
        var resultId = result.Id.ToString() ?? throw new($"Invalid responseID {result.Id}");
        return result.Id;
    }

    public async Task<IList<TorrentClientAvailableFile>> GetAvailableFiles(String hash)
    {
        var result = await GetClient().Torrents.GetAvailableFiles(hash);

        var files = result.SelectMany(m => m.Value).SelectMany(m => m.Value).SelectMany(m => m.Values);

        var groups = files.Where(m => m.Filename != null).GroupBy(m => $"{m.Filename}-{m.Filesize}");

        var torrentClientAvailableFiles = groups.Select(m => new TorrentClientAvailableFile
        {
            Filename = m.First().Filename!,
            Filesize = m.First().Filesize
        }).ToList();

        return torrentClientAvailableFiles;
    }

    public async Task SelectFiles(Data.Models.Data.Torrent torrent)
    {
        var files = torrent.Files;

        Log("Seleting files", torrent);

        if (torrent.DownloadAction == TorrentDownloadAction.DownloadAvailableFiles)
        {
            Log($"Determining which files are already available on RealDebrid", torrent);

            var availableFiles = await GetAvailableFiles(torrent.Hash);

            Log($"Found {files.Count}/{torrent.Files.Count} available files on RealDebrid", torrent);

            files = torrent.Files.Where(m => availableFiles.Any(f => m.Path.EndsWith(f.Filename))).ToList();
        }
        else if (torrent.DownloadAction == TorrentDownloadAction.DownloadAll)
        {
            Log("Selecting all files", torrent);
            files = [.. torrent.Files];
        }
        else if (torrent.DownloadAction == TorrentDownloadAction.DownloadManual)
        {
            Log("Selecting manual selected files", torrent);
            files = torrent.Files.Where(m => torrent.ManualFiles.Any(f => m.Path.EndsWith(f))).ToList();
        }
        if (files.Count == 0)
        {
            Log($"Filtered all files out! Downloading ALL files instead!", torrent);

            files = torrent.Files;
        }
        Log($"Selecting {files.Count}/{torrent.Files.Count} files", torrent);

        if (torrent.DownloadAction != TorrentDownloadAction.DownloadManual && torrent.DownloadMinSize > 0)
        {
            var minFileSize = torrent.DownloadMinSize * 1024 * 1024;

            Log($"Determining which files are over {minFileSize} bytes", torrent);

            files = files.Where(m => m.Bytes > minFileSize)
                         .ToList();

            Log($"Found {files.Count} files that match the minimum file size criterea", torrent);
        }

        if (!String.IsNullOrWhiteSpace(torrent.IncludeRegex))
        {
            Log($"Using regular expression {torrent.IncludeRegex} to include only files matching this regex", torrent);

            var newFiles = new List<TorrentClientFile>();
            foreach (var file in files)
            {
                if (Regex.IsMatch(file.Path, torrent.IncludeRegex))
                {
                    Log($"* Including {file.Path}", torrent);
                    newFiles.Add(file);
                }
                else
                {
                    Log($"* Excluding {file.Path}", torrent);
                }
            }

            files = newFiles;

            Log($"Found {files.Count} files that match the regex", torrent);
        } 
        else if (!String.IsNullOrWhiteSpace(torrent.ExcludeRegex))
        {
            Log($"Using regular expression {torrent.IncludeRegex} to ignore files matching this regex", torrent);

            var newFiles = new List<TorrentClientFile>();
            foreach (var file in files)
            {
                if (!Regex.IsMatch(file.Path, torrent.ExcludeRegex))
                {
                    Log($"* Including {file.Path}", torrent);
                    newFiles.Add(file);
                }
                else
                {
                    Log($"* Excluding {file.Path}", torrent);
                }
            }

            files = newFiles;

            Log($"Found {files.Count} files that match the regex", torrent);
        }

        if (files.Count == 0)
        {
            Log($"Filtered all files out! Downloading NO files!", torrent);
			throw new($"No Files Available to Download");
            files = null;
        } else {

			var fileIds = files.Select(m => m.Id.ToString()).ToArray();

			Log($"Selecting files:");

			foreach (var file in files)
			{
				Log($"{file.Id}: {file.Path} ({file.Bytes}b)");
			}

			Log("", torrent);

			await GetClient().Torrents.SelectFilesAsync(torrent.RdId!, [.. fileIds]);
		}
    }

    public async Task Delete(String torrentId)
    {
        await GetClient().Torrents.DeleteAsync(torrentId);
    }

    public async Task<String> Unrestrict(String link)
    {
        var result = await GetClient().Unrestrict.LinkAsync(link);

        if (result.Download == null)
        {
            throw new($"Unrestrict returned an invalid download");
        }

        return result.Download;
    }

    public async Task<Data.Models.Data.Torrent> UpdateData(Data.Models.Data.Torrent torrent, TorrentClientTorrent? torrentClientTorrent)
    {
        try
        {
            if (torrent.RdId == null)
            {
                return torrent;
            }

            var rdTorrent = await GetInfo(torrent.RdId) ?? throw new($"Resource not found");

            if (!String.IsNullOrWhiteSpace(rdTorrent.Filename))
            {
                torrent.RdName = rdTorrent.Filename;
            }

            if (!String.IsNullOrWhiteSpace(rdTorrent.OriginalFilename))
            {
                torrent.RdName = rdTorrent.OriginalFilename;
            }

            if (rdTorrent.Bytes > 0)
            {
                torrent.RdSize = rdTorrent.Bytes;
            }
            else if (rdTorrent.OriginalBytes > 0)
            {
                torrent.RdSize = rdTorrent.OriginalBytes;
            }

            if (rdTorrent.Files != null)
            {
                torrent.RdFiles = JsonConvert.SerializeObject(rdTorrent.Files);
            }

            torrent.RdHost = rdTorrent.Host;
            torrent.RdSplit = rdTorrent.Split;
            torrent.RdProgress = rdTorrent.Progress;
            torrent.RdAdded = rdTorrent.Added;
            torrent.RdEnded = rdTorrent.Ended;
            torrent.RdSpeed = rdTorrent.Speed;
            torrent.RdSeeders = rdTorrent.Seeders;
            torrent.RdStatusRaw = rdTorrent.Status;

            torrent.RdStatus = rdTorrent.Status switch
            {
                "magnet_error" => TorrentStatus.Error,
                "magnet_conversion" => TorrentStatus.Processing,
                "waiting_files_selection" => TorrentStatus.WaitingForFileSelection,
                "queued" => TorrentStatus.Downloading,
                "downloading" => TorrentStatus.Downloading,
                "downloaded" => TorrentStatus.Finished,
                "error" => TorrentStatus.Error,
                "virus" => TorrentStatus.Error,
                "compressing" => TorrentStatus.Downloading,
                "uploading" => TorrentStatus.Uploading,
                "dead" => TorrentStatus.Error,
                _ => TorrentStatus.Error
            };
        }
        catch (Exception ex)
        {
            if (ex.Message == "Resource not found")
            {
                torrent.RdStatusRaw = "deleted";
            }
            else
            {
                throw;
            }
        }

        return torrent;
    }

    public async Task<IList<String>?> GetDownloadLinks(Data.Models.Data.Torrent torrent)
    {
        if (torrent.RdId == null)
        {
            return null;
        }

        var rdTorrent = await GetInfo(torrent.RdId);

        if (rdTorrent.Links == null)
        {
            return null;
        }

        var downloadLinks = rdTorrent.Links.Where(m => !String.IsNullOrWhiteSpace(m)).ToList();

        Log($"Found {downloadLinks.Count} links", torrent);

        foreach (var link in downloadLinks)
        {
            Log($"{link}", torrent);
        }

        Log($"Torrent has {torrent.Files.Count(m => m.Selected)} selected files out of {torrent.Files.Count} files, found {downloadLinks.Count} links, torrent ended: {torrent.RdEnded}", torrent);
        
        // Check if all the links are set that have been selected
        if (torrent.Files.Count(m => m.Selected) == downloadLinks.Count)
        {
            Log($"Matched {torrent.Files.Count(m => m.Selected)} selected files expected files to {downloadLinks.Count} found files", torrent);

            return downloadLinks;
        }

        // Check if all all the links are set for manual selection
        if (torrent.ManualFiles.Count == downloadLinks.Count)
        {
            Log($"Matched {torrent.ManualFiles.Count} manual files expected files to {downloadLinks.Count} found files", torrent);

            return downloadLinks;
        }

        // If there is only 1 link, delay for 1 minute to see if more links pop up.
        if (downloadLinks.Count == 1 && torrent.RdEnded.HasValue)
        {
            var expired = DateTime.UtcNow - torrent.RdEnded.Value.ToUniversalTime();

            Log($"Waiting to see if more links appear, checked for {expired.TotalSeconds} seconds", torrent);

            if (expired.TotalSeconds > 60.0)
            {
                Log($"Waited long enough", torrent);

                return downloadLinks;
            }
        }

        Log($"Did not find any suiteable download links", torrent);
            
        return null;
    }

    private DateTimeOffset? ChangeTimeZone(DateTimeOffset? dateTimeOffset)
    {
        if (_offset == null)
        {
            return dateTimeOffset;
        }

        return dateTimeOffset?.Subtract(_offset.Value).ToOffset(_offset.Value);
    }

    private async Task<TorrentClientTorrent> GetInfo(String torrentId)
    {
        var result = await GetClient().Torrents.GetInfoAsync(torrentId);

        return Map(result);
    }

    private void Log(String message, Data.Models.Data.Torrent? torrent = null)
    {
        if (torrent != null)
        {
            message = $"{message} {torrent.ToLog()}";
        }

        logger.LogDebug(message);
    }
}