using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeStudio.Services;

public sealed class InterviewQuestion
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData {get;set;}
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public string Title {get;set;}="";
    public string Answer {get;set;}="";
    public string Status {get;set;}="unseen";
    public string ReviewedAt {get;set;}="";
}
public sealed class InterviewDeck
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData {get;set;}
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public string Title {get;set;}="";
    public string SourceName {get;set;}="";
    public string UpdatedAt {get;set;}="";
    public List<InterviewQuestion> Questions {get;set;}=[];
    public override string ToString()=>Title;
}
public readonly record struct InterviewCounts(int Total,int Remembered,int Learning,int Unseen);
public static class InterviewProgress
{
    public static InterviewCounts Count(IEnumerable<InterviewQuestion> questions){var list=questions.ToList();return new(list.Count,list.Count(q=>q.Status=="remembered"),list.Count(q=>q.Status=="learning"),list.Count(q=>q.Status=="unseen"));}
    public static IEnumerable<InterviewQuestion> Filter(InterviewDeck deck,string status)=>deck.Questions.Where(q=>status=="all"||q.Status==status);
    public static string Label(string status)=>status switch {"remembered"=>"已记住","learning"=>"未掌握",_=>"未标记"};
}
public sealed class InterviewWheelNavigator
{
    private int accumulated;
    private DateTimeOffset last=DateTimeOffset.MinValue;
    public static bool ReadingAnswer(int delta,double offset,double extent)=>delta>0?offset>1:offset<extent-1;
    public int Step(int delta,DateTimeOffset now)
    {
        if(delta==0||(now-last).TotalMilliseconds<350)return 0;
        accumulated=Math.Sign(accumulated)==Math.Sign(delta)?accumulated+delta:delta;
        if(Math.Abs(accumulated)<120)return 0;
        last=now;accumulated=0;return delta<0?1:-1;
    }
}
