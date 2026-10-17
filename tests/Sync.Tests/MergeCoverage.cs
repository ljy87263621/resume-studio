using System.Text;
using System.Text.Json.Nodes;
using ResumeStudio.Services;
internal static class MergeCoverage
{
    private sealed class Local(JsonObject initial):ISyncLocalStore {
        public JsonObject Data=initial;public int Writes;
        public Task<SyncLocal> ReadAsync(CancellationToken token)=>Task.FromResult(new SyncLocal((JsonObject)Data.DeepClone(),SyncProtocol.Hash(Data)));
        public Task ValidateAsync(JsonObject snapshot,CancellationToken token)=>Task.CompletedTask;
        public Task<SyncLocal> ApplyAsync(string expected,JsonObject snapshot,CancellationToken token){if(SyncProtocol.Hash(Data)!=expected)throw new IOException("stale local");Data=(JsonObject)snapshot.DeepClone();Writes++;return ReadAsync(token);}
    }
    private sealed class Remote(JsonObject initial):ISyncRemoteStore {
        public byte[] Bytes=Encoding.UTF8.GetBytes(SyncProtocol.Create(initial,"remote-device").ToJsonString());public string Tag="\"one\"";public int Writes;
        public Task<SyncRemote?> ReadAsync(CancellationToken token)=>Task.FromResult<SyncRemote?>(new(Bytes,Tag));
        public Task<string> WriteAsync(byte[] bytes,string? expected,CancellationToken token){if(expected!=Tag)throw new IOException("stale remote");Bytes=bytes;Writes++;return Task.FromResult(Tag="\"two\"");}
    }
    public static async Task RunAsync(){
        var snapshot=JsonNode.Parse("{\"resumes\":[],\"favorites\":[],\"applications\":[],\"sources\":[],\"schedule\":[],\"settings\":{},\"history\":[]}")!.AsObject();
        var local=new Local((JsonObject)snapshot.DeepClone());var remote=new Remote(snapshot);string path=Path.Combine(Path.GetTempPath(),"merge-"+Guid.NewGuid().ToString("N"));
        var coordinator=new SyncCoordinator(local,remote,path,"fixture","local-device");var plan=await coordinator.InspectAsync();var merged=(JsonObject)snapshot.DeepClone();merged["favorites"]!.AsArray().Add(new JsonObject { ["id"]="one",["title"]="保留两端内容" });
        await coordinator.MergeAsync(plan,merged);
        if(local.Writes!=1||remote.Writes!=1||SyncProtocol.Hash(SyncProtocol.Snapshot(SyncProtocol.Parse(remote.Bytes),merged))!=SyncProtocol.Hash(merged)||!Directory.GetFiles(Path.Combine(path,"sync-backups"),"merge-remote-*.json").Any())throw new Exception("Merge did not preserve both backup and content");
        plan=await coordinator.InspectAsync();remote.Tag="\"changed\"";int writes=local.Writes;bool rejected=false;try{await coordinator.MergeAsync(plan,snapshot);}catch(IOException){rejected=true;}
        if(!rejected||local.Writes!=writes)throw new Exception("Stale merge mutated local data");
        Console.WriteLine("PASS sync merge: validate, back up remote, apply local, conditional upload, stale-plan rejection");
    }
}
