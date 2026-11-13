using StdUnit.Tags;
using StdUnit.Tags.R3;
using R3;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Media;

namespace WPFDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly IDisposable _attachment;
    private readonly ITagsProjectCtrl? _ctrl;

    public MainWindow()
    {
        InitializeComponent();
        this.Closed += (_, _) => this.Dispose();

        var app = App.Current as App ?? throw new InvalidCastException("App.Current is not of type App");
        this._ctrl = app.Ctrl;

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
            return SubscribeTags(project.Tags);
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
            return Disposable.Empty;
        }
    }

    private IDisposable SubscribeTags(ITagGrp tags)
    {
        var req = tags.SelectTag("IoBox/通用状态/PLC/心跳请求");
        var ack = tags.SelectTag("IoBox/通用状态/MST/心跳响应");

        var d = new CompositeDisposable();
        req.Watch()
            .ObserveOnDispatcher(this.Dispatcher)
            .Subscribe(evt =>
            {
                var newvalue = evt.NewValue;
                this.txtReq.Text = newvalue?.ToString();
                this.txtReq.Foreground= newvalue is true ? Brushes.Green : Brushes.Black;
            })
            .AddTo(d);

        ack.Watch()
            .ObserveOnDispatcher(this.Dispatcher)
            .Subscribe(evt =>
            {
                var newvalue = evt.NewValue;
                this.txtAck.Text = newvalue?.ToString();
                this.txtAck.Foreground = newvalue is true ? Brushes.Green : Brushes.Black;
            })
            .AddTo(d);
        return d;
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

    private async void btnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (this._ctrl is null || !this._ctrl.TryGetRunning(out var proj, out _))
        {
            return;
        }

        var enqueue = proj.WriteIntent(
            entry:"IoBox", 
            intent: (grp, ct) => {
                var tag = proj.Tags.SelectTag("IoBox/通用状态/PLC/心跳请求");
                var old = tag.GetTagValue<bool>();
                tag.Value = !old;
                return ValueTask.CompletedTask;
            }, 
            out var task
        );
        await task;
    }
}