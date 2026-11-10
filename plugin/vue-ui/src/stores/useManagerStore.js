import { defineStore } from 'pinia';
import { toast } from 'vue-sonner';
import {
  LIST_SCOPE_ALL,
  LIST_SCOPE_BUILTIN,
  LIST_SCOPE_CATEGORY,
  getKeySenderApi,
  isKeySenderMock,
  withSession,
} from '@/api/key-sender.js';
import { emptyDraft, newEventId, toNodeWrite } from '@/api/types.js';

const validationTimers = new WeakMap();

function draftFingerprint(draft) {
  if (!draft) return '';
  return JSON.stringify(toNodeWrite(draft));
}

/** Normalize Initialize / GetCategories payloads into unique CategoryListItem[]. */
function normalizeCategoryItems(payload) {
  let list = [];
  if (Array.isArray(payload)) {
    list = payload;
  } else if (Array.isArray(payload?.items)) {
    list = payload.items;
  } else if (Array.isArray(payload?.Items)) {
    list = payload.Items;
  }

  const seen = new Set();
  const result = [];
  for (const raw of list) {
    if (!raw || typeof raw !== 'object') continue;
    const categoryId = (raw.categoryId ?? raw.CategoryId ?? '').trim();
    if (!categoryId || seen.has(categoryId)) continue;
    seen.add(categoryId);
    result.push({
      categoryId,
      name: raw.name ?? raw.Name ?? '',
      description: raw.description ?? raw.Description ?? '',
      nodeCount: raw.nodeCount ?? raw.NodeCount ?? 0,
      canRename: raw.canRename ?? raw.CanRename ?? true,
      canDelete: raw.canDelete ?? raw.CanDelete ?? true,
    });
  }
  return result;
}

