using Itminus.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
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

        var th = new Thread(async () =>
        {
            var loggerFactory = this.Root.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<App>();

            var dir = Path.Combine(AppContext.BaseDirectory, "Tags");
            await this.Ctrl.StartPollAsync(dir, null, (proj,sp, ct) =>
            {
                proj.Logicets.Add(new HeartBeatLogicet(
                    proj.Channels,
                    proj.Tags,
                    loggerFactory.CreateLogger<HeartBeatLogicet>()
                ));

                proj.RunnerStarted += (grp, ch) => {
                    logger.LogInformation("Tags处理开始,grp={grpName}", grp.TagName());
                    return Task.CompletedTask;
                };
                proj.RunnerCrashed += (grp, ch, ex) => {
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
        Environment.Exit(0);
    }
}

