package com.resumestudio;

import android.content.*;
import android.content.pm.PackageInstaller;
import java.util.Objects;

/** Records installer results without retaining an Activity. */
public final class MobileInstallReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent){
        int status=intent.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
        if(status==PackageInstaller.STATUS_PENDING_USER_ACTION){Intent confirmation=intent.getParcelableExtra(Intent.EXTRA_INTENT);if(confirmation!=null){confirmation.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);context.startActivity(confirmation);}return;}
        String message=status==PackageInstaller.STATUS_SUCCESS?"安装完成，请重新打开拾简":Objects.toString(intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE),"安装未完成");
        context.getSharedPreferences("resume-studio",0).edit().putString("installStatus",message).apply();
    }
}
