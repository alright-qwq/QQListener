using System.Reflection;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Automations.Actions;
using QQListener.Automations.Triggers;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Automations;

public sealed class AutomationContractTests
{
    public static TheoryData<Type, string> TriggerIdContracts => new()
    {
        { typeof(NormalMessageReceivedTrigger), "QQListener.NormalMessageReceived" },
        { typeof(ImportantMessageReceivedTrigger), "QQListener.ImportantMessageReceived" },
        { typeof(CallingMessageReceivedTrigger), "QQListener.CallingMessageReceived" }
    };

    public static TheoryData<Type, string> ActionIdContracts => new()
    {
        { typeof(SetListenerEnabledAction), "QQListener.SetListenerEnabled" },
        { typeof(ShowLatestMessageAction), "QQListener.ShowLatestMessage" },
        { typeof(ClearLatestMessageAction), "QQListener.ClearLatestMessage" }
    };

    public static TheoryData<Type, MessageImportance> TriggerImportanceContracts => new()
    {
        { typeof(NormalMessageReceivedTrigger), MessageImportance.Normal },
        { typeof(ImportantMessageReceivedTrigger), MessageImportance.Important },
        { typeof(CallingMessageReceivedTrigger), MessageImportance.Calling }
    };

    [Theory]
    [MemberData(nameof(TriggerIdContracts))]
    public void ConcreteTriggerIdsRemainStable(Type triggerType, string expectedId)
    {
        var attribute = triggerType.GetCustomAttribute<TriggerInfo>();

        Assert.NotNull(attribute);
        Assert.Equal(expectedId, attribute.Id);
    }

    [Theory]
    [MemberData(nameof(ActionIdContracts))]
    public void ActionAttributesAndMenuContractsUseStableIds(Type actionType, string expectedId)
    {
        var attribute = actionType.GetCustomAttribute<ActionInfo>();
        var menuContract = Assert.Single(
            Plugin.CreateActionMenuContracts(),
            contract => contract.ActionType == actionType);

        Assert.NotNull(attribute);
        Assert.Equal(expectedId, attribute.Id);
        Assert.Equal(expectedId, menuContract.ActionId);
    }

    [Theory]
    [MemberData(nameof(TriggerImportanceContracts))]
    public void ConcreteTriggersMatchOnlyTheirContractImportance(
        Type triggerType,
        MessageImportance expectedImportance)
    {
        var hub = new MessageAutomationEventHub(
            NullLogger<MessageAutomationEventHub>.Instance);
        var trigger = CreateTrigger(triggerType, hub);

        foreach (var importance in Enum.GetValues<MessageImportance>())
        {
            Assert.Equal(
                importance == expectedImportance,
                trigger.MatchesImportance(importance));
        }
    }

    [Fact]
    public void ProductionAutomationHelperRegistersEveryConcreteType()
    {
        var services = new ServiceCollection();

        Plugin.RegisterAutomations(services);

        var expectedTypes = TriggerIdContracts
            .Select(row => (Type)row[0])
            .Concat(ActionIdContracts.Select(row => (Type)row[0]));
        foreach (var expectedType in expectedTypes)
        {
            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == expectedType
                || GetImplementationType(descriptor) == expectedType);
        }
    }

    private static Type? GetImplementationType(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;

    private static MessageReceivedTriggerBase CreateTrigger(
        Type triggerType,
        MessageAutomationEventHub hub) =>
        triggerType == typeof(NormalMessageReceivedTrigger)
            ? new NormalMessageReceivedTrigger(hub)
            : triggerType == typeof(ImportantMessageReceivedTrigger)
                ? new ImportantMessageReceivedTrigger(hub)
                : triggerType == typeof(CallingMessageReceivedTrigger)
                    ? new CallingMessageReceivedTrigger(hub)
                    : throw new ArgumentOutOfRangeException(nameof(triggerType));
}
