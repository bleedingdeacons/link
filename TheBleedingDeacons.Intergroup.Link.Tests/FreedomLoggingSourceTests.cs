using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;
using EnrolmentResult = TheBleedingDeacons.Freedom.Client.EnrolmentResult;
using SyncResult = TheBleedingDeacons.Freedom.Client.SyncResult;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The Better Stack settings from Freedom, signed in to with the handset's
/// Fellowship session.
///
/// <para>Freedom's client is tested in freedom-sharp; these are Link's
/// decisions on top of it. When to hand the session over — only when
/// Freedom has no tablet for this handset — and which answers count: a
/// clean sync is an answer, even one with no values (the intergroup has
/// said not to ship); anything else is no answer, and the logger keeps
/// what it has.</para>
/// </summary>
public sealed class FreedomLoggingSourceTests
{
	private static readonly DeviceSession Session = new() { Token = "fdt_test", DeviceId = 1, MemberId = 2 };

	private readonly FakeFreedom _freedom = new();

	private readonly FakeHandover _handover = new() { IsCurrent = true };

	private FreedomLoggingSource Source() => new(_freedom, _handover);

	[Fact]
	public async Task ASignedInHandsetReadsBothValues()
	{
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		var config = await Source().FetchAsync(Session);

		Assert.NotNull(config);
		Assert.Equal("https://s1.betterstackdata.com", config.Endpoint);
		Assert.Equal("src-token", config.SourceToken);
		Assert.True(config.IsValid());
		Assert.Null(_freedom.HandedOver);
	}

	/// <summary>
	/// The point of the handover: a handset that has signed in to
	/// Fellowship never sees a second Google screen.
	/// </summary>
	[Theory]
	[InlineData(SyncStatus.NotEnrolled)]
	[InlineData(SyncStatus.Revoked)]
	public async Task HandsTheSessionOverWhenFreedomHasNoTablet(SyncStatus status)
	{
		_freedom.Syncs.Enqueue(status);
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		var config = await Source().FetchAsync(Session);

		Assert.Equal("fdt_test", _freedom.HandedOver);
		Assert.True(config!.IsValid());
	}

	[Fact]
	public async Task AHandoverIsRecordedAsCurrent()
	{
		_handover.IsCurrent = false;
		_freedom.Syncs.Enqueue(SyncStatus.NotEnrolled);

		await Source().FetchAsync(Session);

		Assert.True(_handover.IsCurrent);
	}

	/// <summary>
	/// A handset signed in on an older build hands over once more, so the
	/// site's Devices tab gets this build's name, model and version.
	/// </summary>
	[Fact]
	public async Task AnUpdatedHandsetHandsOverOnceMore()
	{
		_handover.IsCurrent = false;
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		var config = await Source().FetchAsync(Session);

		Assert.Equal("fdt_test", _freedom.HandedOver);
		Assert.True(_handover.IsCurrent);
		Assert.True(config!.IsValid());
	}

	[Fact]
	public async Task ARefusedRefreshStillAnswersWithTheSync()
	{
		_handover.IsCurrent = false;
		_freedom.Enrolment = EnrolmentStatus.Refused;
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		var config = await Source().FetchAsync(Session);

		Assert.True(config!.IsValid());
		Assert.False(_handover.IsCurrent);
	}

	[Fact]
	public async Task NoAnswerDoesNotHandOverForTheRefresh()
	{
		_handover.IsCurrent = false;
		_freedom.Syncs.Enqueue(SyncStatus.Offline);

		Assert.Null(await Source().FetchAsync(Session));
		Assert.Null(_freedom.HandedOver);
	}

	[Fact]
	public async Task ARefusedHandoverIsNoAnswer()
	{
		_freedom.Syncs.Enqueue(SyncStatus.NotEnrolled);
		_freedom.Enrolment = EnrolmentStatus.Refused;

		Assert.Null(await Source().FetchAsync(Session));
	}

