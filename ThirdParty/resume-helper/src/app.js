const {
  MODULES,
  createSampleResume,
  getOrderedModuleItems,
  getModuleStatuses,
  getSuggestions,
  moveModuleOrderItem,
  normalizeResumeData,
  validateImportedResume,
} = window.ResumeCore;

const STORAGE_KEY = 'resume-helper-data-v1';
const MAX_IMAGE_BYTES = 1024 * 1024;
const ALLOWED_IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const STARTER_MEDIA_TABLE_HEADER = ['Category', 'Content', 'Notes'];
const STARTER_MEDIA_TABLE_ROW_COUNT = 2;
const A4_WIDTH_MM = 210;
const A4_PREVIEW_WIDTH_PX = 780;
const A4_PREVIEW_HEIGHT_PX = 1102;
const A4_HEIGHT_MM = 297;
const A4_PRINT_HORIZONTAL_PADDING_MM = 18;
const A4_PRINT_VERTICAL_PADDING_MM = 16;
const A4_OVERFLOW_TOLERANCE_PX = 2;
const A4_NEAR_EDGE_THRESHOLD_MM = 8;

let draggedModuleId = '';
let pendingImageTarget = null;

const state = {
  activeModule: 'basics',
  resume: loadResume(),
};

const dom = {
  moduleNav: document.querySelector('#moduleNav'),
  addCustomModuleButton: document.querySelector('#addCustomModuleButton'),
  completionSummary: document.querySelector('#completionSummary'),
  resumePreview: document.querySelector('#resumePreview'),
  editorTitle: document.querySelector('#editorTitle'),
  activeStatus: document.querySelector('#activeStatus'),
  editorFields: document.querySelector('#editorFields'),
  suggestionList: document.querySelector('#suggestionList'),
  rewriteBox: document.querySelector('#rewriteBox'),
  rewriteButton: document.querySelector('#rewriteButton'),
  a4RangeState: document.querySelector('#a4RangeState'),
  autosaveState: document.querySelector('#autosaveState'),
  templateSelect: document.querySelector('#templateSelect'),
  aiJsonGuideButton: document.querySelector('#aiJsonGuideButton'),
  aiJsonDialog: document.querySelector('#aiJsonDialog'),
  aiJsonCloseButton: document.querySelector('#aiJsonCloseButton'),
  aiJsonCopyButton: document.querySelector('#aiJsonCopyButton'),
  aiJsonCopyState: document.querySelector('#aiJsonCopyState'),
  aiJsonPrompt: document.querySelector('#aiJsonPrompt'),
  importButton: document.querySelector('#importButton'),
  exportButton: document.querySelector('#exportButton'),
  resetButton: document.querySelector('#resetButton'),
  printButton: document.querySelector('#printButton'),
  importFile: document.querySelector('#importFile'),
  avatarFile: document.querySelector('#avatarFile'),
  customImageFile: document.querySelector('#customImageFile'),
};

dom.templateSelect.addEventListener('change', (event) => {
  updateResume((draft) => {
    draft.template = event.target.value;
  });
});

dom.importButton.addEventListener('click', () => dom.importFile.click());
dom.importFile.addEventListener('change', importJson);
dom.avatarFile.addEventListener('change', (event) => handleImageFileSelect(event, (draft, image) => {
  draft.avatar = image;
}));
dom.customImageFile.addEventListener('change', handleCustomImageFileSelect);
dom.exportButton.addEventListener('click', exportJson);
dom.printButton.addEventListener('click', handlePrint);
dom.resetButton.addEventListener('click', resetData);
dom.rewriteButton.addEventListener('click', showRewriteSuggestion);
dom.addCustomModuleButton.addEventListener('click', addCustomModule);
dom.aiJsonGuideButton.addEventListener('click', openAiJsonGuide);
dom.aiJsonCloseButton.addEventListener('click', closeAiJsonGuide);
dom.aiJsonCopyButton.addEventListener('click', copyAiJsonPrompt);
dom.aiJsonDialog.addEventListener('click', handleAiJsonDialogClick);

render();

function loadResume() {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (!stored) return createSampleResume();

  try {
    const parsed = JSON.parse(stored);
    const result = validateImportedResume(parsed);
    return result.valid ? result.data : createSampleResume();
  } catch {
    return createSampleResume();
  }
}

function saveResume() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state.resume));
  dom.autosaveState.textContent = `已自动保存 ${new Date().toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
  })}`;
}

function updateResume(mutator) {
  const draft = structuredClone(state.resume);
  mutator(draft);
  state.resume = normalizeResumeData(draft);
  saveResume();
  render();
}

function updateResumeSoft(mutator) {
  const draft = structuredClone(state.resume);
  mutator(draft);
  state.resume = normalizeResumeData(draft);
  ensureActiveModule();
  saveResume();
  renderModuleNav();
  renderActiveEditorHeading();
  renderPreview();
  renderSuggestions();
  renderActiveStatus();
}

function render() {
  state.resume = normalizeResumeData(state.resume);
  ensureActiveModule();
  dom.templateSelect.value = state.resume.template;
  renderModuleNav();
  renderPreview();
  renderEditor();
  renderSuggestions();
}

function renderModuleNav() {
  const modules = getOrderedModuleItems(state.resume);
  const statuses = getModuleStatuses(state.resume);
  const completeCount = modules.filter((module) => {
    const status = statuses[module.id] || statuses[module.key];
    return status.tone === 'success';
  }).length;
  dom.completionSummary.textContent = `${completeCount}/${modules.length} 已完成`;

  dom.moduleNav.innerHTML = modules.map((module) => {
    const status = statuses[module.id] || statuses[module.key];
    return `
      <button class="module-item ${module.id === state.activeModule ? 'active' : ''}" type="button" draggable="true" data-module="${module.id}" aria-label="拖动或选择 ${escapeAttribute(module.label)}">
        <span class="drag-handle" aria-hidden="true">☰</span>
        <span class="module-label">${escapeHtml(module.label)}</span>
        <span class="status-pill status-${status.tone}">${escapeHtml(status.label)}</span>
      </button>
    `;
  }).join('');

  dom.moduleNav.querySelectorAll('.module-item').forEach((button) => {
    button.addEventListener('click', () => {
      state.activeModule = button.dataset.module;
      dom.rewriteBox.hidden = true;
      render();
    });

    button.addEventListener('dragstart', handleModuleDragStart);
    button.addEventListener('dragover', handleModuleDragOver);
    button.addEventListener('drop', handleModuleDrop);
    button.addEventListener('dragend', handleModuleDragEnd);
  });
}

function renderPreview() {
  const resume = state.resume;
  dom.resumePreview.className = `resume-paper template-${resume.template}`;

  dom.resumePreview.innerHTML = getOrderedModuleItems(resume).map(renderPreviewModule).join('');
  updateA4RangeState();
  bindA4RangeAssetChecks();
}

function renderPreviewModule(module) {
  if (module.id === 'basics') return renderBasicsPreview(state.resume.basics);
  if (module.id === 'education') return renderEducation(state.resume.education);
  if (module.id === 'skills') return renderSkills(state.resume.skills);
  if (module.id === 'work') return renderWork(state.resume.work);
  if (module.id === 'projects') return renderProjects(state.resume.projects);
  if (module.id === 'certificates') return renderCertificates(state.resume.certificates);
  if (module.id === 'selfEvaluation') return renderSelfEvaluation(state.resume.selfEvaluation);
  if (module.type === 'custom') return renderCustomSection(state.resume.customSections[module.customIndex]);
  return '';
}

