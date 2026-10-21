package com.resumestudio;

import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.Build;
import java.time.*;
import java.util.*;

/** One next-event alarm; delivery is deduplicated and reconstructed from persisted data after reboot. */
public final class MobileReminders extends BroadcastReceiver {
    private static final String CHANNEL="schedule",PREFS="schedule-notifications";
    public static boolean permitted(Context context){
        NotificationManager manager=(NotificationManager)context.getSystemService(Context.NOTIFICATION_SERVICE);
        if(Build.VERSION.SDK_INT>=33&&context.checkSelfPermission("android.permission.POST_NOTIFICATIONS")!=PackageManager.PERMISSION_GRANTED)return false;
        NotificationChannel channel=manager.getNotificationChannel(CHANNEL);return manager.areNotificationsEnabled()&&(channel==null||channel.getImportance()!=NotificationManager.IMPORTANCE_NONE);
    }
    public static void reconcile(Context context){
        try{
            var manager=(NotificationManager)context.getSystemService(Context.NOTIFICATION_SERVICE);
            if(Build.VERSION.SDK_INT>=26)manager.createNotificationChannel(new NotificationChannel(CHANNEL,"面试与日程提醒",NotificationManager.IMPORTANCE_HIGH));
            Store store=MobileData.open(context.getFilesDir().toPath().resolve("data"));List<Map<String,Object>> events=new ArrayList<>();
            for(var item:store.list("schedule"))if(Boolean.TRUE.equals(item.get("reminderEnabled"))&&!Boolean.TRUE.equals(item.get("completed"))){var event=new LinkedHashMap<>(item);event.put("_kind","schedule");events.add(event);}
            for(var item:store.list("applications"))if(!Boolean.TRUE.equals(item.get("reminderDone"))&&!Arrays.asList("已录用","已结束").contains(Json.str(item,"status"))&&!Json.str(item,"dueAt").isEmpty()){
                var event=new LinkedHashMap<>(item);event.put("startAt",item.get("dueAt"));event.put("reminderMinutes",1440);event.put("_kind","applications");events.add(event);
            }
            long now=System.currentTimeMillis(),next=Long.MAX_VALUE;var prefs=context.getSharedPreferences(PREFS,0);
            for(var event:events){
                long at;try{at=OffsetDateTime.parse(Json.str(event,"startAt")).toInstant().toEpochMilli()-((Number)event.getOrDefault("reminderMinutes",0)).longValue()*60000;}catch(Exception ignored){continue;}
                String key=Json.str(event,"id")+"|"+at;
                if(at>now){next=Math.min(next,at);continue;}
                if(at<now-86400000||prefs.getBoolean(key,false)||!permitted(context))continue;
                Intent open=new Intent(context,MainActivity.class);open.putExtra("reminderId",Json.str(event,"id"));open.putExtra("reminderKind",Json.str(event,"_kind"));
                PendingIntent target=PendingIntent.getActivity(context,key.hashCode(),open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
                Notification.Builder builder=Build.VERSION.SDK_INT>=26?new Notification.Builder(context,CHANNEL):new Notification.Builder(context);
                manager.notify(key.hashCode(),builder.setSmallIcon(android.R.drawable.ic_dialog_info).setContentTitle(Json.str(event,"title")).setContentText("日程即将开始或已到期，点击打开拾简查看").setContentIntent(target).setAutoCancel(true).build());
                prefs.edit().putBoolean(key,true).apply();
            }
            PendingIntent alarm=PendingIntent.getBroadcast(context,904,new Intent(context,MobileReminders.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
            AlarmManager scheduler=(AlarmManager)context.getSystemService(Context.ALARM_SERVICE);scheduler.cancel(alarm);
            if(next!=Long.MAX_VALUE)scheduler.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP,next,alarm);
        }catch(Exception ex){android.util.Log.w("ResumeStudio","Schedule notifications",ex);}
    }
    @Override public void onReceive(Context context,Intent intent){PendingResult result=goAsync();new Thread(()->{try{reconcile(context.getApplicationContext());}finally{result.finish();}},"schedule-notifications").start();}
}
