using System.Reflection;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Automations.Actions;
using QQListener.Automations.Triggers;
using QQListener.Components;
using QQListener.Models;
using QQListener.Services;
using QQListener.Views.ActionSettingsControls;
using QQListener.Views.SettingsPages;

namespace QQListener;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var settings = QqListenerSettings.Load(PluginConfigFolder);
        settings.Normalize();
        // Normalize 仅在内存里把越界值夹紧；若不回写，磁盘上的非法值每次启动都会被
        // 重新读出并再次夹紧，用户从外部编辑文件时也会感到设置"被回退"。
        if (!settings.HasLoadWarning)
        {
            TrySaveSettings(settings);
        }

        var diagnostics = new QqListenerDiagnosticsState();
        if (settings.HasLoadWarning)
        {
            diagnostics.RecordSettingsWarning(settings.LoadWarning);
        }

        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        diagnostics.RecordRuntimeInfo(
            Info.Manifest.Version ?? "",
            Info.PluginFolderPath,
            settings.SettingsFilePath,
            assemblyPath,
            File.Exists(assemblyPath) ? File.GetLastWriteTime(assemblyPath) : null);

        var pluginInfo = QqListenerPluginInfo.FromManifest(
            Info.Manifest.Version ?? "",
            Info.Manifest.Author ?? "",
            Info.Manifest.Url);

        RegisterRuntimeServices(services, settings, diagnostics);
        services.AddSingleton(pluginInfo);

        services.AddNotificationProvider<QqNotificationProvider>();
        services.AddComponent<LatestQqMessageComponent, LatestQqMessageComponentSettingsControl>();
        RegisterBaseActions(services);
        services.AddTrigger<NormalMessageReceivedTrigger>();
        services.AddTrigger<ImportantMessageReceivedTrigger>();
        services.AddTrigger<CallingMessageReceivedTrigger>();
        RegisterActionMenu();

        services.AddSettingsPage<QqListenerSettingsPage>();
    }


    internal static void RegisterRuntimeServices(
        IServiceCollection services,
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics)
    {
        services.AddSingleton(settings);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IWindowsNotificationPlatform, WindowsNotificationPlatform>();
        services.AddSingleton<IWindowsNotificationReader, WindowsNotificationReader>();
        services.AddSingleton<QqToastMessageParser>();
        services.AddSingleton<MessageRuleEvaluator>();
        services.AddSingleton<MessageRulePreviewService>();
        services.AddSingleton<MessageEventPipeline>();
        services.AddSingleton<IQqNotificationPoller, QqNotificationPoller>();
        services.AddSingleton<IQqListenerDelay, SystemQqListenerDelay>();
        services.AddSingleton<ClassIslandMessageNotifier>();
        services.AddSingleton<LatestMessageState>();
        services.AddSingleton<MessageAutomationEventHub>();
        services.AddSingleton<QqListenerWorker>();
        services.AddSingleton(diagnostics);
    }
    private static void TrySaveSettings(QqListenerSettings settings)
    {
        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            // 启动期间没有 logger，吞掉异常避免拖累整个宿主启动。
            System.Diagnostics.Debug.WriteLine(
                $"[QQListener] {QqListenerDiagnosticsState.FormatError(ex, "回写设置失败")}");
        }
    }

    private static void RegisterBaseActions(IServiceCollection services)
    {
        services.AddAction<SetListenerEnabledAction, SetListenerEnabledActionSettingsControl>();
        services.AddAction<ShowLatestMessageAction>();
        services.AddAction<ClearLatestMessageAction>();
    }

    private static void RegisterActionMenu()
    {
        const string groupName = "QQListener 行动";
        if (!IActionService.ActionMenuTree.Contains(groupName))
        {
            IActionService.ActionMenuTree.Add(new ActionMenuTreeGroup(groupName, "\uE02B"));
        }

        var children = IActionService.ActionMenuTree[groupName];
        if (!children.Contains("设置监听状态"))
        {
            children.Add(new ActionMenuTreeItem<SetListenerEnabledActionSettings>(
                "QQListener.SetListenerEnabled",
                "设置监听状态",
                "\uE73C",
                _ => { }));
        }

        if (!children.Contains("显示最后一条 QQ 消息"))
        {
            children.Add(new ActionMenuTreeItem(
                "QQListener.ShowLatestMessage",
                "显示最后一条 QQ 消息",
                "\uE02B"));
        }

        if (!children.Contains("清空最后一条 QQ 消息"))
        {
            children.Add(new ActionMenuTreeItem(
                "QQListener.ClearLatestMessage",
                "清空最后一条 QQ 消息",
                "\uE74D"));
        }
    }
}
