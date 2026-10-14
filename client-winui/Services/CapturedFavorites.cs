namespace ResumeStudio.Services;

/// <summary>One browser session: accepted jobs are saved in order, with retryable failures.</summary>
public sealed class CapturedFavorites(Func<Job,Task<CollectionResult<Job>>> save)
{
    private readonly object gate=new();
    private readonly Dictionary<string,Job> pending=new(StringComparer.Ordinal);
    private readonly HashSet<string> stored=new(StringComparer.Ordinal);
    private Task running=Task.CompletedTask;
    private int added,existing;
    private string error="";
    public Action? Changed {get;set;}
    public Action<Job>? Saved {get;set;}
    public int Added {get {lock(gate)return added;}}
    public int Existing {get {lock(gate)return existing;}}
    public int Pending {get {lock(gate)return pending.Count;}}
    public bool Busy {get {lock(gate)return !running.IsCompleted;}}
    public string Error {get {lock(gate)return error;}}
    public void Accept(Job job)
    {
        if(string.IsNullOrEmpty(job.CanonicalUrl))throw new ArgumentException("采集职位缺少规范链接。");
        lock(gate) {
            if(stored.Contains(job.CanonicalUrl))return;
            pending[job.CanonicalUrl]=pending.TryGetValue(job.CanonicalUrl,out var previous)?PlatformCapture.Merge(previous,job):job;
        }
    }
    public Task SaveAsync(bool retry=false)
    {
        TaskCompletionSource completion;
        lock(gate) {
            if(!running.IsCompleted)return running;
            if(error.Length>0&&!retry)return Task.CompletedTask;
            error="";completion=new(TaskCreationOptions.RunContinuationsAsynchronously);running=completion.Task;
        }
        _=Drain(completion);return completion.Task;
    }
    private async Task Drain(TaskCompletionSource completion)
    {
        try {
            Changed?.Invoke();
            while(true) {
                Job? job;lock(gate){job=pending.Values.FirstOrDefault();if(job is null)completion.TrySetResult();}if(job is null)break;
                var result=await save(job);
                lock(gate){pending.Remove(job.CanonicalUrl);stored.Add(job.CanonicalUrl);if(result.Created)added++;else existing++;}
                Saved?.Invoke(result.Record);Changed?.Invoke();
            }
        }catch(Exception ex){lock(gate)error=ex.Message;}
        finally {completion.TrySetResult();Changed?.Invoke();}
    }
}
