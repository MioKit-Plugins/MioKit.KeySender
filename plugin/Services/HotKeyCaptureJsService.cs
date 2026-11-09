using CommunityToolkit.Mvvm.ComponentModel;
using MioKit.Webview2.Core;

namespace KeySender.Services;

[JsService("HotKeyCaptureService")]
public partial class HotKeyCaptureJsService : ObservableObject
{
    [ObservableProperty] private bool _isCapturing;

    [ObservableProperty] private string _draftValueText = "0";

    [ObservableProperty] private string _draftDisplay = string.Empty;

    [ObservableProperty] private string _editorStatus = "Initializing";

    [ObservableProperty] private string? _errorMessage;

    public void BeginCapture(string baselineValueText, string? excludeOwnerId)
    {
        DraftValueText = string.IsNullOrWhiteSpace(baselineValueText)
            ? "0"
            : baselineValueText;
        DraftDisplay = DraftValueText == "0" ? string.Empty : DraftValueText;
        EditorStatus = "Initializing";
        ErrorMessage = null;
        IsCapturing = true;
    }

    public void CancelCapture()
    {
        IsCapturing = false;
    }

    public void ClearDraft()
    {
        DraftValueText = "0";
        DraftDisplay = string.Empty;
        EditorStatus = "CheckSuccess";
        ErrorMessage = null;
    }

    public void ValidateDraft()
    {
        EditorStatus = "CheckSuccess";
        ErrorMessage = null;
    }

    public string CommitDraftDisplay()
    {
        IsCapturing = false;
        ValidateDraft();
        return DraftDisplay;
    }
}
