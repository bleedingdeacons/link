using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The back end this phone last heard about. Not a secret, and it signs nobody in,
/// so it lives in Preferences.
/// </summary>
public sealed class BackendChoice : IBackendStore
{
	private const string PreferenceKey = "link_backend";

	public LinkBackend Backend
	{
		get => Preferences.Default.Get(PreferenceKey, nameof(LinkBackend.Fellowship)) == nameof(LinkBackend.Firebase)
			? LinkBackend.Firebase
			: LinkBackend.Fellowship;
		set => Preferences.Default.Set(PreferenceKey, value.ToString());
	}
}
