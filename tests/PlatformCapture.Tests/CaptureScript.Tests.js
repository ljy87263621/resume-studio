const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../../client-winui/MainWindow.PlatformCapture.cs'), 'utf8');
const script = source.match(/PlatformJobsScript="""\r?\n([\s\S]*?)\r?\n\s*""";/)[1];
function read(body, url='https://www.nowcoder.com/jobs/intern/center') {
  const location=new URL(url);
  const document={title:'牛客职位',querySelectorAll(selector){return selector.startsWith('script[')?[{textContent:body}]:[];}};
  return vm.runInNewContext(script,{location,document,window:{},URL,getComputedStyle:()=>({visibility:'visible'})});
}
const state=JSON.stringify({data:{jobs:[{id:12,jobName:'芯片设计',companyId:1,companyName:'芯片公司',description:'负责芯片开发、驱动设计和硬件调试，确保软硬件产品可靠运行。',requirements:'熟悉 C 语言和嵌入式 Linux。'},{id:13,jobName:'无关推荐职位',companyId:1,description:'不能收藏'}]}});
assert.equal(read(state).jobs.length,0,'list JSON never becomes a collection target');
const jobs=read(state,'https://www.nowcoder.com/jobs/detail/12').jobs;
assert.equal(jobs.length,1,'only current URL is eligible');
assert.equal(jobs[0].title,'芯片设计');
assert.ok(jobs[0].description.includes('熟悉 C'),'separate requirements are retained');
assert.ok(!jobs[0].description.includes('不能收藏'),'recommendation state is ignored');
assert.equal(read('{broken','https://www.nowcoder.com/jobs/detail/12').jobs.length,0,'malformed state does not interrupt browsing');
assert.equal(read(' '.repeat(4000001),'https://www.nowcoder.com/jobs/detail/12').jobs.length,0,'oversized state is skipped');
console.log('PASS: 7 single-current-detail browser extraction script checks');
