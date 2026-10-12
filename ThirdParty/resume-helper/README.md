# 简历助手

一个本地运行的简历制作辅助工具，参考在线简历编辑器的工作台体验，提供结构化填写、A4 实时预览、模块状态和规则化内容建议。

## 功能

- 三栏工作台：左侧模块状态，中间 A4 预览，右侧编辑与辅助建议。
- 模块状态：`已填` 为绿色，`待优化` 为黄色，`未填` 为灰色。
- 支持基本信息、教育背景、专业技能、工作经历、项目经历、证书荣誉、自我评价和自定义模块。
- 基本信息支持自定义信息项，可配置 Emoji、标签和内容；默认更适合应届生，包含电话、邮箱、城市、政治面貌、学历。
- 基本信息支持可选头像；未上传头像时会保留默认居中排版。
- 左侧模块栏支持拖拽排序，预览和导出数据会保留模块顺序。
- 自定义模块从左侧模块栏添加，可独立排序、编辑和删除。
- 自定义模块支持多种模板：自由文本、条目列表、经历列表、目录板块。
- 目录板块支持一层子板块；每个子板块可选择自由文本、条目列表或经历列表。
- 自定义模块和目录子板块支持图片模板，可填写图片、替代说明和图片标题。
- 自定义模块和目录子板块支持表格模板，可新增或删除行列并直接编辑单元格。
- 支持新增和删除重复条目、经历条目和自定义子板块。
- 支持两套模板：经典简洁、蓝色分割线。
- 预览栏实时检测单页 A4 范围：内容接近底部时提示余量，超出时显示超出毫米数。
- 自动保存到浏览器本地。
- 支持 JSON 导入和导出，并提供“AI 写简历”入口，可复制 JSON 书写规则和生成提示词。
- 支持浏览器打印或保存为 PDF；超出 A4 时打印前会二次确认。

## 使用方式

直接用浏览器打开 `index.html` 即可。

上传的图片会以 data URL 形式保存在浏览器本地数据和导出的 JSON 中。建议单张图片不超过 1 MB，支持 PNG、JPEG 和 WebP 格式。

点击顶部的“AI 写简历”可以复制 Resume Helper 的 JSON 书写规则。把规则和原始简历资料交给 AI，让它只输出纯 JSON，然后通过“导入”载入即可。

## AI 生成 JSON 规则

导入文件必须是一个 JSON 对象，不要包含 Markdown 代码块、注释或解释。常用字段如下：

- `template`：只能是 `blue` 或 `classic`。
- `basics`：基础字段对象，至少建议包含 `name`；可补充 `phone`、`email`、`location`、`politics`、`educationLevel`。
- `basicItems`：基础信息展示项数组，每项为 `{ "emoji": "", "label": "", "value": "" }`。
- `education`：教育经历数组，每项包含 `school`、`major`、`degree`、`start`、`end`。
- `skills`：技能字符串数组。
- `work`：工作经历数组，每项包含 `company`、`role`、`start`、`end`、`description`。
- `projects`：项目经历数组，每项包含 `name`、`role`、`tech`、`start`、`end`、`description`。
- `certificates`：证书或荣誉字符串数组。
- `selfEvaluation`：自我评价字符串，建议 2-3 句。
- `customSections`：自定义模块数组；`template` 支持 `text`、`list`、`experience`、`directory`、`image`、`table`。
- `moduleOrder`：模块顺序数组；标准模块使用 `basics`、`education`、`skills`、`work`、`projects`、`certificates`、`selfEvaluation`，自定义模块使用 `custom:<id>`。

自定义模块字段规则：

- `id` 只使用英文、数字、短横线或下划线，不要写 `custom:` 前缀。
- `text` 使用 `content`。
- `list` 使用 `items` 字符串数组。
- `experience` 使用 `entries` 数组，每项包含 `name`、`role`、`start`、`end`、`description`。
- `directory` 使用一层 `blocks` 子板块；子板块模板可为 `text`、`list`、`experience`、`image`、`table`。
- `image` 使用 `{ "dataUrl": "", "name": "", "alt": "", "caption": "" }`，没有图片时 `dataUrl` 留空。
- `table` 使用二维字符串数组，例如 `[[ "类别", "内容", "说明" ], [ "项目", "负责前端开发", "React" ]]`。

也可以用任意静态服务器预览，例如：

```powershell
npx http-server . -p 5173
```

启动后访问：

```text
http://127.0.0.1:5173/
```

如需用 SakuraFRP 映射到外网，映射类型选择 HTTP，本地地址为 `127.0.0.1`，本地端口为 `5173`。如果使用随机可用端口启动静态服务器，请把 SakuraFRP 的本地端口同步改为实际端口。

## 测试

项目使用 Node.js 内置测试框架验证核心规则和预览渲染逻辑：

```powershell
npm test
```

测试覆盖：

- 简历模块状态和规则化建议。
- JSON 导入校验和旧数据兼容。
- 基本信息自定义项、应届生默认信息项和基础信息建议规则。
- 自定义模块排序、自定义模块模板、目录子板块归一化，以及头像、图片和表格媒体数据处理。
- `tests/resume-preview.test.js` 覆盖 A4 范围检测、AI JSON 提示词复制、打印确认、头像控件、自定义图片/表格编辑器和空媒体模块预览过滤。
- 打印样式是否只输出 A4 简历预览。

## 目录

```text
.
├── index.html
├── src/
│   ├── app.js
│   ├── resume-core.js
│   ├── resume-core.mjs
│   └── styles.css
├── tests/
│   ├── print-css.test.js
│   ├── resume-core.test.js
│   └── resume-preview.test.js
├── docs/
│   └── superpowers/
│       ├── plans/
│       └── specs/
├── package.json
└── README.md
```

## 主要文件

- `index.html`：静态页面入口、三栏工作台骨架和 AI JSON 规则弹窗。
- `src/app.js`：浏览器状态、模块拖拽、基础信息自定义项、编辑器、预览、A4 范围检测、AI JSON 提示词复制、导入导出和打印行为。
- `src/resume-core.js`：简历数据模型、基础信息自定义项、模块顺序、自定义模块模板、状态和建议规则。
- `src/resume-core.mjs`：Node 测试使用的 ESM 包装入口。
- `src/styles.css`：工作台布局、模块拖拽、自定义模块编辑器、简历预览和打印样式。
- `tests/resume-core.test.js`：核心数据、媒体归一化和规则测试。
- `tests/resume-preview.test.js`：预览渲染、媒体编辑器和上传流程测试。
- `tests/print-css.test.js`：打印 CSS 回归测试。

## GitHub Pages

这是一个纯静态项目，可以直接发布到 GitHub Pages。发布后将 Pages source 指向仓库根目录即可。
