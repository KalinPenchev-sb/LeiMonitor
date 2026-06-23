using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Services;
using LeiMonitor.Data.Notifications;
using LeiMonitor.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<ILeiRepository, LeiRepository>();
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
        services.AddSingleton<IAlertSender, CompositeAlertSender>();
        services.AddSingleton<LeiExpiryChecker>();
    })
    .Build();

try
{
    var checker = host.Services.GetRequiredService<LeiExpiryChecker>();
    await checker.RunAsync();
    return 0;
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Unhandled exception. Exiting.");
    return 1;
}
