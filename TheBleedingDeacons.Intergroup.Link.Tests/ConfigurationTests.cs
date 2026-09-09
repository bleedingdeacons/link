using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What counts as somewhere Link is willing to talk to.
///
/// <para>Both of these were looser than they read. <see
/// cref="FellowshipConfiguration.IsConfigured"/> was a non-empty check, so
/// any string at all passed and <c>Route</c> interpolated it; <see
/// cref="BetterStackConfiguration.IsValid"/> accepted <c>http://</c>
/// alongside HTTPS. Either one downgrades a bearer credential to cleartext
/// without saying so.</para>
/// </summary>
public sealed class ConfigurationTests
{
	[Theory]
	[InlineData("https://aa-bristol.org", true)]
	[InlineData("https://aa-bristol.org/amber", true)]
	[InlineData("http://aa-bristol.org", false)]
	[InlineData("http://localhost:8080", false)]
	[InlineData("aa-bristol.org", false)]
	[InlineData("link://auth", false)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	public void IsConfigured_AcceptsOnlyAnAbsoluteHttpsUrl(string baseUrl, bool expected) =>
		Assert.Equal(expected, new FellowshipConfiguration { BaseUrl = baseUrl }.IsConfigured);

	/// <summary>
	/// A bare hostname is still accepted: Better Stack's dashboard shows the
	/// ingest address without a scheme, so that is what gets pasted, and the
	/// setter gives it https:// on the way in.
	/// </summary>
	[Theory]
	[InlineData("s123456.betterstackdata.com", true)]
	[InlineData("https://s123456.betterstackdata.com", true)]
	[InlineData("http://s123456.betterstackdata.com", false)]
	[InlineData("http://localhost:9000", false)]
	[InlineData("ftp://logs.example", false)]
	[InlineData("", false)]
	public void IsValid_AcceptsOnlyAnAbsoluteHttpsEndpoint(string endpoint, bool expected) =>
		Assert.Equal(
			expected,
			new BetterStackConfiguration { SourceToken = "t", Endpoint = endpoint }.IsValid());
}
