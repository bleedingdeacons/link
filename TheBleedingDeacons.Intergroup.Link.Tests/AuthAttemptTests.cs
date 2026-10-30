using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What a sign-in leaves in the log: every line tied to its attempt, and
/// one outcome at a level that matches what happened.
/// </summary>
public sealed class AuthAttemptTests
{
	private readonly Collecting _sink = new();
	private readonly Logger _log;

	public AuthAttemptTests()
	{
		_log = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.FromLogContext()
			.WriteTo.Sink(_sink)
			.CreateLogger();
	}

	[Fact]
	public void EveryLineLoggedDuringTheAttemptCarriesItsIdOperationAndMethod()
	{
		string id;
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			id = attempt.Id;

			// Logged from somewhere that knows nothing of the attempt, as
			// Fellowship's client does.
			_log.Warning("POST {Route} refused with {Status}", "auth/device/exchange", 403);
			attempt.Finished(EnrolmentResult.Failed("Not a member."));
		}

		Assert.Equal(3, _sink.Events.Count);
		Assert.All(_sink.Events, e =>
		{
			Assert.Equal("\"" + id + "\"", e.Properties["AuthAttempt"].ToString());
			Assert.Equal("\"Sign-in\"", e.Properties["AuthOperation"].ToString());
			Assert.Equal("\"google\"", e.Properties["AuthMethod"].ToString());
		});
	}

	[Fact]
	public void NothingAfterTheAttemptCarriesIt()
	{
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			attempt.Finished(EnrolmentResult.Cancelled());
		}

		_log.Information("Later");

		Assert.False(_sink.Events[^1].Properties.ContainsKey("AuthAttempt"));
	}

	[Fact]
	public void ASuccessIsInformationWithTheDeviceAndMember()
	{
		using (var attempt = AuthAttempt.Begin("Sign-in", "password", _log))
		{
			attempt.Finished(EnrolmentResult.Ok(new DeviceSession { Token = "secret", DeviceId = 12, MemberId = 7 }));
		}

		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Information, outcome.Level);
		Assert.Equal("12", outcome.Properties["DeviceId"].ToString());
		Assert.Equal("7", outcome.Properties["MemberId"].ToString());
		Assert.True(outcome.Properties.ContainsKey("ElapsedMs"));
	}

	[Fact]
	public void TheDeviceTokenIsNeverLogged()
	{
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			attempt.Finished(EnrolmentResult.Ok(new DeviceSession { Token = "device-token-xyz", DeviceId = 1, MemberId = 2 }));
		}

		Assert.All(_sink.Events, e =>
		{
			Assert.DoesNotContain("device-token-xyz", e.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
			Assert.DoesNotContain(e.Properties.Values, v => v.ToString().Contains("device-token-xyz", StringComparison.Ordinal));
		});
	}

	[Fact]
	public void ARefusalIsAWarningWithTheReasonTheMemberWasShown()
	{
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			attempt.Finished(EnrolmentResult.Failed("That address is not one the intergroup holds for you."));
		}

		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Warning, outcome.Level);
		Assert.Contains("not one the intergroup holds", outcome.Properties["Reason"].ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void ACancellationIsInformationNotAWarning()
	{
		// A closed browser tab is a member changing their mind.
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			attempt.Finished(EnrolmentResult.Cancelled());
		}

		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Information, outcome.Level);
		Assert.Contains("cancelled", outcome.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void ACancellationCarriesWhatThePlatformSaid()
	{
		// Not every "cancelled" is the member: Android's picker answers it
		// unseen once Google has paused the prompt, and says so only here.
		using (var attempt = AuthAttempt.Begin("Sign-in", "google", _log))
		{
			attempt.Finished(EnrolmentResult.Cancelled("Caller has been temporarily blocked"));
		}

		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Information, outcome.Level);
		Assert.Contains("temporarily blocked", outcome.Properties["CancelDetail"].ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void NotOfferedIsNeitherACancellationNorARefusal()
	{
		using (var attempt = AuthAttempt.Begin("Key replacement", "google", _log))
		{
			attempt.Finished(RotateKeyResult.NotOffered());
		}

		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Information, outcome.Level);
		Assert.Contains("not offered", outcome.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void AnExceptionIsLoggedAsAnErrorWithTheAttemptAndNotCaught()
	{
		var thrown = new InvalidOperationException("No browser.");
		string id = string.Empty;

		var escaped = Assert.Throws<InvalidOperationException>(() =>
		{
			using var attempt = AuthAttempt.Begin("Sign-in", "microsoft", _log);
			id = attempt.Id;
			try
			{
				throw thrown;
			}
			catch (Exception ex) when (attempt.Threw(ex))
			{
				Assert.Fail("Threw must never catch.");
			}
		});

		Assert.Same(thrown, escaped);
		var outcome = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Error, outcome.Level);
		Assert.Same(thrown, outcome.Exception);
		Assert.Equal("\"" + id + "\"", outcome.Properties["AuthAttempt"].ToString());
	}

	[Fact]
	public void AnAttemptLeftWithoutAnOutcomeSaysSo()
	{
		using (AuthAttempt.Begin("Sign-in", "apple", _log))
		{
		}

		var last = _sink.Events[^1];
		Assert.Equal(LogEventLevel.Debug, last.Level);
		Assert.Contains("without an outcome", last.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void AFinishedAttemptDoesNotAlsoSayItHadNoOutcome()
	{
		using (var attempt = AuthAttempt.Begin("Sign-out", "device", _log))
		{
			attempt.Finished(succeeded: true, "the intergroup revoked this device");
		}

		Assert.DoesNotContain(_sink.Events, e => e.RenderMessage(CultureInfo.InvariantCulture).Contains("without an outcome", StringComparison.Ordinal));
	}

	[Fact]
	public void EachAttemptHasItsOwnId()
	{
		using var first = AuthAttempt.Begin("Sign-in", "google", _log);
		using var second = AuthAttempt.Begin("Sign-in", "google", _log);

		Assert.NotEqual(first.Id, second.Id);
		Assert.Matches("^[0-9a-f]{8}$", first.Id);
	}

	private sealed class Collecting : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
