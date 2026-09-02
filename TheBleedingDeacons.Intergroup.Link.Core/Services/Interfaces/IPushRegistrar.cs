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
	Task<string> CurrentTokenAsync();
}
