using KeySender.Services;
using Serilog;

namespace KeySender;

public class KeySenderRegister : RegisterBase<KeySenderRegister>
{
    public override void RegisterService(ContainerBuilder builder, IServiceCollection services)
    {
        var plugin = new KeySenderPlugin();

        builder.RegisterInstance(plugin)
            .As<IPlugin>()
            .Keyed<IPlugin>(KeySenderConst.PluginId)
            .As(plugin.GetType())
            .AsSelf();

        builder.RegisterType<KeySenderSearchGroup>().AsSelf().InstancePerDependency();
        builder.RegisterType<KeySenderCategory>().AsSelf().InstancePerDependency();
        builder.RegisterType<KeySenderNode>().AsSelf().InstancePerDependency();

        builder.RegisterType<ConfigurationService>()
            .As<IConfigurationService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HeldKeyLedger>().AsSelf().SingleInstance();
        builder.RegisterType<NativeKeyboardSender>().As<IKeyboardSender>().SingleInstance();
        builder.RegisterType<Win32TargetWindowOps>().As<ITargetWindowOps>().SingleInstance();
        builder.RegisterType<TaskDelayService>().As<IDelayService>().SingleInstance();
        builder.RegisterType<Win32ExternalKeyPrep>().As<IExternalKeyPrep>().SingleInstance();
        builder.RegisterType<TargetWindowService>().AsSelf().SingleInstance();
        builder.RegisterType<ConfigurationRunSource>().As<IRunConfigurationSource>().SingleInstance();
        builder.RegisterType<StopwatchMonotonicClock>().As<IMonotonicClock>().SingleInstance();
        builder.RegisterType<NativeKeyboardHook>().As<IKeyboardHookSource>().SingleInstance();

        builder.Register(c =>
            {
                ILogger? logger = null;
                try
                {
                    logger = c.Resolve<KeySenderPlugin>().Context.Logger;
                }
                catch
                {
                    // Context may be unavailable during early resolve in tests.
                }

                return new ExecutionService(
                    c.Resolve<HeldKeyLedger>(),
                    c.Resolve<IKeyboardSender>(),
                    c.Resolve<TargetWindowService>(),
                    c.Resolve<IRunConfigurationSource>(),
                    c.Resolve<IDelayService>(),
                    c.Resolve<IExternalKeyPrep>(),
                    c.ResolveOptional<IMioTaskService>(),
                    c.ResolveOptional<IGlobalHotKeyService>(),
                    logger);
            })
            .As<IKeySenderExecutionService>()
            .As<IInputActivityGate>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RecordingService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TransferService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<KeySenderUiService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HotKeyCaptureJsService>()
            .AsSelf()
            .SingleInstance();

        services.AddSingleton<TimerJsService>();
        services.AddSingleton<HotKeyCaptureJsService>();
    }
}
