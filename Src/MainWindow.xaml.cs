using StdUnit.Tags;
using StdUnit.Tags.R3;
using R3;
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
        var projobs = app.Ctrl.ObserveStartedOrStopped()
            .Select(evt => evt.IsStarted ? evt.Project : null)
            // 测点项目由后台线程在 Application_Startup 中启动，可能早于本窗口构造；
            // StartedOrStopped 是普通事件、不会重放历史记录，因此用当前 Project 作首值兜底。
            .Prepend(app.Ctrl.Project)
            .DistinctUntilChanged()
            .Publish()
            .RefCount();

        this._disposables = projobs.Select(proj => Observable.Create<Unit>(observer => {
            return proj is null ?
                Disposable.Empty :
                SubscribeTags(proj!.Tags);
            }))
            .Switch()
            .Subscribe();
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