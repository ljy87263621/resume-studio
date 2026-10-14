package com.resumestudio;

import android.content.Context;
import android.security.keystore.*;
import android.util.AtomicFile;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;
import java.util.*;

/** Credentials remain device-local, AES-GCM encrypted with an Android Keystore key. */
final class MobileSecrets {
    private final Context context;
    MobileSecrets(Context context){this.context=context;}
    private SecretKey key()throws Exception {
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(!store.containsAlias("resume-studio-secrets")){
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder("resume-studio-secrets",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());generator.generateKey();
        }
        return (SecretKey)store.getKey("resume-studio-secrets",null);
    }
    void write(String name,String plain)throws Exception {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());
        AtomicFile file=new AtomicFile(new File(context.getFilesDir(),name+".protected"));FileOutputStream out=file.startWrite();
        try{out.write(cipher.getIV().length);out.write(cipher.getIV());out.write(cipher.doFinal(plain.getBytes(StandardCharsets.UTF_8)));file.finishWrite(out);}catch(Exception ex){file.failWrite(out);throw ex;}
    }
    String read(String name)throws Exception {
        AtomicFile file=new AtomicFile(new File(context.getFilesDir(),name+".protected"));if(!file.getBaseFile().exists())return "";
        byte[] bytes=file.readFully();int iv=bytes[0]&255;if(iv!=12||bytes.length<29)throw new IOException("本机密钥无效，请重新保存");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Arrays.copyOfRange(bytes,1,iv+1)));return new String(cipher.doFinal(Arrays.copyOfRange(bytes,iv+1,bytes.length)),StandardCharsets.UTF_8);
    }
}
