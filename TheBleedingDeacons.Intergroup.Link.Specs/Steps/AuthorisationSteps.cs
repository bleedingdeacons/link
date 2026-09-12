using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Losing authorisation, and what survives it.
/// </summary>
[Binding]
public sealed class AuthorisationSteps(World world)
{
	[Given(@"^Fellowship no longer accepts this handset's token$")]
	public void TokenRevoked() =>
		world.Fellowship.Refusal = InboxPage.Refused(FellowshipFailure.Unauthenticated);

	[Given(@"^Fellowship no longer has a member record for this address$")]
	public void NotAMember() =>
		world.Fellowship.Refusal = InboxPage.Refused(FellowshipFailure.NotEligible);

	[Given(@"^Fellowship answers with a server error$")]
	public void ServerError() =>
		world.Fellowship.Refusal = InboxPage.Refused(FellowshipFailure.Server);

	/// <summary>
	/// Enrolment, as far as the history is concerned: a member is now
	/// holding this phone, and the store is told so.
	///
	/// <para>Both a Given and a When — a scenario either starts with
	/// somebody signed in, or has somebody sign in part-way through to
	/// see what happens to what is already there.</para>
	/// </summary>
	[Given(@"^member (\d+) is signed in on this phone$")]
	[When(@"^member (\d+) signs in on this phone$")]
	public async Task SignsIn(long memberId)
	{
		world.Sessions.Session = new()
		{
			Token = "device-token",
			MemberId = memberId,
			MemberName = "Member " + memberId,
		};

		await world.History.AdoptAsync(memberId);
	}

	[Then(@"^the handset has signed itself out$")]
	public void SignedOut() => world.SignedOut.ShouldNotBeNull();

	[Then(@"^the handset is still signed in$")]
	public void StillSignedIn()
	{
		world.SignedOut.ShouldBeNull();
		world.Sessions.Session.ShouldNotBeNull();
	}

	[Then(@"^it holds no token$")]
	public void NoToken() => world.Sessions.Session.ShouldBeNull();

	[Then(@"^it holds no keypair$")]
	public async Task NoKeypair() => (await world.Keys.HasKeyAsync()).ShouldBeFalse();

	[Then(@"^the member is told something that mentions ""(.+)""$")]
	public void ToldWhy(string words) =>
		world.SignedOut.ShouldNotBeNull().Reason.ShouldContain(words);

	[Then(@"^the failure reads as (network|unauthenticated|not eligible|server)$")]
	public void FailureReadsAs(string failure) =>
		world.LastSync.ShouldNotBeNull().Failure.ShouldBe(failure switch
		{
			"network" => FellowshipFailure.Network,
			"unauthenticated" => FellowshipFailure.Unauthenticated,
			"not eligible" => FellowshipFailure.NotEligible,
			_ => FellowshipFailure.Server,
		});
}
