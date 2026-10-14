package com.resumestudio;

import java.nio.file.*;
import java.util.*;

public final class InterviewStudyTest {
    private InterviewStudyTest() {}
    public static void main(String[] args) throws Exception {
        var question=new LinkedHashMap<String,Object>(Map.of("id","q1","title","1. 什么是指针？","answer","保存地址","status","unseen"));
        var deck=new LinkedHashMap<String,Object>(Map.of("id","deck1","title","嵌入式题库","questions",List.of(question)));
        Server.validate("interviews",deck);
        var store=new Store(Files.createTempDirectory("InterviewStudyTest-"));store.save("interviews",deck);
        question.put("status","remembered");store.save("interviews",deck);
        if(!Json.str(Json.object(Json.array(store.list("interviews").getFirst().get("questions")).getFirst()),"status").equals("remembered"))throw new AssertionError("Review state did not persist");
        var snapshot=store.syncSnapshot();store.syncImport(Json.str(snapshot,"hash"),Json.object(snapshot.get("snapshot")));
        if(store.list("interviews").size()!=1)throw new AssertionError("Existing local study data was lost during sync");
        question.put("status","bad");
        boolean rejected=false;try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Invalid progress was accepted");
        question.put("status","learning");deck.put("questions",List.of(question,question));rejected=false;
        try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Duplicate question IDs accepted");
        System.out.println("PASS InterviewStudy: atomic persistence, state changes, sync preservation and validation");
    }
}
