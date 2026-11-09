# 后台实现验收与差异

日期：2026-11-09（按代码刷新）。范围：C# 服务、单元/伪造测试、桥接与管理窗入口；**宿主真机端到端**仍待手工验收。

## 1. 构建与测试

| 项 | 结果 |
|---|---|
| `dotnet build MioKit.KeySender.slnx -c Release` | 0 error（存在 MessagePack NU1902、WindowsBase MSB3277 传递警告） |
| `dotnet test tests/MioKit.KeySender.Tests.csproj -c Release` | **93 passed / 0 failed** |
| 包基线 | `MioKit.Extensions` / `MioKit.Webview2` / `MioKit.SourceGenerate` **1.3.1**；`minSdkVersion`/`minHostVersion` **1.3.0** |

主要测试类：`SequenceAnalyzerTests`、`ConfigHelpersTests`、`ConfigurationServiceBuiltinTests`、`ConfigurationServiceCategoryTests`、`ExecutionServiceTests`、`RecordingServiceTests`、`TransferServiceTests`、`NativeKeyboardSenderLayoutTests`、`KeyTokenCatalogTests`、`BuiltInKeyCatalogTests`、`BuiltinSequenceFactoryTests`、`KeySenderConstTests`、`ScaffoldSmokeTests`。

## 2. 已交付后台能力

- 领域：`KeyboardSequence` v2、`KeyTokenCatalog`、`SequenceAnalyzer`（配对警告 + CAD 轨迹 + 指纹）
- 固定身份：`KeySenderConst` 与设计 02§10 GUID 一致；搜索组/管理入口/`key-sender-default-category` 字符串 Id
- 配置：`ConfigurationService`（gate、指纹确认、内置只读、分类 CRUD/迁移、复制命名）、EAV `None` 冷字段、`BuiltinSequenceFactory`
- 节点：`KeySenderSearchGroup`、`KeySenderCategory`、`KeySenderNode`（Invoke → Execution）、`OpenKeySenderManagerNode`
- 执行：`ExecutionService` FIFO 64+1、`HeldKeyLedger`、成功不清账本、失败/取消/`ReleaseAll`/Stop 清理、`IInputActivityGate` 录制预约
- Native：`NativeKeyboardSender`（`SendInput(1)`）、`NativeKeyboardHook`（生产）；测试用 Fake
- 录制：`RecordingService` + 可注入 Hook 源；自动重复 Down 去重；间隔默认 0；Stop 后排空 Up 不追加
- 传输：`TransferPackageParser` / `TransferService`（formatVersion=2，预览 TTL，警告指纹；Commit 按节点 `CreateAsync`）
- 桥接：`KeySenderUiService`（`[JsService]`）；窗口 `PluginWebView` → `OpenPluginConfigWindow` / `ShowUniqueWindow`
- 目标：`TargetWindowService`、`Win32TargetWindowOps`、`Win32ExternalKeyPrep`

## 3. 对照 06 验收清单（摘要）

图例：**UT** = 已有单元测试；**Impl** = 代码已实现、待宿主真机；**UI** = 管理页相关；**Host** = 需真实宿主/硬件；**Stub** = 入口占位未接。

### 事件与确认（T01–T14）

| ID | 状态 | 说明 |
|---|---|---|
| T01–T04, T07–T10 | UT | `SequenceAnalyzerTests` / `ConfigHelpersTests` |
| T05–T06 | UT 部分 | 目录与序列形状；左右/小键盘 token 有目录测试 |
| T11 | Impl+UT 部分 | 分析不自动补 Up；编辑区警告属 UI |
| T12 | UI | `ShortcutInserter.vue` 已实现 |
| T13–T14 | Impl+UT 部分 | 管理页复制需确认；**搜索菜单复制会自动带 fingerprint**（无对话框） |

### 配置与持久化（T15–T26）

| ID | 状态 | 说明 |
|---|---|---|
| T15–T20 | Impl 设计对齐 | None/轻量队列已编码；千节点计数/Store 干净缓存需 Host |
| T21–T26 | Impl | `ConfigurationService` 路径已写；完整 SaveObjects/父关系失败恢复需 Host |
| 内置拒绝 | UT | `ConfigurationServiceBuiltinTests` |
| 分类 | UT+UI | `ConfigurationServiceCategoryTests` + 侧栏 |

