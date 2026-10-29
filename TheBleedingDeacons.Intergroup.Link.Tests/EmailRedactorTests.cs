using System.Globalization;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Intergroup.Link.Support;
using TheBleedingDeacons.Inventory.Redaction;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Email addresses in the logs become per-handset tokens: hidden, but the
/// same address still lines up with itself across one handset's sign-in
/// attempts.
/// </summary>
public sealed class EmailRedactorTests
{
	private static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
	private static readonly byte[] KeyB = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

	private readonly EmailRedactor _redactor = new(KeyA);

	[Fact]
	public void AnAddressBecomesAToken()
	{
		var redacted = _redactor.Redact("Password sign-in for jo.bloggs@example.org");

		Assert.DoesNotContain("jo.bloggs", redacted, StringComparison.Ordinal);
		Assert.DoesNotContain("example.org", redacted, StringComparison.Ordinal);
		Assert.Matches("^Password sign-in for email:[0-9a-f]{8}$", redacted);
	}

	[Fact]
	public void TheSameAddressGivesTheSameTokenEveryTime()
	{
		Assert.Equal(_redactor.Token("jo@example.org"), _redactor.Token("jo@example.org"));
		Assert.Equal(_redactor.Token("jo@example.org"), new EmailRedactor(KeyA).Token("jo@example.org"));
	}

	[Fact]
	public void CaseAndSurroundingSpaceDoNotChangeTheToken()
	{
		// A member who typed it differently the second time is still the
		// same member failing twice.
		Assert.Equal(_redactor.Token("jo@example.org"), _redactor.Token("  Jo@Example.ORG "));
	}

	[Fact]
	public void DifferentAddressesGiveDifferentTokens()
	{
		Assert.NotEqual(_redactor.Token("jo@example.org"), _redactor.Token("sam@example.org"));
	}

	[Fact]
	public void AnotherHandsetGivesTheSameAddressADifferentToken()
	{
		Assert.NotEqual(_redactor.Token("jo@example.org"), new EmailRedactor(KeyB).Token("jo@example.org"));
	}

	[Fact]
	public void EveryAddressInTheTextIsReplaced()
	{
		var redacted = _redactor.Redact("From a@x.org, to b@y.co.uk; cc <c+tag@z.net>");

		Assert.DoesNotContain("@", redacted, StringComparison.Ordinal);
		Assert.Equal(3, redacted.Split("email:").Length - 1);
	}

	[Theory]
	[InlineData("No addresses here")]
	[InlineData("Enrolled as device 12 for member 7")]
	[InlineData("Reply @ the meeting")]
	[InlineData("")]
	public void TextWithoutAnAddressIsReturnedAsItWas(string text)
	{
		Assert.Same(text, _redactor.Redact(text));
	}

	[Fact]
	public void AShortKeyIsRefused()
	{
		Assert.Throws<ArgumentException>(() => new EmailRedactor(new byte[15]));
	}

	[Fact]
	public void ANewKeyIsRandom()
	{
		Assert.NotEqual(EmailRedactor.NewKey(), EmailRedactor.NewKey());
	}

	[Fact]
	public void ThroughInventoryAnAddressPropertyNeverReachesTheMessage()
	{
		// How it is wired in MauiProgram: Inventory's enricher, handed
		// Redact. The sign-in log writes the typed address as {Email}.
		var logEvent = new LogEvent(
			DateTimeOffset.UnixEpoch,
			LogEventLevel.Information,
			exception: null,
			new MessageTemplateParser().Parse("Password sign-in for {Email}"),
			[new LogEventProperty("Email", new ScalarValue("jo@example.org"))]);

		new RedactingEnricher(_redactor.Redact).Enrich(logEvent, null!);

		Assert.Equal(
			"Password sign-in for \"" + _redactor.Token("jo@example.org") + "\"",
			logEvent.RenderMessage(CultureInfo.InvariantCulture));
	}
}
