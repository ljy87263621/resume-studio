using System.Text.Json;
using System.Text.Json.Nodes;
using ResumeStudio;
using ResumeStudio.Services;

static class ModelRoundTrip
{
    public static void Verify()
    {
        const string resumeJson="""
        {"title":"编辑前","templateId":"campus","futureResume":{"precise":792281625142643375935439503361},"profile":{"name":"张三","futureProfile":"保留"},"sections":[{"title":"项目","content":"正文","visible":true,"futureSection":true}]}
        """;
        var resume=JsonSerializer.Deserialize<Resume>(resumeJson,BackendClient.Json)!;resume.Title="编辑后";
        var roundtrip=JsonNode.Parse(JsonSerializer.Serialize(resume,BackendClient.Json))!;
        if(roundtrip["futureResume"]?["precise"]?.ToJsonString()!="792281625142643375935439503361"||roundtrip["profile"]?["futureProfile"]?.GetValue<string>()!="保留"||roundtrip["sections"]?[0]?["futureSection"]?.GetValue<bool>()!=true) throw new Exception("Unknown resume/profile/section fields lost after typed editing");
        var app=JsonSerializer.Deserialize<ApplicationRecord>("{\"status\":\"已投递\",\"futureApplication\":1,\"resumeSnapshot\":"+resumeJson+",\"timeline\":[{\"status\":\"已投递\",\"at\":\"2026-10-14T00:00:00Z\",\"futureEvent\":true}]}",BackendClient.Json)!;app.Notes="更新备注";
        var applied=JsonNode.Parse(JsonSerializer.Serialize(app,BackendClient.Json))!;
        if(applied["futureApplication"]?.GetValue<int>()!=1||applied["resumeSnapshot"]?["futureResume"] is null||applied["timeline"]?[0]?["futureEvent"]?.GetValue<bool>()!=true) throw new Exception("Unknown application/snapshot/timeline fields lost");
        CheckExtension<Job>("{\"title\":\"岗位\",\"future\":true}");
        CheckExtension<CompanySource>("{\"name\":\"公司\",\"future\":true}");
        CheckExtension<ScheduleItem>("{\"title\":\"日程\",\"future\":true}");
        CheckExtension<ResumeRevision>("{\"id\":\"版本\",\"future\":true}");
        var cased=JsonSerializer.Deserialize<Resume>("{\"title\":\"正常名称\",\"Title\":123}",BackendClient.Json)!;
        var casing=JsonNode.Parse(JsonSerializer.Serialize(cased,BackendClient.Json))!;
        if(cased.Title!="正常名称"||casing["Title"]?.GetValue<int>()!=123) throw new Exception("CamelCase known fields must be distinct from case-sensitive extension keys");
        Console.WriteLine("PASS typed models: editing retains resume/profile/section/job/application/timeline/source/schedule/history extensions, precise numbers and case-sensitive keys");
    }
    static void CheckExtension<T>(string json)
    {
        var value=JsonSerializer.Deserialize<T>(json,BackendClient.Json)!;
        if(JsonNode.Parse(JsonSerializer.Serialize(value,BackendClient.Json))!["future"]?.GetValue<bool>()!=true) throw new Exception(typeof(T).Name+" lost extension fields");
    }
}
