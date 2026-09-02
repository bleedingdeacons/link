using Android.App;
using Android.Content;
using Android.Content.PM;

namespace TheBleedingDeacons.Intergroup.Link;

/// <summary>
/// Catches the redirect that ends the browser leg of a Google sign-in.
///
/// <para>The scheme and host here have to agree with three other places:
/// <c>appsettings.json</c>'s CallbackUrl, Fellowship's
/// <c>DeviceRedirectValidator</c> allow-list, and the redirect URI
/// registered with Google. All four say <c>link://auth</c>. A mismatch
/// shows up as a browser tab that opens and never comes back, with
/// nothing in any log to say why.</para>
///
/// <para>What arrives here is a one-time code, never a token — see
/// Fellowship's <c>DeviceCodeStore</c>. A custom scheme can in principle
/// be claimed by another app on the device, which is exactly why the
/// thing that travels this way is worthless two minutes later and
/// worthless once used, and why the credential itself is fetched over
/// TLS from this process afterwards.</para>
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "link",
	DataHost = "auth")]
public sealed class WebAuthenticatorCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
