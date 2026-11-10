using Avalonia;
using Avalonia.Controls;
using KeySender.Services;
using Ti.Avalonia.Shadcn.Controls;

namespace KeySender.Views;

public partial class PluginWebView : ShadWindow
{
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isClosed;
    private ServiceProvider? _bridgeServiceProvider;
    private KeySenderUiService? _uiService;
    private string? _windowSessionId;

    public PluginWebView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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

            DisableWebViewAutofill();

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

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        Loaded -= OnLoaded;
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

    private void DisableWebViewAutofill()
    {
        try
        {
            var profile = WebView.CoreWebView2?.Profile;
            if (profile is null)
            {
                return;
            }

            profile.IsGeneralAutofillEnabled = false;
            profile.IsPasswordAutosaveEnabled = false;
        }
        catch
        {
            // HTML autocomplete=off remains as fallback.
        }
    }

    private void DisposeBridgeServices()
    {
        _bridgeServiceProvider?.Dispose();
        _bridgeServiceProvider = null;
        _uiService = null;
        _windowSessionId = null;
    }


}
