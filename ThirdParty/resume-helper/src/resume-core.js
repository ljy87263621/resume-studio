(function attachResumeCore(global) {
  const STATUS = {
    filled: { label: '已填', tone: 'success' },
    warning: { label: '待优化', tone: 'warning' },
    empty: { label: '未填', tone: 'muted' },
  };

  const MODULES = [
    { key: 'basics', label: '基本信息' },
    { key: 'education', label: '教育背景' },
    { key: 'skills', label: '专业技能' },
    { key: 'work', label: '工作经历' },
    { key: 'projects', label: '项目经历' },
    { key: 'certificates', label: '证书荣誉' },
    { key: 'selfEvaluation', label: '自我评价' },
  ];

  const STANDARD_MODULE_KEYS = MODULES.map((module) => module.key);
  const CUSTOM_MODULE_PREFIX = 'custom:';
  const CUSTOM_TEMPLATES = ['text', 'list', 'experience', 'directory', 'image', 'table'];
  const EMPTY_MEDIA_IMAGE = { dataUrl: '', name: '', alt: '', caption: '' };
  const MAX_MEDIA_IMAGE_BYTES = 1024 * 1024;
  const SUPPORTED_MEDIA_IMAGE_DATA_URL_PATTERN = /^data:image\/(?:png|jpe?g|webp);base64,/;
  const STARTER_MEDIA_TABLE = [
    ['Category', 'Content', 'Notes'],
    ['', '', ''],
  ];
  const STARTER_MEDIA_TABLE_HEADER = STARTER_MEDIA_TABLE[0];

  function createSampleResume() {
  return {
    template: 'blue',
    basics: {
      name: '张三',
      phone: '138****8888',
      email: 'zhangsan@example.com',
      gender: '男',
      years: '',
      targetRole: '',
      location: '上海',
      politics: '共青团员',
      educationLevel: '应届本科',
    },
    basicItems: [
      { emoji: '📞', label: '电话', value: '138****8888' },
      { emoji: '✉️', label: '邮箱', value: 'zhangsan@example.com' },
      { emoji: '📍', label: '城市', value: '上海' },
      { emoji: '🏛️', label: '政治面貌', value: '共青团员' },
      { emoji: '🎓', label: '学历', value: '应届本科' },
    ],
    education: [
      {
        school: '某某大学',
        major: '计算机科学与技术',
        degree: '本科',
        start: '2016.09',
        end: '2020.06',
      },
    ],
    skills: [
      '熟练掌握 React、Vue、TypeScript 等前端技术栈',
      '熟悉 Node.js 服务端开发',
      '具备良好的代码规范和团队协作能力',
    ],
    work: [
      {
        company: '某某科技有限公司',
        role: '高级前端工程师',
        start: '2022.01',
        end: '至今',
        description: '负责公司核心产品的前端架构设计与开发，主导多个重要项目的技术选型和实施。',
      },
    ],
    projects: [
      {
        name: '企业管理系统',
        role: '前端负责人',
        tech: 'React, TypeScript, Vite',
        start: '2023.01',
        end: '2023.12',
        description: '负责企业级管理系统的前端开发，沉淀通用组件并提升交付效率。',
      },
    ],
    certificates: ['2023年度优秀员工', '2022年最佳新人奖'],
    selfEvaluation: '对待工作认真负责，善于沟通协调，有较强的团队合作精神。',
    customSections: [],
    moduleOrder: [...STANDARD_MODULE_KEYS],
    avatar: { ...EMPTY_MEDIA_IMAGE },
  };
  }

  function normalizeResumeData(input = {}) {
  const sample = createSampleResume();
  const customSections = normalizeCustomSections(input.customSections, sample.customSections);

  const resume = {
    ...sample,
    ...input,
    basics: { ...sample.basics, ...(isObject(input.basics) ? input.basics : {}) },
    basicItems: normalizeBasicItems(input.basicItems, input.basics || sample.basics),
    education: normalizeArray(input.education, sample.education),
    skills: normalizeArray(input.skills, sample.skills),
    work: normalizeArray(input.work, sample.work),
    projects: normalizeArray(input.projects, sample.projects),
    certificates: normalizeArray(input.certificates, sample.certificates),
    customSections,
    selfEvaluation: typeof input.selfEvaluation === 'string' ? input.selfEvaluation : sample.selfEvaluation,
    template: input.template === 'classic' || input.template === 'blue' ? input.template : sample.template,
    avatar: normalizeMediaImage(input.avatar),
  };

  resume.moduleOrder = normalizeModuleOrder(input.moduleOrder, customSections);

  return resume;
  }

  function validateImportedResume(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) {
    return { valid: false, reason: '导入文件不是有效的对象。' };
  }

  const hasResumeShape = [
    'basics',
    'education',
    'skills',
    'work',
    'projects',
    'selfEvaluation',
    'certificates',
    'customSections',
  ].some((key) => key in input);

  if (!hasResumeShape) {
    return { valid: false, reason: '导入文件不包含简历字段。' };
  }

  return { valid: true, data: normalizeResumeData(input) };
  }

  function getModuleStatuses(resume) {
  const data = normalizeResumeData(resume);
  const statuses = Object.fromEntries(MODULES.map((module) => [module.key, getModuleStatus(data, module.key)]));

  data.customSections.forEach((section, index) => {
    const key = customModuleId(section.id);
    statuses[key] = getCustomModuleStatus(section, index);
  });

  statuses.customSections = getModuleStatus(data, 'customSections');

  return statuses;
  }

  function getOrderedModuleItems(resume) {
  const data = normalizeResumeData(resume);
  const standardModules = Object.fromEntries(MODULES.map((module) => [module.key, module]));
  const customModules = Object.fromEntries(data.customSections.map((section, index) => {
    const id = customModuleId(section.id);
    return [id, {
      id,
      key: 'customSections',
      type: 'custom',
      label: section.title?.trim() || `自定义模块 ${index + 1}`,
      customIndex: index,
    }];
  }));

  return data.moduleOrder.map((id) => {
    if (standardModules[id]) return { ...standardModules[id], id, type: 'standard' };
    return customModules[id] || null;
  }).filter(Boolean);
  }

  function moveModuleOrderItem(moduleOrder, sourceId, targetId, placeAfter = false) {
  const order = [];

  if (Array.isArray(moduleOrder)) {
    moduleOrder.forEach((id) => {
      if (typeof id === 'string' && !order.includes(id)) order.push(id);
    });
  }

  if (!order.includes(sourceId) || !order.includes(targetId) || sourceId === targetId) return order;

  const movedOrder = order.filter((id) => id !== sourceId);
  const targetIndex = movedOrder.indexOf(targetId);
  movedOrder.splice(targetIndex + (placeAfter ? 1 : 0), 0, sourceId);
  return movedOrder;
  }

  function getSuggestions(resume, moduleKey) {
  const data = normalizeResumeData(resume);
  const suggestions = [];

  if (moduleKey === 'basics') {
    const phone = findBasicItemValue(data, '电话') ?? data.basics.phone;
    const email = findBasicItemValue(data, '邮箱') ?? data.basics.email;

    if (!data.basics.name?.trim()) suggestions.push('姓名为空，建议填写真实姓名或常用中文名。');
    if (!phone?.trim()) suggestions.push('电话为空，招聘方可能无法快速联系你。');
    if (!isLikelyEmail(email)) suggestions.push('邮箱格式可能不正确，请检查是否包含 @ 和域名。');
  }

  if (moduleKey === 'education') {
    if (!data.education.length) suggestions.push('教育背景为空，建议至少填写最高学历。');
    data.education.forEach((item, index) => {
      if (!item.school || !item.major || !item.degree) {
        suggestions.push(`第 ${index + 1} 段教育经历缺少学校、专业或学历。`);
      }
      if (!item.start || !item.end) suggestions.push(`第 ${index + 1} 段教育经历缺少时间范围。`);
    });
  }

  if (moduleKey === 'skills') {
    const filledSkills = data.skills.filter((skill) => String(skill || '').trim());
    if (!filledSkills.length) suggestions.push('专业技能为空，建议填写与目标岗位最相关的 3-6 项技能。');
    if (filledSkills.some((skill) => String(skill).length > 38)) {
      suggestions.push('部分技能描述较长，建议拆成更短的关键词或能力短句。');
    }
    if (filledSkills.length > 8) suggestions.push('技能数量偏多，建议按岗位相关度保留最关键内容。');
  }

  if (moduleKey === 'work') {
    if (!data.work.length) suggestions.push('工作经历为空，建议填写最近或最相关的一段经历。');
    data.work.forEach((item, index) => {
      const text = item.description || '';
      if (!item.company || !item.role) suggestions.push(`第 ${index + 1} 段工作经历缺少公司或职位。`);
      if (!hasActionWord(text)) suggestions.push(`第 ${index + 1} 段工作经历可以加入“负责、主导、搭建、优化”等动作。`);
      if (!hasMetric(text)) {
        suggestions.push(`第 ${index + 1} 段工作经历缺少量化结果，例如性能提升、效率提升、转化率或用户规模。`);
      }
      if (!hasOutcome(text)) suggestions.push(`第 ${index + 1} 段工作经历建议补充结果，让贡献更具体。`);
    });
  }

  if (moduleKey === 'projects') {
    if (!data.projects.length) suggestions.push('项目经历为空，建议补充最能体现岗位能力的项目。');
    data.projects.forEach((item, index) => {
      if (!item.name || !item.role) suggestions.push(`第 ${index + 1} 个项目缺少项目名称或个人角色。`);
      if (!item.tech?.trim()) suggestions.push(`第 ${index + 1} 个项目缺少技术栈。`);
      if (!hasOutcome(item.description || '')) suggestions.push(`第 ${index + 1} 个项目建议补充成果或业务价值。`);
    });
  }

  if (moduleKey === 'certificates') {
    if (!data.certificates.filter((item) => String(item || '').trim()).length) {
      suggestions.push('证书荣誉为空；如果没有强相关内容，可以保持为空。');
    }
  }

  if (moduleKey === 'selfEvaluation') {
    const text = data.selfEvaluation || '';
    if (!text.trim()) suggestions.push('自我评价为空，可用 2-3 句话概括优势、经验和求职方向。');
    if (text.length > 120) suggestions.push('自我评价偏长，建议压缩到 120 字以内。');
    if (text.length > 0 && text.length < 20) suggestions.push('自我评价偏短，建议补充关键优势或岗位匹配点。');
    if (/认真负责|团队合作精神/.test(text)) suggestions.push('自我评价略通用，建议加入具体技术方向或业务经验。');
  }

  if (moduleKey === 'customSections') {
    data.customSections.forEach((item, index) => {
      if (!item.title?.trim()) suggestions.push(`第 ${index + 1} 个自定义模块缺少标题。`);
    });
  }

  if (isCustomModuleId(moduleKey)) {
    const index = data.customSections.findIndex((item) => customModuleId(item.id) === moduleKey);
    if (index >= 0 && !data.customSections[index].title?.trim()) {
      suggestions.push(`第 ${index + 1} 个自定义模块缺少标题。`);
    }
  }

  return suggestions;
  }

function getModuleStatus(data, key) {
  if (isCustomModuleId(key)) {
    const index = data.customSections.findIndex((item) => customModuleId(item.id) === key);
    return index >= 0 ? getCustomModuleStatus(data.customSections[index], index) : STATUS.empty;
  }

  if (!hasModuleContent(data, key)) return STATUS.empty;
  return getSuggestions(data, key).length ? STATUS.warning : STATUS.filled;
}

function getCustomModuleStatus(section, index) {
  if (!hasText(section.title) && !hasCustomTemplateContent(section)) return STATUS.empty;
  return section.title?.trim() ? STATUS.filled : STATUS.warning;
}

function hasModuleContent(data, key) {
  if (key === 'basics') return Object.values(data.basics).some(hasText);
  if (key === 'education') return data.education.some((item) => Object.values(item).some(hasText));
  if (key === 'skills') return data.skills.some(hasText);
  if (key === 'work') return data.work.some((item) => Object.values(item).some(hasText));
  if (key === 'projects') return data.projects.some((item) => Object.values(item).some(hasText));
  if (key === 'certificates') return data.certificates.some(hasText);
  if (key === 'selfEvaluation') return hasText(data.selfEvaluation);
  if (key === 'customSections') return data.customSections.some((item) => hasText(item.title) || hasCustomTemplateContent(item));
  return false;
}

function normalizeArray(value, fallback) {
  return Array.isArray(value) ? value : fallback;
}

function normalizeBasicItems(value, basics = {}) {
  if (Array.isArray(value)) {
    return value.map((item) => {
      const source = isObject(item) ? item : {};

      return {
        emoji: typeof source.emoji === 'string' ? source.emoji : '',
        label: typeof source.label === 'string' ? source.label : '',
        value: typeof source.value === 'string' ? source.value : '',
      };
    });
  }

  return [
    { emoji: '📞', label: '电话', value: basics.phone || '' },
    { emoji: '✉️', label: '邮箱', value: basics.email || '' },
    { emoji: '📍', label: '城市', value: basics.location || '' },
    { emoji: '🏛️', label: '政治面貌', value: basics.politics || '' },
    { emoji: '🎓', label: '学历', value: basics.educationLevel || '' },
  ];
}

function normalizeCustomSections(value, fallback) {
  const sections = normalizeArray(value, fallback);
  const usedIds = new Set();

  return sections.map((section, index) => {
    const source = isObject(section) ? section : {};
    const id = normalizeCustomId(source.id, index, usedIds);
    usedIds.add(id);

    return {
      id,
      title: typeof source.title === 'string' ? source.title : '',
      template: normalizeTemplate(source.template),
      content: typeof source.content === 'string' ? source.content : '',
      items: normalizeStringItems(source.items),
      entries: normalizeExperienceEntries(source.entries),
      blocks: normalizeCustomBlocks(source.blocks),
      image: normalizeMediaImage(source.image),
      table: normalizeMediaTable(source.table),
    };
  });
}

function normalizeCustomBlocks(value) {
  const blocks = normalizeArray(value, []);
  const usedIds = new Set();

  return blocks.map((block, index) => {
    const source = isObject(block) ? block : {};
    const id = normalizeCustomId(source.id, index, usedIds);
    usedIds.add(id);

    return {
      id,
      title: typeof source.title === 'string' ? source.title : '',
      template: normalizeTemplate(source.template === 'directory' ? 'text' : source.template),
      content: typeof source.content === 'string' ? source.content : '',
      items: normalizeStringItems(source.items),
      entries: normalizeExperienceEntries(source.entries),
      image: normalizeMediaImage(source.image),
      table: normalizeMediaTable(source.table),
    };
  });
}

function normalizeTemplate(value) {
  return CUSTOM_TEMPLATES.includes(value) ? value : 'text';
}

function normalizeStringItems(value) {
  return normalizeArray(value, []).map((item) => String(item || ''));
}

function normalizeExperienceEntries(value) {
  return normalizeArray(value, []).map((entry) => {
    const source = isObject(entry) ? entry : {};

    return {
      name: typeof source.name === 'string' ? source.name : '',
      role: typeof source.role === 'string' ? source.role : '',
      start: typeof source.start === 'string' ? source.start : '',
      end: typeof source.end === 'string' ? source.end : '',
      description: typeof source.description === 'string' ? source.description : '',
    };
  });
}

function normalizeMediaImage(value) {
  const source = isObject(value) ? value : {};
  const dataUrl = typeof source.dataUrl === 'string' && isSupportedMediaImageDataUrl(source.dataUrl)
    ? source.dataUrl
    : '';

  return createMediaObject({
    dataUrl,
    name: typeof source.name === 'string' ? source.name : '',
    alt: typeof source.alt === 'string' ? source.alt : '',
    caption: typeof source.caption === 'string' ? source.caption : '',
  });
}

function normalizeMediaTable(value) {
  if (!Array.isArray(value) || !value.length) return cloneMediaTable(STARTER_MEDIA_TABLE);

  const rows = value
    .filter(Array.isArray)
    .map((row) => row.map((cell) => String(cell ?? '')));
  const columnCount = rows.reduce((max, row) => Math.max(max, row.length), 0);

  return rows.length && columnCount
    ? cloneMediaTable(rows.map((row) => [...row, ...Array(columnCount - row.length).fill('')]))
    : cloneMediaTable(STARTER_MEDIA_TABLE);
}

function isSupportedMediaImageDataUrl(value) {
  if (!SUPPORTED_MEDIA_IMAGE_DATA_URL_PATTERN.test(value)) return false;
  return estimateBase64PayloadBytes(value.slice(value.indexOf(',') + 1)) <= MAX_MEDIA_IMAGE_BYTES;
}

function estimateBase64PayloadBytes(value) {
  const compact = value.replace(/\s/g, '');
  const padding = compact.endsWith('==') ? 2 : compact.endsWith('=') ? 1 : 0;
  return Math.floor((compact.length * 3) / 4) - padding;
}

function createMediaObject(fields) {
  const ObjectConstructor = getPlainObjectConstructor();
  return Object.assign(new ObjectConstructor(), fields);
}

function cloneMediaTable(rows) {
  const ArrayConstructor = getArrayConstructor();
  return ArrayConstructor.from(rows, (row) => ArrayConstructor.from(row));
}

function getPlainObjectConstructor() {
  return global?.constructor?.name === 'Object' ? global.constructor : Object;
}

function getArrayConstructor() {
  const ObjectConstructor = getPlainObjectConstructor();
  if (ObjectConstructor === Object || typeof ObjectConstructor.constructor !== 'function') return Array;
  return ObjectConstructor.constructor('return Array')();
}

function hasCustomTemplateContent(section) {
  if (!section) return false;
  if (section.template === 'list') return section.items.some(hasText);
  if (section.template === 'experience') return section.entries.some((entry) => Object.values(entry).some(hasText));
  if (section.template === 'directory') {
    return section.blocks.some((block) => hasText(block.title) || hasCustomTemplateContent(block));
  }
  if (section.template === 'image') return hasText(section.image?.dataUrl) || hasText(section.image?.caption);
  if (section.template === 'table') return hasMediaTableContent(section.table);

  return hasText(section.content);
}

function hasMediaTableContent(table = []) {
  const rows = Array.isArray(table)
    ? table.filter(Array.isArray).map((row) => row.map((cell) => String(cell ?? '')))
    : [];
  const bodyHasContent = rows.slice(1).some((row) => row.some(hasText));

  if (bodyHasContent) return true;
  if (isStarterMediaTableHeader(rows[0])) return false;
  if (rows.length !== STARTER_MEDIA_TABLE.length) return rows.some((row) => row.some(hasText));

  return rows.some((row, rowIndex) => row.some((cell, columnIndex) => {
    if (!hasText(cell)) return false;
    return rowIndex !== 0 || cell !== STARTER_MEDIA_TABLE_HEADER[columnIndex];
  }));
}

function isStarterMediaTableHeader(row) {
  return Array.isArray(row)
    && row.length === STARTER_MEDIA_TABLE_HEADER.length
    && row.every((cell, index) => cell === STARTER_MEDIA_TABLE_HEADER[index]);
}

function findBasicItemValue(data, label) {
  const item = data.basicItems.find((basicItem) => basicItem.label === label);
  return item ? item.value : undefined;
}

function normalizeCustomId(value, index, usedIds) {
  const raw = typeof value === 'string' ? value.trim() : '';
  const safe = raw.replace(/^custom:/, '').replace(/[^a-zA-Z0-9_-]/g, '');
  if (safe && !usedIds.has(safe)) return safe;

  let fallback = `custom-${index + 1}`;
  let suffix = index + 1;
  while (usedIds.has(fallback)) {
    suffix += 1;
    fallback = `custom-${suffix}`;
  }

  return fallback;
}

function normalizeModuleOrder(value, customSections) {
  const customIds = customSections.map((section) => customModuleId(section.id));
  const validIds = new Set([...STANDARD_MODULE_KEYS, ...customIds]);
  const order = [];

  if (Array.isArray(value)) {
    value.forEach((id) => {
      if (id === 'customSections') {
        customIds.forEach((customId) => pushUnique(order, customId, validIds));
        return;
      }

      pushUnique(order, id, validIds);
    });
  }

  [...STANDARD_MODULE_KEYS, ...customIds].forEach((id) => pushUnique(order, id, validIds));

  return order;
}

function pushUnique(order, id, validIds) {
  if (!validIds.has(id) || order.includes(id)) return;
  order.push(id);
}

function customModuleId(id) {
  return `${CUSTOM_MODULE_PREFIX}${id}`;
}

function isCustomModuleId(value) {
  return typeof value === 'string' && value.startsWith(CUSTOM_MODULE_PREFIX);
}

function isObject(value) {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function hasText(value) {
  return Boolean(String(value || '').trim());
}

function isLikelyEmail(value = '') {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}

function hasMetric(text) {
  return /\d|%|％|提升|降低|减少|增长|节省|用户|规模/.test(text);
}

function hasOutcome(text) {
  return /提升|降低|减少|增长|完成|上线|落地|交付|优化|成果|效率|质量|稳定性|转化/.test(text);
}

function hasActionWord(text) {
  return /负责|主导|搭建|优化|设计|开发|推进|协作|维护|重构|沉淀/.test(text);
}

  global.ResumeCore = {
    MODULES,
    createSampleResume,
    getOrderedModuleItems,
    getModuleStatuses,
    getSuggestions,
    moveModuleOrderItem,
    normalizeResumeData,
    validateImportedResume,
  };
})(typeof window !== 'undefined' ? window : globalThis);
