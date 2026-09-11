using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// What each sign-in outcome means.
///
/// <para>These bind to the results rather than to a flow, because the
/// flow is <c>DeviceAuthService</c>'s and <c>DeviceAuthService</c> is
/// MAUI. What is settled here is the part a screen reads: whether it
/// succeeded, and whether there is anything to tell the member.</para>
/// </summary>
[Binding]
public sealed class SignInSteps
{
	private SignInStart? _start;
	private EnrolmentResult? _enrolment;
	private PasswordSetResult? _password;
	private bool _linkAccepted;

	[Given(@"^a sign-in that answered an authorization URL$")]
	public void BrowserFlow() =>
		_start = new SignInStart { State = "state-1", AuthorizationUrl = "https://aa-bristol.org/oauth/start" };

	[Given(@"^a sign-in that answered a nonce and no URL$")]
	public void NativeFlow() => _start = new SignInStart { State = "state-1", Nonce = "nonce-1" };

	[Given(@"^the member cancelled the sign-in$")]
	public void Cancelled() => _enrolment = EnrolmentResult.Cancelled();

	[Given(@"^Fellowship refused the sign-in, saying ""(.+)""$")]
	public void Refused(string reason) => _enrolment = EnrolmentResult.Failed(reason);

	[Given(@"^Fellowship accepted the sign-in$")]
	public void Accepted() =>
		_enrolment = EnrolmentResult.Ok(new() { Token = "device-token", MemberId = 7, MemberName = "Dave B" });

	[Given(@"^a password link was asked for$")]
	public void LinkAsked() => _linkAccepted = true;

	[Given(@"^a password link was asked for and Fellowship could not be reached$")]
	public void LinkUnreachable() => _linkAccepted = false;

	[Given(@"^setting a password was refused, saying ""(.+)""$")]
	public void PasswordRefused(string reason) => _password = PasswordSetResult.Failed(reason);

	[Given(@"^setting a password was accepted$")]
	public void PasswordAccepted() => _password = PasswordSetResult.Ok();

	[Then(@"^the browser is used$")]
	public void BrowserUsed() => _start.ShouldNotBeNull().IsBrowserFlow.ShouldBeTrue();

	[Then(@"^the browser is not used$")]
	public void BrowserNotUsed() => _start.ShouldNotBeNull().IsBrowserFlow.ShouldBeFalse();

	[Then(@"^the sign-in (succeeded|did not succeed)$")]
	public void SignInOutcome(string outcome) =>
		_enrolment.ShouldNotBeNull().Succeeded
			.ShouldBe(string.Equals(outcome, "succeeded", StringComparison.Ordinal));

	[Then(@"^there is nothing to tell them$")]
	public void NothingToSay() => _enrolment.ShouldNotBeNull().Error.ShouldBeEmpty();

	[Then(@"^they are told ""(.+)""$")]
	public void ToldWhy(string reason)
	{
		if (_enrolment is not null)
		{
			_enrolment.Error.ShouldBe(reason);
			return;
		}

		_password.ShouldNotBeNull().Error.ShouldBe(reason);
	}

	[Then(@"^it was accepted$")]
	public void LinkWasAccepted() => _linkAccepted.ShouldBeTrue();

	[Then(@"^it was not accepted$")]
	public void LinkWasNotAccepted() => _linkAccepted.ShouldBeFalse();

	[Then(@"^setting the password (succeeded|did not succeed)$")]
	public void PasswordOutcome(string outcome) =>
		_password.ShouldNotBeNull().Succeeded
			.ShouldBe(string.Equals(outcome, "succeeded", StringComparison.Ordinal));
}
