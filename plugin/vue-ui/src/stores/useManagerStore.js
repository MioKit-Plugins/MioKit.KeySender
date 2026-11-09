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
    sidebarCollapsed: false,
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
    draftWarnings: [],
    draftErrors: [],
    saving: false,

    heldKeys: {
      sequenceNumber: 0,
      keys: [],
      isReleasing: false,
      senderAvailable: true,
    },
    runs: {
      sequenceNumber: 0,
      active: [],
      recent: [],
    },

    recording: null,
    recordingFragment: null,
    recordingBusy: false,

    dialogs: {
      dirtyLeave: false,
      confirmSequence: false,
      deleteConfirm: false,
      heldDelete: false,
      importPreview: false,
      releaseAndRecord: false,
      deleteCategoryConfirm: false,
    },
    dirtyLeaveAction: null,
    confirmContext: null,
    importPreview: null,
    pendingDeleteIds: [],

    _unsubs: [],
  }),

  getters: {
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
    heldCount(state) {
      return state.heldKeys?.keys?.length ?? 0;
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
        api.on('HeldKeysChanged', (snap) => this.onHeldKeysChanged(snap)),
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
        this.heldKeys = data.heldKeys ?? this.heldKeys;
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

    toggleSidebarCollapsed() {
      this.sidebarCollapsed = !this.sidebarCollapsed;
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
            acknowledgedWarningFingerprint: null,
          }));
          if (!res.ok) {
            if (res.error?.code === 'sequence_confirmation_required') {
              this.confirmContext = {
                mode: 'copy',
                nodeId,
                categoryId: targetCategoryId,
                fingerprint: res.error.details?.warningFingerprint,
                warnings: res.error.details?.warnings ?? [],
              };
              this.dialogs.confirmSequence = true;
              return;
            }
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
      if (nodeId === this.selectedNodeId && this.draft?.nodeId === nodeId) return;
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'select', nodeId };
        this.dialogs.dirtyLeave = true;
        return;
      }
      await this.selectNode(nodeId);
    },

    async selectNode(nodeId) {
      this.selectedNodeId = nodeId;
      if (!nodeId) {
        this.draft = null;
        this.draftBaseline = '';
        this.draftWarnings = [];
        this.draftErrors = [];
        return;
      }
      const generation = (this.draftGeneration += 1);
      this.draftLoading = true;
      try {
        const api = getKeySenderApi();
        const res = await api.GetDetailAsync(withSession({ nodeId }));
        if (generation !== this.draftGeneration) return;
        if (!res.ok) {
          toast.error('加载详情失败', { description: res.error?.message });
          return;
        }
        this.draft = { ...res.data };
        this.draftBaseline = draftFingerprint(this.draft);
        this.draftWarnings = [];
        this.draftErrors = [];
        await this.validateDraftLocal();
      } finally {
        if (generation === this.draftGeneration) this.draftLoading = false;
      }
    },

    async requestNewNode() {
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
    },

    setEvents(events) {
      if (!this.draft || this.draftReadOnly) return;
      this.draft.sequence = {
        version: this.draft.sequence?.version ?? 2,
        events,
      };
      this.validateDraftLocal();
    },

    async validateDraftLocal() {
      if (!this.draft) {
        this.draftWarnings = [];
        this.draftErrors = [];
        return null;
      }
      const api = getKeySenderApi();
      const res = await api.ValidateDraftAsync(withSession({
        detail: toNodeWrite(this.draft),
      }));
      if (!res.ok) return null;
      this.draftErrors = res.data.errors ?? [];
      this.draftWarnings = res.data.warnings ?? [];
      return res.data;
    },

    async saveDraft({ continueAfter = false } = {}) {
      if (!this.draft || this.saving) return false;
      if (this.draftReadOnly && this.draft.nodeId) {
        toast.message('内置节点只读');
        return false;
      }
      this.saving = true;
      try {
        const validation = await this.validateDraftLocal();
        if (!validation) return false;
        if (!validation.valid) {
          toast.error('无法保存', {
            description: validation.errors?.[0]?.message ?? '存在硬错误',
          });
          return false;
        }
        if (validation.warnings?.length) {
          this.confirmContext = {
            mode: this.draft.nodeId ? 'update' : 'create',
            fingerprint: validation.warningFingerprint,
            warnings: validation.warnings,
            continueAfter,
          };
          this.dialogs.confirmSequence = true;
          return false;
        }
        return await this.commitSave(null, continueAfter);
      } finally {
        this.saving = false;
      }
    },

    async commitSave(acknowledgedWarningFingerprint, continueAfter = false) {
      const api = getKeySenderApi();
      const detail = toNodeWrite(this.draft);
      this.saving = true;
      try {
        let res;
        if (this.draft.nodeId) {
          res = await api.UpdateNodeAsync(withSession({
            nodeId: this.draft.nodeId,
            detail,
            acknowledgedWarningFingerprint: acknowledgedWarningFingerprint ?? null,
          }));
        } else {
          res = await api.CreateNodeAsync(withSession({
            detail,
            categoryId: this.effectiveCreateCategoryId,
            acknowledgedWarningFingerprint: acknowledgedWarningFingerprint ?? null,
          }));
        }

        if (!res.ok) {
          if (res.error?.code === 'sequence_confirmation_required') {
            this.confirmContext = {
              mode: this.draft.nodeId ? 'update' : 'create',
              fingerprint: res.error.details?.warningFingerprint,
              warnings: res.error.details?.warnings ?? [],
              continueAfter,
            };
            this.dialogs.confirmSequence = true;
            return false;
          }
          toast.error('保存失败', { description: res.error?.message });
          return false;
        }

        toast.success('已保存配置');
        this.draft.nodeId = res.data.nodeId;
        this.draft.isBuiltIn = res.data.isBuiltIn;
        this.draft.isReadOnly = res.data.isReadOnly;
        this.draft.canEdit = res.data.canEdit;
        this.draft.canDelete = res.data.canDelete;
        this.selectedNodeId = res.data.nodeId;
        this.draftBaseline = draftFingerprint(this.draft);
        await this.refreshList();
        if (continueAfter) await this.resumeDirtyLeave();
        return true;
      } finally {
        this.saving = false;
      }
    },

    async confirmSequenceSave() {
      const ctx = this.confirmContext;
      this.dialogs.confirmSequence = false;
      if (!ctx) return;
      if (ctx.mode === 'copy') {
        await this.commitCopy(ctx.nodeId, ctx.fingerprint);
        return;
      }
      if (ctx.mode === 'import') {
        await this.commitImport(ctx.fingerprint);
        return;
      }
      await this.commitSave(ctx.fingerprint, ctx.continueAfter);
      this.confirmContext = null;
    },

    cancelSequenceConfirm() {
      this.dialogs.confirmSequence = false;
      this.confirmContext = null;
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
    },

    requestCloseEditor() {
      if (this.recording) {
        toast.message('请先停止或取消录制');
        return;
      }
      if (this.isDirty) {
        this.dirtyLeaveAction = { type: 'close' };
        this.dialogs.dirtyLeave = true;
        return;
      }
      this.closeEditor();
    },

    closeEditor() {
      this.draft = null;
      this.draftBaseline = '';
      this.draftWarnings = [];
      this.draftErrors = [];
      this.selectedNodeId = null;
      this.recording = null;
      this.recordingFragment = null;
    },

    async dirtyLeaveSave() {
      const ok = await this.saveDraft({ continueAfter: true });
      if (ok) {
        /* commitSave handles continue */
      }
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
      const api = getKeySenderApi();
      const res = await api.CopyNodeAsync(withSession({
        nodeId,
        categoryId: categoryId ?? this.effectiveCreateCategoryId,
        acknowledgedWarningFingerprint: null,
      }));
      if (!res.ok) {
        if (res.error?.code === 'sequence_confirmation_required') {
          this.confirmContext = {
            mode: 'copy',
            nodeId,
            categoryId: categoryId ?? this.effectiveCreateCategoryId,
            fingerprint: res.error.details?.warningFingerprint,
            warnings: res.error.details?.warnings ?? [],
          };
          this.dialogs.confirmSequence = true;
          return;
        }
        toast.error('复制失败', { description: res.error?.message });
        return;
      }
      toast.success('已复制为自定义节点');
      await this.refreshCategories();
      await this.refreshList();
      await this.selectNode(res.data.nodeId);
    },

    async commitCopy(nodeId, fingerprint) {
      const api = getKeySenderApi();
      const categoryId = this.confirmContext?.categoryId ?? this.effectiveCreateCategoryId;
      const res = await api.CopyNodeAsync(withSession({
        nodeId,
        categoryId,
        acknowledgedWarningFingerprint: fingerprint,
      }));
      this.confirmContext = null;
      if (!res.ok) {
        toast.error('复制失败', { description: res.error?.message });
        return;
      }
      toast.success('已复制为自定义节点');
      await this.refreshCategories();
      await this.refreshList();
      await this.selectNode(res.data.nodeId);
    },

    requestDelete(ids) {
      this.pendingDeleteIds = ids?.length ? ids : [...this.selectedIds];
      if (!this.pendingDeleteIds.length) return;
      this.dialogs.deleteConfirm = true;
    },

    async confirmDelete({ releaseHeldKeys = false } = {}) {
      const api = getKeySenderApi();
      const ids = this.pendingDeleteIds;
      this.dialogs.deleteConfirm = false;
      this.dialogs.heldDelete = false;
      const res = await api.DeleteNodesAsync(withSession({
        nodeIds: ids,
        releaseHeldKeys,
      }));
      if (!res.ok) {
        if (res.error?.code === 'held_keys_confirmation_required') {
          this.dialogs.heldDelete = true;
          return;
        }
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
      const api = getKeySenderApi();
      this.recordingBusy = true;
      try {
        const res = await api.BeginRecordingAsync(withSession());
        if (!res.ok) {
          const code = res.error?.code;
          if (code === 'keys_held' || code === 'execution_busy') {
            this.dialogs.releaseAndRecord = true;
            return;
          }
          toast.error('无法开始录制', { description: res.error?.message });
          return;
        }
        this.recording = res.data;
        this.recordingFragment = null;
      } finally {
        this.recordingBusy = false;
      }
    },

    cancelReleaseAndRecord() {
      this.dialogs.releaseAndRecord = false;
    },

    async confirmReleaseAndRecord() {
      this.dialogs.releaseAndRecord = false;
      await this.releaseHeldKeys();
      await this.beginRecording();
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
        const frag = await api.GetRecordingFragmentAsync(withSession({
          recordingId: this.recording.recordingId,
        }));
        if (frag.ok) this.recordingFragment = frag.data;

        // 停止后直接全量替换草稿，不再经过「应用」步骤。
        if (this.recording?.state === 'review' && !this.draftReadOnly) {
          const count = this.recordingFragment?.events?.length
            ?? this.recording?.eventCount
            ?? 0;
          if (count > 0) {
            await this.doApplyRecording();
          } else {
            await this.cancelRecording();
            toast.message('未录到按键');
          }
        }
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
      const res = await api.ApplyRecordingAsync(withSession({ recordingId }));
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
      toast.message('已写入草稿');
    },

    injectMockKey(type, key) {
      const api = getKeySenderApi();
      api.injectMockKey?.(type, key);
    },

    onRecordingChanged(snap) {
      this.recording = snap;
    },

    onRunsChanged(page) {
      if (!page) return;
      if ((page.sequenceNumber ?? 0) < (this.runs.sequenceNumber ?? 0)) return;
      this.runs = page;
    },

    onHeldKeysChanged(snap) {
      if (!snap) return;
      if ((snap.sequenceNumber ?? 0) < (this.heldKeys.sequenceNumber ?? 0)) return;
      this.heldKeys = snap;
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

    async releaseHeldKeys() {
      const api = getKeySenderApi();
      const res = await api.ReleaseHeldKeysAsync(withSession());
      if (!res.ok) {
        toast.error('释放失败', { description: res.error?.message });
        if (res.error?.details) {
          /* keep panel usable */
        }
        const held = await api.GetHeldKeysAsync(withSession());
        if (held.ok) this.heldKeys = held.data;
        return;
      }
      this.heldKeys = res.data;
      toast.success('已停止后续任务并释放保持键');
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

    async commitImport(fingerprint = null) {
      if (!this.importPreview) return;
      const api = getKeySenderApi();
      const res = await api.CommitImportAsync(withSession({
        previewId: this.importPreview.previewId,
        acknowledgedWarningFingerprint: fingerprint,
      }));
      if (!res.ok) {
        if (res.error?.code === 'sequence_confirmation_required') {
          this.confirmContext = {
            mode: 'import',
            fingerprint: res.error.details?.warningFingerprint,
            warnings: res.error.details?.warnings ?? [],
          };
          this.dialogs.confirmSequence = true;
          return;
        }
        toast.error('导入失败', { description: res.error?.message });
        return;
      }
      this.dialogs.importPreview = false;
      this.importPreview = null;
      this.confirmContext = null;
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