### 回放与持有键（T27–T45）

| ID | 状态 | 说明 |
|---|---|---|
| T27, T29–T32, T36 部分, T38–T39, T43 | UT | `ExecutionServiceTests` + FakeKeyboardSender |
| T28, T33–T35, T37, T40–T42, T44–T45 | Impl | 等待/准备/封闭发送器等已编码；真 SendInput/焦点竞态属 Host |

### 录制（T46–T59）

| ID | 状态 | 说明 |
|---|---|---|
| T46–T49, T51, T54 部分, T57, T59 部分 | UT | `RecordingServiceTests` + FakeKeyboardHookSource |
| T50, T52–T53, T55–T56, T58 | Impl | `NativeKeyboardHook` 存在；真机 Hook/失焦/10000 上限需 Host |
| 草稿替换 | UI | Apply 返回片段，前端全量替换草稿 |

### 窗口 / 桥接（T60–T69）

| ID | 状态 | 说明 |
|---|---|---|
| T60–T65 | Impl | TargetWindow / 热键冲突可选服务；需 Host |
| T66 | Host | 特殊键实际验证 |
| T67 | UI+Impl | Bridge 已实现；Mock ≠ native 通过 |
| T68 | UI+Impl | Session / 脏离开 / 关窗三选一已实现；需 Host 断桥验证 |
| T69 | UI 部分 | **无 HeldKeys 面板**（刻意）；关窗三选一 + `ReleaseAndRecordDialog` + `GetHeldKeysAsync` |

### 文件（T70–T81）

| ID | 状态 | 说明 |
|---|---|---|
| T70–T75 核心, T81 | UT | `TransferServiceTests` |
| T76–T78 | Impl | 预览 TTL/指纹/消费一次；对话框属 UI/Host |
| T79–T80 | Impl 差异 | 导出先在内存收集 `NodeWrite` 再写用户选择的 `*.json` 路径；非「临时文件原子替换」理想路径 |

### 生命周期 / 竞态 / 内置（T82–T103）

| ID | 状态 | 说明 |
|---|---|---|
| T82–T83 | Impl | Stop → `ReleaseAll`；Host 任务排空需实测 |
| T84–T88, T89–T92 | Host | 窗口保持、泄漏、竞态 |
| T93–T102 | UT+Impl | Const/工厂/只读路径已测；启停幂等与冲突需 Host |
| T103 | Impl | 统一 ExecutionService；无剪贴板旁路 |

**结论：** 不得把 Fake 测试宣称成「原生录制 / 跨节点保持 / 卸载释放已在宿主可靠」。服务语义与 UT 已就绪；Host 为剩余主线。

## 4. 实现差异与已知缺口

1. **导入 Commit**：按节点顺序调用 `ConfigurationService.CreateAsync`，非整包单次 `SaveObjectsAsync`；部分已创建后失败返回 `persisted`/`createdIds` 并消费预览。
2. **导出 I/O**：`TransferService.ExportAsync` 逐节点 `GetDetailAsync` 后内存组装再写流；`KeySenderUiService` 文件框过滤 `*.json`（建议名 `keysender-export.json`），逻辑格式仍为 `miokit.key-sender` v2。
3. **错误码**：权威源 `ValidationCodes.cs`；无 `recursive_trigger`，冲突统一 `output_hotkey_conflict`。另有 `category_*`、`empty_sequence`、`execution_not_ready` 等。
4. **搜索附加操作**：`KeySenderNodeActions` 中查看/编辑/导出仍为 `DeferredUiAction` → `NotSupportedException`；复制/删除已接 `ConfigurationService`。
5. **Runs/HeldKeys 面板**：桥接与轮询（~400ms）已有，管理页不渲染；释放入口为录制阻挡对话框与 Avalonia 关窗三选一。
6. **测试缝**：`IRunConfigurationSource`、`IKeyboardHookSource`、`IDelayService`、`ITargetWindowOps`、`IExternalKeyPrep` — 生产有 Win32/Native，CI 默认 Fake。
7. **Scaffold 传递警告**（MessagePack / WindowsBase）未处理。

## 5. 后续

- 保持 `dotnet test` 绿
- 在 MioKit 宿主补 Host 标记 T 项与 06 阶段 6
- 按需补搜索菜单「打开管理器/编辑」与 Runs 面板
