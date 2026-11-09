# MioKit.KeySender 设计与实现入口

状态：**首版已实现**（C# 后台 + WebView2/Vue 管理页 + 单元测试）。宿主真机端到端验收与正式发布签字仍见 implementation-notes。

日期：2026-11-09。修订：按仓库代码同步实现状态；事件序列模型不变。

## 1. 产品及确定决策

- 工程名称：MioKit.KeySender；中文名：模拟按键。
- 插件 Id：com.tinyisle.plugin.key-sender；作者默认 tinyisle；分类 Productivity。
- C# 承担节点、持久化、原生录制和回放；WebView2 + Vue 提供管理界面。
- 业务节点类型 `KeySenderNode` 直接继承 MioObject；另有搜索组、管理入口与用户分类 `KeySenderCategory`。
- 首批内置“复制、剪切、粘贴”三个节点，动作定义全部只读、不可删除，使用预先固定的实例 GUID；需要自定义时复制为用户节点。
- **持久化的是有序 KeyDown/KeyUp 事件和每条事件前的等待，不是“若干键的集合”。**
- 按录制或编辑顺序原样回放，不自动排序、去重、配对或追加 KeyUp。
- 支持左右修饰键、多普通键、交错按下/释放、长按和重复 KeyDown。
- 节点前等待、事件前等待、节点后等待均为整数 ms，默认 0。
- 不配对事件是可确认的警告，不是默认禁止保存的错误。
- **正常执行结束后保留未释放的按键，直到后续节点或当前节点显式发送 KeyUp。**
- 活动运行失败或取消、手动“释放全部”、插件停止/卸载时清理本插件保持的键；仅取消排队项不改变现有按键状态。
- Hook 和 SendInput 封装由插件自有；节点搜索、别名、固定、触发热键和拖放仍接宿主公开能力。
- 完整事件序列和等待配置按需查询，详情 EAV 使用 None，不预加载全部配置。
- 队列仅保存轻量请求，出队读取当时配置；运行开始后不受编辑影响。
- JSON 分享仅含节点动作定义；本机入口绑定、运行数据与当前保持键状态留在本机。

自有 Hook/发送和正常结束保持按下均来自用户明确要求，优先于技能的默认约定。

### 1.1 实现快照（对照代码）

| 项 | 现状 |
|---|---|
| 工程 | `MioKit.KeySender.slnx`；包引用 MioKit.* **1.3.1**；`minSdkVersion`/`minHostVersion` **1.3.0** |
| 桥接 | `[JsService("KeySenderUiService")]`，方法见 `vue-ui/src/api/key-sender.js` 的 `METHOD_NAMES` |
| 管理页 | 分类侧栏 + 节点表 + `NodeEditSheet`（录制 / 详细）；Mock：`pnpm dev` |
| 单元测试 | `dotnet test` Release：**93 passed** |
| 宿主真机 | Hook / SendInput / 焦点 / 文件框 / 启停泄漏等仍需手工验收 |
| nupkg | 可本地 pack；正式发布签字未完成 |

实现差异与验收记录：

- [implementation-notes-phase0.md](implementation-notes-phase0.md) — SDK API 映射
- [implementation-notes-backend.md](implementation-notes-backend.md) — 后台与测试对照
- [implementation-notes-ui.md](implementation-notes-ui.md) — 管理页与调试路径

## 2. 阅读顺序

1. [产品功能与交互设计](01-产品功能与交互设计.md)：界面、录制、编辑、保存提示和释放入口。
2. [架构与数据模型](02-架构与数据模型.md)：事件结构、冷热数据、服务和持有键账本。
3. [键盘录入与执行设计](03-键盘录入与执行设计.md)：原生事件、时间、录制、回放及故障清理。
4. [WebView2 界面与桥接契约](04-WebView2界面与桥接契约.md)：DTO、方法、警告确认和事件同步。
5. [导入导出格式与持久化](05-导入导出格式与持久化.md)：JSON、预览确认、事务和恢复。
6. [实施步骤与验收测试](06-实施步骤与验收测试.md)：阶段状态、测试项与交付条件。

产品行为以第 01 篇为准，数据以第 02 篇为准，输入执行以第 03 篇为准，桥接以第 04 篇为准，交换文件以第 05 篇为准。实现笔记记录与设计的刻意差异；修订设计时须同步示例、验收与笔记。

## 3. 最小例子

“Ctrl+C”的实际配置是四个事件：

```text
LeftControl KeyDown，前等待 0 ms
KeyC        KeyDown，前等待 50 ms
KeyC        KeyUp，  前等待 30 ms
LeftControl KeyUp，  前等待 20 ms
```

这表示按住 Ctrl，50 ms 后按 C，30 ms 后松开 C，再等 20 ms 松开 Ctrl。

只有 LeftControl KeyDown 的节点也可以保存：保存时提示“结束后左 Ctrl 仍将保持按下”，再次确认后保存。执行成功不会自动释放；另一个 LeftControl KeyUp 节点可以释放它。

“插入组合键”只是编辑快捷方式，生成上述显式事件，不是另一种底层存储。

## 4. 术语与版本