function renderBasicsPreview(basics) {
  const items = state.resume.basicItems.filter((item) => hasText(item.value));
  const avatar = state.resume.avatar || {};

  if (!hasText(avatar.dataUrl)) {
    return `
    <header class="resume-header">
      <h2>${escapeHtml(basics.name || '未填写姓名')}</h2>
      <div class="resume-meta">
        ${items.map((item) => `
          <span class="resume-meta-item">
            <span class="meta-emoji">${escapeHtml(item.emoji || '')}</span>
            <span>${escapeHtml(item.label ? `${item.label}：${item.value}` : item.value)}</span>
          </span>
        `).join('')}
      </div>
    </header>
  `;
  }

  return `
    <header class="resume-header resume-header-with-avatar">
      <div class="resume-header-main">
        <h2>${escapeHtml(basics.name || '未填写姓名')}</h2>
        <div class="resume-meta">
          ${items.map((item) => `
            <span class="resume-meta-item">
              <span class="meta-emoji">${escapeHtml(item.emoji || '')}</span>
              <span>${escapeHtml(item.label ? `${item.label}：${item.value}` : item.value)}</span>
            </span>
          `).join('')}
        </div>
      </div>
      <img class="resume-avatar" src="${escapeAttribute(avatar.dataUrl)}" alt="${escapeAttribute(avatar.alt || avatar.caption || basics.name || '')}">
    </header>
  `;
}

function renderEducation(items) {
  if (!items.some(hasEntryText)) return '';
  return section('教育背景', items.map((item) => `
    <div class="resume-line">
      <strong>${escapeHtml(item.school || '学校名称')}</strong>
      <span>${escapeHtml(joinRange(item.start, item.end))}</span>
    </div>
    <div class="resume-subline">${escapeHtml([item.major, item.degree].filter(Boolean).join('（') + (item.major && item.degree ? '）' : ''))}</div>
  `).join(''));
}

function renderSkills(skills) {
  const items = skills.filter(hasText);
  if (!items.length) return '';
  return section('专业技能', `<ul class="resume-list">${items.map((skill) => `<li>${escapeHtml(skill)}</li>`).join('')}</ul>`);
}

function renderWork(items) {
  if (!items.some(hasEntryText)) return '';
  return section('工作经历', items.map((item) => `
    <div class="resume-line">
      <strong>${escapeHtml(item.company || '公司名称')}</strong>
      <span>${escapeHtml(joinRange(item.start, item.end))}</span>
    </div>
    <div class="resume-subline">${escapeHtml(item.role || '职位')}</div>
    <div class="resume-text">${escapeHtml(item.description || '')}</div>
  `).join(''));
}

function renderProjects(items) {
  if (!items.some(hasEntryText)) return '';
  return section('项目经历', items.map((item) => `
    <div class="resume-line">
      <strong>${escapeHtml(item.name || '项目名称')}</strong>
      <span>${escapeHtml(joinRange(item.start, item.end))}</span>
    </div>
    <div class="resume-subline">${escapeHtml([item.role, item.tech].filter(Boolean).join(' | '))}</div>
    <div class="resume-text">${escapeHtml(item.description || '')}</div>
  `).join(''));
}

function renderCertificates(items) {
  const certificates = items.filter(hasText);
  if (!certificates.length) return '';
  return section('证书荣誉', `<ul class="resume-list">${certificates.map((item) => `<li>${escapeHtml(item)}</li>`).join('')}</ul>`);
}

function renderSelfEvaluation(text) {
  if (!hasText(text)) return '';
  return section('自我评价', `<div class="resume-text">${escapeHtml(text)}</div>`);
}

function renderCustomSections(items) {
  return items.map(renderCustomSection).join('');
}

function renderCustomSection(item) {
  if (!hasCustomPreviewContent(item)) return '';
  return section(item.title || '自定义模块', renderCustomTemplateBody(item));
}

function renderCustomTemplateBody(item) {
  if (item.template === 'list') {
    const items = item.items.filter(hasText);
    return items.length
      ? `<ul class="resume-list">${items.map((value) => `<li>${escapeHtml(value)}</li>`).join('')}</ul>`
      : '';
  }

  if (item.template === 'experience') {
    return renderCustomExperienceEntries(item.entries);
  }

  if (item.template === 'image') {
    return renderMediaImage(item.image);
  }

  if (item.template === 'table') {
    return renderMediaTable(item.table);
  }

  if (item.template === 'directory') {
    return item.blocks.filter(hasCustomPreviewContent).map((block) => `
      <div class="custom-block-preview">
        <h4>${escapeHtml(block.title || '子板块')}</h4>
        ${renderCustomTemplateBody(block)}
      </div>
    `).join('');
  }

  const lines = String(item.content || '').split('\n').filter(hasText);
  return lines.length > 1
    ? `<ul class="resume-list">${lines.map((line) => `<li>${escapeHtml(line)}</li>`).join('')}</ul>`
    : `<div class="resume-text">${escapeHtml(item.content || '')}</div>`;
}

function renderCustomExperienceEntries(entries) {
  if (!entries.some(hasEntryText)) return '';
  return entries.map((item) => `
    <div class="resume-line">
      <strong>${escapeHtml(item.name || '条目名称')}</strong>
      <span>${escapeHtml(joinRange(item.start, item.end))}</span>
    </div>
    <div class="resume-subline">${escapeHtml(item.role || '')}</div>
    <div class="resume-text">${escapeHtml(item.description || '')}</div>
  `).join('');
}

function renderMediaImage(image = {}) {
  const hasImage = hasText(image.dataUrl);
  const hasCaption = hasText(image.caption);
  if (!hasImage && !hasCaption) return '';

  return `
    <figure class="resume-media-figure">
      ${hasImage ? `<img class="resume-media-image" src="${escapeAttribute(image.dataUrl)}" alt="${escapeAttribute(image.alt || image.caption || '自定义图片')}">` : ''}
      ${hasCaption ? `<figcaption>${escapeHtml(image.caption)}</figcaption>` : ''}
    </figure>
  `;
}

function renderMediaTable(table) {
  const rows = trimEmptyTableRows(table);
  if (!rows.length) return '';

  const headRows = rows.length > 1 ? rows.slice(0, 1) : [];
  const bodyRows = rows.length > 1 ? rows.slice(1) : rows;

  return `
    <table class="resume-media-table">
      ${headRows.length ? `<thead>${headRows.map(renderMediaTableRow('th')).join('')}</thead>` : ''}
      <tbody>${bodyRows.map(renderMediaTableRow('td')).join('')}</tbody>
    </table>
  `;
}

function renderMediaTableRow(cellTag) {
  return (row) => `<tr>${row.map((cell) => `<${cellTag}>${escapeHtml(cell)}</${cellTag}>`).join('')}</tr>`;
}

function trimEmptyTableRows(table = []) {
  const rows = Array.isArray(table)
    ? table.filter(Array.isArray).map((row) => row.map((cell) => String(cell ?? '')))
    : [];
  const hasHeaderContent = rows.length > 1 && rows[0].some(hasText);
  const hasBodyContent = rows.slice(1).some((row) => row.some(hasText));

  while (
    rows.length
    && rows[rows.length - 1].every((cell) => !hasText(cell))
    && !(hasHeaderContent && !hasBodyContent && rows.length === 2)
  ) {
    rows.pop();
  }

  return rows;
}

