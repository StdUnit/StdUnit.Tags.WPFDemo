using StdUnit.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
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

    /// <summary>主窗口由 App 自己创建并持有：关停的触发权归这里。</summary>
    private MainWindow? _mainWindow;

    /// <summary>关停是否已经触发（0/1，用 CAS 保证只跑一次）。</summary>
    private int _shutdownTriggered;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 关窗不再自动退出：要走完 ShutdownAsync（异步）再显式 Shutdown()。
        // 否则窗口一关 Dispatcher 就停摆，清理只能靠同步阻塞去等。
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ServiceCollection services = ConfigureServiceCollections();
        this.Root = services.BuildServiceProvider();
        this.Ctrl = this.Root.GetRequiredService<ITagsProjectCtrl>();
        this.Ctrl.OnStartingException = ex =>
        {
            MessageBox.Show($"Tags处理启动异常:{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.FromResult(true);
        };

        // 观测端（MainWindow）用 AttachRunningProject 挂到"当前正在运行的项目"上：
        // 晚挂补偿、按 RunId 去重、停止/换运行时摘旧挂新都由它负责，这里不再维护项目流。

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

        this._mainWindow = new MainWindow();
        this._mainWindow.Closing += this.OnMainWindowClosing;
        this._mainWindow.Show();
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

    #region 关停

    /// <summary>
    /// 关窗前拦一次：此时 Dispatcher 仍活着，可以放心 <c>await</c> 收尾，不需要同步阻塞。
    /// </summary>
    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (Volatile.Read(ref this._shutdownTriggered) == 1)
        {
            return;   // 已清理完（或正在收尾），放行真正的关闭
        }

        e.Cancel = true;
        await this.ShutdownAsync();
    }

    /// <summary>
    /// 注销 / 关机：同一条收尾路径。WPF 会先派发这个事件，此时 Dispatcher 也还活着。
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (Volatile.Read(ref this._shutdownTriggered) == 1)
        {
            base.OnSessionEnding(e);
            return;
        }

        e.Cancel = true;
        _ = this.ShutdownAsync();
        base.OnSessionEnding(e);
    }

    /// <summary>
    /// 收尾并退出。幂等；全程异步（没有 <c>.Wait()</c> / <c>.Result</c>）。
    /// </summary>
    internal async Task ShutdownAsync()
    {
        if (Interlocked.CompareExchange(ref this._shutdownTriggered, 1, 0) == 1)
        {
            return;
        }

        try
        {
            var logger = this.Root?.GetService<ILogger<App>>();
            if (this.Ctrl is not null)
            {
                try
                {
                    // 单独限时：Tags 内部卡住时也要让后续的释放动作跑完
                    await this.Ctrl.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Tags StopAsync 异常");
                }
            }

            // 停完之后再释放 DI 容器（Ctrl / 日志等单例都在里面）
            if (this.Root is IDisposable disposableRoot)
            {
                try
                {
                    disposableRoot.Dispose();
                }
                catch
                {
                    // 进程即将退出：此时日志单例可能已被释放，不再上报释放异常
                }
            }
        }
        finally
        {
            // 收尾失败也要退出，不能把窗口/进程卡住。
            if (!this.Dispatcher.HasShutdownStarted)
            {
                this.Dispatcher.Invoke(() =>
                {
                    this._mainWindow?.Close();   // 关窗会触发 Closed：退订测点观测
                    this._mainWindow = null;
                    this.Shutdown();
                });
            }
        }
    }

    #endregion
}

