using GitPersona.Core.Models;
using GitPersona.Core.Settings;

namespace GitPersona.App.Services;

/// <summary>The in-memory copy of <see cref="AppSettings"/>; every change is saved immediately.</summary>
public sealed class SettingsService(SettingsStore store)
{
    private AppSettings? _current;

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => _current ??= store.Load();

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var updated = change(Current);
        if (updated == Current)
        {
            return;
        }

        store.Save(updated);
        _current = updated;
        Changed?.Invoke(this, updated);
    }
}
