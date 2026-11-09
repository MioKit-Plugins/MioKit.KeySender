// =============================================================================
// Vue Bridge 测试服务 — 复制到你的 Avalonia 后端项目并注册
// =============================================================================
//
// 依赖 NuGet:
//   CommunityToolkit.Mvvm
//
// ⚠️ 两个服务都必须注册，否则 JS 端会报错：
//   BridgeTestService         → not found / metadata error
//   BridgeTestSessionService  → Service 'BridgeTestSessionService' not found
//
// 注册示例（服务名 = 类名，须与 JS 一致）:
//   bridgeHost.RegisterSingleton<BridgeTestService>();
//   bridgeHost.RegisterTransient<BridgeTestSessionService>();
//
// JS 对应关系:
//   useService('BridgeTestService')
//   useTransientService('BridgeTestSessionService', () => [sessionId])
//
// 注意:
//   - 不要使用 [RelayCommand]，JS 调用的是 Methods.Increment() 等普通 public 方法
//   - 不要使用带默认值的参数（如 delayMs = 1000），Bridge 元数据反射可能越界
//   - 属性用 SetProperty 显式声明，避免源生成器成员干扰元数据扫描
//
// =============================================================================

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MioKit.Webview2.Core;

namespace Miokit;

/// <summary>
/// Singleton 测试服务
/// </summary>
[JsService("BridgeTestService")]
public class BridgeTestService : ObservableObject
{
    private string _title = "Bridge 测试服务";
    private int _counter;
    private string _message = "等待操作...";
    private bool _isEnabled = true;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public int Counter
    {
        get => _counter;
        set => SetProperty(ref _counter, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>ObservableCollection — JS: vm.Collections.Items</summary>
    public ObservableCollection<BridgeTestItem> Items { get; } = new();

    public BridgeTestService()
    {
        Items.Add(new BridgeTestItem { Id = 1, Name = "初始项 A" });
        Items.Add(new BridgeTestItem { Id = 2, Name = "初始项 B" });
    }

    // -------------------------------------------------------------------------
    // 普通 public 方法 — JS: await vm.Methods.Increment()
    // -------------------------------------------------------------------------

    public void Increment()
    {
        Counter++;
        Message = $"Counter = {Counter}";
    }

    public void Decrement()
    {
        Counter--;
        Message = $"Counter = {Counter}";
    }

    public void Reset()
    {
        Counter = 0;
        Message = "已重置";
    }

    public string Echo(string text)
    {
        Message = $"Echo: {text}";
        return text;
    }

    public string DelayedEcho(string text, int delayMs)
    {
        Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            Message = $"DelayedEcho: {text}";
        }).GetAwaiter().GetResult();
        return text;
    }

    public void ThrowError()
    {
        throw new InvalidOperationException("Bridge 错误测试：这是预期的异常");
    }

    public void AddItem(string name)
    {
        var nextId = Items.Count > 0 ? Items.Max(x => x.Id) + 1 : 1;
        Items.Add(new BridgeTestItem { Id = nextId, Name = name });
        Message = $"已添加: {name}";
    }

    public void RemoveItem(int id)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            Message = $"未找到 ID={id}";
            return;
        }

        Items.Remove(item);
        Message = $"已删除 ID={id}";
    }

    // -------------------------------------------------------------------------
    // 事件 — JS: useServiceEvent(vm, 'MessageReceived', handler)
    // -------------------------------------------------------------------------

    public event EventHandler<string>? MessageReceived;

    public void FireEvent(string payload)
    {
        MessageReceived?.Invoke(this, payload);
        Message = $"事件已触发: {payload}";
    }
}

/// <summary>
/// Transient 测试服务 — 构造参数: (string sessionId)
/// </summary>
[JsService("BridgeTestSessionService")]
public class BridgeTestSessionService : ObservableObject
{
    private string _sessionId;
    private string _label;

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public BridgeTestSessionService(string sessionId)
    {
        _sessionId = sessionId;
        _label = $"会话 {sessionId}";
    }

    public string Ping()
    {
        return $"Pong from {SessionId}";
    }
}

public class BridgeTestItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}