using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using Amireux999000.NeteaseCloudPilot;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;
using Amireux999000.NeteaseCloudPilot.Variables;

var builder = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .UseLocalization(Strings.LocalizationCatalog);

builder.Services.AddSingleton<NeteaseStateManager>();
builder.Services.AddSingleton<SmtcMonitor>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SmtcMonitor>());
builder.Services.AddSingleton<WindowController>();
builder.Services.AddSingleton<NeteaseVariableProvider>();

var plugin = builder
    .RegisterIntegration<NeteaseIntegration>()
    .Build();

await plugin.RunAsync();
