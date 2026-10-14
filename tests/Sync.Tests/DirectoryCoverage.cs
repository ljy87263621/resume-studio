using System.Net;
using System.Net.Sockets;
using System.Text;
using ResumeStudio.Services;

static class DirectoryCoverage
{
    static void Check(bool value,string label) { if(!value) throw new Exception("DAV directories: "+label); }
    public static async Task RunAsync()
    {
        foreach(string root in new[] {"https://dav.jianguoyun.com/dav","https://dav.jianguoyun.com/dav/"})
        {
            Check(SyncWebDav.Address(root).AbsoluteUri=="https://dav.jianguoyun.com/dav/","Nutstore entry accepted as base address");
        }
        Check(SyncWebDav.Address("https://dav.jianguoyun.com/dav/ResumeStudio").AbsoluteUri=="https://dav.jianguoyun.com/dav/ResumeStudio/","folder address accepted and normalized");
        Check(SyncWebDav.SyncDirectory("https://dav.jianguoyun.com/dav").AbsoluteUri=="https://dav.jianguoyun.com/dav/ResumeStudio/","Nutstore base uses application folder");
        Check(SyncWebDav.SyncDirectory("https://example.org/remote.php/dav/files/user/").AbsoluteUri=="https://example.org/remote.php/dav/files/user/ResumeStudio/","third-party base uses application folder");
        Check(SyncWebDav.SyncDirectory("https://example.org/dav/ResumeStudio/").AbsoluteUri=="https://example.org/dav/ResumeStudio/","application folder is not appended twice");
        Check(SyncWebDav.SyncDirectory("https://example.org/dav/legacy/","https://example.org/dav/legacy/").AbsoluteUri=="https://example.org/dav/legacy/","saved legacy directory stays in place");
        using var listener=new HttpListener(); var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();string mode="missing";int puts=0,properties=0;
        var server=Task.Run(async ()=>
        {
            while(listener.IsListening)
            {
                HttpListenerContext context;try {context=await listener.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
                var request=context.Request;var response=context.Response;
                if(request.HttpMethod=="PROPFIND")
                {
                    properties++;Check(request.Url!.AbsolutePath=="/dav/ResumeStudio/"&&request.Headers["Depth"]=="0","check only selected directory");
                    if(mode=="missing") response.StatusCode=404;
                    else if(mode=="unauthorized") response.StatusCode=401;
                    else if(mode=="forbidden") response.StatusCode=403;
                    else if(mode=="limited") response.StatusCode=429;
                    else if(mode=="redirect") {response.StatusCode=302;response.RedirectLocation=$"http://127.0.0.1:{port}/unexpected/";}
                    else
                    {
                        response.StatusCode=207;response.ContentType="application/xml";
                        string type=mode=="file"?"":"<d:collection/>";
                        string status=mode=="property-error"?"404 Not Found":"200 OK";
                        string xml=mode=="bad-xml"?"invalid":"<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/ResumeStudio/</d:href><d:propstat><d:prop><d:resourcetype>"+type+"</d:resourcetype></d:prop><d:status>HTTP/1.1 "+status+"</d:status></d:propstat></d:response></d:multistatus>";
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(xml));
                    }
                }
                else if(request.HttpMethod=="PUT") {puts++;response.StatusCode=404;}
                else response.StatusCode=404;
                response.Close();
            }
        });
        using var dav=new SyncWebDav(new Uri($"http://127.0.0.1:{port}/dav/ResumeStudio/"),"user","password");
        try
        {
            async Task Error(Func<Task> action,string expected)
            {
                try {await action();throw new Exception("Expected directory error: "+expected);}
                catch(IOException ex) {Check(ex.Message.Contains(expected),"actionable error for "+mode+": "+ex.Message);}
            }
            await Error(()=>dav.ReadAsync(default),"创建目录失败");Check(puts==0,"failed directory creation never starts an upload");
            mode="normal";Check(await dav.ReadAsync(default) is null,"existing directory with no sync file is a valid first sync");
            await Error(()=>dav.WriteAsync(new byte[] {123,125},null,default),"上传失败（HTTP 404）");
            foreach(var item in new[] {("unauthorized","应用密码"),("forbidden","权限"),("limited","频率"),("redirect","重定向"),("file","不是文件夹"),("property-error","无法确认"),("bad-xml","无法确认")})
            {mode=item.Item1;await Error(()=>dav.ReadAsync(default),item.Item2);}
            Check(properties==10,"missing-file and first-upload directory checks; redirects never followed");
        }
        finally {listener.Stop();await server;}
        Console.WriteLine("PASS DAV directories: Nutstore base accepted, failed creation vs first sync, Depth 0, HTTP errors, malformed/non-collection properties");
    }
}
