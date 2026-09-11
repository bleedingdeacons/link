namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The noise a message makes when it lands while somebody is looking at
/// the app.
///
/// <para><b>This is not the notification sound.</b> A message that arrives
/// with Link closed is announced by the platform, which plays whatever the
/// member has chosen for the app's channel and is theirs to configure. The
/// gap this fills is the other case: the app open, the list on screen, a
/// message appearing in silence with nothing to draw the eye.</para>
///
/// <para><b>Off is a supported answer, and a first-class one.</b> A
/// fellowship phone sits in meetings, and an app that cannot be quietened
/// is an app that gets uninstalled. The switch is on the settings screen
/// beside the others rather than buried.</para>
/// </summary>
public interface IArrivalSound
{
	/// <summary>
	/// Whether to make any noise. Persisted, and read by the settings
	/// screen as well as by whatever is playing.
	/// </summary>
	bool Enabled { get; set; }

	/// <summary>
	/// Play it, if <see cref="Enabled"/>.
	///
	/// <para>Never throws. A phone in silent mode, a platform with no
	/// implementation, an audio stack that refuses — none of those is a
	/// reason for a message not to arrive, and every caller is on the path
	/// that delivers one.</para>
	/// </summary>
	void Play();
}
