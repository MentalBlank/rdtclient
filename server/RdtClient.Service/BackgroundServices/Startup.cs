using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Data;
using RdtClient.Service.Services;
using Serilog;

namespace RdtClient.Service.BackgroundServices;

public class Startup(IServiceProvider serviceProvider) : IHostedService
{
    public static Boolean Ready { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Startup>>();

        logger.LogWarning($"Starting host on version {version}");

        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var settings = scope.ServiceProvider.GetRequiredService<Settings>();
        await settings.Seed();
        await settings.ResetCache();

        // Création du fichier readme.txt
        var readmeFilePath = "/data/db/readme.txt";
        var readmeContent = @"
*******************************************************************************
*                            RdtClient Service                                 *
*******************************************************************************

Exemple de configuration:
   - Si le fichier de configuration n'existe pas il sera généré.
   - Exemple configuration: `/data/db/instances.json.example`.
   - Format:
        {
            ""Category"": {
                ""Host"": ""http://host:port"",
                ""ApiKey"": ""api_key""
                ""RootFolderPath"": ""root_folder""
                ""qualityProfileId"": ""identitiant_profile""


            },
            ""OtherCategory"": {
                ""Host"": ""http://other_host:port"",
                ""ApiKey"": ""other_api_key""
                ""RootFolderPath"": ""root_folder""
                ""qualityProfileId"": ""identitiant_profile""

            }
        }

Pour trouver la valeur de qualityProfileId 
   - radarr
     curl -X 'GET' \
       'http://localhost:7878/api/v3/qualityprofile' \
       -H 'accept: application/json' \
       -H 'X-Api-Key: api_radarr'

   - sonarr
     curl -X 'GET' \
        'http://localhost:8989/api/v3/qualityprofile' \
        -H 'accept: application/json' \
        -H 'X-Api-Key: api_sonarr'

*******************************************************************************
";
        await File.WriteAllTextAsync(readmeFilePath, readmeContent, cancellationToken);

        var exampleConfigPath = "/data/db/instances.json.example";
        if (!File.Exists(exampleConfigPath))
        {
            var directoryPath = Path.GetDirectoryName(exampleConfigPath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var defaultConfig = new
            {
                Category = new { Host = "http://host:port", ApiKey = "api_key", RootFolderPath = "root_folder", qualityProfileId = "identitiant_profile" },
                OtherCategory = new { Host = "http://other_host:port", ApiKey = "other_api_key", RootFolderPath = "root_folder", qualityProfileId = "identitiant_profile" }
            };
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(exampleConfigPath, json, cancellationToken);
        }

        Ready = true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}