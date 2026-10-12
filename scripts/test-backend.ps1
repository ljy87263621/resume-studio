param([string]$ReleaseDirectory = '')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dir=Join-Path $root ('artifacts\backend-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $dir | Out-Null
$application = if($ReleaseDirectory) { [IO.Path]::GetFullPath($ReleaseDirectory) } else { Join-Path $root 'release\ResumeStudio' }
$java=Join-Path $application 'runtime\bin\java.exe'
$jar=Join-Path $root 'backend\build\resume-backend.jar'
$tcp=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$tcp.Start(); $port=$tcp.LocalEndpoint.Port; $tcp.Stop()
$token=[guid]::NewGuid().ToString('N')
$headers=@{'X-Resume-Token'=$token}
$base="http://127.0.0.1:$port/api"
$checks=[System.Collections.Generic.List[string]]::new()
function Assert($condition,[string]$name) { if(!$condition) { throw "FAILED: $name" }; $checks.Add($name); Write-Output "PASS: $name" }
function Start-Backend {
    $info=[System.Diagnostics.ProcessStartInfo]::new($java)
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    foreach($arg in @('--add-modules','jdk.httpserver','-jar',$jar,'--port',"$port",'--data',$dir,'--token',$token)) { $info.ArgumentList.Add($arg) }
    $script:server=[System.Diagnostics.Process]::Start($info)
    for($i=0;$i -lt 60;$i++) {
        try { $script:health=Invoke-RestMethod "$base/health" -Headers $headers -NoProxy; return } catch { Start-Sleep -Milliseconds 100 }
    }
    throw 'Backend did not start'
}
function Post($collection,$doc) { Invoke-RestMethod "$base/$collection" -Method Post -Headers $headers -NoProxy -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes(($doc | ConvertTo-Json -Depth 30 -Compress))) }
try {
    Start-Backend
    Assert ($health.status -eq 'ok') 'Java runtime startup'
    $res=Invoke-WebRequest "$base/health" -SkipHttpErrorCheck -NoProxy
    Assert ($res.StatusCode -eq 401) 'Session authentication'
    $templates=Invoke-RestMethod "$base/templates" -Headers $headers -NoProxy
    Assert ($templates.Count -eq 14) 'Template expansion with legacy compatibility'
    $sources=Invoke-RestMethod "$base/job-sources" -Headers $headers -NoProxy
    Assert ($sources.Count -eq 21) 'Extensible employer source registry'
    $configured=Post sources @{name='Board fixture';adapter='greenhouse';slug='stripe';url='https://stripe.com/jobs';enabled=$false;region='全球';builtin=$false}
    $reload=Invoke-RestMethod "$base/job-sources" -Headers $headers -NoProxy
    Assert (@($reload|Where-Object id -eq $configured.id).Count -eq 1) 'Custom employer board persistence'
    $null=Invoke-RestMethod "$base/sources/$($configured.id)" -Method Delete -Headers $headers -NoProxy
    $github=@($templates | Where-Object origin -eq 'github')
    Assert ($github.Count -eq 8 -and @($github.category | Select-Object -Unique).Count -eq 4) 'GitHub templates cover four job categories'
    Assert (@($github | Where-Object { $_.stars -lt 1000 -or !$_.sourceRepo -or !$_.license -or !$_.sourceCommit }).Count -eq 0) 'Verified high-star provenance and licenses'
    $r=@{title='验证简历';templateId='campus';profile=@{name='中文姓名';email='example@test.local'};sections=@(@{title='项目经历';content="## 项目名称`n- 跨行内容 **加粗**`n<script>test</script>";visible=$true});photo=''}
    $saved=Post resumes $r
    Assert ($saved.id -and $saved.profile.name -eq '中文姓名') 'Resume create and Unicode JSON'
    $saved.title='第二版'; $updated=Post resumes $saved
    $list=Invoke-RestMethod "$base/resumes" -Headers $headers -NoProxy
    Assert ($list.Count -eq 1 -and $list[0].title -eq '第二版') 'Resume update/read'
    $advice=Post resume-check $saved
    Assert ($advice.modules.Count -eq 2 -and $advice.tips.Count -ge 1) 'Resume Helper style rule suggestions'
    $imported=Post capture-preview @{text="Java 后端工程师`nhttps://www.zhipin.com/job_detail/abc123456.html?ka=share&utm_source=copy"}
    Assert ($imported.job.source -eq 'BOSS直聘' -and $imported.job.title -eq 'Java 后端工程师') 'BOSS shared link recognition'
    $duplicate=Post capture-preview @{text='https://www.zhipin.com/job_detail/abc123456.html?sid=other'}
    Assert ($duplicate.job.id -eq $imported.job.id -and $imported.job.canonicalUrl -eq 'https://www.zhipin.com/job_detail/abc123456.html') 'Canonical duplicate detection preserves original link'
    $external=Post favorites $imported.job
    $duplicate.job.title='补充后的职位名称'; $null=Post favorites $duplicate.job
    $externalList=Invoke-RestMethod "$base/favorites" -Headers $headers -NoProxy
    Assert ($externalList.Count -eq 1 -and $externalList[0].title -eq '补充后的职位名称') 'External favorite create/update'
    $null=Invoke-RestMethod "$base/favorites/$($external.id)" -Method Delete -Headers $headers -NoProxy
    Assert (Test-Path (Join-Path $dir "resumes\$($saved.id).json.bak.gz")) 'Compressed previous revision backup'
    $server.Kill($true); $server.WaitForExit(); Start-Backend
    $list=Invoke-RestMethod "$base/resumes" -Headers $headers -NoProxy
    Assert ($list[0].profile.name -eq '中文姓名') 'Persistence after server restart'
    $invalid=$saved | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    $invalid.id='../escape'
    $res=Invoke-WebRequest "$base/resumes" -Method Post -Headers $headers -NoProxy -Body ($invalid|ConvertTo-Json -Depth 30) -ContentType 'application/json' -SkipHttpErrorCheck
    Assert ($res.StatusCode -eq 400) 'Path traversal rejected'
    $invalid.id='invalid'; $invalid.templateId='nonexistent'
    $res=Invoke-WebRequest "$base/resumes" -Method Post -Headers $headers -NoProxy -Body ($invalid|ConvertTo-Json -Depth 30) -ContentType 'application/json' -SkipHttpErrorCheck
    Assert ($res.StatusCode -eq 400) 'Unknown template rejected'
    $res=Invoke-WebRequest "$base/resumes" -Method Post -Headers $headers -NoProxy -Body '{bad json' -ContentType 'application/json' -SkipHttpErrorCheck
    Assert ($res.StatusCode -eq 400) 'Malformed JSON rejected'
    $jobs=Invoke-RestMethod "$base/jobs?provider=tencent&keyword=Java&page=1" -Headers $headers -NoProxy -TimeoutSec 30
    Assert ($jobs.status -eq 'ok' -and $jobs.items.Count -gt 0) 'Live Tencent recruitment'
    $employers=@{}
    foreach($provider in @('stripe','palantir','spotify','notion','bosch','nvidia')) {
        $feed=Invoke-RestMethod "$base/jobs?provider=$provider&keyword=&page=1" -Headers $headers -NoProxy -TimeoutSec 40
        Write-Output "$provider source state: $($feed.status) $($feed.message)"
        Assert ($feed.status -eq 'ok' -and $feed.items.Count -gt 0 -and $feed.sources.Count -eq 1) "Live official source: $provider"
        $employers[$provider]=@{count=$feed.items.Count;total=$feed.total;sampleUrl=$feed.items[0].url}
    }
    $cached=Invoke-RestMethod "$base/jobs?provider=tencent&keyword=Java&page=1" -Headers $headers -NoProxy
    Assert ($cached.cached -and $cached.fetchedAt -eq $jobs.fetchedAt) 'Recruitment cache'
    $aggregate=Invoke-RestMethod "$base/jobs?provider=all&keyword=&page=1" -Headers $headers -NoProxy -TimeoutSec 45
    Assert ($aggregate.sources.Count -eq 21 -and @($aggregate.items.company|Select-Object -Unique).Count -gt 3) 'Multi-company aggregation and explicit per-source state'
    $res=Invoke-WebRequest "$base/sources" -Method Post -Headers $headers -NoProxy -Body '{"name":"invalid","adapter":"jsonld","url":"https://127.0.0.1/private","enabled":true}' -ContentType 'application/json' -SkipHttpErrorCheck
    Assert ($res.StatusCode -eq 400) 'Private source configuration rejected'
    $city=Invoke-RestMethod "$base/jobs?provider=tencent&keyword=Java&city=not-a-real-city&page=1" -Headers $headers -NoProxy
    Assert ($city.items.Count -eq 0) 'Current-page city filter'
    $fav=Post favorites $jobs.items[0]
    $favs=Invoke-RestMethod "$base/favorites" -Headers $headers -NoProxy
    Assert ($favs[0].url -eq $jobs.items[0].url) 'Favorite persistence'
    $record=Post applications @{title=$fav.title;company=$fav.company;url=$fav.url;jobId=$fav.id;status='已打开';notes=''}
    $record.status='面试中'; $record.notes='2026-10-12 备注'; $null=Post applications $record
    $records=Invoke-RestMethod "$base/applications" -Headers $headers -NoProxy
    Assert ($records[0].status -eq '面试中') 'Application update'
    $res=Invoke-WebRequest "$base/applications" -Method Post -Headers $headers -NoProxy -Body '{"title":"bad","url":"javascript:alert(1)","status":"已投递"}' -ContentType 'application/json' -SkipHttpErrorCheck
    Assert ($res.StatusCode -eq 400) 'Unsafe job link rejected'
    $blocked=Invoke-RestMethod "$base/jobs?provider=arbeitnow&keyword=Java&page=1" -Headers $headers -NoProxy -TimeoutSec 30
    Assert ($blocked.status -in @('ok','error') -and $null -ne $blocked.items) 'Optional provider exposes state'
    $null=Invoke-RestMethod "$base/resumes/$($saved.id)" -Method Delete -Headers $headers -NoProxy
    $list=Invoke-RestMethod "$base/resumes" -Headers $headers -NoProxy
    Assert ($list.Count -eq 0 -and !(Test-Path (Join-Path $dir "resumes\$($saved.id).json.bak.gz"))) 'Resume delete including compressed backup'
    @{status='passed';checks=$checks;liveJobCount=$jobs.items.Count;sampleJobUrl=$jobs.items[0].url;employers=$employers;aggregateJobCount=$aggregate.items.Count;aggregateSources=$aggregate.sources;optionalProviderStatus=$blocked.status;time=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $root 'artifacts\backend-result.json') -Encoding utf8
} finally { if($server -and !$server.HasExited) { $server.Kill($true); $server.WaitForExit() } }
