const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../../client-winui/MainWindow.PlatformCapture.cs'), 'utf8');
const script = source.match(/PlatformJobsScript="""\r?\n([\s\S]*?)\r?\n\s*""";/)[1];
function read(body) {
  const location = new URL('https://www.nowcoder.com/jobs/intern/center');
  const document = {title:'牛客职位',querySelectorAll(selector) {
    return selector.includes('script') ? [{textContent:body}] : [];
  }};
  return vm.runInNewContext(script, {location, document, window:{}, URL});
}
const jobs = read(JSON.stringify({data:{jobs:[{id:12,jobName:'芯片设计',companyId:1,companyName:'芯片公司'}]}})).jobs;
assert.equal(jobs.length, 1, 'inert application/json initial state supplies jobs without a global object');
assert.equal(jobs[0].title, '芯片设计');
assert.equal(read('{broken').jobs.length, 0, 'malformed initial state does not interrupt browsing');
assert.equal(read(' '.repeat(4000001)).jobs.length, 0, 'oversized initial state is skipped');
console.log('PASS: 4 browser extraction script checks');
