# UI 交互阶段实现说明

日期：2026-11-09（按代码刷新）。对应：分类侧栏 + 节点表 + Sheet 编辑、录制主路径、隐藏任务/保持键面板。

## 1. 如何调试

| 路径 | 命令 / 入口 | 说明 |
|---|---|---|
| 浏览器 Mock（默认） | `cd plugin/vue-ui && pnpm dev` → 打开 Vite URL | 无 WebView2；顶栏显示 **Mock**；可走保存确认、录制注入、导入预览、分类 CRUD |
| 宿主 WebView2 DEBUG | `pnpm dev`（端口 **9988**）+ 宿主打开「模拟按键管理」 | `PluginWebView` 导航 `http://localhost:9988` |
| 宿主 Release | `pnpm build` → `plugin/ui/dist` | 加载 `PluginPath/ui/dist/index.html` |

Mock **不能**宣称 Hook / SendInput / 焦点恢复已验证。

## 2. 架构

- Vue facade：`src/api/key-sender.js` → 有 `chrome.webview` + `DotnetBridge` 则 `KeySenderUiService`，否则 `key-sender.mock.js`
- 布局：`ManagerView.vue` — 顶栏 + 左侧 `CategorySidebar`（`all` / `builtin` / `category`）+ `NodeList`；编辑在右侧 `NodeEditSheet`（录制 / 详细 Tabs）
- 节点拖放：MIME `application/x-keysender-node`；拖到用户分类调用 `MoveNodesAsync`；Ctrl+拖为复制
- 录制：停止后仅「应用 / 丢弃」；应用全量替换；间隔默认 0；自动重复 Down 去重
- C#：`KeySenderUiService` + `KeySenderPlugin.OpenPluginConfigWindow` → `ShowUniqueWindow<PluginWebView>`
- 会话：`PluginWebView` 调用 `OpenSession`；前端 `createSessionId()`；`InitializeAsync(allowBind)` 可采纳客户端 id
- 关窗有 held：Avalonia 对话框三选一（释放并关闭 / 保持并关闭 / 返回）；普通关闭不 `ReleaseAll`
- 任务 / 保持键面板不展示；录制被挡住时用「释放全部并开始」对话框（`ReleaseAndRecordDialog`）
- Runs/HeldKeys：前端订阅 `RunsChanged` / `HeldKeysChanged`，后端另有 ~400ms `PollState` 后备；UI 不渲染面板

## 3. 验收对照（UI 相关）

| 项 | 状态 |
|---|---|
| Mock 可走主要交互 | 已实现 |
| 侧栏 + 表格 + Sheet Tabs | 已实现 |
| 指纹确认 / 脏离开 / 导入警告 | Mock + UiService |
| 内置只读 + 复制为自定义 | 已实现 |
| 分类 CRUD / 拖放移动 | 已实现 |
| 组合键助手 | `ShortcutInserter.vue` 已实现 |
| Bridge DTO / 方法列表 | 已实现（见 `METHOD_NAMES` / `EVENT_NAMES`） |
| 真实 Hook / 焦点 / 文件框 | 需在 MioKit 宿主手工验收 |

## 4. 已知缺口

- 搜索结果「查看 / 编辑」经 `RequestOpenNode` 打开管理窗并聚焦；「导出」走宿主无 session 导出
- 管理页不编辑触发热键（列表仅显示 `hotkeyDisplay`；宿主 `HotKeyAction`）
- 无 Runs / HeldKeys 可视化面板（API 已就绪）
- 文件对话框过滤 `*.json`，非 `.miokeys`
- 热键显示可能为原始序列化串，未做宿主美化格式
- 模板附带的 `HotKeyCaptureJsService` / `BridgeTest` 不参与管理主路径
