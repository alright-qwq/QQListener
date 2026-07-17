using System.ComponentModel;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

internal sealed class QqListenerSettingsPersistenceCoordinator(
    QqListenerSettings settings,
    QqListenerDiagnosticsState diagnostics,
    Action<PropertyChangedEventArgs> settingsChanged)
{
    private bool _isAttached;

    public bool IsDirty { get; private set; }

    public void Attach()
    {
        if (_isAttached) return;

        settings.PropertyChanged += OnSettingsPropertyChanged;
        _isAttached = true;
    }

    public void Detach()
    {
        SavePendingSettings();
        if (!_isAttached) return;

        settings.PropertyChanged -= OnSettingsPropertyChanged;
        _isAttached = false;
    }

    public void SavePendingSettings()
    {
        if (!IsDirty || settings.SuppressAutoSave) return;

        try
        {
            settings.Normalize();
            settings.Save();
            IsDirty = false;
        }
        catch (Exception ex)
        {
            diagnostics.RecordError(ex, "保存设置失败");
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!settings.SuppressAutoSave)
        {
            IsDirty = true;
        }

        settingsChanged(e);
    }
}