export const useManagerStore = defineStore('keySenderManager', {
  state: () => ({
    ready: false,
    loading: false,
    isMock: false,
    displayName: '按键发送',
    limits: { maxEvents: 10000, maxPageSize: 100, defaultPageSize: 20, maxNameLength: 100 },
    keyCatalog: [],

    query: '',
    page: 1,
    pageSize: 20,
    total: 0,
    nodes: [],
    selectedIds: [],
    selectedNodeId: null,

    listScope: LIST_SCOPE_ALL,
    selectedCategoryId: null,
    categories: [],
    categoryDialog: {
      open: false,
      mode: 'create',
      categoryId: null,
      name: '',
      description: '',
    },
    pendingDeleteCategoryId: null,

    draft: null,
    draftBaseline: '',
    draftLoading: false,
    draftGeneration: 0,
    draftValidationSequence: 0,
    draftWarnings: [],
    draftErrors: [],
    saving: false,

    runs: {
      sequenceNumber: 0,
      active: [],
      recent: [],
    },

    recording: null,
    recordingFragment: null,
    recordingBusy: false,
    recordingApplyPromise: null,

    dialogs: {
      dirtyLeave: false,
      deleteConfirm: false,
      importPreview: false,
      deleteCategoryConfirm: false,
    },
    dirtyLeaveAction: null,
    importPreview: null,
    pendingDeleteIds: [],

    _unsubs: [],
  }),

  getters: {
    recordingInProgress(state) {
      return !!(state.recording || state.recordingBusy || state.recordingApplyPromise);
    },
    keyCatalogGroups(state) {
      const groups = new Map();
      for (const item of state.keyCatalog) {
        const group = item.group || '其它';
        if (!groups.has(group)) groups.set(group, []);
        groups.get(group).push(item);
      }
      return [...groups.entries()];
    },
    isDirty(state) {
      if (!state.draft) return false;
      return draftFingerprint(state.draft) !== state.draftBaseline;
    },
    selectedNodes(state) {
      return state.nodes.filter((n) => state.selectedIds.includes(n.nodeId));
    },
    canBatchDelete(state) {
      const selected = state.nodes.filter((n) => state.selectedIds.includes(n.nodeId));
      if (!selected.length) return false;
      return selected.every((n) => n.canDelete && !n.isBuiltIn);
    },
    batchDeleteBlockedReason(state) {
      const selected = state.nodes.filter((n) => state.selectedIds.includes(n.nodeId));
      if (!selected.length) return '未选择节点';
      if (selected.some((n) => n.isBuiltIn)) return '选择中包含内置节点，不能批量删除';
      return '';
    },
    draftReadOnly(state) {
      return !state.draft || state.draft.isReadOnly || state.draft.isBuiltIn || !state.draft.canEdit;
    },
    canCreateNode(state) {
      return state.listScope !== LIST_SCOPE_BUILTIN;
    },
    effectiveCreateCategoryId(state) {
      if (state.listScope === LIST_SCOPE_CATEGORY && state.selectedCategoryId) {
        return state.selectedCategoryId;
      }
      // 「全部」等非分类 scope：不指定分类，后端挂到搜索组（未分类）
      return null;
    },
    selectedCategory(state) {
      if (state.listScope !== LIST_SCOPE_CATEGORY || !state.selectedCategoryId) return null;
      return state.categories.find((c) => c.categoryId === state.selectedCategoryId) ?? null;
    },
    canEditSelectedCategory(state) {
      return state.listScope === LIST_SCOPE_CATEGORY && !!state.selectedCategoryId;
    },
    canDeleteSelectedCategory(state) {
      return state.listScope === LIST_SCOPE_CATEGORY && !!state.selectedCategoryId;
    },
  },

  actions: {
    async bootstrap() {
      const api = getKeySenderApi();
      this.isMock = isKeySenderMock();
      this._unsubs.forEach((u) => u?.());
      this._unsubs = [
        api.on('RecordingChanged', (snap) => this.onRecordingChanged(snap)),
        api.on('RunsChanged', (page) => this.onRunsChanged(page)),
        api.on('NodesChanged', async () => {
          await this.refreshCategories();
          await this.refreshList();
        }),
        api.on('CategoriesChanged', () => this.refreshCategories()),
        api.on('FocusNodeRequested', (payload) => {
          const nodeId = payload?.nodeId;
          if (!nodeId || !this.ready) return;
          void this.requestSelectNode(nodeId);
        }),
      ];

      this.loading = true;
      try {
        const res = await api.InitializeAsync(withSession());
        if (!res.ok) {
          toast.error('初始化失败', { description: res.error?.message });
          return;
        }
        const data = res.data;
        this.displayName = data.displayName ?? '按键发送';
        this.limits = { ...this.limits, ...(data.limits ?? {}) };
        this.keyCatalog = data.keyCatalog ?? [];
        this.categories = normalizeCategoryItems(data.categories);
        this.applyListPage(data.nodes);
        this.runs = data.runs ?? this.runs;
        this.ready = true;
        if (data.focusNodeId) {
          await this.selectNode(data.focusNodeId);
        }
      } catch (e) {
        toast.error('无法连接服务', { description: e?.message ?? String(e) });
      } finally {
        this.loading = false;
      }
    },

    dispose() {
      this.cancelDraftValidation();
      this.draftGeneration++;
      this._unsubs.forEach((u) => u?.());
      this._unsubs = [];
    },

    applyListPage(page) {
      if (!page) return;
      this.nodes = page.items ?? [];
      this.total = page.total ?? 0;
      this.page = page.page ?? this.page;
      this.pageSize = page.pageSize ?? this.pageSize;
    },

    async refreshList() {
      const api = getKeySenderApi();
      const res = await api.GetListAsync(withSession({
        query: this.query,
        scope: this.listScope,
        categoryId: this.listScope === LIST_SCOPE_CATEGORY ? this.selectedCategoryId : null,
        page: this.page,
        pageSize: this.pageSize,
      }));
      if (!res.ok) {
        toast.error('刷新列表失败', { description: res.error?.message });
        return;
      }
      this.applyListPage(res.data);
      this.selectedIds = this.selectedIds.filter((id) =>
        this.nodes.some((n) => n.nodeId === id));
    },

    async refreshCategories() {
      const api = getKeySenderApi();
      const res = await api.GetCategoriesAsync(withSession());
      if (!res.ok) {
        toast.error('刷新分类失败', { description: res.error?.message });
        return;
      }
      this.categories = normalizeCategoryItems(res.data);
      if (
        this.listScope === LIST_SCOPE_CATEGORY
        && this.selectedCategoryId
        && !this.categories.some((c) => c.categoryId === this.selectedCategoryId)
      ) {
        await this.selectScope(LIST_SCOPE_ALL);
      }
    },

    async setQuery(query) {
      this.query = query;
      this.page = 1;
      this.selectedIds = [];
      await this.refreshList();
    },

    async setPage(page) {
      this.page = page;
      this.selectedIds = [];
      await this.refreshList();
    },

    async selectScope(scope, categoryId = null) {
      this.listScope = scope;
      this.selectedCategoryId = scope === LIST_SCOPE_CATEGORY ? categoryId : null;
      this.page = 1;
      this.selectedIds = [];
      this.query = '';
      await this.refreshList();
    },

    openCreateCategoryDialog() {
      this.categoryDialog = {
        open: true,
        mode: 'create',
        categoryId: null,
        name: '',
        description: '',
      };
    },

    openRenameCategoryDialog() {
      const category = this.selectedCategory;
      if (!category) return;
      this.categoryDialog = {
        open: true,
        mode: 'rename',
        categoryId: category.categoryId,
        name: category.name,
        description: category.description ?? '',
      };
    },

    closeCategoryDialog() {
      this.categoryDialog.open = false;
    },

    async submitCategoryDialog() {
      const api = getKeySenderApi();
      const name = this.categoryDialog.name.trim();
      if (!name) {
        toast.error('请输入分类名称');
        return;
      }
      if (this.categoryDialog.mode === 'create') {
        const res = await api.CreateCategoryAsync(withSession({
          name,
          description: this.categoryDialog.description.trim(),
        }));
        if (!res.ok) {
          toast.error('创建分类失败', { description: res.error?.message });
          return;
        }
        toast.success('已创建分类');
        this.categoryDialog.open = false;
        await this.refreshCategories();
        await this.selectScope(LIST_SCOPE_CATEGORY, res.data.categoryId);
        return;
      }

      const res = await api.RenameCategoryAsync(withSession({
        categoryId: this.categoryDialog.categoryId,
        name,
        description: this.categoryDialog.description.trim(),
      }));
      if (!res.ok) {
        toast.error('重命名失败', { description: res.error?.message });
        return;
      }
      toast.success('已更新分类');
      this.categoryDialog.open = false;
      await this.refreshCategories();
    },

    requestDeleteCategory() {
      if (!this.canDeleteSelectedCategory) return;
      this.pendingDeleteCategoryId = this.selectedCategoryId;
      this.dialogs.deleteCategoryConfirm = true;
    },

    async confirmDeleteCategory() {
      const api = getKeySenderApi();
      const categoryId = this.pendingDeleteCategoryId;
      this.dialogs.deleteCategoryConfirm = false;
      this.pendingDeleteCategoryId = null;
      if (!categoryId) return;
      const res = await api.DeleteCategoryAsync(withSession({ categoryId }));
      if (!res.ok) {
        toast.error('删除分类失败', { description: res.error?.message });
        return;
      }
      toast.success(`已删除分类及其 ${res.data?.deletedIds?.length ?? 0} 个节点`);
      if (this.selectedNodeId && (res.data?.deletedIds ?? []).includes(this.selectedNodeId)) {
        this.closeEditor();
      }
      await this.refreshCategories();
      await this.selectScope(LIST_SCOPE_ALL);
    },

    async dropNodesToCategory(targetCategoryId, { nodeIds, copy }) {
      if (!targetCategoryId || !nodeIds?.length) return;
      const api = getKeySenderApi();
      if (copy) {
        for (const nodeId of nodeIds) {
          const res = await api.CopyNodeAsync(withSession({
            nodeId,
            categoryId: targetCategoryId,
          }));
          if (!res.ok) {
              toast.error('复制失败', { description: res.error?.message });
            return;
          }
        }
        toast.success(`已复制 ${nodeIds.length} 个节点到分类`);
        await this.refreshCategories();
        await this.refreshList();
        return;
      }

      const movable = nodeIds.filter((id) => {
        const node = this.nodes.find((n) => n.nodeId === id);
        return node && !node.isBuiltIn;
      });
      if (!movable.length) {
        toast.message('内置节点只能按住 Ctrl 拖拽复制');
        return;
      }
      const res = await api.MoveNodesAsync(withSession({
        nodeIds: movable,
        targetCategoryId,
      }));
      if (!res.ok) {
        toast.error('移动失败', { description: res.error?.message });
        return;
      }
      toast.success(`已移动 ${movable.length} 个节点`);
      await this.refreshCategories();
      await this.refreshList();
    },

    toggleSelect(nodeId, checked) {
      if (checked) {
        if (!this.selectedIds.includes(nodeId)) this.selectedIds.push(nodeId);
      } else {
        this.selectedIds = this.selectedIds.filter((id) => id !== nodeId);
      }
    },

    toggleSelectAll(checked) {
      this.selectedIds = checked ? this.nodes.map((n) => n.nodeId) : [];
    },

    async requestSelectNode(nodeId) {
      if (!this.canLeaveRecording()) return;
      if (nodeId === this.selectedNodeId && this.draft?.nodeId === nodeId) return;
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'select', nodeId };
        this.dialogs.dirtyLeave = true;
        return;
      }
      await this.selectNode(nodeId);
    },

    async selectNode(nodeId) {
      if (!this.canLeaveRecording()) return;
      this.cancelDraftValidation();
      const generation = (this.draftGeneration += 1);
      this.selectedNodeId = nodeId;
      this.draft = null;
      this.draftBaseline = '';
      this.draftWarnings = [];
      this.draftErrors = [];
      if (!nodeId) {
        this.draftLoading = false;
        return;
      }
      this.draftLoading = true;
      try {
        const api = getKeySenderApi();
        const res = await api.GetDetailAsync(withSession({ nodeId }));
        if (generation !== this.draftGeneration) return;
        if (!res.ok) {
          this.selectedNodeId = null;
          toast.error('加载详情失败', { description: res.error?.message });
          return;
        }
        this.draft = { ...res.data };
        this.draftBaseline = draftFingerprint(this.draft);
        this.draftWarnings = [];
        this.draftErrors = [];
        this.scheduleDraftValidation();
      } catch (error) {
        if (generation === this.draftGeneration) {
          this.selectedNodeId = null;
          toast.error('加载详情失败', { description: error?.message ?? String(error) });
        }
      } finally {
        if (generation === this.draftGeneration) this.draftLoading = false;
      }
    },

    async requestNewNode() {
      if (!this.canLeaveRecording()) return;
      if (!this.canCreateNode) {
        toast.message('系统内置分类只读，请复制为自定义节点');
        return;
      }
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'new' };
        this.dialogs.dirtyLeave = true;
        return;
      }
      this.startNewDraft();
    },

    startNewDraft() {
      if (!this.canLeaveRecording()) return;
      this.cancelDraftValidation();
      this.draftGeneration++;
      this.draftLoading = false;
      this.selectedNodeId = null;
      this.draft = emptyDraft({ name: '新节点' });
      this.draftBaseline = draftFingerprint(emptyDraft({ name: '__unset__' }));
      this.draftWarnings = [];
      this.draftErrors = [];
      this.markDirtyBaselineEmpty();
    },

    markDirtyBaselineEmpty() {
      // Force dirty until first save for new drafts
      this.draftBaseline = '';
    },

    patchDraft(partial) {
      if (!this.draft || this.draftReadOnly) return;
      Object.assign(this.draft, partial);
      this.scheduleDraftValidation();
    },

    setEvents(events) {
      if (!this.draft || this.draftReadOnly) return;
      this.draft.sequence = {
        version: this.draft.sequence?.version ?? 2,
        events,
      };
      this.scheduleDraftValidation();
    },

    cancelDraftValidation() {
      clearTimeout(validationTimers.get(this));
      validationTimers.delete(this);
      this.draftValidationSequence++;
    },

    scheduleDraftValidation() {
      this.cancelDraftValidation();
      validationTimers.set(this, setTimeout(() => {
        validationTimers.delete(this);
        void this.validateDraftLocal().catch(() => {});
      }, 180));
    },

    async validateDraftLocal() {
      this.cancelDraftValidation();
      if (!this.draft) {
        this.draftWarnings = [];
        this.draftErrors = [];
        return null;
      }
      const draft = this.draft;
      const sequence = this.draftValidationSequence;
      const api = getKeySenderApi();
      const res = await api.ValidateDraftAsync(withSession({
        detail: toNodeWrite(draft),
      }));
      if (sequence !== this.draftValidationSequence || draft !== this.draft) return null;
      if (!res.ok) return null;
      this.draftErrors = res.data.errors ?? [];
      this.draftWarnings = res.data.warnings ?? [];
      return res.data;
    },

    async saveDraft({ continueAfter = false } = {}) {
      if (!this.draft || this.saving || this.draftLoading) return false;
      if (!this.canLeaveRecording()) return false;
      if (this.draftReadOnly && this.draft.nodeId) {
        toast.message('内置节点只读');
        return false;
      }
      const api = getKeySenderApi();
      const draft = this.draft;
      const generation = this.draftGeneration;
      const detail = toNodeWrite(draft);
      const submittedFingerprint = draftFingerprint(detail);
      const nodeId = draft.nodeId;
      const categoryId = this.effectiveCreateCategoryId;
      const leaveAction = continueAfter ? this.dirtyLeaveAction : null;
      const isCurrentEditor = () => this.draft === draft && this.draftGeneration === generation;
      // Create/Update perform authoritative validation. Background validation is editor feedback
      // and must never cancel a save just because its response has been superseded.
      this.cancelDraftValidation();
      this.saving = true;
      try {
        let res;
        if (nodeId) {
          res = await api.UpdateNodeAsync(withSession({
            nodeId,
            detail,
          }));
        } else {
          res = await api.CreateNodeAsync(withSession({
            detail,
            categoryId,
          }));
        }

        if (!res.ok) {
          if (isCurrentEditor() && draftFingerprint(draft) === submittedFingerprint && res.error?.details) {
            this.draftErrors = res.error.details.errors ?? [];
            this.draftWarnings = res.error.details.warnings ?? [];
          }
          toast.error('保存失败', { description: res.error?.message });
          return false;
        }

        if (isCurrentEditor()) {
          const persisted = {
            ...detail,
            name: res.data.name ?? detail.name,
            description: res.data.description ?? detail.description,
          };
          // Preserve edits made while the request was pending. Only the submitted DTO is saved.
          if (draft.name === detail.name) draft.name = persisted.name;
          if (draft.description === detail.description) draft.description = persisted.description;
          for (const field of ['nodeId', 'isBuiltIn', 'isReadOnly', 'canEdit', 'canDelete']) {
            if (res.data[field] !== undefined) draft[field] = res.data[field];
          }
          this.selectedNodeId = res.data.nodeId;
          this.draftBaseline = draftFingerprint(persisted);
          if (!this.isDirty) {
            this.cancelDraftValidation();
            this.draftErrors = [];
            this.draftWarnings = [];
          }
        }
        toast.success('已保存配置');
        try { await this.refreshList(); }
        catch (error) {
          toast.error('配置已保存，但列表刷新失败', { description: error?.message ?? String(error) });
        }
        if (leaveAction && isCurrentEditor() && this.dirtyLeaveAction === leaveAction) {
          if (!this.isDirty) await this.resumeDirtyLeave();
          else toast.message('保存期间有新的修改，请再次保存后继续');
        }
        return true;
      } catch (error) {
        toast.error('保存失败', { description: error?.message ?? String(error) });
        return false;
      } finally {
        this.saving = false;
      }
    },

    async resumeDirtyLeave() {
      const action = this.dirtyLeaveAction;
      this.dirtyLeaveAction = null;
      this.dialogs.dirtyLeave = false;
      if (!action) return;
      if (action.type === 'select') await this.selectNode(action.nodeId);
      if (action.type === 'new') this.startNewDraft();
      if (action.type === 'import') await this.openImport();
      if (action.type === 'close') this.closeEditor();
      if (action.type === 'copy') await this.copyNode(action.nodeId, action.categoryId);
    },

    canLeaveRecording() {
      if (!this.recordingInProgress) return true;
      toast.message('请先停止或取消录制');
      return false;
    },

    requestCloseEditor() {
      if (!this.canLeaveRecording()) return;
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'close' };
        this.dialogs.dirtyLeave = true;
        return;
      }
      this.closeEditor();
    },

    closeEditor() {
      this.cancelDraftValidation();
      this.draftGeneration++;
      this.draftLoading = false;
      this.draft = null;
      this.draftBaseline = '';
      this.draftWarnings = [];
      this.draftErrors = [];
      this.selectedNodeId = null;
      this.recording = null;
      this.recordingFragment = null;
    },

    async dirtyLeaveSave() {
      await this.saveDraft({ continueAfter: true });
    },

    dirtyLeaveDiscard() {
      this.draftBaseline = draftFingerprint(this.draft);
      this.resumeDirtyLeave();
    },

    dirtyLeaveStay() {
      this.dialogs.dirtyLeave = false;
      this.dirtyLeaveAction = null;
    },

    async copySelectedOrDraft() {
      const nodeId = this.draft?.nodeId || this.selectedNodeId;
      if (!nodeId) return;
      await this.copyNode(nodeId);
    },

    async copyNode(nodeId, categoryId = null) {
      if (!this.canLeaveRecording()) return;
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'copy', nodeId, categoryId };
        this.dialogs.dirtyLeave = true;
        return;
      }
      const generation = this.draftGeneration;
      const api = getKeySenderApi();
      const res = await api.CopyNodeAsync(withSession({
        nodeId,
        categoryId: categoryId ?? this.effectiveCreateCategoryId,
      }));
      if (!res.ok) {
        toast.error('复制失败', { description: res.error?.message });
        return;
      }
      toast.success('已复制为自定义节点');
      await this.refreshCategories();
      await this.refreshList();
      if (generation === this.draftGeneration) await this.requestSelectNode(res.data.nodeId);
    },

    requestDelete(ids) {
      this.pendingDeleteIds = ids?.length ? ids : [...this.selectedIds];
      if (!this.pendingDeleteIds.length) return;
      this.dialogs.deleteConfirm = true;
    },

    async confirmDelete() {
      const api = getKeySenderApi();
      const ids = this.pendingDeleteIds;
      this.dialogs.deleteConfirm = false;
      const res = await api.DeleteNodesAsync(withSession({
        nodeIds: ids,
      }));
      if (!res.ok) {
        toast.error('删除失败', { description: res.error?.message });
        return;
      }
      toast.success(`已删除 ${res.data.deletedIds?.length ?? 0} 个节点`);
      if (ids.includes(this.selectedNodeId)) {
        this.selectedNodeId = null;
        this.draft = null;
        this.draftBaseline = '';
      }
      this.selectedIds = [];
      this.pendingDeleteIds = [];
      await this.refreshList();
    },

    async beginRecording() {
      if (this.draftReadOnly || this.draftLoading || this.recordingInProgress) return;
      const api = getKeySenderApi();
      this.recordingBusy = true;
      try {
        const res = await api.BeginRecordingAsync(withSession());
        if (!res.ok) {
          toast.error('无法开始录制', { description: res.error?.message });
          return;
        }
        this.recording = res.data;
        this.recordingFragment = null;
      } finally {
        this.recordingBusy = false;
        if (this.recording?.state === 'review') await this.finishRecording();
      }
    },

    async stopRecording() {
      if (!this.recording?.recordingId) return;
      const api = getKeySenderApi();
      this.recordingBusy = true;
      try {
        const res = await api.StopRecordingAsync(withSession({
          recordingId: this.recording.recordingId,
        }));
        if (!res.ok) {
          toast.error('停止录制失败', { description: res.error?.message });
          return;
        }
        this.recording = res.data;
        await this.finishRecording();
      } finally {
        this.recordingBusy = false;
      }
    },

    async cancelRecording() {
      if (!this.recording?.recordingId) {
        this.recording = null;
        this.recordingFragment = null;
        return;
      }
      const api = getKeySenderApi();
      await api.CancelRecordingAsync(withSession({
        recordingId: this.recording.recordingId,
      }));
      this.recording = null;
      this.recordingFragment = null;
    },

    async applyRecording() {
      if (!this.recording?.recordingId || this.draftReadOnly) return;
      await this.doApplyRecording();
    },

    async doApplyRecording() {
      const api = getKeySenderApi();
      const recordingId = this.recording.recordingId;
      const draft = this.draft;
      const generation = this.draftGeneration;
      const res = await api.ApplyRecordingAsync(withSession({ recordingId }));
      if (draft !== this.draft || generation !== this.draftGeneration) return;
      if (!res.ok) {
        toast.error('应用录制失败', { description: res.error?.message });
        return;
      }
      const incoming = (res.data.events ?? []).map((e) => ({
        ...e,
        eventId: e.eventId || newEventId(),
        delayBeforeMs: 0,
      }));
      const max = this.limits.maxEvents ?? 10000;
      if (incoming.length > max) {
        toast.error('录制事件超过上限', { description: `上限 ${max} 条` });
        this.recording = null;
        this.recordingFragment = null;
        return;
      }
      this.setEvents(incoming);
      this.recording = null;
      this.recordingFragment = null;
    },

    injectMockKey(type, key) {
      const api = getKeySenderApi();
      api.injectMockKey?.(type, key);
    },

    onRecordingChanged(snap) {
      if (!snap) return;
      if (this.recording?.recordingId === snap.recordingId
        && (snap.sequenceNumber ?? 0) < (this.recording.sequenceNumber ?? 0)) return;
      this.recording = ['idle', 'cancelled', 'failed'].includes(snap.state) ? null : snap;
      if (snap.state === 'review' && !this.recordingBusy) void this.finishRecording();
    },

    async finishRecording() {
      if (this.recordingApplyPromise) return this.recordingApplyPromise;
      if (this.recording?.state !== 'review' || this.draftReadOnly) return;
      this.recordingApplyPromise = (async () => {
        try {
          if (this.recording.eventCount > 0) await this.doApplyRecording();
          else {
            await this.cancelRecording();
            toast.message('未录到按键');
          }
        } catch (error) {
          toast.error('应用录制失败', { description: error?.message ?? String(error) });
        }
      })();
      try { await this.recordingApplyPromise; }
      finally { this.recordingApplyPromise = null; }
    },

    onRunsChanged(page) {
      if (!page) return;
      if ((page.sequenceNumber ?? 0) < (this.runs.sequenceNumber ?? 0)) return;
      this.runs = page;
    },

    async cancelRun(runId) {
      const api = getKeySenderApi();
      const res = await api.CancelRunAsync(withSession({ runId }));
      if (!res.ok) {
        toast.error('取消失败', { description: res.error?.message });
        return;
      }
      toast.message('已请求取消');
      const runs = await api.GetRunsAsync(withSession());
      if (runs.ok) this.runs = runs.data;
    },

    async openImport() {
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'import' };
        this.dialogs.dirtyLeave = true;
        return;
      }
      const api = getKeySenderApi();
      const res = await api.PreviewImportAsync(withSession());
      if (!res.ok) {
        if (res.error?.code === 'cancelled') return;
        toast.error('导入预览失败', { description: res.error?.message });
        return;
      }
      this.importPreview = res.data;
      this.dialogs.importPreview = true;
    },

    async commitImport() {
      if (!this.importPreview) return;
      const api = getKeySenderApi();
      const res = await api.CommitImportAsync(withSession({
        previewId: this.importPreview.previewId,
      }));
      if (!res.ok) {
        toast.error('导入失败', { description: res.error?.message });
        return;
      }
      this.dialogs.importPreview = false;
      this.importPreview = null;
      toast.success(`已导入 ${res.data.createdItems?.length ?? 0} 个节点`);
      await this.refreshCategories();
      await this.refreshList();
    },

    async cancelImport() {
      const api = getKeySenderApi();
      if (this.importPreview?.previewId) {
        await api.ReleaseImportPreviewAsync(withSession({
          previewId: this.importPreview.previewId,
        }));
      }
      this.dialogs.importPreview = false;
      this.importPreview = null;
    },

    async exportSelected() {
      const ids = this.selectedIds.length
        ? this.selectedIds
        : (this.selectedNodeId ? [this.selectedNodeId] : []);
      if (!ids.length) {
        toast.message('请先选择要导出的节点');
        return;
      }
      const api = getKeySenderApi();
      const res = await api.ExportNodesAsync(withSession({ nodeIds: ids }));
      if (!res.ok) {
        if (res.error?.code === 'cancelled') return;
        toast.error('导出失败', { description: res.error?.message });
        return;
      }
      toast.success(`已导出 ${res.data.exportedCount} 项`, {
        description: res.data.fileName,
      });
    },

  },
});
