using StdUnit.Tags;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace WPFDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly IDisposable _attachment;

    public MainWindow()
    {
        InitializeComponent();
        this.Closed += (_, _) => this.Dispose();

        var app = App.Current as App ?? throw new InvalidCastException("App.Current is not of type App");

        // 不用再手写 ObserveStartedOrStopped + Select + Prepend + Switch：
        // AttachRunningProject 负责晚挂补偿、按 RunId 去重、停止/换运行时自动摘掉上一次挂载。
        this._attachment = app.Ctrl.AttachRunningProject(SubscribeTagsSafe);
    }

    /// <summary>
    /// 订阅失败时留痕，避免界面一直空白却没有任何线索
    /// </summary>
    private IDisposable SubscribeTagsSafe(ITagsProject project, long runId)
    {
        try
        {
            return SubscribeTags(project);
        }
        catch (Exception ex)
        {
            var logger = (App.Current as App)?.Root?
                .GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            logger?.CreateLogger<MainWindow>().LogError(ex, "订阅测点失败（RunId={RunId}）", runId);

            if (!this.Dispatcher.HasShutdownStarted)
            {
                this.Dispatcher.InvokeAsync(() => MessageBox.Show(
                    $"订阅测点失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            return NoopDisposable.Instance;
        }
    }

    public void Dispose()
    {
        try
        {
            this._attachment.Dispose();
        }
        catch 
        {
            // swallow exceptions during dispose to avoid crashing the application
        }
    }

    private IDisposable SubscribeTags(ITagsProject project)
    {
        var req = project.Tags.SelectTag("IoBox/通用状态/PLC/心跳请求");
        var ack = project.Tags.SelectTag("IoBox/通用状态/MST/心跳响应");

        // 每轮处理结束后取一次本轮最终值；轮询线程上只投递，不等待 UI。
        return project.SubscribeTurnProcess("IoBox", (entry, channel, ct) =>
        {
            var reqValue = req.GetTagValue<bool>();
            var ackValue = ack.GetTagValue<bool>();

            if (!this.Dispatcher.HasShutdownStarted)
            {
                this.Dispatcher.InvokeAsync(() =>
                {
                    this.txtReq.Text = reqValue.ToString();
                    this.txtAck.Text = ackValue.ToString();
                });
            }

            return Task.CompletedTask;
        });
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}