function section(title, body) {
  return `
    <section class="resume-section">
      <h3>${escapeHtml(title)}</h3>
      ${body || '<p class="empty-preview">暂无内容</p>'}
    </section>
  `;
}

function renderEditor() {
  const module = getActiveModuleItem();

  renderActiveEditorHeading();
  renderActiveStatus();

  if (module.id === 'basics') renderBasicsEditor();
  if (module.id === 'education') renderEntryEditor('education', educationFields(), emptyEducation);
  if (module.id === 'skills') renderStringListEditor('skills', '技能');
  if (module.id === 'work') renderEntryEditor('work', workFields(), emptyWork);
  if (module.id === 'projects') renderEntryEditor('projects', projectFields(), emptyProject);
  if (module.id === 'certificates') renderStringListEditor('certificates', '证书/荣誉');
  if (module.id === 'selfEvaluation') renderSelfEditor();
  if (module.type === 'custom') renderCustomEditor(module);
}

function renderActiveStatus() {
  const statuses = getModuleStatuses(state.resume);
  const module = getActiveModuleItem();
  const status = statuses[module.id] || statuses[module.key];
  dom.activeStatus.className = `status-pill status-${status.tone}`;
  dom.activeStatus.textContent = status.label;
}

function renderActiveEditorHeading() {
  const module = getActiveModuleItem();
  dom.editorTitle.textContent = module.label;
  dom.editorFields.querySelector('[data-custom-entry-title]')?.replaceChildren(document.createTextNode(module.label));
}

function renderBasicsEditor() {
  const avatar = state.resume.avatar || {};
  const hasAvatar = hasText(avatar.dataUrl);

  dom.editorFields.innerHTML = `
    <div class="field">
      <label for="basics-name">姓名</label>
      <input id="basics-name" value="${escapeAttribute(state.resume.basics.name || '')}" data-basics="name">
    </div>
    <div class="entry-card avatar-editor-card">
      <div class="entry-head">
        <span class="entry-title">头像（可选）</span>
        ${hasAvatar ? '<button class="delete-button" type="button" data-remove-avatar>删除</button>' : ''}
      </div>
      <div class="avatar-editor-body">
        ${hasAvatar
          ? `<img class="avatar-editor-preview" src="${escapeAttribute(avatar.dataUrl)}" alt="${escapeAttribute(avatar.alt || avatar.caption || state.resume.basics.name || '')}">`
          : '<div class="avatar-editor-placeholder">未上传</div>'}
        <div class="avatar-editor-actions">
          <button class="add-button" type="button" data-upload-avatar>上传头像</button>
          <div class="field">
            <label for="avatar-alt">替代文本</label>
            <input id="avatar-alt" value="${escapeAttribute(avatar.alt || '')}" data-avatar-alt>
          </div>
        </div>
      </div>
    </div>
    <div class="basic-item-list">
      ${state.resume.basicItems.map((item, index) => `
        <div class="entry-card basic-item-card">
          <div class="entry-head">
            <span class="entry-title">信息项 ${index + 1}</span>
            <button class="delete-button" type="button" data-delete-basic-item="${index}">删除</button>
          </div>
          <div class="field-grid basic-item-grid">
            <div class="field basic-emoji-field">
              <label for="basic-item-${index}-emoji">Emoji</label>
              <input id="basic-item-${index}-emoji" value="${escapeAttribute(item.emoji || '')}" data-basic-item-index="${index}" data-basic-item-field="emoji">
            </div>
            <div class="field">
              <label for="basic-item-${index}-label">标签</label>
              <input id="basic-item-${index}-label" value="${escapeAttribute(item.label || '')}" data-basic-item-index="${index}" data-basic-item-field="label">
            </div>
            <div class="field wide-field">
              <label for="basic-item-${index}-value">内容</label>
              <input id="basic-item-${index}-value" value="${escapeAttribute(item.value || '')}" data-basic-item-index="${index}" data-basic-item-field="value">
            </div>
          </div>
        </div>
      `).join('')}
      <button class="add-button" type="button" data-add-basic-item>+ 添加信息项</button>
    </div>
  `;

  dom.editorFields.querySelectorAll('[data-basics]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.basics[input.dataset.basics] = input.value;
      });
    });
  });

  dom.editorFields.querySelector('[data-upload-avatar]').addEventListener('click', () => {
    dom.avatarFile.click();
  });

  dom.editorFields.querySelector('[data-remove-avatar]')?.addEventListener('click', () => {
    updateResume((draft) => {
      draft.avatar = { dataUrl: '', name: '', alt: '', caption: '' };
    });
  });

  dom.editorFields.querySelector('[data-avatar-alt]').addEventListener('input', (event) => {
    updateResumeSoft((draft) => {
      draft.avatar.alt = event.target.value;
    });
  });

  dom.editorFields.querySelectorAll('[data-basic-item-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.basicItems[Number(input.dataset.basicItemIndex)][input.dataset.basicItemField] = input.value;
      });
    });
  });

  dom.editorFields.querySelector('[data-add-basic-item]').addEventListener('click', () => {
    updateResume((draft) => {
      draft.basicItems.push({ emoji: '', label: '', value: '' });
    });
  });

  dom.editorFields.querySelectorAll('[data-delete-basic-item]').forEach((button) => {
    button.addEventListener('click', () => {
      updateResume((draft) => {
        draft.basicItems.splice(Number(button.dataset.deleteBasicItem), 1);
      });
    });
  });
}

function renderSelfEditor() {
  dom.editorFields.innerHTML = `
    <div class="field">
      <label for="selfEvaluation">自我评价</label>
      <textarea id="selfEvaluation" data-self>${escapeHtml(state.resume.selfEvaluation || '')}</textarea>
    </div>
  `;

  dom.editorFields.querySelector('[data-self]').addEventListener('input', (event) => {
    updateResumeSoft((draft) => {
      draft.selfEvaluation = event.target.value;
    });
  });
}

function renderStringListEditor(key, label) {
  const values = state.resume[key];
  dom.editorFields.innerHTML = `
    ${values.map((value, index) => `
      <div class="entry-card">
        <div class="entry-head">
          <span class="entry-title">${label} ${index + 1}</span>
          <button class="delete-button" type="button" data-delete="${index}">删除</button>
        </div>
        <div class="field">
          <label for="${key}-${index}">${label}</label>
          <input id="${key}-${index}" value="${escapeAttribute(value || '')}" data-list-index="${index}">
        </div>
      </div>
    `).join('')}
    <button class="add-button" type="button" data-add>+ 添加${label}</button>
  `;

  dom.editorFields.querySelectorAll('[data-list-index]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft[key][Number(input.dataset.listIndex)] = input.value;
      });
    });
  });

  wireListButtons(key, () => '');
}

function renderEntryEditor(key, fields, emptyFactory) {
  const entries = state.resume[key];
  const addLabel = MODULES.find((item) => item.key === key).label;

  dom.editorFields.innerHTML = `
    ${entries.map((entry, index) => `
      <div class="entry-card">
        <div class="entry-head">
          <span class="entry-title">${addLabel} ${index + 1}</span>
          <button class="delete-button" type="button" data-delete="${index}">删除</button>
        </div>
        <div class="field-grid">
          ${fields.map((field) => renderField(key, entry, index, field)).join('')}
        </div>
      </div>
    `).join('')}
    <button class="add-button" type="button" data-add>+ 添加${addLabel}</button>
  `;

  dom.editorFields.querySelectorAll('[data-entry-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft[key][Number(input.dataset.entryIndex)][input.dataset.entryField] = input.value;
      });
    });
  });

  wireListButtons(key, emptyFactory);
}

