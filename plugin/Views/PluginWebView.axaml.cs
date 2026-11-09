using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KeySender.Services;
using Ti.Avalonia.Shadcn.Controls;

namespace KeySender.Views;

public partial class PluginWebView : ShadWindow
{
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isClosed;
    private bool _forceClose;
    private bool _closePromptOpen;
    private ServiceProvider? _bridgeServiceProvider;
    private KeySenderUiService? _uiService;
    private IKeySenderExecutionService? _execution;
    private string? _windowSessionId;

    public PluginWebView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isInitialized || _isClosed)
        {
            return;
        }

        _isInitialized = true;
        _isInitializing = true;
        KeySenderPlugin? plugin = null;

        try
        {
            var componentContext = KeySenderRegister.Instance.ComponentContext;
            plugin = componentContext.Resolve<KeySenderPlugin>();
            _uiService = componentContext.Resolve<KeySenderUiService>();
            _execution = componentContext.Resolve<IKeySenderExecutionService>();
            var hotKeyCapture = componentContext.Resolve<HotKeyCaptureJsService>();

            _windowSessionId = Guid.NewGuid().ToString("D");
            _uiService.OnWindowOpened();
            _uiService.OpenSession(_windowSessionId);

            var services = new ServiceCollection();
            services.AddSingleton(_uiService);
            services.AddSingleton(hotKeyCapture);
            _bridgeServiceProvider = services.BuildServiceProvider();

            await WebView.EnsureCoreWebView2Async();
            if (_isClosed)
            {
                return;
            }

            await WebView.InitializeBridgeAsync(services, _bridgeServiceProvider);
            if (_isClosed)
            {
                return;
            }

#if DEBUG
            WebView.Source = new Uri("http://localhost:9988");
#else
            var indexHtml = Path.Combine(plugin.Context.PluginPath, "ui", "dist", "index.html");
            WebView.Source = new Uri(indexHtml);
#endif
        }
        catch (Exception exception)
        {
            _isInitialized = false;
            if (!_isClosed)
            {
                (plugin?.Context.Logger ?? MioAppContext.Current.FrameworkLogger)
                    .Error(exception, "初始化模拟按键插件窗口失败");
                Close();
            }
        }
        finally
        {
            _isInitializing = false;
            if (_isClosed)
            {
                DisposeBridgeServices();
            }
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || _isClosed || Design.IsDesignMode)
        {
            return;
        }

        var heldCount = 0;
        try
        {
            heldCount = _execution?.GetHeldKeys().Keys.Count ?? 0;
        }
        catch
        {
            heldCount = 0;
        }

        if (heldCount <= 0 || _closePromptOpen)
        {
            return;
        }

        e.Cancel = true;
        _closePromptOpen = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var choice = await PromptCloseWithHeldKeysAsync(heldCount).ConfigureAwait(true);
                switch (choice)
                {
                    case CloseHeldChoice.ReleaseAndClose:
                        if (_execution is not null)
                        {
                            await _execution.ReleaseAllAsync().ConfigureAwait(true);
                        }

                        _forceClose = true;
                        Close();
                        break;
                    case CloseHeldChoice.KeepAndClose:
                        _forceClose = true;
                        Close();
                        break;
                    default:
                        break;
                }
            }
            finally
            {
                _closePromptOpen = false;
            }
        });
    }

    private async Task<CloseHeldChoice> PromptCloseWithHeldKeysAsync(int heldCount)
    {
        var result = CloseHeldChoice.Return;
        var dialog = new Window
        {
            Title = "关闭确认",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var message = new TextBlock
        {
            Text = $"当前仍有 {heldCount} 个本插件保持的按键。关闭窗口不会取消正在运行的任务。\n\n" +
                   "释放并关闭：先释放保持键再关闭。\n" +
                   "保持并关闭：保留按键状态，宿主入口仍可继续发送抬起。\n" +
                   "返回：不关闭窗口。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16),
        };

        var release = new Button { Content = "释放并关闭", MinWidth = 108, Margin = new Thickness(4) };
        var keep = new Button { Content = "保持并关闭", MinWidth = 108, Margin = new Thickness(4) };
        var back = new Button { Content = "返回", MinWidth = 72, Margin = new Thickness(4) };

        release.Click += (_, _) =>
        {
            result = CloseHeldChoice.ReleaseAndClose;
            dialog.Close();
        };
        keep.Click += (_, _) =>
        {
            result = CloseHeldChoice.KeepAndClose;
            dialog.Close();
        };
        back.Click += (_, _) =>
        {
            result = CloseHeldChoice.Return;
            dialog.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
            Children = { release, keep, back },
        };

        dialog.Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                buttons,
                message,
            },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        await dialog.ShowDialog(this).ConfigureAwait(true);
        return result;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Closed -= OnClosed;

        try
        {
            _uiService?.OnWindowClosed();
        }
        catch
        {
            // best-effort session cleanup
        }

        if (!_isInitializing)
        {
            DisposeBridgeServices();
        }
    }

    private void DisposeBridgeServices()
    {
        _bridgeServiceProvider?.Dispose();
        _bridgeServiceProvider = null;
        _uiService = null;
        _execution = null;
        _windowSessionId = null;
    }

    private enum CloseHeldChoice
    {
        Return,
        ReleaseAndClose,
        KeepAndClose,
    }
}
