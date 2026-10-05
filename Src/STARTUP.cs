using StdUnit.Tags.McpServer;
using WPFDemo.Tags;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace WPFDemo;

public static class STARTUP
{
    public static void ConfigureServies(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddSerilog((sp, lc) => lc
            .ReadFrom.Services(sp)
            .WriteTo.Console()
            .WriteTo.File("logs/log.txt", outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message}{NewLine}{Exception}", rollingInterval: RollingInterval.Day)
            .Enrich.FromLogContext()
        );
        services.AddWpfDemoTags();

        services.AddMcpServer()
            .WithHttpTransport(opts => {
                opts.Stateless = true;
            })
            .AddTagsMcp();
        services.AddAuthentication();
    }

    public static void ConfigureMiddlewares(this WebApplication app)
    {
        app.MapMcp("/mcp");
    }
}
