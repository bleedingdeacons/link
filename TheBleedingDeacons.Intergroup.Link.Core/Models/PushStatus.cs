namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Whether this phone can actually be pushed to, as one of four answers
/// rather than a bool.
///
/// <para>Two separate things have to be true before a message can
/// announce itself on a closed app, and they fail for completely
/// different reasons: the build has to carry push at all — the iOS head
/// does not, see <c>PushRegistrar.ios.cs</c> — and the phone's owner has
/// to have left notifications switched on for Link. A single "push:
/// yes/no" collapses those into one word and tells a member nothing
/// about which of them to go and fix.</para>
///
/// <para>Hand carries the same four states for the same reason, in the
/// same shape. The wording differs because the consequence does: a Hand
/// handset that is not pushed to is a responder who might miss a call,
/// and a Link phone that is not pushed to is a member who reads a
/// message later.</para>
/// </summary>
public enum PushState
{
	/// <summary>
	/// This build has no push transport — an iOS build, or an Android one
	/// made without <c>google-services.json</c>. Poll-only, which is a
	/// documented working state rather than a fault.
	/// </summary>
	Unsupported = 0,

	/// <summary>
	/// The transport is there and the phone's owner has turned
	/// notifications off for Link. Messages still arrive and are still
	/// decrypted; nothing says so until the app is opened.
	/// </summary>
	Blocked,

	/// <summary>
	/// Transport and permission both present, and the intergroup has not
	/// been given this phone's registration — so nothing is being pushed
	/// to it. The silent failure this indicator exists for.
	/// </summary>
	Unregistered,

	/// <summary>Available and enabled. Push is working.</summary>
	Active,
}

/// <summary>
/// The push indicator's whole content: what to say, and what colour to
/// say it in.
///
/// <para><b>Why a model in Link.Core rather than three properties on the
/// view model.</b> The interesting part is the state machine — which of
/// the three inputs decides the answer, and in what order — and that is
/// exactly the part a view model cannot be unit tested for, because this
/// project's tests cannot reference the app. Here it is covered by the
/// same test run as everything else.</para>
///
/// <para>The colour is a hex string because Link.Core has no MAUI
/// workload and so cannot hold a <c>Color</c> at all. XAML converts it on
/// binding.</para>
/// </summary>
/// <param name="Supported">
/// Whether this build has a push transport at all —
/// <c>IPushRegistrar.Supported</c>.
/// </param>
/// <param name="Permitted">
/// Whether the phone's own settings still let Link show a notification.
/// </param>
/// <param name="Registered">
/// Whether the intergroup has been given this phone's push token and
/// accepted it, this run. See <c>DeviceAuthService.PushRegistered</c> for
/// why that is the honest question rather than "do we hold a token".
/// </param>
public sealed record PushStatus(bool Supported, bool Permitted, bool Registered)
{
	/// <summary>
	/// Before anything has been read. Shows as "not available", which is
	/// the safe way round: a phone briefly under-promising is one somebody
	/// checks, and the opposite is one that quietly stops telling anybody
	/// about their messages.
	/// </summary>
	public static PushStatus Unknown { get; } = new(false, false, false);

	/// <summary>
	/// The three inputs collapsed, in the order they can fail. Transport
	/// first, because permission on a build with no push is not a problem
	/// anybody can act on; permission before registration, because a
	/// registration on a silenced phone would report success for something
	/// that shows nothing.
	/// </summary>
	public PushState State =>
		!Supported ? PushState.Unsupported
		: !Permitted ? PushState.Blocked
		: !Registered ? PushState.Unregistered
		: PushState.Active;

	/// <summary>Available and enabled, both.</summary>
	public bool IsActive => State == PushState.Active;

	/// <summary>
	/// True for the two states a member can do something about, and false
	/// for a build that simply has no push. Lets the screen give the
	/// fixable cases the room they need without shouting about one nobody
	/// can change.
	/// </summary>
	public bool NeedsAttention => State is PushState.Blocked or PushState.Unregistered;

	public string Headline => State switch
	{
		PushState.Active => "Push notifications are on",
		PushState.Unregistered => "Push notifications are not connected",
		PushState.Blocked => "Push notifications are turned off",
		_ => "Push notifications are not available",
	};

	/// <summary>
	/// What it means for the person holding the phone: whether they get
	/// told about a message, and how late. Never in terms of FCM, tokens
	/// or transports.
	/// </summary>
	public string Detail => State switch
	{
		PushState.Active =>
			"New messages announce themselves as soon as they are sent, with Link closed.",
		PushState.Unregistered =>
			"This phone can take pushed messages, but the intergroup has no registration for it — so nothing is "
				+ "being pushed to it. Messages still arrive on the next sync, and none of them is lost.",
		PushState.Blocked =>
			"Notifications are switched off for Link in this phone's own settings. Messages still arrive and are "
				+ "still readable, but nothing will tell you about one until you open the app. Turn them back on there.",
		_ =>
			"This build has no push, so Link collects messages when it syncs. Nothing is lost — new messages "
				+ "arrive when you open the app or pull down to refresh.",
	};

	/// <summary>
	/// The dot. Green working, amber degraded but still covered by the
	/// sync, red silenced, grey no transport at all.
	///
	/// <para>Amber rather than red for an unregistered phone because the
	/// sync is carrying it and every message still arrives. Red is kept
	/// for the state where a message can arrive and the member is never
	/// told.</para>
	/// </summary>
	public string IndicatorColour => State switch
	{
		PushState.Active => "#2E7D32",
		PushState.Unregistered => "#F9A825",
		PushState.Blocked => "#B3261E",
		_ => "#757575",
	};
}
