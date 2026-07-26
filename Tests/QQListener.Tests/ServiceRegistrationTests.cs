using Microsoft.Extensions.DependencyInjection;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void RuntimeServicesValidateAndResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var settings = new QqListenerSettings { IsEnabled = false };
        var diagnostics = new QqListenerDiagnosticsState();

        Plugin.RegisterRuntimeServices(services, settings, diagnostics);
        services.AddSingleton<QqNotificationProvider>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IQqNotificationPoller>());
        Assert.NotNull(provider.GetRequiredService<QqListenerWorker>());
        Assert.NotNull(provider.GetRequiredService<MessageAutomationEventHub>());
        Assert.NotNull(provider.GetRequiredService<MessageEventPipeline>());
        Assert.NotNull(provider.GetRequiredService<MessageRulePreviewService>());
        var constructor = Assert.Single(typeof(QqNotificationProvider).GetConstructors());
        foreach (var parameter in constructor.GetParameters())
        {
            Assert.NotNull(provider.GetService(parameter.ParameterType));
        }
    }
}
