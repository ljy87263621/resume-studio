using System.Text;
namespace ResumeStudio.Services;
public static class CalendarExport
{
    private static string Escape(string text)=>text.Replace("\\","\\\\").Replace("\r","").Replace("\n","\\n").Replace(";","\\;").Replace(",","\\,");
    public static string Create(IEnumerable<AgendaEntry> entries)
    {
        var lines=new List<string>{"BEGIN:VCALENDAR","VERSION:2.0","PRODID:-//ResumeStudio//Calendar//ZH","CALSCALE:GREGORIAN"};
        foreach(var entry in entries.Where(e=>!e.Completed)){
            lines.AddRange(["BEGIN:VEVENT","UID:"+Escape(entry.Id)+"@resumestudio.local","DTSTAMP:"+DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"),entry.AllDay?"DTSTART;VALUE=DATE:"+entry.Date.ToString("yyyyMMdd"):"DTSTART:"+entry.At.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"),entry.AllDay?"DTEND;VALUE=DATE:"+entry.Date.AddDays(1).ToString("yyyyMMdd"):"DTEND:"+entry.At.AddHours(1).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"),"SUMMARY:"+Escape(entry.Title),"DESCRIPTION:"+Escape(entry.Item?.Notes??entry.Application?.Notes??"")]);
            if(entry.ReminderEnabled)lines.AddRange(["BEGIN:VALARM","ACTION:DISPLAY","DESCRIPTION:"+Escape(entry.Title),"TRIGGER:-PT"+entry.ReminderMinutes+"M","END:VALARM"]);
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");var folded=new StringBuilder();foreach(var line in lines){int bytes=0;foreach(var rune in line.EnumerateRunes()){int size=rune.Utf8SequenceLength;if(bytes+size>73){folded.Append("\r\n ");bytes=1;}folded.Append(rune);bytes+=size;}folded.Append("\r\n");}return folded.ToString();
    }
}
