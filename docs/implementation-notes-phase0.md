# Phase 0 — SDK 公开 API 映射（Scaffold）

状态：**已完成**（仓库内已有完整解决方案；本篇保留为 SDK 映射存档）。

日期：2026-11-09。包基线：nuget.org `MioKit.Sdk` / `MioKit.Extensions` / `MioKit.Webview2` / `MioKit.SourceGenerate` **1.3.1**（模板生成）；`plugin.json` 的 `minSdkVersion` / `minHostVersion` 为 **1.3.0**（设计下限）。未使用反射绕过或私有宿主 DLL。

## 1. `IMioDataProvider.SaveObjectsAsync`

| 项 | 映射 |
|---|---|
| 类型 | `MioKit.Sdk.IMioDataProvider` |
| 签名 | `Task SaveObjectsAsync(IReadOnlyCollection<MioObject> objects, Dictionary<MioObject, Dictionary<IAttachEavProperty, object>>? propertyValues)` |
| 用途 | 主表 + 属性同事务写入；新建节点主事务在挂树前调用。 |
| 参考 | miokit-plugin-core `nodes-and-tree.md` §8；QuickInput `QuickInputUiService.SaveNodeAsync` 传入 `[node]` 与属性字典。 |
| 阻塞 | **无**。公开 API 足够覆盖设计中的“主表+属性一次提交”。 |

## 2. `IMioDataProvider.UpdateParentsAsync`

| 项 | 映射 |
|---|---|
| 类型 | `MioKit.Sdk.IMioDataProvider` |
| 签名 | `Task UpdateParentsAsync(IReadOnlyDictionary<string, string?> parentMap)`（另有单条 `UpdateParentAsync(string objectId, string? parentId)`） |
| 用途 | 仅回写 ParentId；设计要求新建先 `SaveObjectsAsync`（Parent 未挂树），再 `UpdateParentsAsync` 到固定搜索组，最后内存 `SetParent` 发布。 |
| 参考 | miokit-plugin-core `nodes-and-tree.md` §8；设计 `02` / `05`。 |
| 阻塞 | **无**。父关系与主事务分离是公开契约，勿写非公开 Parent setter。 |

## 3. `EavCachePolicy.None`

| 项 | 映射 |
|---|---|
| 类型 | `MioKit.Sdk.EavCachePolicy` |
| 枚举值 | `None`, `Absolute`, `Sliding` |
| 用法 | `EavPropertyBuilder<T>.Create()...WithPolicy(EavCachePolicy.None).Build()` |
| 用途 | 事件序列与 BeforeDelay 等冷字段：每次访问可打库，避免启动/列表预加载整段配置。 |
| 参考 | miokit-plugin-core `extension-properties-advanced.md` §1；工作区 `MioKit.GrabAndMove` / `MioKit.Clipboard` 已用 `None`。 |
| 阻塞 | **无**。勿对详情字段误用 `Absolute`。 |

## 4. 插件绑定任务取消

| 项 | 映射 |
|---|---|
| 类型 | `MioKit.Sdk.IMioTaskService`（插件 Scope 构造注入；勿用根 `MioIoc.Resolve`） |
| 登记 | `Task RunAsync(MioTaskOptions options, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)`（及泛型重载） |
| 取消 | `bool RequestCancellation(Guid taskId)`；`int RequestCancellationAll()` |
| 原因 | `MioTaskCancellationReason`：`Manual`, `Caller`, `Timeout`, `PluginStopping`, `HostStopping` |
| 生命周期 | Runtime 在 `StartAsync` 前打开入口；停止/卸载时关闭入口、请求取消并等待所属任务退出，再执行插件停止逻辑。 |
| 参考 | miokit-plugin-core `managed-tasks.md`、`plugin-core.md`；通用 Context `GetRequiredService`/`TryGetService` 需 1.3.0+。 |
| 阻塞 | **无公开缺口**。实现注意：停止清理阶段不能再向已关闭的任务入口登记新任务；业务循环必须只靠任务 token 退出。 |

## 5. 版本与脚手架记录

- 开发环境：.NET SDK 10.0.402；WebView2 Runtime 157.0.4341.42；pnpm 10.33.0。
- 模板：`miokit-plugin-webview2` via MCP `create_plugin`（临时目录合并，保留 `docs/`）。
- `plugin.json`：`id=com.tinyisle.plugin.key-sender`，`name=模拟按键`，`author=tinyisle`，`category=Productivity`，`minSdkVersion=1.3.0`，`minHostVersion=1.3.0`。
- 包引用：插件 csproj 直接引用 `MioKit.Extensions` / `MioKit.Webview2` / `MioKit.SourceGenerate` **1.3.1**（nuget.org 最新；满足 ≥1.3.0）。`MioKit.Sdk` 由 Extensions 传递引入，无需额外直接引用即可编译使用上述 API。

## 6. 阻塞项汇总

当前阶段未发现必须用反射或私有 API 才能完成设计路径的阻塞。后续实现阶段仍须在真实宿主上验证任务排空顺序、热键注册与 WebView2 运行时行为。
