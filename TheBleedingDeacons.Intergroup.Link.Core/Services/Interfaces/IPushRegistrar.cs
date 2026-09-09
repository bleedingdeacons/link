namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// This handset's push registration.
///
/// <para>A seam over Firebase, which is Android-only and per-head. It
/// lives in Link.Core so <see cref="DeviceAuthService"/> and the view
/// models can depend on it without dragging the workload in.</para>
///
/// <para><b>Empty is a normal answer, not a fault.</b> A phone with no
/// Play Services, one that has just installed the app and not yet been
/// handed a token, or the iOS head before its Firebase SDK is in place —
/// all of them answer empty, and all of them are perfectly usable
/// handsets that collect their messages by polling. Nothing here should
/// ever block on getting a token.</para>
/// </summary>
public interface IPushRegistrar
{
	/// <summary>
	/// Whether this build has a push transport at all.
	///
	/// <para><b>Not the same question as "is there a token".</b> An empty
	/// token means any of three things — no transport, a transport that
	/// has not answered yet, or a phone with no Play Services — and only
	/// the first of them is permanent. The settings indicator has to tell
	/// "this build will never be pushed to" from "it has not been
	/// registered yet", because one of those is worth a member's
	/// attention and the other is not. See <c>PushStatus</c>.</para>
	///
	/// <para>An Android head answers true even without
	/// <c>google-services.json</c>: the transport is compiled in and the
	/// missing file is a build-configuration fault, which shows as an
	/// unregistered phone rather than as an iOS-shaped one.</para>
	/// </summary>
	bool Supported { get; }

	Task<string> CurrentTokenAsync();
}
