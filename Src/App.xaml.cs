using StdUnit.Tags;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
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

    private WebApplication? _app;

    /// <summary>关停是否已经触发（0/1，用 CAS 保证只跑一次）。</summary>
    private int _shutdownTriggered;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 关窗不再自动退出：要走完 ShutdownAsync（异步）再显式 Shutdown()。
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureServies();
        var app = builder.Build();
        this.Root = app.Services;
        this.Ctrl = this.Root.GetRequiredService<ITagsProjectCtrl>();
        this.Ctrl.OnStartingException = ex =>
        {
            MessageBox.Show($"Tags处理启动异常:{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.FromResult(true);
        };

        // 观测端（MainWindow）用 AttachRunningProject 挂到"当前正在运行的项目"上：
        // 晚挂补偿、按 RunId 去重、停止/换运行时摘旧挂新都由它负责，这里不再维护项目流。
        this._app = app;

        StartTagsPoll(this.Root, this.Ctrl);
        StartWeb(app);

        this._mainWindow = new MainWindow();
        this._mainWindow.Closing += this.OnMainWindowClosing;
        this._mainWindow.Show();
    }

    private void StartTagsPoll(IServiceProvider sp, ITagsProjectCtrl ctrl)
    {
        var th1 = new Thread(async () =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<App>();

            var dir = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
            await ctrl.StartPollAsync(Path.Combine(dir!.FullName, "Tags"), null, (proj, sp, ct) =>
            {
                proj.Logicets.Add(new HeartBeatLogicet(
                    proj,
                    loggerFactory.CreateLogger<HeartBeatLogicet>()
                ));

                proj.RunnerStarted += (grp, ch, ct) =>
                {
                    logger.LogInformation("Tags处理开始,grp={grpName}", grp.TagName());
                    return Task.CompletedTask;
                };
                proj.RunnerCrashed += (grp, ch, ex, ct) =>
                {
                    logger.LogError(ex, "Tags处理异常,grp={grpName}", grp.TagName());
                    MessageBox.Show($"Tags处理发生异常：{ex.Message}");
                    return Task.CompletedTask;
                };

                return Task.CompletedTask;
            });
        });
        th1.IsBackground = true;
        th1.Start();
    }

    private static void StartWeb(WebApplication app)
    {
        app.ConfigureMiddlewares();
        var th2 = new Thread(() =>
        {
            app.Run("http://localhost:3001");
        });
        th2.IsBackground = true;
        th2.Start();
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

            // 先停 Web，避免关停过程中还有请求打进来；再停 Tags 本体
            if (this._app is { } app)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await app.StopAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Web 宿主 StopAsync 异常");
                }
            }

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

            // 释放 Web 宿主会连带释放承载 Ctrl 的 DI 容器，所以只能放在关停末端
            if (this._app is { } disposingApp)
            {
                try
                {
                    await disposingApp.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Web 宿主释放异常");
                }
                finally
                {
                    this._app = null;
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

