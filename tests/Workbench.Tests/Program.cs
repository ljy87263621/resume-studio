using ResumeStudio;
using ResumeStudio.Services;
using System.Text.Json;

int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
var templates=JsonSerializer.Deserialize<List<Template>>(File.ReadAllText(args[0]),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
foreach(var template in templates){
    var resume=ResumeExamples.Blank(template);string original=JsonSerializer.Serialize(resume);
    var preview=ResumeExamples.ForPreview(resume,template);
    Check(preview.Sections.Count==template.Sections.Count&&preview.Sections.All(s=>s.Content.Length>0)&&preview.Profile.Name=="示例姓名"&&JsonSerializer.Serialize(resume)==original,"complete sample preview leaves source empty: "+template.Id);
}
var selected=templates.First(t=>t.Sections.Count>2);
var draft=ResumeExamples.Blank(selected);draft.Profile.Name="用户姓名";draft.Profile.Email="my@example.com";draft.Sections[1].Content="- 用户唯一经历";
var composed=ResumeExamples.ForPreview(draft,selected);
Check(composed.Profile.Name=="用户姓名"&&composed.Profile.Email=="my@example.com"&&composed.Sections[1].Content=="- 用户唯一经历"&&composed.Sections[0].Content.Length>0,"partial edits replace only their own fields and complete module");
draft.Sections[0].Visible=false;draft.Sections.Reverse();draft.Sections[0].Title="重复标题";draft.Sections[1].Title="重复标题";
draft.FontFamily="SimSun";draft.Color="#123456";draft.Photo="photo-data";
composed=ResumeExamples.ForPreview(draft,selected);
Check(composed.Sections.Select(s=>s.Title).SequenceEqual(draft.Sections.Select(s=>s.Title))&&composed.Sections.Select(s=>s.Visible).SequenceEqual(draft.Sections.Select(s=>s.Visible))&&composed.FontFamily==draft.FontFamily&&composed.Color==draft.Color&&composed.Photo==draft.Photo,"manual order, duplicate titles, visibility, font, color and photo remain intact");
composed.Sections[0].Content="changed preview";composed.Profile.Name="changed preview";
Check(draft.Sections[0].Content!="changed preview"&&draft.Profile.Name=="用户姓名","preview copy cannot mutate the saved source");
draft.Sections.Clear();composed=ResumeExamples.ForPreview(draft,selected);
Check(composed.Sections.Count==0,"removed modules are not resurrected from the template");
draft.Sections.Add(new ResumeSection {Title="自定义模块",Content=" \n "});composed=ResumeExamples.ForPreview(draft,selected);
Check(composed.Sections.Single().Content.Contains("自定义模块示例")&&draft.Sections.Single().Content==" \n ","blank custom module receives an isolated sample without altering whitespace input");
Console.WriteLine($"Workbench checks passed: {checks}");
