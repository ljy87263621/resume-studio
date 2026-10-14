using System.Net;
using System.Net.Sockets;
using ResumeStudio.Services;

static class TimeoutCoverage
{
    private static SyncWebDav Client(Uri directory,TimeSpan timeout) => new(directory,"","",timeout);
    public static async Task RunAsync()
    {
        await WithServer(async (context,stop)=>
        {
            context.Response.Headers["ETag"]="\"stall\""; context.Response.ContentLength64=100;
            await context.Response.OutputStream.WriteAsync(new byte[] {123},stop);
            await context.Response.OutputStream.FlushAsync(stop);
            await Task.Delay(Timeout.Infinite,stop);
        },async directory=>
        {
            using var dav=Client(directory,TimeSpan.FromMilliseconds(100)); using var cancel=new CancellationTokenSource();
            await ExpectTimeout(dav.ReadAsync(cancel.Token),cancel,"GET response body stall");
        });
        await WithServer(async (context,stop)=>
        {
            if(context.Request.HttpMethod=="PUT")
            {
                await context.Request.InputStream.CopyToAsync(Stream.Null,stop);
                await Task.Delay(150,stop); context.Response.StatusCode=201; context.Response.Close();
            }
            else
            {
                context.Response.Headers["ETag"]="\"committed\""; context.Response.ContentLength64=2;
                await context.Response.OutputStream.WriteAsync(new byte[] {123},stop); await context.Response.OutputStream.FlushAsync(stop);
                await Task.Delay(150,stop); await context.Response.OutputStream.WriteAsync(new byte[] {125},stop); context.Response.Close();
            }
        },async directory=>
        {
            using var dav=Client(directory,TimeSpan.FromMilliseconds(250)); using var cancel=new CancellationTokenSource();
            await ExpectTimeout(dav.WriteAsync(new byte[] {123,125},null,cancel.Token),cancel,"PUT and read-back share one deadline");
        });
        await WithServer(async (context,stop)=>
        {
            context.Response.Headers["ETag"]="\"cancel\""; context.Response.ContentLength64=100;
            await context.Response.OutputStream.WriteAsync(new byte[] {123},stop); await context.Response.OutputStream.FlushAsync(stop);
            await Task.Delay(Timeout.Infinite,stop);
        },async directory=>
        {
            using var dav=Client(directory,TimeSpan.FromSeconds(2)); using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try {await dav.ReadAsync(cancel.Token);throw new Exception("Caller cancellation was ignored");}
            catch(OperationCanceledException) when(cancel.IsCancellationRequested) {}
        });
        Console.WriteLine("PASS DAV deadlines: stalled GET body, shared PUT/read-back deadline, caller cancellation preserved");
    }
    private static async Task ExpectTimeout(Task operation,CancellationTokenSource cancel,string label)
    {
        if(await Task.WhenAny(operation,Task.Delay(1000))!=operation)
        {
            cancel.Cancel(); try {await operation;} catch(OperationCanceledException) {}
            throw new Exception(label+" did not enforce the operation deadline");
        }
        try {await operation;} catch(IOException ex) when(ex.Message.Contains("超时")) {return;}
        throw new Exception(label+" did not produce an explicit timeout IOException");
    }
    private static async Task WithServer(Func<HttpListenerContext,CancellationToken,Task> serve,Func<Uri,Task> test)
    {
        var probe=new TcpListener(IPAddress.Loopback,0); probe.Start(); int port=((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var listener=new HttpListener(); string address=$"http://127.0.0.1:{port}/"; listener.Prefixes.Add(address); listener.Start();
        using var stop=new CancellationTokenSource();
        var server=Task.Run(async ()=>
        {
            while(!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try {context=await listener.GetContextAsync();} catch(HttpListenerException) when(stop.IsCancellationRequested) {break;} catch(ObjectDisposedException) when(stop.IsCancellationRequested) {break;}
                try {await serve(context,stop.Token);}
                catch(OperationCanceledException) when(stop.IsCancellationRequested)
                {
                    // Stop can dispose the listener queue before this cancellation continuation resumes.
                    try {context.Response.Abort();} catch(ObjectDisposedException) when(stop.IsCancellationRequested) {}
                }
                catch(Exception ex) when(stop.IsCancellationRequested&&(ex is HttpListenerException or ObjectDisposedException or IOException)) {}
            }
        });
        try {await test(new Uri(address));}
        finally {stop.Cancel();listener.Stop();await server;}
    }
}
