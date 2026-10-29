using GitPersona.Core.Models;
using GitPersona.Core.Profiles;

namespace GitPersona.App.Services;

/// <summary>The in-memory profile list shared by the pages and the tray; every change is saved immediately.</summary>
public sealed class ProfilesService(ProfileStore store)
{
    private List<Profile>? _profiles;

    public event EventHandler? Changed;

    public IReadOnlyList<Profile> Profiles => _profiles ??= [.. store.Load()];

    /// <summary>Adds <paramref name="profile"/>, or replaces the profile that had <paramref name="originalId"/>.</summary>
    /// <exception cref="ProfileValidationException">Nothing was saved.</exception>
    public void Save(Profile profile, string? originalId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var updated = Profiles.ToList();
        var index = originalId is null ? -1 : updated.FindIndex(p => p.Id == originalId);
        if (index >= 0)
        {
            updated[index] = profile;
        }
        else
        {
            updated.Add(profile);
        }

        Commit(updated);
    }

    public void Delete(string id) => Commit(Profiles.Where(p => p.Id != id).ToList());

    private void Commit(List<Profile> updated)
    {
        store.Save(updated);
        _profiles = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
