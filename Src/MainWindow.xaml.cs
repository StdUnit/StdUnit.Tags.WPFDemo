using System.Reactive;
using StdUnit.Tags;
using StdUnit.Tags.Rx;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;

namespace WPFDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly IDisposable _disposable;
    public MainWindow()
    {
        InitializeComponent();

        var app = App.Current as App ?? throw new InvalidCastException("App.Current is not of type App");
        var projobs = app.Ctrl.ObserveStartedOrStopped()
            .Select(evt => evt.IsStarted ? evt.Project : null)
            // 测点项目由后台线程在 Application_Startup 中启动，可能早于本窗口构造；
            // StartedOrStopped 是普通事件、不会重放历史记录，因此用当前 Project 作首值兜底。
            .Prepend(app.Ctrl.Project)
            .DistinctUntilChanged()
            .Publish()
            .RefCount();

        this._disposable = projobs.Select(proj => 
                Observable.Create<Unit>(observer => {
                    return proj is null ?
                        Disposable.Empty :
                        SubscribeTags(proj!.Tags);
                })
            )
            .Switch()
            .Subscribe();
    }

    public void Dispose()
    {
        try
        {
            this._disposable.Dispose();
        }
        catch 
        {
            // swallow exceptions during dispose to avoid crashing the application
        }
    }

    private IDisposable SubscribeTags(ITagGrp tags)
    {
        var req = tags.SelectTag("IoBox/通用状态/PLC/心跳请求");
        var ack = tags.SelectTag("IoBox/通用状态/MST/心跳响应");

        CompositeDisposable d = new CompositeDisposable();

        req.Watch()
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(evt =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.txtReq.Text = evt.EventArgs.NewValue?.ToString();
                });
            })
            .DisposeWith(d);

        ack.Watch()
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(evt =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.txtAck.Text = evt.EventArgs.NewValue?.ToString();
                });
            })
            .DisposeWith(d);
        return d;

    }
}