namespace KeySender.Services;

[JsService("Timer")]
public partial class TimerJsService : ObservableObject
{
    [ObservableProperty]
    private bool isRunning;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            IsRunning = false;
        });
    }
}