	[Theory]
	[InlineData(SyncStatus.Offline)]
	[InlineData(SyncStatus.ServerError)]
	[InlineData(SyncStatus.Suspended)]
	[InlineData(SyncStatus.NotAuthorised)]
	[InlineData(SyncStatus.NotConfigured)]
	public async Task AnythingButACleanSyncIsNoAnswer(SyncStatus status)
	{
		_freedom.Syncs.Enqueue(status);
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		Assert.Null(await Source().FetchAsync(Session));
		Assert.Null(_freedom.HandedOver);
	}

	/// <summary>
	/// A secret this handset could not open: Freedom kept the value it had,
	/// which is still the best answer there is.
	/// </summary>
	[Fact]
	public async Task AKeyFaultStillAnswersWithWhatWasKept()
	{
		_freedom.Syncs.Enqueue(SyncStatus.KeyFault);
		_freedom.Values[FreedomLoggingSource.EndpointKey] = "s1.betterstackdata.com";
		_freedom.Values[FreedomLoggingSource.SourceTokenKey] = "src-token";

		Assert.True((await Source().FetchAsync(Session))!.IsValid());
	}

	/// <summary>
	/// The link application with no Better Stack values is the intergroup
	/// saying not to ship — an answer, and one that drops what is held.
	/// </summary>
	[Fact]
	public async Task NoValuesIsAnAnswerNotToShip()
	{
		var config = await Source().FetchAsync(Session);

		Assert.NotNull(config);
		Assert.False(config.IsValid());
	}

	[Fact]
	public async Task ForgettingSignsOutOfFreedom()
	{
		await Source().ForgetAsync();

		Assert.Equal(1, _freedom.SignOuts);
	}

	[Fact]
	public async Task LastRetrievedIsFreedomsVerifiedTime()
	{
		_freedom.VerifiedAt = new DateTimeOffset(2026, 9, 30, 19, 57, 0, TimeSpan.Zero);

		Assert.Equal(_freedom.VerifiedAt, await Source().LastRetrievedAsync());
	}

	[Fact]
	public async Task RejectsNulls()
	{
		Assert.Throws<ArgumentNullException>(() => new FreedomLoggingSource(null!, _handover));
		Assert.Throws<ArgumentNullException>(() => new FreedomLoggingSource(_freedom, null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => Source().FetchAsync(null!));
	}

	private sealed class FakeHandover : IHandoverRecord
	{
		public bool IsCurrent { get; set; }

		public void MarkCurrent() => IsCurrent = true;
	}

	private sealed class FakeFreedom : IFreedomSession
	{
		public Queue<SyncStatus> Syncs { get; } = new();

		public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

		public EnrolmentStatus Enrolment { get; set; } = EnrolmentStatus.Enrolled;

		public string? HandedOver { get; private set; }

		public int SignOuts { get; private set; }

		public DateTimeOffset? VerifiedAt { get; set; }

		public Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(SyncResult.Nothing(Syncs.Count > 0 ? Syncs.Dequeue() : SyncStatus.UpToDate, DateTimeOffset.UtcNow, "fake"));

		public async Task<EnrolmentResult> EnrolAsync(FreedomProof proof, CancellationToken cancellationToken = default)
		{
			HandedOver = Assert.IsType<FreedomProof.SessionProof>(proof).Token;

			return Enrolment is EnrolmentStatus.Enrolled or EnrolmentStatus.Reattached
				? new EnrolmentResult(Enrolment, null, "fake", await SyncAsync(cancellationToken))
				: new EnrolmentResult(Enrolment, "not_authorised", "fake", null);
		}

		public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

		public Task SignOutAsync(CancellationToken cancellationToken = default)
		{
			SignOuts++;
			return Task.CompletedTask;
		}

		public Task<DateTimeOffset?> LastRetrievedAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(VerifiedAt);
	}
}
