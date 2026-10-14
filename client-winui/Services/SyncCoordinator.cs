using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResumeStudio.Services;

public sealed record SyncLocal(JsonObject Snapshot,string Hash);
public interface ISyncLocalStore
{
    Task<SyncLocal> ReadAsync(CancellationToken cancellationToken);
    Task ValidateAsync(JsonObject snapshot,CancellationToken cancellationToken);
    Task<SyncLocal> ApplyAsync(string expectedHash,JsonObject snapshot,CancellationToken cancellationToken);
}
public sealed record SyncPlan(SyncAction Action,SyncLocal Local,JsonObject? RemoteEnvelope,string? ETag,string Target);
public sealed record SyncBaseline(string Target,string DataHash,string ETag,string At);

/// <summary>Whole-snapshot three-way decisions: divergent edits require an explicit direction.</summary>
public sealed class SyncCoordinator(ISyncLocalStore local,ISyncRemoteStore remote,string directory,string target,string deviceId)
{
    private readonly string baselineFile=Path.Combine(directory,"sync-baseline.json");
    private SyncBaseline? ReadBaseline()
    {
        try { var baseline=JsonSerializer.Deserialize<SyncBaseline>(File.ReadAllText(baselineFile)); return baseline?.Target==target?baseline:null; }
        catch(Exception ex) when(ex is IOException or JsonException) { return null; }
    }
    public async Task<SyncPlan> InspectAsync(CancellationToken cancellationToken=default)
    {
        var current=await local.ReadAsync(cancellationToken); var fetched=await remote.ReadAsync(cancellationToken); var baseline=ReadBaseline();
        if(fetched is null) return new(baseline is null?SyncAction.Push:SyncAction.Conflict,current,null,null,target);
        var envelope=SyncProtocol.Parse(fetched.Bytes); var data=SyncProtocol.Snapshot(envelope,current.Snapshot);
        // Backend's schema and document validation runs even before deciding to upload local data over remote.
        await local.ValidateAsync(data,cancellationToken);
        var action=SyncDecision.Choose(SyncProtocol.Hash(current.Snapshot),SyncProtocol.Hash(data),baseline?.DataHash);
        if(action==SyncAction.Unchanged&&current.Snapshot.ContainsKey("interviews")&&envelope["extensions"]?["interviewStudy"] is null)action=SyncAction.Push;
        return new(action,current,envelope,fetched.ETag,target);
    }
    public async Task<SyncAction> ExecuteAsync(SyncPlan plan,SyncAction? resolution=null,CancellationToken cancellationToken=default)
    {
        if(plan.Target!=target) throw new IOException("同步目标已改变，请重新检查。");
        SyncAction action=plan.Action==SyncAction.Conflict?resolution??SyncAction.Conflict:plan.Action;
        if(action==SyncAction.Conflict) throw new IOException("本机与云端都有变化，请明确选择保留哪一端。");
        if(plan.Action==SyncAction.Conflict&&action is not (SyncAction.Push or SyncAction.Pull)) throw new IOException("冲突处理方向无效。");
        var current=await local.ReadAsync(cancellationToken);
        if(current.Hash!=plan.Local.Hash) throw new IOException("本机资料已改变，请重新检查同步。");
        // Re-read the observed remote, so a stale conflict dialog cannot overwrite newer cloud data.
        var observed=await remote.ReadAsync(cancellationToken);
        if(observed?.ETag!=plan.ETag) throw new IOException("云端资料已改变，请重新检查同步。");
        string etag=plan.ETag??"",dataHash;
        if(action==SyncAction.Push)
        {
            var envelope=plan.RemoteEnvelope is null?SyncProtocol.Create(current.Snapshot,deviceId):(JsonObject)plan.RemoteEnvelope.DeepClone();
            SyncProtocol.WriteSnapshot(envelope,current.Snapshot); envelope["deviceId"]=deviceId; envelope["snapshotId"]=Guid.NewGuid().ToString("N"); envelope["createdAt"]=DateTimeOffset.UtcNow.ToString("O");
            var bytes=Encoding.UTF8.GetBytes(envelope.ToJsonString()); etag=await remote.WriteAsync(bytes,plan.ETag,cancellationToken); dataHash=SyncProtocol.Hash(current.Snapshot);
        }
        else if(action==SyncAction.Pull)
        {
            if(plan.RemoteEnvelope is null) throw new IOException("云端同步包已不存在，无法下载。可明确选择重新上传本机资料。");
            var data=SyncProtocol.Snapshot(plan.RemoteEnvelope,plan.Local.Snapshot); var result=await local.ApplyAsync(plan.Local.Hash,data,cancellationToken); dataHash=SyncProtocol.Hash(result.Snapshot);
            if(dataHash!=SyncProtocol.Hash(data)) throw new IOException("导入结果不一致，本次不建立同步基线，请检查本机备份。");
        }
        else dataHash=SyncProtocol.Hash(current.Snapshot);
        Directory.CreateDirectory(directory); string temporary=baselineFile+".tmp";
        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new SyncBaseline(target,dataHash,etag,DateTimeOffset.UtcNow.ToString("O"))),cancellationToken); File.Move(temporary,baselineFile,true);
        return action;
    }
}
