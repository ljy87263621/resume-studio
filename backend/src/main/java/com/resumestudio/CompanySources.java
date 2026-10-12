package com.resumestudio;

import java.net.URI;
import java.util.*;

/** Public employer boards and extensible company websites. Entries are sources, not claims of verified coverage. */
public final class CompanySources {
    private final Store store;
    public CompanySources(Store store) { this.store=store; }
    private static Map<String,Object> source(String id,String name,String adapter,String slug,String url,String region) {
        var s=new LinkedHashMap<String,Object>(); s.put("id",id); s.put("name",name); s.put("adapter",adapter); s.put("slug",slug); s.put("url",url); s.put("region",region); s.put("enabled",true); s.put("builtin",true); return s;
    }
    public List<Map<String,Object>> all() throws Exception {
        var sources=new LinkedHashMap<String,Map<String,Object>>();
        for(var s:List.of(
            source("tencent","腾讯","tencent","","https://careers.tencent.com/","国内"),
            source("netease","网易","jsonld","","https://hr.163.com/","国内"),
            source("huawei","华为","jsonld","","https://career.huawei.com/cn/campus-recruitment-job-list","国内"),
            source("alibaba","阿里巴巴","jsonld","","https://talent.alibaba.com/","国内"),
            source("bytedance","字节跳动","jsonld","","https://jobs.bytedance.com/experienced/position","国内"),
            source("meituan","美团","jsonld","","https://zhaopin.meituan.com/web/position","国内"),
            source("xiaomi","小米","jsonld","","https://hr.xiaomi.com/","国内"),
            source("jd","京东","jsonld","","https://zhaopin.jd.com/","国内"),
            source("byd","比亚迪","jsonld","","https://job.byd.com/","国内"),
            source("midea","美的","jsonld","","https://careers.midea.com/","国内"),
            source("oppo","OPPO","jsonld","","https://careers.oppo.com/","国内"),
            source("vivo","vivo","jsonld","","https://hr.vivo.com/","国内"),
            source("stripe","Stripe","greenhouse","stripe","https://stripe.com/jobs","全球"),
            source("cloudflare","Cloudflare","greenhouse","cloudflare","https://www.cloudflare.com/careers/","全球"),
            source("datadog","Datadog","greenhouse","datadog","https://careers.datadoghq.com/","全球"),
            source("palantir","Palantir","lever","palantir","https://jobs.lever.co/palantir","全球"),
            source("spotify","Spotify","lever","spotify","https://jobs.lever.co/spotify","全球"),
            source("notion","Notion","ashby","notion","https://jobs.ashbyhq.com/notion","全球"),
            source("bosch","博世","smartrecruiters","BoschGroup","https://jobs.smartrecruiters.com/BoschGroup","全球"),
            source("nvidia","NVIDIA","workday","","https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite","全球"),
            source("arbeitnow","Arbeitnow 聚合源","arbeitnow","","https://www.arbeitnow.com/","全球"))) sources.put(Json.str(s,"id"),s);
        for(var custom:store.list("sources")) sources.put(Json.str(custom,"id"),custom);
        return new ArrayList<>(sources.values());
    }
    public static void validate(Map<String,Object> source) {
        String type=Json.str(source,"adapter");
        if(!Set.of("tencent","arbeitnow","greenhouse","lever","ashby","smartrecruiters","workday","jsonld").contains(type)) throw new IllegalArgumentException("不支持的官网接入类型");
        if(Json.str(source,"name").isBlank()) throw new IllegalArgumentException("公司名称不能为空");
        URI url=URI.create(Json.str(source,"url"));
        if(!"https".equals(url.getScheme())||url.getHost()==null||url.getUserInfo()!=null||(url.getPort()!=-1&&url.getPort()!=443)||url.getHost().matches("[0-9.:]+")||!url.getHost().contains(".")||url.getHost().equals("localhost")||url.getHost().endsWith(".local")) throw new IllegalArgumentException("请输入公司的公共 HTTPS 招聘官网");
        if(Set.of("greenhouse","lever","ashby","smartrecruiters").contains(type)&&!Json.str(source,"slug").matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("招聘系统的公司 / Board 标识只允许字母、数字、短横线和下划线");
        if(type.equals("workday")&&(!url.getHost().endsWith(".myworkdayjobs.com")||!url.getPath().replaceAll("^/|/$","").matches("[a-zA-Z0-9_-]{1,100}"))) throw new IllegalArgumentException("Workday 请填写 https://企业.wd5.myworkdayjobs.com/职位板名称，去掉语言和职位详情路径");
        if(!(source.get("enabled") instanceof Boolean)) throw new IllegalArgumentException("缺少启用状态");
    }
}
