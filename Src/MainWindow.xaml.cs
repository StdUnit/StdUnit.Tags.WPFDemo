using System.Reactive;
using StdUnit.Tags;
using StdUnit.Tags.Rx;
using Microsoft.Extensions.Logging;
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
        this._disposable = app.ObservableProject
            .Select(proj => 
                Observable.Create<Unit>(observer => {
                    return proj is null ?
                        Disposable.Empty :
                        SubscribeTagsSafe(proj);
                })
            )
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