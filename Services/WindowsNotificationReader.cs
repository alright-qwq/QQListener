using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public sealed class WindowsNotificationReader(
    IWindowsNotificationPlatform platform,
    QqListenerDiagnosticsState diagnostics,
    ILogger<WindowsNotificationReader> logger) : IWindowsNotificationReader
{
    private readonly SemaphoreSlim _permissionGate = new(1, 1);
    private PlatformAccessStatus _accessStatus = PlatformAccessStatus.Unspecified;
    private bool _hasRequestedAccess;
    private WindowsNotificationReadResult? _latchedAccessFailure;

    public async Task<WindowsNotificationReadResult> ReadToastNotificationsAsync(
        bool retryPermission,
        CancellationToken cancellationToken)
    {
        var accessFailure = await EnsureAccessAsync(retryPermission, cancellationToken);
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        try
        {
            var snapshots = await platform.ReadToastNotificationsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            diagnostics.RecordRead(
                snapshots.Count,
                snapshots.Count(snapshot => !snapshot.IsAppIdentityAvailable));
            diagnostics.ClearError();
            return new WindowsNotificationReadResult(
                WindowsNotificationReadStatus.Success,
                snapshots,
                "读取通知成功");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WindowsNotificationListenerUnavailableException ex)
        {
            return RecordListenerUnavailable(ex);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取通知失败。");
            diagnostics.RecordError(ex, "读取通知失败");
            return new WindowsNotificationReadResult(
                WindowsNotificationReadStatus.ReadFailed,
                [],
                "读取通知失败",
                ex);
        }
    }

    private async Task<WindowsNotificationReadResult?> EnsureAccessAsync(
        bool retryPermission,
        CancellationToken cancellationToken)
    {
        await _permissionGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessStatus == PlatformAccessStatus.Allowed)
            {
                return null;
            }

            if (_hasRequestedAccess && !retryPermission)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _latchedAccessFailure ?? CreatePermissionDeniedResult(_accessStatus);
            }

            logger.LogInformation("正在请求通知监听权限...");
            try
            {
                _accessStatus = await platform.RequestAccessAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _hasRequestedAccess = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (WindowsNotificationListenerUnavailableException ex)
            {
                return RecordListenerUnavailable(ex);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "请求通知监听权限失败。");
                diagnostics.RecordError(ex, "请求通知监听权限失败");
                return new WindowsNotificationReadResult(
                    WindowsNotificationReadStatus.ReadFailed,
                    [],
                    "请求通知监听权限失败",
                    ex);
            }

            diagnostics.RecordPermission(_accessStatus.ToString());
            if (_accessStatus != PlatformAccessStatus.Allowed)
            {
                logger.LogWarning("通知监听权限不可用（状态={Status}），无法读取通知。", _accessStatus);
                _latchedAccessFailure = CreatePermissionDeniedResult(_accessStatus);
                return _latchedAccessFailure;
            }

            _latchedAccessFailure = null;
            logger.LogInformation("已获取通知监听权限。");
            return null;
        }
        finally
        {
            _permissionGate.Release();
        }
    }

    private WindowsNotificationReadResult RecordListenerUnavailable(
        WindowsNotificationListenerUnavailableException exception)
    {
        logger.LogWarning(exception, "无法获取 UserNotificationListener.Current，跳过本次轮询。");
        diagnostics.RecordError(exception, "获取通知监听器失败");
        return new WindowsNotificationReadResult(
            WindowsNotificationReadStatus.ListenerUnavailable,
            [],
            "通知监听器不可用",
            exception);
    }

    private static WindowsNotificationReadResult CreatePermissionDeniedResult(
        PlatformAccessStatus accessStatus)
    {
        return new WindowsNotificationReadResult(
            WindowsNotificationReadStatus.PermissionDenied,
            [],
            $"通知监听权限不可用（状态={accessStatus}）");
    }
}

public sealed record WindowsToastSnapshot(
    uint Id,
    string AppName,
    string AppId,
    bool IsAppIdentityAvailable,
    IReadOnlyList<string> Texts);
