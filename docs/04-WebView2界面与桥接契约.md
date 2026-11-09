# WebView2 界面与桥接契约

本篇 DTO/方法为插件**已实现**桥接契约（类名 `KeySenderUiService`）。使用模板 ServiceBridge，不另建 REST 或任意 postMessage API。前端真相源：`plugin/vue-ui/src/api/key-sender.js` 的 `METHOD_NAMES` / `EVENT_NAMES`；DTO：`plugin/Models/Ui/UiDtos.cs`。

## 1. 前端与窗口边界

Vue 管理页面包括左侧 CategorySidebar（全部 / 系统内置 / 用户分类）、NodeList（表格）、NodeEditSheet（录制/详细 Tabs）、EventList、EventRow、KeyPicker、ShortcutInserter、RecorderPanel、ImportPreview。任务与保持键面板不展示；账本仍由后端维护。节点可拖到用户分类移动，Ctrl+拖为复制。

只保留当前页和一个草稿；RecorderPanel 最多一份片段，应用后释放。键目录和轻量状态可共享，不建立所有节点详情 store。

外层 ShadWindow + PluginWindowBehavior、CanPin=true，单窗口（`PluginWebView`）。会话引导：

1. 宿主打开窗口时 `OpenSession(windowSessionId)`（Avalonia 侧预分配 Guid）。
2. Vue `key-sender.js` 另用 `createSessionId()` 生成客户端 id，请求均携带 `windowSessionId`。
3. `InitializeAsync` 在 `allowBind: true` 时可采纳客户端 id 绑定会话；旧窗口请求不能影响新窗口。

JsService 复用插件 DI 实例；桥接 provider 只登记这些实例。关闭窗口释放自己的 provider/订阅/录制/预览，不释放整个执行服务和账本。

先 setupVueBridge 再挂载。独立浏览器 mock 只能验布局，不得启用真实录制或声明 native 已验证。

## 2. Wire 与通用返回

JSON camelCase，方法 PascalCase。所有方法仅一个必填 request DTO 参数，包含 windowSessionId。返回 Task<ApiResult<T>>，不得用 async void、默认参数、自定义 awaitable 或 JSON CancellationToken。

成功为 ok=true/data/error=null；失败为 ok=false/data=null/error。示例：

```json
{
  "ok": false,
  "data": null,
  "error": {
    "code": "sequence_confirmation_required",
    "message": "序列结束后左 Ctrl 仍将保持按下",
    "fieldPath": "sequence.events[0]",
    "eventIndex": 0,
    "nodeId": null,
    "runId": null,
    "details": {
      "warningFingerprint": "24341a9bc395181487a27ee060c48600cce0076db2938e3b43195bc4b72586ed",
      "warnings": [
        {
          "code": "unclosed_keydown",
          "key": "LeftControl",
          "eventIndex": 0,
          "message": "左 Ctrl 未释放"
        }
      ]
    }
  }
}
```

事件索引从 0，中文显示 +1。details 仅使用本篇定义的诊断结构，不传 native 服务、完整节点、异常对象或堆栈。上例指纹对应前后等待均为 0、仅 LeftControl KeyDown 且事件前等待为 0 的语义内容，按架构篇固定字段顺序和无空白 JSON 计算。

文件对话框取消 code=cancelled，正常退出不弹红色错误。预期业务问题返回 envelope，非预期 Bridge 故障 Promise reject，前端显示重试提示。

## 3. 核心 DTO

NodeListItem：nodeId、name、description、aliasNames、hotkeyDisplay、isPinned、dragText、isBuiltIn、isReadOnly、canEdit、canDelete、categoryId、categoryName。没有 events 和详情等待。内置四个能力字段分别为 true/true/false/false，来自 C# 固定身份目录；用户节点为 false/false/true/true（运行中删除仍需动态预检）。内置节点 categoryId/categoryName 为空。

CategoryListItem：categoryId、name、description、nodeCount、isDefault、canRename、canDelete。

ListRequest：query、scope（`all` | `builtin` | `category`）、categoryId、page、pageSize。搜索仅在当前 scope 内过滤。CreateNodeRequest / CopyNodeRequest 可带可选 categoryId；缺省落到默认分类。

