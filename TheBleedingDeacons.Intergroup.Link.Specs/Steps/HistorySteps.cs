using Reqnroll;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Emptying this phone's copy, and the two different ways of doing it.
/// </summary>
[Binding]
public sealed class HistorySteps(World world)
{
	[When(@"^the history is cleared$")]
	public async Task Cleared() => await world.History.ClearAsync();

	/// <summary>
	/// What sign-out calls. Sign-out itself is <c>DeviceAuthService</c>'s,
	/// which is MAUI and out of reach from here — that it calls this
	/// rather than <c>ClearAsync</c> is an on-device criterion. What this
	/// step settles is the difference between the two, which is where a
	/// mistake would actually live.
	/// </summary>
	[When(@"^the history is reset, as signing out resets it$")]
	public async Task Reset() => await world.History.ResetAsync();
}
