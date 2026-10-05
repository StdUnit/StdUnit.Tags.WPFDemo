using StdUnit.Tags;
using StdUnit.Tags.S7;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WPFDemo.Tags;

internal static class ServiceExtensions
{
    public static IServiceCollection AddWpfDemoTags(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddTagsProjectServices(builder =>
        {
            builder.EnableXmlSchemaValidation();
            builder.AddS7Support();
        });
        return services;
    }

}
