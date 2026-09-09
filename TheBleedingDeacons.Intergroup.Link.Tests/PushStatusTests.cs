using TheBleedingDeacons.Intergroup.Link.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The push indicator's state machine.
///
/// <para>The point of these is the <i>order</i> the three inputs are
/// consulted in. Any of them can be false at once, and the state that
/// comes out has to be the one a member can act on — telling somebody on
/// an iOS build to go and switch notifications back on would be worse
/// than saying nothing.</para>
///
/// <para>Hand carries the same cases against its own copy of this model.
/// The two are kept in step by hand, because the two apps share no
/// library.</para>
/// </summary>
public class PushStatusTests
{
	[Fact]
	public void All_three_present_is_active()
	{
		var status = new PushStatus(Supported: true, Permitted: true, Registered: true);

		Assert.Equal(PushState.Active, status.State);
		Assert.True(status.IsActive);
		Assert.False(status.NeedsAttention);
	}

	[Fact]
	public void No_transport_is_unsupported()
	{
		var status = new PushStatus(Supported: false, Permitted: true, Registered: true);

		Assert.Equal(PushState.Unsupported, status.State);
		Assert.False(status.IsActive);
	}

	[Fact]
	public void Silenced_phone_is_blocked()
	{
		var status = new PushStatus(Supported: true, Permitted: false, Registered: true);

		Assert.Equal(PushState.Blocked, status.State);
	}

	[Fact]
	public void Transport_and_permission_without_a_server_registration_is_unregistered()
	{
		var status = new PushStatus(Supported: true, Permitted: true, Registered: false);

		Assert.Equal(PushState.Unregistered, status.State);
	}

	/// <summary>
	/// A build with no push is reported as that and nothing else, however
	/// the other two read. Sending an iOS member to a notification
	/// settings screen they cannot fix this from is the failure being
	/// guarded against.
	/// </summary>
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void No_transport_beats_everything_else(bool permitted, bool registered)
	{
		var status = new PushStatus(Supported: false, Permitted: permitted, Registered: registered);

		Assert.Equal(PushState.Unsupported, status.State);
		Assert.False(status.NeedsAttention);
	}

	/// <summary>
	/// A silenced phone is reported as silenced even when it is also
	/// unregistered: registering it would change nothing while the phone
	/// is still refusing to show anything.
	/// </summary>
	[Fact]
	public void Permission_is_reported_before_registration()
	{
		var status = new PushStatus(Supported: true, Permitted: false, Registered: false);

		Assert.Equal(PushState.Blocked, status.State);
	}

	/// <summary>
	/// Both of the fixable states say so, so the screen can give them the
	/// room they need.
	/// </summary>
	[Fact]
	public void Blocked_and_unregistered_both_need_attention()
	{
		Assert.True(new PushStatus(true, false, true).NeedsAttention);
		Assert.True(new PushStatus(true, true, false).NeedsAttention);
	}

	[Fact]
	public void Unknown_reads_as_no_push_rather_than_working_push()
	{
		Assert.Equal(PushState.Unsupported, PushStatus.Unknown.State);
		Assert.False(PushStatus.Unknown.IsActive);
	}

	/// <summary>
	/// Every state has something to say and a colour to say it in, and no
	/// two states share a colour — the dot is the whole indicator at a
	/// glance, so a duplicate would make two different situations look
	/// identical.
	/// </summary>
	[Fact]
	public void Every_state_has_its_own_words_and_its_own_colour()
	{
		PushStatus[] all =
		[
			new(false, true, true),
			new(true, false, true),
			new(true, true, false),
			new(true, true, true),
		];

		Assert.All(all, s => Assert.NotEmpty(s.Headline));
		Assert.All(all, s => Assert.NotEmpty(s.Detail));

		Assert.Equal(4, all.Select(s => s.State).Distinct().Count());
		Assert.Equal(4, all.Select(s => s.Headline).Distinct().Count());
		Assert.Equal(4, all.Select(s => s.IndicatorColour).Distinct().Count());
	}
}
