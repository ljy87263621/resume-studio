package com.resumestudio;
import java.io.*;
import java.util.*;
/** Public editable Word export entry point, independent of HTML/PDF templates. */
public final class ResumeDocx {
    private ResumeDocx(){}
    public static byte[] create(Map<String,Object> resume)throws IOException{
        return new ResumeWord(resume).build();
    }
}