function renderCustomEditor(module) {
  const entry = state.resume.customSections[module.customIndex] || emptyCustom();
  const templateOptions = customTemplateOptions(entry.template);

  dom.editorFields.innerHTML = `
    <div class="entry-card custom-module-editor">
      <div class="entry-head">
        <span class="entry-title" data-custom-entry-title>${escapeHtml(module.label)}</span>
        <button class="delete-button" type="button" data-delete-custom>删除模块</button>
      </div>
      <div class="field-grid">
        <div class="field">
          <label for="customSections-${module.customIndex}-title">模块标题</label>
          <input id="customSections-${module.customIndex}-title" value="${escapeAttribute(entry.title || '')}" data-custom-title>
        </div>
        <div class="field">
          <label for="customSections-${module.customIndex}-template">模块模板</label>
          <select id="customSections-${module.customIndex}-template" data-custom-template>
            ${templateOptions.map((option) => `<option value="${option.value}" ${option.value === entry.template ? 'selected' : ''}>${option.label}</option>`).join('')}
          </select>
        </div>
      </div>
      ${renderCustomTemplateEditor(entry, module.customIndex)}
    </div>
  `;

  dom.editorFields.querySelector('[data-custom-title]').addEventListener('input', (event) => {
    updateResumeSoft((draft) => {
      draft.customSections[module.customIndex].title = event.target.value;
    });
  });

  dom.editorFields.querySelector('[data-custom-template]').addEventListener('change', (event) => {
    updateResume((draft) => {
      draft.customSections[module.customIndex].template = event.target.value;
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex][input.dataset.customField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-item-index]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex].items[Number(input.dataset.customItemIndex)] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-entry-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex].entries[Number(input.dataset.customEntryIndex)][input.dataset.customEntryField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-image-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex].image[input.dataset.customImageField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-table-row]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        const table = ensureEditableTable(draft.customSections[module.customIndex]);
        table[Number(input.dataset.customTableRow)][Number(input.dataset.customTableColumn)] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-title]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex].blocks[Number(input.dataset.customBlockIndex)].title = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-template]').forEach((select) => {
    select.addEventListener('change', () => {
      updateResume((draft) => {
        draft.customSections[module.customIndex].blocks[Number(select.dataset.customBlockIndex)].template = select.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        draft.customSections[module.customIndex].blocks[Number(input.dataset.customBlockIndex)][input.dataset.customBlockField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-item-index]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        const block = draft.customSections[module.customIndex].blocks[Number(input.dataset.customBlockIndex)];
        block.items[Number(input.dataset.customBlockItemIndex)] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-entry-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        const block = draft.customSections[module.customIndex].blocks[Number(input.dataset.customBlockIndex)];
        block.entries[Number(input.dataset.customBlockEntryIndex)][input.dataset.customBlockEntryField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-image-field]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        const block = draft.customSections[module.customIndex].blocks[Number(input.dataset.customBlockIndex)];
        block.image[input.dataset.customBlockImageField] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-block-table-row]').forEach((input) => {
    input.addEventListener('input', () => {
      updateResumeSoft((draft) => {
        const block = draft.customSections[module.customIndex].blocks[Number(input.dataset.blockIndex)];
        const table = ensureEditableTable(block);
        table[Number(input.dataset.customBlockTableRow)][Number(input.dataset.customBlockTableColumn)] = input.value;
      });
    });
  });

  dom.editorFields.querySelectorAll('[data-custom-action]').forEach((button) => {
    button.addEventListener('click', () => {
      handleCustomAction(button, module.customIndex);
    });
  });

  dom.editorFields.querySelector('[data-delete-custom]').addEventListener('click', () => {
    updateResume((draft) => {
      const removed = draft.customSections.splice(module.customIndex, 1)[0];
      draft.moduleOrder = draft.moduleOrder.filter((id) => id !== `custom:${removed.id}`);
      state.activeModule = draft.moduleOrder[0] || 'basics';
    });
  });
}

function renderCustomTemplateEditor(entry, sectionIndex) {
  if (entry.template === 'list') return renderCustomListEditor(entry, sectionIndex, 'custom');
  if (entry.template === 'experience') return renderCustomExperienceEditor(entry, sectionIndex, 'custom');
  if (entry.template === 'directory') return renderCustomDirectoryEditor(entry, sectionIndex);
  if (entry.template === 'image') return renderCustomImageEditor(entry, sectionIndex, 'custom');
  if (entry.template === 'table') return renderCustomTableEditor(entry, sectionIndex, 'custom');
  return `
    <div class="field custom-template-body">
      <label for="customSections-${sectionIndex}-content">模块内容</label>
      <textarea id="customSections-${sectionIndex}-content" data-custom-field="content">${escapeHtml(entry.content || '')}</textarea>
    </div>
  `;
}

function renderCustomDirectoryEditor(entry, sectionIndex) {
  return `
    <div class="custom-block-list">
      ${entry.blocks.map((block, blockIndex) => renderCustomBlockEditor(block, sectionIndex, blockIndex)).join('')}
      <button class="add-button" type="button" data-custom-action="add-block">+ 添加子板块</button>
    </div>
  `;
}

function renderCustomBlockEditor(block, sectionIndex, blockIndex) {
  return `
    <div class="entry-card custom-block-card">
      <div class="entry-head">
        <span class="entry-title">子板块 ${blockIndex + 1}</span>
        <button class="delete-button" type="button" data-custom-action="delete-block" data-block-index="${blockIndex}">删除</button>
      </div>
      <div class="field-grid">
        <div class="field">
          <label for="custom-block-${sectionIndex}-${blockIndex}-title">子板块标题</label>
          <input id="custom-block-${sectionIndex}-${blockIndex}-title" value="${escapeAttribute(block.title || '')}" data-custom-block-title data-custom-block-index="${blockIndex}">
        </div>
        <div class="field">
          <label for="custom-block-${sectionIndex}-${blockIndex}-template">子板块模板</label>
          <select id="custom-block-${sectionIndex}-${blockIndex}-template" data-custom-block-template data-custom-block-index="${blockIndex}">
            ${customTemplateOptions(block.template, { allowDirectory: false }).map((option) => `<option value="${option.value}" ${option.value === block.template ? 'selected' : ''}>${option.label}</option>`).join('')}
          </select>
        </div>
      </div>
      ${renderCustomBlockBodyEditor(block, sectionIndex, blockIndex)}
    </div>
  `;
}

function renderCustomBlockBodyEditor(block, sectionIndex, blockIndex) {
  if (block.template === 'list') return renderCustomListEditor(block, sectionIndex, 'block', blockIndex);
  if (block.template === 'experience') return renderCustomExperienceEditor(block, sectionIndex, 'block', blockIndex);
  if (block.template === 'image') return renderCustomImageEditor(block, sectionIndex, 'block', blockIndex);
  if (block.template === 'table') return renderCustomTableEditor(block, sectionIndex, 'block', blockIndex);
  return `
    <div class="field custom-template-body">
      <label for="custom-block-${sectionIndex}-${blockIndex}-content">子板块内容</label>
      <textarea id="custom-block-${sectionIndex}-${blockIndex}-content" data-custom-block-field="content" data-custom-block-index="${blockIndex}">${escapeHtml(block.content || '')}</textarea>
    </div>
  `;
}

