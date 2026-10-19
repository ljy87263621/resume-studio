using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
namespace ResumeStudio.Services;
public static class SystemNotices
{
    private static bool registered;
    public static string LastError {get;private set;}="";
    public static event Action? Activated;
    public static bool Show(string title,string body)
    {
        try {
            if(!registered){AppNotificationManager.Default.NotificationInvoked+=(_,_)=>Activated?.Invoke();AppNotificationManager.Default.Register();registered=true;}
            var notification=new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();AppNotificationManager.Default.Show(notification);LastError=notification.Id==0?"Notifier returned ID 0; setting="+AppNotificationManager.Default.Setting:"";return notification.Id!=0;
        }catch(Exception ex){LastError=ex.ToString();System.Diagnostics.Debug.WriteLine("System notification: "+ex.Message);return false;}
    }
    public static async Task<bool> VerifyAsync(){
        if(!Show("拾简 · 系统通知验收","这是一条本地验证通知，不是真实日程。"))return false;
        var manager=AppNotificationManager.Default;await Task.Delay(500);var notifications=await manager.GetAllAsync();
        var matches=notifications.Where(n=>n.Payload.Contains("系统通知验收",StringComparison.Ordinal)).ToList();
        foreach(var notification in matches)await manager.RemoveByIdAsync(notification.Id);if(matches.Count==0)LastError="No matching notification in Action Center; count="+notifications.Count+"; setting="+manager.Setting;return matches.Count>0;
    }
}
