using StdUnit.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StdUnit.Tags.Rx;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows;
using WPFDemo.Tags;
using WPFDemo.Tags.Logicets;

namespace WPFDemo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public IServiceProvider? Root { get; private set; }
    internal ITagsProjectCtrl Ctrl { get; private set; } = null!;

    /// <summary>
    /// 观测 ITagsProject
    /// </summary>
    internal IObservable<ITagsProject?> ObservableProject { get; private set; } = null!;

    /// <summary>
    /// Project 流的连接句柄（Connect 到 hot 事件源），随进程生命周期释放
    /// </summary>
    private IDisposable? _projectConnection;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ServiceCollection services = ConfigureServiceCollections();
        this.Root = services.BuildServiceProvider();
        this.Ctrl = this.Root.GetRequiredService<ITagsProjectCtrl>();
        this.Ctrl.OnStartingException = ex =>
        {
            MessageBox.Show($"Tags处理启动异常:{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.FromResult(true);
        };

        // 上游 ObserveStartedOrStopped() 是 hot 事件，启动事件可能早于窗口订阅就发完：
        // 1) 在 Ctrl.StartPollAsync 之前先 .Connect()，确保不会漏掉启动事件；
        // 2) Replay(1) 缓存最近一个项目，后订阅者（MainWindow）也能立即拿到；
        // 3) Prepend 用 Func 重载：每次订阅时读取当前项目，兜住“连接时项目已启动/重启”的情况。
        var projectStream = Observable.Defer(() =>
                Observable.Return(this.Ctrl.Project)
                    .Concat(this.Ctrl.ObserveStartedOrStopped()
                        .Select(evt => evt.IsStarted ? evt.Project : null)))
            .DistinctUntilChanged()
            .Replay(1);
        _projectConnection = projectStream.Connect();
        this.ObservableProject = projectStream;

        var th = new Thread(async () =>
        {
            var loggerFactory = this.Root.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<App>();

            var dir = Path.Combine(AppContext.BaseDirectory, "Tags");
            await this.Ctrl.StartPollAsync(dir, null, (proj,sp, ct) =>
            {
                proj.Logicets.Add(new HeartBeatLogicet(
                    proj,
                    loggerFactory.CreateLogger<HeartBeatLogicet>()
                ));

                proj.RunnerStarted += (grp, ch, ct) => {
                    logger.LogInformation("Tags处理开始,grp={grpName}", grp.TagName());
                    return Task.CompletedTask;
                };
                proj.RunnerCrashed += (grp, ch, ex, ct) => {
                    logger.LogError(ex, "Tags处理异常,grp={grpName}", grp.TagName());
                    return Task.CompletedTask;
                };

                return Task.CompletedTask;
            });
        });
        th.IsBackground = true;
        th.Start();

    }

    private static ServiceCollection ConfigureServiceCollections()
    {
        var services = new ServiceCollection();
        services.AddWpfDemoTags();
        services.AddSerilog((sp, lc) => lc
            .ReadFrom.Services(sp)
            .WriteTo.Console()
            .WriteTo.File("logs/log.txt", outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message}{NewLine}{Exception}", rollingInterval: RollingInterval.Day)
            .Enrich.FromLogContext()
        );
        return services;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (this.Ctrl is not null)
        {
            await this.Ctrl.StopAsync();
        }
        _projectConnection?.Dispose();
        Environment.Exit(0);
    }
}