function renderCustomListEditor(entry, sectionIndex, scope, blockIndex = null) {
  const prefix = scope === 'block' ? `custom-block-${sectionIndex}-${blockIndex}` : `customSections-${sectionIndex}`;
  const itemAttr = scope === 'block'
    ? (index) => `data-custom-block-item-index="${index}" data-custom-block-index="${blockIndex}"`
    : (index) => `data-custom-item-index="${index}"`;
  const deleteAction = scope === 'block' ? 'delete-block-item' : 'delete-item';
  const addAction = scope === 'block' ? 'add-block-item' : 'add-item';

  return `
    <div class="custom-template-body">
      ${entry.items.map((item, index) => `
        <div class="entry-card custom-inline-card">
          <div class="entry-head">
            <span class="entry-title">条目 ${index + 1}</span>
            <button class="delete-button" type="button" data-custom-action="${deleteAction}" data-item-index="${index}" ${blockIndex === null ? '' : `data-block-index="${blockIndex}"`}>删除</button>
          </div>
          <div class="field">
            <label for="${prefix}-item-${index}">内容</label>
            <input id="${prefix}-item-${index}" value="${escapeAttribute(item || '')}" ${itemAttr(index)}>
          </div>
        </div>
      `).join('')}
      <button class="add-button" type="button" data-custom-action="${addAction}" ${blockIndex === null ? '' : `data-block-index="${blockIndex}"`}>+ 添加条目</button>
    </div>
  `;
}

function renderCustomExperienceEditor(entry, sectionIndex, scope, blockIndex = null) {
  const prefix = scope === 'block' ? `custom-block-${sectionIndex}-${blockIndex}` : `customSections-${sectionIndex}`;
  const fieldAttr = scope === 'block'
    ? (entryIndex, fieldName) => `data-custom-block-entry-index="${entryIndex}" data-custom-block-entry-field="${fieldName}" data-custom-block-index="${blockIndex}"`
    : (entryIndex, fieldName) => `data-custom-entry-index="${entryIndex}" data-custom-entry-field="${fieldName}"`;
  const deleteAction = scope === 'block' ? 'delete-block-entry' : 'delete-entry';
  const addAction = scope === 'block' ? 'add-block-entry' : 'add-entry';

  return `
    <div class="custom-template-body">
      ${entry.entries.map((customEntry, entryIndex) => `
        <div class="entry-card custom-inline-card">
          <div class="entry-head">
            <span class="entry-title">经历 ${entryIndex + 1}</span>
            <button class="delete-button" type="button" data-custom-action="${deleteAction}" data-entry-index="${entryIndex}" ${blockIndex === null ? '' : `data-block-index="${blockIndex}"`}>删除</button>
          </div>
          <div class="field-grid">
            ${customExperienceFields().map((field) => renderCustomExperienceField(prefix, customEntry, entryIndex, field, fieldAttr)).join('')}
          </div>
        </div>
      `).join('')}
      <button class="add-button" type="button" data-custom-action="${addAction}" ${blockIndex === null ? '' : `data-block-index="${blockIndex}"`}>+ 添加经历</button>
    </div>
  `;
}

