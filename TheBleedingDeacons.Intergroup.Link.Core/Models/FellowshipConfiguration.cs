namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Where this build of Link talks to.
///
/// <para>Read from appsettings.json, which is why it is a settable
/// record rather than a required-init one: the configuration binder needs
/// to construct it empty and fill it in.</para>
/// </summary>
public sealed class FellowshipConfiguration
{
	/// <summary>
	/// The site root, e.g. <c>https://aa-bristol.org</c>.
	///
	/// <para>No trailing slash is required; <see cref="Route"/> normalises
	/// it. The REST namespace is appended here rather than configured, so
	/// a site cannot be pointed at a path that is not Fellowship's.</para>
	/// </summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>
	/// The custom scheme the OAuth browser leg comes back to. Must match
	/// the intent filter in AndroidManifest.xml and the allow-list in
	/// Fellowship's <c>DeviceRedirectValidator</c>; all three say
	/// <c>link://auth</c>.
	/// </summary>
	public string CallbackUrl { get; set; } = "link://auth";

	/// <summary>
	/// How often the app polls when it is on screen.
	///
	/// <para>Push is the fast path; this is the reliable one. A phone in a
	/// tunnel catches up when it surfaces, and a handset whose FCM token
	/// silently rotated still gets its messages — which is exactly why
	/// polling is not switched off when push is working.</para>
	/// </summary>
	public int PollSeconds { get; set; } = 120;

	public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

	/// <summary>
	/// Build an absolute URL for one of Fellowship's routes.
	/// </summary>
	/// <param name="path">A route below the namespace, e.g. <c>messages</c>.</param>
	public Uri Route(string path)
	{
		var root = BaseUrl.TrimEnd('/');
		var tail = (path ?? string.Empty).TrimStart('/');

		return new Uri($"{root}/wp-json/fellowship/v1/{tail}");
	}
}
