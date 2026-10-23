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
    private IDisposable _disposables;
    private ITagsProjectCtrl? _ctrl;

    public MainWindow()
    {
        InitializeComponent();


        var app = App.Current as App ?? throw new InvalidCastException("App.Current is not of type App");
        this._ctrl = app.Ctrl;

        this._disposables = app.ObservableProject
            .Select(proj => Observable.Create<Unit>(observer => {
                return proj is null ?
                    Disposable.Empty :
                    SubscribeTagsSafe(proj);
            }))
            .Switch()
            .Subscribe();
    }

    /// <summary>
    /// 订阅失败时留痕，避免界面一直空白却没有任何线索
    /// </summary>
    private IDisposable SubscribeTagsSafe(ITagsProject project)
    {
        try
        {
            return SubscribeTags(project.Tags);
        }
        catch (Exception ex)
        {
            var logger = (App.Current as App)?.Root?
                .GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            logger?.CreateLogger<MainWindow>().LogError(ex, "订阅测点失败");

            if (!this.Dispatcher.HasShutdownStarted)
            {
                this.Dispatcher.Invoke(() => MessageBox.Show(
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
        _disposables.Dispose();
    }

    private async void btnToggle_Click(object sender, RoutedEventArgs e)
    {
        var proj = this._ctrl?.Project;
        if(proj is null)
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