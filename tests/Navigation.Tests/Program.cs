using System.Collections;
using System.Reflection;

var checks = new List<string>();
void Check(bool condition, string name) { if(!condition) throw new Exception(name); checks.Add(name); }
var catalog = Assembly.GetExecutingAssembly().GetType("ResumeStudio.Services.NavigationCatalog");
Check(catalog is not null, "shared navigation catalog exists");
var roots = ((IEnumerable)catalog!.GetProperty("Roots")!.GetValue(null)!).Cast<object>().ToList();
string Key(object node) => (string)node.GetType().GetProperty("Key")!.GetValue(node)!;
string Label(object node) => (string)node.GetType().GetProperty("Label")!.GetValue(node)!;
List<object> Children(object node) => ((IEnumerable)node.GetType().GetProperty("Children")!.GetValue(node)!).Cast<object>().ToList();
var discovery = roots.Single(n => Key(n) == "discovery");
Check(Label(discovery) == "发现职位", "discovery has a visible group label");
Check(Children(discovery).Select(Key).SequenceEqual(new[] { "jobs", "sources" }), "jobs and sources are ordered children of discovery");
Check(Children(discovery).Select(Label).SequenceEqual(new[] { "主页面", "公司来源管理" }), "child labels match the requested directory");
Check(!roots.Any(n => Key(n) is "jobs" or "sources"), "job pages have no duplicate top-level entry");
string Selected(string page) => (string)catalog.GetMethod("SelectedKey")!.Invoke(null, new object[] { page })!;
string? Parent(string page) => (string?)catalog.GetMethod("ParentKey")!.Invoke(null, new object[] { page });
Check(Selected("editor") == "resumes", "editor highlights My Resumes");
Check(Selected("sources") == "sources" && Selected("jobs") == "jobs", "child pages keep their own selected key");
Check(Parent("sources") == "discovery" && Parent("jobs") == "discovery", "both job pages expand the discovery group");
Check(Parent("editor") is null && Parent("settings") is null, "unrelated pages do not expand discovery");
Check(roots.Where(n => (bool)n.GetType().GetProperty("IsFooter")!.GetValue(n)!).Select(Key).SequenceEqual(new[] { "about", "settings" }), "about and settings remain in the footer");
Console.WriteLine($"PASS: {checks.Count} navigation checks");
foreach(var check in checks) Console.WriteLine(check);
