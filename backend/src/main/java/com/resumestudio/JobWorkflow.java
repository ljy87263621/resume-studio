package com.resumestudio;

import java.util.*;

/** Match the same position across discovery and pasted links without changing its browser URL. */
final class JobWorkflow {
    private JobWorkflow() { }
    static boolean sameJob(Map<String,Object> job,Map<String,Object> saved,boolean application) {
        String id=Json.str(job,"id"), savedId=Json.str(saved,application?"jobId":"id");
        if(!id.isBlank()&&id.equals(savedId)) return true;
        try { return JobCapture.canonical(Json.str(job,"url")).equals(JobCapture.canonical(Json.str(saved,"url"))); }
        catch(IllegalArgumentException ex) { return false; }
    }
}
