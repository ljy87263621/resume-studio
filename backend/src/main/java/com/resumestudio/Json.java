package com.resumestudio;

import java.util.*;

/** Small dependency-free JSON codec; preserves large recruitment identifiers as strings. */
public final class Json {
    private final String text; private int pos;
    private Json(String text) { this.text = text; }
    public static Object parse(String text) {
        Json p = new Json(text); Object result = p.value(0); p.ws();
        if (p.pos != text.length()) throw new IllegalArgumentException("JSON has trailing data");
        return result;
    }
    private void ws() { while (pos < text.length() && Character.isWhitespace(text.charAt(pos))) pos++; }
    private char take() { if (pos >= text.length()) throw new IllegalArgumentException("Incomplete JSON"); return text.charAt(pos++); }
    private Object value(int depth) {
        if (depth > 40) throw new IllegalArgumentException("JSON nesting too deep");
        ws(); char c = take();
        if (c == '"') return string();
        if (c == '{') {
            Map<String,Object> map = new LinkedHashMap<>(); ws();
            if (pos < text.length() && text.charAt(pos) == '}') { pos++; return map; }
            while (true) {
                ws(); if (take() != '"') throw new IllegalArgumentException("Expected object key");
                String key = string(); ws(); if (take() != ':') throw new IllegalArgumentException("Expected colon");
                map.put(key, value(depth+1)); ws(); char end = take();
                if (end == '}') return map; if (end != ',') throw new IllegalArgumentException("Expected comma");
            }
        }
        if (c == '[') {
            List<Object> list = new ArrayList<>(); ws();
            if (pos < text.length() && text.charAt(pos) == ']') { pos++; return list; }
            while (true) { list.add(value(depth+1)); ws(); char end = take(); if (end == ']') return list; if (end != ',') throw new IllegalArgumentException("Expected comma"); }
        }
        int start = pos-1;
        while (pos < text.length() && ",]} \r\n\t".indexOf(text.charAt(pos)) < 0) pos++;
        String token = text.substring(start,pos);
        return switch(token) { case "true" -> true; case "false" -> false; case "null" -> null; default -> {
            if (!token.matches("-?(0|[1-9][0-9]*)(\\.[0-9]+)?([eE][+-]?[0-9]+)?")) throw new IllegalArgumentException("Invalid JSON token");
            yield new java.math.BigDecimal(token);
        }};
    }
    private String string() {
        StringBuilder b = new StringBuilder();
        while (true) {
            char c = take(); if (c == '"') return b.toString();
            if (c < 32) throw new IllegalArgumentException("Invalid string");
            if (c != '\\') { b.append(c); continue; }
            char e = take();
            switch(e) { case '"', '\\', '/' -> b.append(e); case 'n' -> b.append('\n'); case 'r' -> b.append('\r'); case 't' -> b.append('\t'); case 'b' -> b.append('\b'); case 'f' -> b.append('\f'); case 'u' -> {
                if (pos+4 > text.length()) throw new IllegalArgumentException("Invalid unicode");
                b.append((char)Integer.parseInt(text.substring(pos,pos+4),16)); pos+=4;
            } default -> throw new IllegalArgumentException("Invalid escape"); }
        }
    }
    public static String write(Object obj) {
        if (obj == null) return "null";
        if (obj instanceof Boolean || obj instanceof Number) return obj.toString();
        if (obj instanceof Map<?,?> map) {
            StringJoiner j = new StringJoiner(",","{","}"); map.forEach((k,v)->j.add(write(k.toString())+":"+write(v))); return j.toString();
        }
        if (obj instanceof Iterable<?> list) { StringJoiner j = new StringJoiner(",","[","]"); list.forEach(v->j.add(write(v))); return j.toString(); }
        StringBuilder b = new StringBuilder("\"");
        for (char c : obj.toString().toCharArray()) {
            switch(c) { case '"' -> b.append("\\\""); case '\\' -> b.append("\\\\"); case '\n' -> b.append("\\n"); case '\r' -> b.append("\\r"); case '\t' -> b.append("\\t"); default -> { if (c<32) b.append(String.format("\\u%04x",(int)c)); else b.append(c); } }
        } return b.append('"').toString();
    }
    @SuppressWarnings("unchecked") public static Map<String,Object> object(Object value) {
        if (!(value instanceof Map)) throw new IllegalArgumentException("Expected JSON object"); return (Map<String,Object>)value;
    }
    @SuppressWarnings("unchecked") public static List<Object> array(Object value) {
        if (!(value instanceof List)) throw new IllegalArgumentException("Expected JSON array"); return (List<Object>)value;
    }
    public static String str(Map<String,Object> map, String key) { Object v=map.get(key); return v == null ? "" : v.toString(); }
}
