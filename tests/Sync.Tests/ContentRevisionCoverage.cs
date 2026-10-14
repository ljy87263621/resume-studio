using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ResumeStudio.Services;

static class ContentRevisionCoverage
{
    static void Check(bool value,string label) {if(!value) throw new Exception("DAV content revision: "+label);}
    static string Fingerprint(byte[] bytes)=>"sha256:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static async Task RunAsync()
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        byte[]? stored=null;string mode="missing-etag";bool corrupt=false;int puts=0;string? matched=null;
        var server=Task.Run(async ()=>
        {
            while(listener.IsListening)
            {
                HttpListenerContext context;try {context=await listener.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
                var request=context.Request;var response=context.Response;
                if(request.HttpMethod=="PROPFIND")
                {response.StatusCode=207;await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>"));}
                else if(request.HttpMethod=="GET")
                {
                    if(stored is null) response.StatusCode=404;
                    else {response.StatusCode=200;if(mode=="weak") response.Headers["ETag"]="W/\"weak\"";if(mode=="strong") response.Headers["ETag"]="\"strong\"";await response.OutputStream.WriteAsync(stored);}
                }
                else
                {
                    puts++;matched=request.Headers["If-Match"];
                    Check(matched is null or "*" or "\"strong\"","local hash never sent as server ETag");
                    if((request.Headers["If-None-Match"]=="*"&&stored is not null)||(matched=="*"&&stored is null)) response.StatusCode=412;
                    else {using var buffer=new MemoryStream();await request.InputStream.CopyToAsync(buffer);stored=corrupt?Encoding.UTF8.GetBytes("corrupted"):buffer.ToArray();response.StatusCode=201;}
                }
                response.Close();
            }
        });
        using var dav=new SyncWebDav(new Uri($"http://127.0.0.1:{port}/dav/"),"user","password");
        try
        {
            async Task Error(Func<Task> operation,string expected)
            {try {await operation();throw new Exception("Expected failure: "+expected);}catch(IOException ex) {Check(ex.Message.Contains(expected),ex.Message);}}
            byte[] first=Encoding.UTF8.GetBytes("first"),second=Encoding.UTF8.GetBytes("second");
            Check(await dav.WriteAsync(first,null,default)==Fingerprint(first),"first upload without ETag returns exact content fingerprint");
            var fetched=await dav.ReadAsync(default);Check(fetched!.ETag==Fingerprint(first),"unchanged content has stable token");
            mode="weak";Check((await dav.ReadAsync(default))!.ETag==fetched.ETag,"weak ETag uses the same content fingerprint");
            Check(await dav.WriteAsync(second,fetched.ETag,default)==Fingerprint(second)&&matched=="*","fallback replacement rechecks bytes and requires an existing file");
            int before=puts;stored=first;
            await Error(()=>dav.WriteAsync(second,Fingerprint(second),default),"云端资料已改变");Check(puts==before,"stale hash never starts PUT");
            stored=null;await Error(()=>dav.WriteAsync(second,Fingerprint(first),default),"云端资料已改变");Check(puts==before,"deleted remote never recreated by stale replacement");
            stored=first;mode="strong";await dav.WriteAsync(second,Fingerprint(first),default);Check(matched=="\"strong\"","newly available strong ETag used after hash precheck");
            mode="missing-etag";stored=first;corrupt=true;
            await Error(()=>dav.WriteAsync(second,Fingerprint(first),default),"内容不一致");corrupt=false;
            before=puts;await Error(()=>dav.WriteAsync(second,"sha256:invalid",default),"版本标识无效");Check(puts==before,"malformed hash rejected before PUT");
            stored=first;await Error(()=>dav.WriteAsync(second,null,default),"云端资料已改变");Check(stored.AsSpan().SequenceEqual(first),"conditional first creation never overwrites existing data");
        }
        finally {listener.Stop();await server;}
        Console.WriteLine("PASS content revisions: missing/weak ETag, create/replace, stable raw-byte hash, stale/deleted remote, strong ETag recovery, read-back mismatch, invalid tokens");
    }
}