分类 API：GetCategoriesAsync、CreateCategoryAsync、RenameCategoryAsync、DeleteCategoryAsync（级联删节点）、MoveNodesAsync。事件 CategoriesChanged（kind/categoryIds/sequenceNumber）与 NodesChanged 并存。InitializeData 额外返回 categories。

NodeWrite：

```json
{
  "name": "复制",
  "description": "按录制间隔发送 Ctrl+C",
  "beforeDelayMs": 200,
  "afterDelayMs": 300,
  "sequence": {
    "version": 2,
    "events": [
      { "eventId": "d10e44de-0001-4000-8000-000000000001", "type": "keyDown", "key": "LeftControl", "delayBeforeMs": 0 },
      { "eventId": "d10e44de-0001-4000-8000-000000000002", "type": "keyDown", "key": "KeyC", "delayBeforeMs": 50 },
      { "eventId": "d10e44de-0001-4000-8000-000000000003", "type": "keyUp", "key": "KeyC", "delayBeforeMs": 30 },
      { "eventId": "d10e44de-0001-4000-8000-000000000004", "type": "keyUp", "key": "LeftControl", "delayBeforeMs": 20 }
    ]
  }
}
```

NodeDetail 在 NodeWrite 外增加 nodeId 以及 isBuiltIn/isReadOnly/canEdit/canDelete。NodeWrite 不接受这些身份/权限字段；客户端不能通过伪造标志取消内置保护。新建不允许客户端提供节点 Id/父 Id。eventId 仅编辑行身份，示例值不作为固定常量。

ValidationResult：valid、errors[]、warnings[]、warningFingerprint。valid 只表示没有硬错误，有警告时仍可为 true，但保存必须提供确认指纹。警告包含 code/key/eventIndex/message；硬错误可定位 fieldPath。

RecordingSnapshot：recordingId、windowSessionId、state、eventCount、elapsedMs、sequenceNumber、notices[]、error。实时通知不反复带完整列表。

RecordingFragment：recordingId、events[]、notices[]。只有停止后的 GetRecordingFragment 和应用时返回片段，不能每条录制事件推送累计全表。

RunSnapshot：runId、nodeId、nodeName、state、eventIndex、eventCount、queuePosition、submittedAt/startedAt/finishedAt、isCancellationRequested、error。排队时 eventCount=null。

HeldKeysSnapshot：sequenceNumber、keys[]、isReleasing、senderAvailable。每键含 key、display、sourceRunId、sourceNodeId、heldSince、uncertain；不含事件详情。

## 4. 初始化与节点方法

- InitializeAsync：返回 displayName、limits、keyCatalog、categories、nodes(PageResult)、runs(RunPage)、heldKeys(HeldKeysSnapshot)。所有内容只含轻量数据。
- GetListAsync：query、scope、categoryId、page、pageSize，返回 items/total/page/pageSize/sequenceNumber。只搜索名称/描述/别名，且限定在当前 scope。
- GetCategoriesAsync / CreateCategoryAsync / RenameCategoryAsync / DeleteCategoryAsync / MoveNodesAsync：分类侧栏 CRUD、级联删除与节点移动。
- GetDetailAsync：nodeId，返回 NodeDetail，按需读取。
- ValidateDraftAsync：detail(NodeWrite)，返回 ValidationResult；无持久化和详情缓存。
- CreateNodeAsync：detail、acknowledgedWarningFingerprint(string或null)，返回新 NodeListItem。
- UpdateNodeAsync：nodeId、detail、acknowledgedWarningFingerprint，返回 NodeListItem；不覆盖宿主偏好。
- CopyNodeAsync：nodeId、acknowledgedWarningFingerprint，返回新 NodeListItem。服务分析已保存源内容，未确认警告时返回 fingerprint，再以同一来源重新验证后复制。
- DeleteNodesAsync：nodeIds、releaseHeldKeys(bool，必填)。存在未结束 Run 返回 node_in_use；来源节点有保持键且未明确允许释放时返回 held_keys_confirmation_required。允许时先串行 ReleaseAll，成功后再删除。
- 删除完整成功返回 deletedIds；底层部分失败返回 delete_incomplete 和 details.deletedIds/remainingIds。
- 所有节点方法验证归属和挂树状态。未发布节点返回 node_not_ready。

