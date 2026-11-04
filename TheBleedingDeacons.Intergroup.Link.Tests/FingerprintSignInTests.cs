using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// When signing out is a lock, and what gets a member back in.
///
/// <para>The edges: a phone whose fingerprints went while it was locked,
/// a sheet that answered something other than a match, and a sign-in by
/// somebody new on a phone that was locked by somebody else.</para>
/// </summary>
public class FingerprintSignInTests
{
	private readonly StubReader _reader = new();
	private readonly MemoryState _state = new();

	private FingerprintSignIn Subject => new(_reader, _state);

	[Fact]
	public async Task A_matched_finger_at_set_up_turns_it_on()
	{
		var result = await Subject.SetUpAsync();

		Assert.True(result.Confirmed);
		Assert.True(_state.Enabled);
		Assert.Equal(FingerprintSignIn.SetUpPrompt, _reader.LastSubtitle);
	}

	[Theory]
	[InlineData(FingerprintOutcome.Cancelled)]
	[InlineData(FingerprintOutcome.Failed)]
	[InlineData(FingerprintOutcome.Unavailable)]
	public async Task Anything_but_a_match_at_set_up_leaves_it_off(FingerprintOutcome answer)
	{
		_reader.Answer = new FingerprintResult(answer);

		await Subject.SetUpAsync();

		Assert.False(_state.Enabled);
	}

	[Fact]
	public async Task A_phone_with_no_fingerprint_is_never_asked()
	{
		_reader.Available = false;

		var result = await Subject.SetUpAsync();

		Assert.Equal(FingerprintOutcome.Unavailable, result.Outcome);
		Assert.Equal(0, _reader.Asked);
		Assert.False(_state.Enabled);
	}

	[Fact]
	public async Task Setting_up_clears_a_lock_somebody_else_left()
	{
		_state.Enabled = true;
		_state.Locked = true;
		_reader.Answer = new FingerprintResult(FingerprintOutcome.Cancelled);

		await Subject.SetUpAsync();

		Assert.False(_state.Locked);
	}

	[Fact]
	public void Signing_out_without_it_set_up_does_not_lock()
	{
		Assert.False(Subject.Lock());
		Assert.False(_state.Locked);
	}

	[Fact]
	public void Signing_out_with_it_set_up_locks()
	{
		_state.Enabled = true;

		Assert.True(Subject.Lock());
		Assert.True(Subject.IsLocked);
	}

	[Fact]
	public void A_phone_whose_fingerprints_have_gone_still_locks_behind_its_code()
	{
		_state.Enabled = true;
		_reader.Available = false;

		Assert.True(Subject.CanLock);
		Assert.True(Subject.Lock());
	}

	[Fact]
	public void A_phone_with_neither_fingerprint_nor_code_is_not_locked_on_sign_out()
	{
		_state.Enabled = true;
		_reader.Available = false;
		_reader.HasCode = false;

		Assert.False(Subject.CanLock);
		Assert.False(Subject.Lock());
	}

	[Fact]
	public async Task A_phone_with_only_a_code_is_not_set_up()
	{
		// Fingerprint sign-in is set up on a phone that reads a finger. The
		// code is what it falls back to, not a way to start.
		_reader.Available = false;

		await Subject.SetUpAsync();

		Assert.Equal(0, _reader.Asked);
		Assert.False(_state.Enabled);
	}

	[Fact]
	public async Task A_matched_finger_unlocks()
	{
		_state.Enabled = true;
		_state.Locked = true;

		var result = await Subject.UnlockAsync();

		Assert.True(result.Confirmed);
		Assert.False(_state.Locked);
		Assert.True(_state.Enabled);
		Assert.Equal(FingerprintSignIn.UnlockPrompt, _reader.LastSubtitle);
	}

	[Theory]
	[InlineData(FingerprintOutcome.Cancelled)]
	[InlineData(FingerprintOutcome.Failed)]
	public async Task Anything_but_a_match_stays_locked(FingerprintOutcome answer)
	{
		_state.Enabled = true;
		_state.Locked = true;
		_reader.Answer = new FingerprintResult(answer);

		var result = await Subject.UnlockAsync();

		Assert.Equal(answer, result.Outcome);
		Assert.True(_state.Locked);
	}

	[Fact]
	public async Task A_locked_phone_that_stopped_reading_fingers_gets_in_with_its_code()
	{
		_state.Enabled = true;
		_state.Locked = true;
		_reader.Available = false;

		var result = await Subject.UnlockAsync();

		Assert.True(result.Confirmed);
		Assert.Equal(1, _reader.Asked);
		Assert.False(_state.Locked);
	}

	[Fact]
	public async Task A_locked_phone_with_neither_fingerprint_nor_code_says_so_and_stays_locked()
	{
		_state.Enabled = true;
		_state.Locked = true;
		_reader.Available = false;
		_reader.HasCode = false;

		var result = await Subject.UnlockAsync();

		Assert.Equal(FingerprintOutcome.Unavailable, result.Outcome);
		Assert.NotEmpty(result.Detail);
		Assert.Equal(0, _reader.Asked);
		Assert.True(_state.Locked);
	}

	[Fact]
	public async Task An_unlocked_phone_is_not_asked_again()
	{
		_state.Enabled = true;

		var result = await Subject.UnlockAsync();

		Assert.True(result.Confirmed);
		Assert.Equal(0, _reader.Asked);
	}

	[Fact]
	public void A_lock_without_it_set_up_is_not_a_lock()
	{
		// Only reachable by a preference written out of step. The shell must
		// not send anybody to a fingerprint page they have no way through.
		_state.Locked = true;

		Assert.False(Subject.IsLocked);
	}

	[Fact]
	public void Forgetting_turns_it_off_and_unlocks()
	{
		_state.Enabled = true;
		_state.Locked = true;

		Subject.Forget();

		Assert.False(_state.Enabled);
		Assert.False(_state.Locked);
	}

	private sealed class StubReader : IFingerprintReader
	{
		public bool Available { get; set; } = true;

		public FingerprintResult Answer { get; set; } = new(FingerprintOutcome.Confirmed);

		public int Asked { get; private set; }

		public string LastSubtitle { get; private set; } = string.Empty;

		/// <summary>Whether the phone has a screen-lock code to fall back to.</summary>
		public bool HasCode { get; set; } = true;

		public bool IsAvailable => Available;

		public bool CanConfirm => Available || HasCode;

		public Task<FingerprintResult> ConfirmAsync(string title, string subtitle, CancellationToken cancellationToken = default)
		{
			Asked++;
			LastSubtitle = subtitle;

			return Task.FromResult(Answer);
		}
	}

	private sealed class MemoryState : IFingerprintState
	{
		public bool Enabled { get; set; }

		public bool Locked { get; set; }
	}
}
