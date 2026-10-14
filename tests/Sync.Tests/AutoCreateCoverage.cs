using System.Net;
using System.Net.Sockets;
using System.Text;
using ResumeStudio.Services;

static class AutoCreateCoverage
{
    static void Check(bool value,string label) {if(!value) throw new Exception("DAV auto-create: "+label);}
    public static async Task RunAsync()
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        bool exists=false;byte[]? stored=null;int creates=0,puts=0;string mode="normal";
        var server=Task.Run(async ()=>
        {
            while(listener.IsListening)
            {
                HttpListenerContext context;try {context=await listener.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
                var request=context.Request;var response=context.Response;
                switch(request.HttpMethod)
                {
                    case "PROPFIND":
                        Check(request.Url!.AbsolutePath=="/dav/ResumeStudio/"&&request.Headers["Depth"]=="0","only selected folder inspected");
                        if(!exists) response.StatusCode=404;
                        else {response.StatusCode=207;await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/ResumeStudio/</d:href><d:propstat><d:prop><d:resourcetype>"+(mode=="file"?"":"<d:collection/>")+"</d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>"));}
                        break;
                    case "MKCOL":
                        creates++;Check(request.Url!.AbsolutePath=="/dav/ResumeStudio/"&&!request.HasEntityBody,"bodyless MKCOL at exact folder");
                        if(mode=="forbidden") response.StatusCode=403;
                        else if(mode=="parent-missing") response.StatusCode=409;
                        else if(mode=="unsupported") response.StatusCode=405;
                        else if(mode=="redirect") {response.StatusCode=302;response.RedirectLocation=$"http://127.0.0.1:{port}/other/";}
                        else {exists=mode!="unconfirmed";response.StatusCode=mode=="race"?405:201;}
                        break;
                    case "PUT":
                        puts++;Check(exists&&request.Url!.AbsolutePath=="/dav/ResumeStudio/resume-studio.sync.json","upload stays inside created folder");
                        if(request.Headers["If-None-Match"]=="*"&&stored is not null) response.StatusCode=412;
                        else {Check(request.Headers["If-None-Match"]=="*","conditional creation preserved");using var buffer=new MemoryStream();await request.InputStream.CopyToAsync(buffer);stored=buffer.ToArray();response.StatusCode=201;}
                        break;
                    default:
                        if(stored is null) response.StatusCode=404;
                        else {response.StatusCode=200;response.Headers["ETag"]="\"created\"";await response.OutputStream.WriteAsync(stored);}
                        break;
                }
                response.Close();
            }
        });
        using var dav=new SyncWebDav(new Uri($"http://127.0.0.1:{port}/dav/ResumeStudio/"),"user","password");
        try
        {
            Check(await dav.ReadAsync(default) is null&&exists&&creates==1&&puts==0,"first inspection creates folder without uploading data");
            byte[] bytes=Encoding.UTF8.GetBytes("{}");Check(await dav.WriteAsync(bytes,null,default)=="\"created\"","upload and read-back after creation");
            Check(creates==1,"existing folder reused");
            stored=null;exists=false;Check(await dav.WriteAsync(bytes,null,default)=="\"created\""&&creates==2,"direct first upload also creates its directory");
            stored=null;exists=false;mode="race";Check(await dav.ReadAsync(default) is null&&exists,"405 concurrent creation verified by PROPFIND");
            foreach(var item in new[] {("forbidden","权限"),("parent-missing","父目录"),("unsupported","不支持"),("redirect","重定向"),("unconfirmed","未确认")})
            {
                mode=item.Item1;exists=false;int previousCreates=creates,previousPuts=puts;
                try {await dav.ReadAsync(default);throw new Exception("Expected MKCOL failure: "+mode);}
                catch(IOException ex) {Check(ex.Message.Contains(item.Item2),"actionable MKCOL failure: "+ex.Message);}
                Check(creates==previousCreates+1&&puts==previousPuts,"no retry or upload after failed creation");
            }
            mode="file";exists=true;int before=creates;
            try {await dav.ReadAsync(default);throw new Exception("File accepted as directory");}
            catch(IOException ex) {Check(ex.Message.Contains("不是文件夹"),"same-name file rejected");}
            Check(creates==before,"existing file never replaced");
        }
        finally {listener.Stop();await server;}
        Console.WriteLine("PASS DAV auto-create: MKCOL, reuse, concurrent creation, permissions, missing parent, unsupported method, redirects, file collision, conditional upload/read-back");
    }
}