GetDetail 可查看内置的完整固定定义；Update 内置节点直接返回 builtin_read_only，不作无改动成功。Delete 的预检如果含任何内置 Id，整批返回 builtin_read_only，必须发生在 ReleaseAll、删除和写库之前。Copy 内置节点允许且生成普通用户节点，返回权限字段不再是只读。客户端列表/编辑/批量按钮使用能力字段，但保护必须由后端身份校验实现。

### 保存确认握手

前端先调用 ValidateDraft 取得警告，弹二次确认。用户点“仍然保存”后将 warningFingerprint 作为 acknowledgedWarningFingerprint 提交。

服务在真正保存时重新计算，匹配才允许；否则返回 sequence_confirmation_required 及新警告，不提交。未修改事件/时间而只改名称时指纹可保持，确认不是权限凭证。

无警告时传 null。确认不自动新增 KeyUp、不持久化确认 token，也不让客户端绕过硬验证。复制和导入必须有同样明确的确认步骤。

## 5. 录制方法

- BeginRecordingAsync：无额外字段。原子预约、检查运行/保持键，安装 Hook，返回 RecordingSnapshot。草稿不由此改变。
- StopRecordingAsync：recordingId，冻结并有限排空后返回 review 快照；重复停止返回同一结果。
- GetRecordingAsync：recordingId，返回标量快照，用于失去事件后的状态重建。
- GetRecordingFragmentAsync：recordingId，仅 review 返回 RecordingFragment。
- ApplyRecordingAsync：recordingId，仅 review、非空且 native 已退出时返回片段并结束会话；前端据此全量替换草稿。服务不接收整个草稿。
- CancelRecordingAsync：recordingId，排空并丢弃片段，返回 cancelled 快照，幂等。

Apply 返回后前端生成/保留唯一 eventId，并全量替换草稿事件；所有 delayBeforeMs 以 0 写入草稿（录制服务亦默认 0），用户可在详细 Tab 修改。

关闭/失焦由 C# 层兜底；停止/取消后的排空事件不进入片段。未知/已关闭会话不能应用到新窗口草稿。

## 6. 任务与释放方法

- GetRunsAsync：返回 RunPage，包含 sequenceNumber、active[]、recent[]（最多50）。
- CancelRunAsync：runId，返回 cancellationRequested。排队取消只影响该项；活动取消按全局清理策略处理。
- GetHeldKeysAsync：返回 HeldKeysSnapshot，不查 EAV。
- ReleaseHeldKeysAsync：无额外字段。串行关闭接收、取消所有任务、等待 native 退出并释放账本，返回最新 HeldKeysSnapshot。失败返回 input_cleanup_failed，同时快照仍反映剩余键和 senderAvailable=false。

释放是明确的用户操作，不是隐藏在窗口关闭/Run 成功中的逻辑。清理请求开始后即使网页关闭，也要完成有限 native 清理，不因 session token 取消中断到半途。

页面不提供接受任意 HWND 或当前草稿直接回放的接口。实际执行使用宿主 InvokeContext，保持原目标策略。

## 7. 文件方法

- PreviewImportAsync：显示本机文件选择，返回 previewId/expiresAt/itemCount/canImport/warningFingerprint/items[]。
- PreviewItem：itemIndex/name/description/eventCount/totalDelayMs/errors[]/warnings[]，不向预览页推全部事件。
- CommitImportAsync：previewId、acknowledgedWarningFingerprint，核对文件哈希及警告确认，返回 createdItems。
- ReleaseImportPreviewAsync：previewId，幂等释放。
- ExportNodesAsync：nodeIds，先保存对话框，再流式导出已保存配置；返回 exportedCount/fileName。

文件路径仅留 C#，不提供任意路径读写网页 API。录制未结束时不打开文件对话框。已提交但发布/后续节点失败返回 persisted=true/createdIds，禁止盲目重放创建。

**当前文件对话框：** 打开/保存均过滤 `*.json`，建议保存名 `keysender-export.json`。逻辑格式仍为 `format: miokit.key-sender` / `formatVersion: 2`（与扩展名无关）。导入 Commit 按节点顺序 `CreateAsync`，见 05 篇。

## 8. 事件与乱序

使用 event EventHandler<T>：

- RecordingChanged：会话、标量 RecordingSnapshot；不要每条事件附完整片段。
- RunsChanged：轻量 RunPage。
- HeldKeysChanged：HeldKeysSnapshot。
- NodesChanged：kind、nodeIds、sequenceNumber。
- CategoriesChanged：kind、categoryIds、sequenceNumber。