- 事件：KeyDown 或 KeyUp，加稳定按键 token 和 delayBeforeMs。
- 序列：有序的 KeyDown/KeyUp 事件列表。
- 录制片段：一次录制的原始事件及相对间隔，应用到草稿前不写 EAV。
- 草稿：Vue 当前唯一的编辑副本。
- Run：一次从排队到结束的调用，用 runId 标识。
- HeldKey：本插件已提交 Down、尚未提交成功 Up 的逻辑持有状态。
- 持有键账本：插件级轻量键状态，不是节点详情缓存，不是物理键状态的全局真相。
- 触发热键：宿主用于调用节点的配置，与回放事件不同。
- 目标窗口：入口捕获的原窗口及身份，不能分享或持久化 HWND。
- 发送成功：系统接受了预期输入事件，不等于目标软件完成了业务操作。
- 内置节点：固定身份及动作定义的 KeySenderNode；只读的是动作本体，宿主热键、别名、固定及轮盘/手势绑定仍是本机入口偏好。
- 分类：`KeySenderCategory` 下挂用户节点；虚拟 scope「全部」「系统内置」仅用于列表过滤。

固定类型、属性和内置实例的 GUID 清单见[架构与数据模型](02-架构与数据模型.md)第 10 节。这些值已通过 MioKit MCP 生成，实施时直接使用，不能重新随机生成。

序列版本和文件格式版本均采用 **2**，明确区别于先前未发布的组模型草案 v1。遇到 v1 明确拒绝，不能静默按新格式解释。

## 5. 统一首版限制

- 名称 Trim 后 1～100 个 UTF-16 code unit；描述最多 500 个；不接受 NUL。
- 一个节点 1～10000 条事件；eventId 在节点内唯一。
- 每项等待 0～2147483647 ms，累计用 checked Int64。
- 待执行队列最多 64 项，另有 1 项活动运行。
- 列表默认每页 20，上限 100；批量选择仅当前页。
- 导入 UTF-8 文件最多 5 MiB、500 节点、JSON 深度 16。
- 外部按键准备等待最多 2000 ms，目标焦点确认最多 500 ms。
- 录制停止后的键事件排空最多 2000 ms。
- 一个窗口草稿、一个录制片段、一个导入预览；预览 TTL 为 5 分钟。
- 有排队/活动运行，或有本插件保持的键时，不能开始录制。
- 录制会话期间不能提交回放；停止并应用/取消片段后释放预约。
- 最近结束运行最多 50 条轻量记录，不持有事件列表。

## 6. 真实 SDK 与插件契约

已经通过工作区参考、现有插件或本机 MioKit.Sdk 元数据核对的能力：

- MioObject、PluginBase、RegisterBase<T>、IPluginLifecycleComponent。
- IInvokeFeature、ISearchableFeature、IAliasNameFeature、IHotkeyFeature、IPinnedFeature。
- IResultActionProviderFeature、IInvocationPresentationFeature、IRequireTargetWindowFeature。
- IPluginConfigWindowFeature.OpenPluginConfigWindow。
- EAV builder、生成 Get/Set、EavCachePolicy、PreloadPropertySource。
- IMioDataProvider.SaveObjectsAsync、UpdateParentsAsync、DeleteManyAsync 和属性/对象查询。
- InvokeContext、InvocationSnapshot、节点拖放、插件绑定任务和通知。
- IGlobalHotKeyService.Registrations 和 Registered/Disabled/Unregistered 状态。
- MioWebview2、ServiceBridge 和 MioKit.Webview2.Core.JsService。

`KeySenderNode`、`KeyboardSequence`、`HeldKeyLedger`、`KeySenderUiService` 及全部 DTO/错误码是**本插件已实现契约**，不是 SDK 现成类型。错误码权威清单见 `plugin/Validation/ValidationCodes.cs`。

实现基线为从 nuget.org 可还原的 1.3.x 配套包，`minSdkVersion`/`minHostVersion` 至少 1.3.0，并须实际验证目标宿主。不使用私有 SDK 源，不携带宿主共享 DLL。

## 7. 参考来源

实现参照 miokit-plugin-new、miokit-plugin-core、miokit-plugin-webview2；外层窗口按 Avalonia UI 技能。技能路径随执行环境解析，不写成项目依赖。

现有 QuickInput 可参考节点、桥接和保存边界，但不照搬其详情缓存。部分现有 HotKeyCaptureJsService 只有界面状态，没有真实 Hook 录制；管理页不依赖它配置触发热键。HotKeyInfo 有最多 7 键、规范化和集合语义，不能保存本插件事件序列。

- [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)：回调时机、消息循环、链和超时。
- [KBDLLHOOKSTRUCT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct)：原始键、扫描码、注入标志。
- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)：输入提交及当前键态。
- [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)：发送结构。
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)：焦点限制。
- [UnhookWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwindowshookex)：卸钩与回调退出。

## 8. 后续工作

已完成：事件模型 v2、配置/执行/录制/传输服务、分类树、`KeySenderUiService` + Vue 管理页、内置三节点、核心单元测试。

仍待：

1. 真实宿主验收：录制 Hook、SendInput、焦点恢复、热键冲突、启停泄漏与卸载释放（06 中 Host 标记项）。
2. 搜索附加操作「查看 / 编辑 / 导出」已接管理窗深链与宿主导出；复制路径在搜索菜单会自动带警告指纹（无二次确认 UI）。
3. 管理页不展示 Runs/HeldKeys 面板（后端 API 与关窗三选一已有）；按产品需要可补。
4. 导入 Commit 为按节点顺序 `CreateAsync`（非整包单次 `SaveObjectsAsync`）；文件对话框当前过滤 `*.json`。
5. 正式 pack / inspect 签字与发布记录。

不得把 Fake 单元测试宣称成「原生录制 / 跨节点保持 / 卸载释放已在宿主可靠」。
