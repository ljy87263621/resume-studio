package com.resumestudio;

import android.content.*;
import android.content.pm.PackageInstaller;
import android.app.PendingIntent;
import java.util.Objects;

/** Records installer results without retaining an Activity. */
public final class MobileInstallReceiver extends BroadcastReceiver {
    /** PackageInstaller fills the result status into this explicit callback when the session completes. */
    public static PendingIntent statusPendingIntent(Context context,int sessionId){
        Intent callback=new Intent(context,MobileInstallReceiver.class);
        return PendingIntent.getBroadcast(context,sessionId,callback,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_MUTABLE);
    }

    @Override public void onReceive(Context context,Intent intent){
        int status=intent.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
        if(status==PackageInstaller.STATUS_PENDING_USER_ACTION){Intent confirmation=intent.getParcelableExtra(Intent.EXTRA_INTENT);if(confirmation!=null){confirmation.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);context.startActivity(confirmation);}return;}
        String message=status==PackageInstaller.STATUS_SUCCESS?"安装完成，请重新打开拾简":Objects.toString(intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE),"安装未完成");
        context.getSharedPreferences("resume-studio",0).edit().putString("installStatus",message).apply();
    }
}