nodes、runs、heldKeys、每个 recording 各自递增 sequenceNumber。状态与序号在同一锁/gate 取快照；初始化先订阅并暂存，再取快照，重放较新事件。detail 请求用独立 generation。

录制普通 eventCount/elapsed 通知最多 10 Hz（`RecordingService`），终态立即通知。**HeldKeys/Runs 另有约 400ms 的 `PollState` 轮询后备**（页面仍可不渲染面板）。列表/任务变化也只传轻量内容，不持有已关闭 WebView。清理、持有键变更不能依赖页面是否订阅。

创建/复制/删除/导入提交不自动重放重试。断桥后先刷新状态确认结果，不能把失去响应理解为事务未提交。

## 9. 错误码与警告码

权威源：`plugin/Validation/ValidationCodes.cs`。硬错误 code（实现清单）：

- validation_failed、invalid_configuration、unsupported_sequence_version、unsupported_key、unsupported_key_combination。
- empty_sequence、unknown_event_type、duplicate_event_id、invalid_delay、event_count_out_of_range、invalid_name、invalid_description、delay_total_overflow。
- node_not_found、node_not_ready、node_in_use、delete_incomplete、persistence_failed、execution_not_ready、window_session_closed。
- category_not_found、category_read_only、invalid_category。
- builtin_read_only 用于拒绝内置修改/删除；builtin_identity_conflict 用于初始化固定 Id 类型或归属冲突，不能静默覆盖。
- sequence_confirmation_required、held_keys_confirmation_required。
- recording_busy、execution_busy、keys_held、recording_unavailable、recording_not_ready、recording_cleanup_failed。
- queue_full、target_missing、target_invalid、target_focus_failed、keys_release_timeout。
- output_hotkey_conflict、input_send_failed、input_cleanup_failed、sender_unavailable、plugin_stopping。
- invalid_format、unsupported_format_version、file_too_large、import_limit_exceeded、import_preview_expired、import_changed、import_invalid、file_access_failed。
- cancelled 用于正常文件对话框退出。

不使用 `recursive_trigger`；输出与宿主热键冲突统一为 `output_hotkey_conflict`。

静态可确认警告：unclosed_keydown、unmatched_keyup、repeated_keydown。

录制 notice：recording_interrupted、recording_limit_reached、recording_release_timeout、recording_unsupported_key。notice 不伪装成完整录制成功，也不禁止用户审阅已保留片段。

details 结构仅按 code 使用：确认指纹/警告；删除已删/剩余；持久化后失败的 persisted/createdIds。native 错误写日志，不传任意异常对象。

## 10. 构建与主题

源码 plugin/vue-ui，构建输出 plugin/ui/dist，相对 base，运行基于 Context.PluginPath。先初始化 native/Bridge 再导航。

沿模板语义主题色和现有组件；按 app-ready 同步色板，不依赖 Vue 未就绪时调用其 API。外层窗口关闭后不重新导航恢复旧会话。

最终在实际 MioWebview2 验证 Bridge、焦点、录制、文件框和释放。独立浏览器 mock 无法证明这些能力可用。调试路径见 [implementation-notes-ui.md](implementation-notes-ui.md)。

## 11. 已实现方法清单

与 `METHOD_NAMES` 一致：InitializeAsync、GetListAsync、GetDetailAsync、ValidateDraftAsync、CreateNodeAsync、UpdateNodeAsync、CopyNodeAsync、DeleteNodesAsync、GetCategoriesAsync、CreateCategoryAsync、RenameCategoryAsync、DeleteCategoryAsync、MoveNodesAsync、BeginRecordingAsync、StopRecordingAsync、GetRecordingAsync、GetRecordingFragmentAsync、ApplyRecordingAsync、CancelRecordingAsync、GetRunsAsync、CancelRunAsync、GetHeldKeysAsync、ReleaseHeldKeysAsync、PreviewImportAsync、CommitImportAsync、ReleaseImportPreviewAsync、ExportNodesAsync。

事件：RecordingChanged、RunsChanged、HeldKeysChanged、NodesChanged、CategoriesChanged（JS 侧 `On{Name}.subscribe`）。
