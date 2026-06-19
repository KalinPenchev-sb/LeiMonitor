using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Services;
using LeiMonitor.Data.Notifications;
using LeiMonitor.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<ILeiRepository, LeiRepository>();
        services.AddSingleton<IAlertSender, EmailAlertSender>();
        services.AddSingleton<LeiExpiryChecker>();
    })
    .Build();

await host.RunAsync();
