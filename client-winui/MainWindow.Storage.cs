using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly SemaphoreSlim maintenanceLock=new(1,1);
    private async Task OptimizeStorage(bool compress=true)
    {
        await maintenanceLock.WaitAsync();
        try {
            var report=await Task.Run(()=>StorageMaintenance.Cleanup(api.DataDirectory,AppContext.BaseDirectory));
            string statePath=Path.Combine(api.DataDirectory,"storage-maintenance.json");
            string manifestHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"distribution-manifest.json"))));
            bool alreadyCompressed=false;
            string compressedBackup="";
            if(File.Exists(statePath))try {using var state=JsonDocument.Parse(await File.ReadAllTextAsync(statePath));alreadyCompressed=state.RootElement.TryGetProperty("compressedManifest",out var saved)&&saved.GetString()==manifestHash;if(state.RootElement.TryGetProperty("compressedBackup",out var backup))compressedBackup=backup.GetString()??"";}catch(JsonException){}
            bool compressionSucceeded=alreadyCompressed;
            if(compress&&!alreadyCompressed)compressionSucceeded=await Task.Run(()=>StorageMaintenance.Compress(AppContext.BaseDirectory));
            if(compress&&report.KeptBackup!=""&&report.KeptBackup!=compressedBackup&&await Task.Run(()=>StorageMaintenance.Compress(AppContext.BaseDirectory,backupDirectory:report.KeptBackup)))compressedBackup=report.KeptBackup;
            await File.WriteAllTextAsync(statePath,JsonSerializer.Serialize(new {completedAt=DateTimeOffset.Now,compressedManifest=compressionSucceeded?manifestHash:"",compressedBackup,report},BackendClient.Json));
        } finally {maintenanceLock.Release();}
    }
    private async Task MaintainStorageAfterStartup()
    {
        // Give the updater time to record its successful startup receipt; heavy hashing
        // and compression run off the UI thread and only operate on app-owned files.
        await Task.Delay(5000);
        if(!closing)await OptimizeStorage();
    }
}