function renderCustomTableEditor(entry, sectionIndex, scope, blockIndex = null) {
  const table = getEditableTable(entry);
  const addRowAction = scope === 'block' ? 'add-block-table-row' : 'add-table-row';
  const addColumnAction = scope === 'block' ? 'add-block-table-column' : 'add-table-column';
  const deleteRowAction = scope === 'block' ? 'delete-block-table-row' : 'delete-table-row';
  const deleteColumnAction = scope === 'block' ? 'delete-block-table-column' : 'delete-table-column';
  const blockAttr = blockIndex === null ? '' : ` data-block-index="${blockIndex}"`;
  const cellAttr = scope === 'block'
    ? (rowIndex, columnIndex) => `data-custom-block-table-row="${rowIndex}" data-custom-block-table-column="${columnIndex}" data-custom-block-index="${blockIndex}" data-block-index="${blockIndex}"`
    : (rowIndex, columnIndex) => `data-custom-table-row="${rowIndex}" data-custom-table-column="${columnIndex}"`;

  return `
    <div class="custom-template-body table-editor">
      <div class="table-editor-actions">
        <button class="add-button" type="button" data-custom-action="${addRowAction}"${blockAttr}>+ 添加行</button>
        <button class="add-button" type="button" data-custom-action="${addColumnAction}"${blockAttr}>+ 添加列</button>
      </div>
      <div class="table-editor-scroll">
        <table class="editable-table">
          <tbody>
            ${table.map((row, rowIndex) => `
              <tr>
                ${row.map((cell, columnIndex) => `
                  <td>
                    <textarea ${cellAttr(rowIndex, columnIndex)}>${escapeHtml(cell)}</textarea>
                  </td>
                `).join('')}
                <td class="table-action-cell">
                  <button class="delete-button" type="button" data-custom-action="${deleteRowAction}" data-row-index="${rowIndex}"${blockAttr}>删除行</button>
                </td>
              </tr>
            `).join('')}
            <tr>
              ${table[0].map((_, columnIndex) => `
                <td class="table-action-cell">
                  <button class="delete-button" type="button" data-custom-action="${deleteColumnAction}" data-column-index="${columnIndex}"${blockAttr}>删除列</button>
                </td>
              `).join('')}
              <td class="table-action-cell"></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function renderCustomExperienceField(prefix, entry, entryIndex, field, fieldAttr) {
  const id = `${prefix}-entry-${entryIndex}-${field.name}`;
  const value = entry[field.name] || '';
  const attrs = fieldAttr(entryIndex, field.name);
  const input = field.multiline
    ? `<textarea id="${id}" ${attrs}>${escapeHtml(value)}</textarea>`
    : `<input id="${id}" value="${escapeAttribute(value)}" ${attrs}>`;

  return `
    <div class="field ${field.wide ? 'wide-field' : ''}">
      <label for="${id}">${field.label}</label>
      ${input}
    </div>
  `;
}

function handleCustomAction(button, sectionIndex) {
  const action = button.dataset.customAction;
  const blockIndex = Number(button.dataset.blockIndex);
  const itemIndex = Number(button.dataset.itemIndex);
  const entryIndex = Number(button.dataset.entryIndex);
  const rowIndex = Number(button.dataset.rowIndex);
  const columnIndex = Number(button.dataset.columnIndex);

  if (action === 'upload-image') {
    pendingImageTarget = { sectionIndex };
    dom.customImageFile.click();
    return;
  }

  if (action === 'upload-block-image') {
    pendingImageTarget = { sectionIndex, blockIndex };
    dom.customImageFile.click();
    return;
  }

  updateResume((draft) => {
    const section = draft.customSections[sectionIndex];
    const block = Number.isNaN(blockIndex) ? null : section.blocks[blockIndex];

    if (action === 'add-item') section.items.push('');
    if (action === 'delete-item') section.items.splice(itemIndex, 1);
    if (action === 'add-entry') section.entries.push(emptyCustomExperience());
    if (action === 'delete-entry') section.entries.splice(entryIndex, 1);
    if (action === 'add-block') section.blocks.push(emptyCustomBlock(section.blocks));
    if (action === 'delete-block') section.blocks.splice(blockIndex, 1);
    if (action === 'add-block-item') block.items.push('');
    if (action === 'delete-block-item') block.items.splice(itemIndex, 1);
    if (action === 'add-block-entry') block.entries.push(emptyCustomExperience());
    if (action === 'delete-block-entry') block.entries.splice(entryIndex, 1);
    if (action === 'remove-image') section.image = emptyMediaImage();
    if (action === 'remove-block-image') block.image = emptyMediaImage();
    if (action === 'add-table-row') addTableRow(section);
    if (action === 'add-block-table-row') addTableRow(block);
    if (action === 'add-table-column') addTableColumn(section);
    if (action === 'add-block-table-column') addTableColumn(block);
    if (action === 'delete-table-row') deleteTableRow(section, rowIndex);
    if (action === 'delete-block-table-row') deleteTableRow(block, rowIndex);
    if (action === 'delete-table-column') deleteTableColumn(section, columnIndex);
    if (action === 'delete-block-table-column') deleteTableColumn(block, columnIndex);
  });
}

function customTemplateOptions(currentValue, options = {}) {
  const allowDirectory = options.allowDirectory !== false;
  const items = [
    { value: 'text', label: '自由文本' },
    { value: 'list', label: '条目列表' },
    { value: 'experience', label: '经历列表' },
    { value: 'image', label: '图片展示' },
    { value: 'table', label: '表格' },
  ];

  if (allowDirectory) items.push({ value: 'directory', label: '目录板块' });
  if (!items.some((item) => item.value === currentValue)) return customTemplateOptions('text', options);
  return items;
}

function renderField(key, entry, index, field) {
  const id = `${key}-${index}-${field.name}`;
  const value = entry[field.name] || '';
  const input = field.multiline
    ? `<textarea id="${id}" data-entry-index="${index}" data-entry-field="${field.name}">${escapeHtml(value)}</textarea>`
    : `<input id="${id}" value="${escapeAttribute(value)}" data-entry-index="${index}" data-entry-field="${field.name}">`;

  return `
    <div class="field ${field.wide ? 'wide-field' : ''}">
      <label for="${id}">${field.label}</label>
      ${input}
    </div>
  `;
}

function wireListButtons(key, emptyFactory) {
  dom.editorFields.querySelector('[data-add]').addEventListener('click', () => {
    updateResume((draft) => {
      draft[key].push(emptyFactory());
    });
  });

  dom.editorFields.querySelectorAll('[data-delete]').forEach((button) => {
    button.addEventListener('click', () => {
      updateResume((draft) => {
        draft[key].splice(Number(button.dataset.delete), 1);
      });
    });
  });
}

function renderSuggestions() {
  const suggestions = getSuggestions(state.resume, state.activeModule);

  if (!suggestions.length) {
    dom.suggestionList.innerHTML = '<div class="suggestion-item good">当前模块信息比较完整，可以继续完善其他模块。</div>';
    return;
  }

  dom.suggestionList.innerHTML = suggestions.map((suggestion) => `
    <div class="suggestion-item">${escapeHtml(suggestion)}</div>
  `).join('');
}

function updateA4RangeState() {
  if (!dom.a4RangeState) return;

  window.requestAnimationFrame(() => {
    const status = getCurrentA4OverflowStatus();
    dom.a4RangeState.textContent = formatA4RangeMessage(status);
    dom.a4RangeState.className = `a4-range-state a4-range-${status.tone}`;
    dom.a4RangeState.title = formatA4RangeTitle(status);
  });
}

function getCurrentA4OverflowStatus() {
  return createA4OverflowStatus({
    actualHeight: measureA4ContentHeight(dom.resumePreview),
    targetHeight: A4_PREVIEW_HEIGHT_PX,
    tolerance: A4_OVERFLOW_TOLERANCE_PX,
  });
}

function bindA4RangeAssetChecks() {
  dom.resumePreview.querySelectorAll('img').forEach((image) => {
    if (!image.complete) {
      image.addEventListener('load', updateA4RangeState, { once: true });
      image.addEventListener('error', updateA4RangeState, { once: true });
    }
  });
}

function measureA4ContentHeight(element) {
  if (!element?.cloneNode || !document.body?.appendChild) {
    return element?.scrollHeight || 0;
  }

  const clone = element.cloneNode(true);
  clone.removeAttribute('id');
  clone.setAttribute('aria-hidden', 'true');

  const horizontalPadding = A4_PREVIEW_WIDTH_PX * (A4_PRINT_HORIZONTAL_PADDING_MM / A4_WIDTH_MM);
  const verticalPadding = A4_PREVIEW_HEIGHT_PX * (A4_PRINT_VERTICAL_PADDING_MM / A4_HEIGHT_MM);
  Object.assign(clone.style, {
    position: 'fixed',
    left: '-10000px',
    top: '0',
    width: `${A4_PREVIEW_WIDTH_PX}px`,
    minHeight: '0',
    height: 'auto',
    margin: '0',
    border: '0',
    boxShadow: 'none',
    padding: `${verticalPadding.toFixed(2)}px ${horizontalPadding.toFixed(2)}px`,
    visibility: 'hidden',
    pointerEvents: 'none',
    zIndex: '-1',
  });

  document.body.appendChild(clone);
  const height = clone.scrollHeight;
  clone.remove();
  return height;
}

function createA4OverflowStatus({ actualHeight, targetHeight, tolerance = A4_OVERFLOW_TOLERANCE_PX }) {
  const rawOverflowPx = Math.max(0, Math.ceil(actualHeight - targetHeight));
  const overflowPx = rawOverflowPx > tolerance ? rawOverflowPx : 0;
  const pxPerMm = targetHeight / A4_HEIGHT_MM;
  const overflowMm = overflowPx / pxPerMm;
  const remainingPx = Math.max(0, Math.floor(targetHeight - actualHeight));
  const remainingMm = remainingPx / pxPerMm;
  const tone = overflowPx > 0
    ? 'warning'
    : remainingMm <= A4_NEAR_EDGE_THRESHOLD_MM ? 'near' : 'ok';

  return {
    tone,
    actualHeight,
    targetHeight,
    overflowPx,
    overflowMm,
    remainingPx,
    remainingMm,
  };
}

function formatA4RangeMessage(status) {
  if (status.tone === 'near') return `接近 A4 底部，余量约 ${status.remainingMm.toFixed(1)} mm`;
  if (status.tone !== 'warning') return 'A4 范围内';
  return `超出 A4 约 ${status.overflowMm.toFixed(1)} mm`;
}

function formatA4RangeTitle(status) {
  if (status.tone === 'warning') return '当前内容可能在导出 PDF 时分页或超出单页 A4。';
  if (status.tone === 'near') return '当前内容仍在单页 A4 范围内，但下方余量较小，导出 PDF 时建议复查。';
  return '当前预览内容在单页 A4 范围内。';
}

function handlePrint() {
  const status = getCurrentA4OverflowStatus();
  if (status.tone === 'warning') {
    const confirmed = window.confirm(`${formatA4RangeMessage(status)}，导出的 PDF 可能分页或被裁切。仍然继续打印吗？`);
    if (!confirmed) return;
  }

  window.print();
}

function openAiJsonGuide() {
  dom.aiJsonCopyState.textContent = '等待复制';
  if (typeof dom.aiJsonDialog.showModal === 'function') {
    dom.aiJsonDialog.showModal();
    return;
  }

  dom.aiJsonDialog.hidden = false;
}

function closeAiJsonGuide() {
  if (typeof dom.aiJsonDialog.close === 'function') {
    dom.aiJsonDialog.close();
    return;
  }

  dom.aiJsonDialog.hidden = true;
}

function handleAiJsonDialogClick(event) {
  if (event.target === dom.aiJsonDialog) closeAiJsonGuide();
}

async function copyAiJsonPrompt() {
  const text = dom.aiJsonPrompt.textContent.trim();

  try {
    await copyTextToClipboard(text);
    dom.aiJsonCopyState.textContent = '已复制';
  } catch {
    selectAiJsonPromptText();
    dom.aiJsonCopyState.textContent = '已选中，请按 Ctrl+C 复制';
  }
}

async function copyTextToClipboard(text) {
  if (window.navigator?.clipboard?.writeText) {
    await window.navigator.clipboard.writeText(text);
    return;
  }

  const textarea = document.createElement?.('textarea');
  if (!textarea || !document.body?.appendChild) throw new Error('Clipboard is unavailable.');

  textarea.value = text;
  textarea.setAttribute('readonly', '');
  Object.assign(textarea.style, {
    position: 'fixed',
    left: '-10000px',
    top: '0',
  });

  document.body.appendChild(textarea);
  textarea.select();
  const copied = document.execCommand?.('copy');
  textarea.remove();

  if (!copied) throw new Error('Copy command failed.');
}

function selectAiJsonPromptText() {
  const selection = window.getSelection?.();
  const range = document.createRange?.();
  if (!selection || !range) return;

  range.selectNodeContents(dom.aiJsonPrompt);
  selection.removeAllRanges();
  selection.addRange(range);
}

function showRewriteSuggestion() {
  const module = getActiveModuleItem();
  const suggestions = getSuggestions(state.resume, state.activeModule);
  const text = suggestions.length
    ? `建议先处理「${module.label}」中的 ${suggestions.length} 条提示。可以优先补充动作、职责和结果，再把句子压缩成“做了什么 + 用了什么方法 + 带来什么结果”。`
    : `「${module.label}」已经比较完整。下一步可以检查关键词是否贴近目标岗位，并把最重要的信息放在更靠前的位置。`;

  dom.rewriteBox.textContent = text;
  dom.rewriteBox.hidden = false;
}

function exportJson() {
  const blob = new Blob([JSON.stringify(state.resume, null, 2)], { type: 'application/json;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `resume-helper-${new Date().toISOString().slice(0, 10)}.json`;
  link.click();
  URL.revokeObjectURL(url);
}

function importJson(event) {
  const file = event.target.files?.[0];
  if (!file) return;

  const reader = new FileReader();
  reader.addEventListener('load', () => {
    try {
      const result = validateImportedResume(JSON.parse(String(reader.result)));
      if (!result.valid) {
        window.alert(result.reason);
        return;
      }
      state.resume = result.data;
      saveResume();
      render();
    } catch {
      window.alert('导入失败，请检查 JSON 文件格式。');
    } finally {
      dom.importFile.value = '';
    }
  });
  reader.readAsText(file, 'UTF-8');
}

function renderCustomImageEditor(entry, sectionIndex, scope, blockIndex = null) {
  const image = entry.image || {};
  const hasImage = hasText(image.dataUrl);
  const prefix = scope === 'block' ? `custom-block-${sectionIndex}-${blockIndex}-image` : `customSections-${sectionIndex}-image`;
  const fieldAttr = scope === 'block'
    ? (fieldName) => `data-custom-block-image-field="${fieldName}" data-custom-block-index="${blockIndex}"`
    : (fieldName) => `data-custom-image-field="${fieldName}"`;
  const uploadAction = scope === 'block' ? 'upload-block-image' : 'upload-image';
  const removeAction = scope === 'block' ? 'remove-block-image' : 'remove-image';
  const blockAttr = blockIndex === null ? '' : ` data-block-index="${blockIndex}"`;
  const imageTitle = image.name || image.alt || image.caption || '自定义图片';

  return `
    <div class="custom-template-body media-editor">
      <div class="media-editor-preview">
        ${hasImage
          ? `<img src="${escapeAttribute(image.dataUrl)}" alt="${escapeAttribute(image.alt || image.caption || image.name || '自定义图片')}">`
          : '<div class="media-empty-preview">未上传图片</div>'}
      </div>
      <div class="entry-head">
        <span class="entry-title">${escapeHtml(imageTitle)}</span>
        <div class="media-editor-actions">
          ${hasImage ? `<button class="delete-button" type="button" data-custom-action="${removeAction}"${blockAttr}>移除图片</button>` : ''}
          <button class="add-button" type="button" data-custom-action="${uploadAction}"${blockAttr}>上传图片</button>
        </div>
      </div>
      <div class="field-grid">
        <div class="field">
          <label for="${prefix}-alt">替代文本</label>
          <input id="${prefix}-alt" value="${escapeAttribute(image.alt || '')}" ${fieldAttr('alt')}>
        </div>
        <div class="field">
          <label for="${prefix}-caption">图片说明</label>
          <textarea id="${prefix}-caption" ${fieldAttr('caption')}>${escapeHtml(image.caption || '')}</textarea>
        </div>
      </div>
    </div>
  `;
}

function handleImageFileSelect(event, applyImage) {
  const input = event.target;
  const resetInput = () => {
    input.value = '';
  };
  const file = input.files?.[0];
  if (!file) {
    resetInput();
    return;
  }

  if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
    window.alert('仅支持 PNG、JPG 或 WebP 图片。');
    resetInput();
    return;
  }

  if (file.size > MAX_IMAGE_BYTES) {
    window.alert('图片不能超过 1 MB，请压缩后再上传。');
    resetInput();
    return;
  }

  const reader = new FileReader();
  reader.addEventListener('load', () => {
    try {
      updateResume((draft) => {
        applyImage(draft, {
          dataUrl: String(reader.result || ''),
          name: file.name,
          alt: file.name.replace(/\.[^.]+$/, ''),
          caption: '',
        });
      });
    } finally {
      resetInput();
    }
  });
  reader.addEventListener('error', () => {
    window.alert('读取图片失败，请重新选择图片。');
    resetInput();
  });
  reader.addEventListener('abort', () => {
    window.alert('已取消读取图片，请重新选择图片。');
    resetInput();
  });

  try {
    reader.readAsDataURL(file);
  } catch {
    window.alert('读取图片失败，请重新选择图片。');
    resetInput();
  }
}

function handleCustomImageFileSelect(event) {
  const target = pendingImageTarget;
  pendingImageTarget = null;

  if (!target) {
    event.target.value = '';
    return;
  }

  handleImageFileSelect(event, (draft, image) => {
    if (Number.isInteger(target.blockIndex)) {
      draft.customSections[target.sectionIndex].blocks[target.blockIndex].image = image;
      return;
    }

    draft.customSections[target.sectionIndex].image = image;
  });
}

function resetData() {
  if (!window.confirm('确定要重置为示例简历吗？当前内容会被覆盖。')) return;
  state.resume = createSampleResume();
  state.activeModule = 'basics';
  saveResume();
  render();
}

function addCustomModule() {
  updateResume((draft) => {
    const nextIndex = draft.customSections.length + 1;
    const section = {
      id: createCustomSectionId(draft.customSections),
      title: `自定义模块 ${nextIndex}`,
      template: 'text',
      content: '',
      items: [],
      entries: [],
      blocks: [],
    };

    draft.customSections.push(section);
    draft.moduleOrder.push(`custom:${section.id}`);
    state.activeModule = `custom:${section.id}`;
  });
}

function handleModuleDragStart(event) {
  const button = event.currentTarget;
  draggedModuleId = button.dataset.module;
  button.classList.add('dragging');
  event.dataTransfer.effectAllowed = 'move';
  event.dataTransfer.setData('text/plain', draggedModuleId);
}

function handleModuleDragOver(event) {
  event.preventDefault();
  event.dataTransfer.dropEffect = 'move';

  const button = event.currentTarget;
  const draggedId = draggedModuleId || event.dataTransfer.getData('text/plain');
  if (!draggedId || draggedId === button.dataset.module) return;

  dom.moduleNav.querySelectorAll('.module-item').forEach((item) => {
    item.classList.remove('drag-over-before', 'drag-over-after');
  });

  const rect = button.getBoundingClientRect();
  const isAfter = event.clientY > rect.top + rect.height / 2;
  button.classList.add(isAfter ? 'drag-over-after' : 'drag-over-before');
}

function handleModuleDrop(event) {
  event.preventDefault();
  const draggedId = event.dataTransfer.getData('text/plain');
  const sourceId = draggedModuleId || draggedId;
  const targetId = event.currentTarget.dataset.module;
  const rect = event.currentTarget.getBoundingClientRect();
  const placeAfter = event.clientY > rect.top + rect.height / 2;
  clearDragState();

  if (!sourceId || sourceId === targetId) return;

  updateResume((draft) => {
    draft.moduleOrder = moveModuleOrderItem(draft.moduleOrder, sourceId, targetId, placeAfter);
  });
}

function handleModuleDragEnd() {
  clearDragState();
}

function clearDragState() {
  draggedModuleId = '';
  dom.moduleNav.querySelectorAll('.module-item').forEach((item) => {
    item.classList.remove('dragging', 'drag-over-before', 'drag-over-after');
  });
}

function ensureActiveModule() {
  const modules = getOrderedModuleItems(state.resume);
  if (modules.some((module) => module.id === state.activeModule)) return;
  state.activeModule = modules[0]?.id || 'basics';
}

function getActiveModuleItem() {
  return getOrderedModuleItems(state.resume).find((module) => module.id === state.activeModule)
    || getOrderedModuleItems(state.resume)[0]
    || { id: 'basics', key: 'basics', type: 'standard', label: '基本信息' };
}

function educationFields() {
  return [
    { name: 'school', label: '学校' },
    { name: 'major', label: '专业' },
    { name: 'degree', label: '学历' },
    { name: 'start', label: '开始时间' },
    { name: 'end', label: '结束时间' },
  ];
}

function workFields() {
  return [
    { name: 'company', label: '公司' },
    { name: 'role', label: '职位' },
    { name: 'start', label: '开始时间' },
    { name: 'end', label: '结束时间' },
    { name: 'description', label: '工作描述', multiline: true, wide: true },
  ];
}

function projectFields() {
  return [
    { name: 'name', label: '项目名称' },
    { name: 'role', label: '个人角色' },
    { name: 'tech', label: '技术栈' },
    { name: 'start', label: '开始时间' },
    { name: 'end', label: '结束时间' },
    { name: 'description', label: '项目描述', multiline: true, wide: true },
  ];
}

function customExperienceFields() {
  return [
    { name: 'name', label: '名称' },
    { name: 'role', label: '角色/说明' },
    { name: 'start', label: '开始时间' },
    { name: 'end', label: '结束时间' },
    { name: 'description', label: '描述', multiline: true, wide: true },
  ];
}

function emptyEducation() {
  return { school: '', major: '', degree: '', start: '', end: '' };
}

function emptyWork() {
  return { company: '', role: '', start: '', end: '', description: '' };
}

function emptyProject() {
  return { name: '', role: '', tech: '', start: '', end: '', description: '' };
}

function emptyCustom() {
  return {
    id: createCustomSectionId(state.resume.customSections),
    title: '',
    template: 'text',
    content: '',
    items: [],
    entries: [],
    blocks: [],
    image: emptyMediaImage(),
  };
}

function emptyCustomBlock(blocks = []) {
  return {
    id: createCustomSectionId(blocks),
    title: `子板块 ${blocks.length + 1}`,
    template: 'text',
    content: '',
    items: [],
    entries: [],
    image: emptyMediaImage(),
  };
}

function emptyCustomExperience() {
  return { name: '', role: '', start: '', end: '', description: '' };
}

function emptyMediaImage() {
  return { dataUrl: '', name: '', alt: '', caption: '' };
}

function defaultEditableTable() {
  return [['Category', 'Content', 'Notes'], ['', '', '']];
}

function getEditableTable(entry) {
  const table = Array.isArray(entry.table) && entry.table.length ? entry.table : defaultEditableTable();
  const columnCount = Math.max(1, ...table.map((row) => Array.isArray(row) ? row.length : 0));
  return table.map((row) => {
    const cells = Array.isArray(row) ? row.map((cell) => String(cell ?? '')) : [];
    while (cells.length < columnCount) cells.push('');
    return cells;
  });
}

function ensureEditableTable(entry) {
  entry.table = getEditableTable(entry);
  return entry.table;
}

function addTableRow(entry) {
  const table = ensureEditableTable(entry);
  table.push(Array.from({ length: table[0].length }, () => ''));
}

function addTableColumn(entry) {
  ensureEditableTable(entry).forEach((row) => row.push(''));
}

function deleteTableRow(entry, rowIndex) {
  const table = ensureEditableTable(entry);
  if (table.length > 1) table.splice(rowIndex, 1);
}

function deleteTableColumn(entry, columnIndex) {
  const table = ensureEditableTable(entry);
  if (table[0].length > 1) table.forEach((row) => row.splice(columnIndex, 1));
}

function createCustomSectionId(customSections) {
  const usedIds = new Set(customSections.map((section) => section.id));
  let index = customSections.length + 1;
  let id = `custom-${index}`;

  while (usedIds.has(id)) {
    index += 1;
    id = `custom-${index}`;
  }

  return id;
}

function joinRange(start, end) {
  return [start, end].filter(Boolean).join(' - ');
}

function hasEntryText(entry) {
  return Object.values(entry || {}).some(hasText);
}

function hasCustomPreviewContent(entry) {
  if (!entry) return false;
  if (hasText(entry.title)) return true;
  if (entry.template === 'list') return (entry.items || []).some(hasText);
  if (entry.template === 'experience') return (entry.entries || []).some(hasEntryText);
  if (entry.template === 'directory') return (entry.blocks || []).some(hasCustomPreviewContent);
  if (entry.template === 'image') return hasText(entry.image?.dataUrl) || hasText(entry.image?.caption);
  if (entry.template === 'table') return hasMediaTablePreviewContent(entry.table);
  return hasText(entry.content);
}

function hasMediaTablePreviewContent(table = []) {
  const rows = Array.isArray(table)
    ? table.filter(Array.isArray).map((row) => row.map((cell) => String(cell ?? '')))
    : [];
  const bodyHasContent = rows.slice(1).some((row) => row.some(hasText));

  if (bodyHasContent) return true;
  if (isStarterMediaTableHeader(rows[0])) return false;
  if (rows.length !== STARTER_MEDIA_TABLE_ROW_COUNT) return rows.some((row) => row.some(hasText));

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

function hasText(value) {
  return Boolean(String(value || '').trim());
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll('\n', '&#10;');
}
