using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Support;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// Who this handset is signed in as, and the three things a member can do
/// about it.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
	private readonly DeviceAuthService _auth;
	private readonly ISessionStore _sessions;
	private readonly IMessageHistory _history;
	private readonly IPushRegistrar _registrar;
	private readonly INotificationPermission _notifications;
	private readonly IArrivalSound _sound;

	public SettingsViewModel(
		DeviceAuthService auth,
		ISessionStore sessions,
		IMessageHistory history,
		IPushRegistrar registrar,
		INotificationPermission notifications,
		IArrivalSound sound)
	{
		_auth = auth;
		_sessions = sessions;
		_history = history;
		_registrar = registrar;
		_notifications = notifications;
		_sound = sound;
		_soundOn = sound.Enabled;
	}

	/// <summary>
	/// Whether a message arriving with the app open makes a noise.
	///
	/// <para>Read once into the backing field at construction and written
	/// straight through on change, rather than proxying the preference on
	/// every get: this drives a Switch, which reads it more often than
	/// anybody toggles it.</para>
	///
	/// <para>A fellowship phone sits in meetings. Somewhere to turn this
	/// off is not a nicety.</para>
	/// </summary>
	[ObservableProperty]
	private bool _soundOn;

	partial void OnSoundOnChanged(bool value) => _sound.Enabled = value;

	[ObservableProperty]
	private string _memberName = string.Empty;

	[ObservableProperty]
	private int _held;

	[ObservableProperty]
	private bool _busy;

	/// <summary>
	/// Whether push is available in this build and enabled on this phone,
	/// as one thing the screen can draw.
	///
	/// <para>Link had nothing here at all: the one screen a member goes to
	/// when messages seem slow said nothing about the mechanism that makes
	/// them fast. See <see cref="PushStatus"/> for why it is four states
	/// rather than a tick.</para>
	///
	/// <para>Flattened onto three properties rather than left as a nested
	/// binding path, because this project compiles its XAML bindings and
	/// escalates the diagnostics that fire when one cannot be compiled —
	/// see the csproj. Shallow paths are what keeps that gate quiet.</para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PushHeadline))]
	[NotifyPropertyChangedFor(nameof(PushDetail))]
	[NotifyPropertyChangedFor(nameof(PushIndicatorColour))]
	private PushStatus _push = PushStatus.Unknown;

	public string PushHeadline => Push.Headline;

	public string PushDetail => Push.Detail;

	public string PushIndicatorColour => Push.IndicatorColour;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNotice))]
	private string _notice = string.Empty;

	public bool HasNotice => !string.IsNullOrEmpty(Notice);

	/// <summary>
	/// Which build is running, in Hand's words and Hand's order: version,
	/// build number, when it was made, which runtime.
	///
	/// <para>A plain get rather than an observable property. Nothing about
	/// it can change while the process lives, so raising change
	/// notifications for it would be notifying nobody about nothing.</para>
	///
	/// <para><b>Instance, not static</b>, which is why the analyser is
	/// silenced rather than obeyed. Hand made it static once and the label
	/// on its settings screen went blank: a XAML Binding resolves against
	/// the BindingContext <i>instance</i> and cannot see static members, so
	/// it binds to nothing. It fails silently in both directions - no
	/// warning, because a binding to a missing member is only a compile
	/// error when the compiler can prove the type, and nothing to notice on
	/// screen, because an empty label looks like an empty label.</para>
	/// </summary>
#pragma warning disable S2325 // See above: static breaks the binding.
	public string Build => BuildInfo.Summary;
#pragma warning restore S2325

	[RelayCommand]
	public async Task LoadAsync()
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(true);
		MemberName = session?.MemberName ?? string.Empty;

		var held = await _history.AllAsync().ConfigureAwait(true);
		Held = held.Count;

		await RefreshPushAsync().ConfigureAwait(true);
	}

	/// <summary>
	/// Work out whether push is both available and enabled, and say so.
	///
	/// <para>Read on every visit rather than cached. The state that most
	/// needs correcting is fixed somewhere other than in Link — a member
	/// switches notifications back on from the phone's own settings — so
	/// somebody who has just gone and done that must find this agreeing
	/// with them when they come back.</para>
	///
	/// <para>The registration half is
	/// <see cref="DeviceAuthService.PushRegistered"/>, which is whether
	/// the intergroup accepted a token, not whether this phone holds one.
	/// Those disagree in the case worth catching.</para>
	/// </summary>
	private async Task RefreshPushAsync()
	{
		// ConfigureAwait(true): the property set below drives a label and a
		// coloured dot on screen.
		var permitted = await _notifications.IsGrantedAsync().ConfigureAwait(true);

		Push = new PushStatus(
			Supported: _registrar.Supported,
			Permitted: permitted,
			Registered: _auth.PushRegistered);
	}

	/// <summary>
	/// Clear the messages held on this handset.
	///
	/// <para><b>The confirmation says what it actually does</b>, because
	/// "clear history" reads to most people as "delete the messages" and
	/// it is not that at all. Other people still have their copies,
	/// nothing is unsent, and the intergroup's own record is untouched.
	/// Saying so is the difference between a member who uses this to tidy
	/// up and one who uses it believing they have recalled something.</para>
	///
	/// <para>What it no longer says is that the messages come back on the
	/// next sync. They did, within seconds, because the poll asked for
	/// everything above the highest id held and a cleared store held
	/// none — so the warning was accurate and the button was useless.
	/// <see cref="IMessageHistory.ClearAsync"/> now leaves a mark behind
	/// and cleared messages stay cleared.</para>
	/// </summary>
	[RelayCommand]
	private async Task ClearHistoryAsync()
	{
		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null)
		{
			return;
		}

		var confirmed = await page.DisplayAlertAsync(
			"Clear messages on this phone?",
			"This deletes the copies held on this handset, and they will not come back. It does not unsend anything — "
				+ "everyone else still has theirs, and the intergroup keeps its own record.",
			"Clear",
			"Keep them").ConfigureAwait(true);

		if (!confirmed)
		{
			return;
		}

		await _history.ClearAsync().ConfigureAwait(true);
		await LoadAsync().ConfigureAwait(true);

		Notice = "The messages held on this phone have been cleared.";
	}

	/// <summary>
	/// Recover from "my messages will not open".
	///
	/// <para>Replaces the keypair and tells Fellowship the new public
	/// half. The device row and its place in the intergroup's list
	/// survive, so nobody has to re-enrol — but messages already sent stay
	/// unreadable, because they were sealed to a key that no longer exists
	/// anywhere, including on the server. The confirmation says that.
	/// </para>
	/// </summary>
	[RelayCommand]
	private async Task ReplaceKeyAsync()
	{
		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null)
		{
			return;
		}

		var confirmed = await page.DisplayAlertAsync(
			"Fix messages that will not open?",
			"This gives the intergroup a new key for this phone. New messages will open normally. "
				+ "Messages already sent to this phone cannot be recovered — nobody, including the intergroup, can unlock them now.",
			"Get a new key",
			"Cancel").ConfigureAwait(true);

		if (!confirmed)
		{
			return;
		}

		Busy = true;

		try
		{
			var ok = await _auth.ReplaceKeyAsync().ConfigureAwait(true);

			Notice = ok
				? "This phone has a new key. New messages will open normally."
				: "Could not reach the intergroup. Try again when you have a connection.";
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// Sign out, and take the local copies with it.
	///
	/// <para>The history goes too, and that is not optional. Signing out
	/// on a shared or handed-on phone has to mean the messages are gone
	/// from it; leaving them behind for whoever signs in next would make
	/// this button worse than useless.</para>
	/// </summary>
	[RelayCommand]
	private async Task SignOutAsync()
	{
		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null)
		{
			return;
		}

		var confirmed = await page.DisplayAlertAsync(
			"Sign out?",
			"This phone will stop receiving messages, and the messages held on it will be deleted.",
			"Sign out",
			"Cancel").ConfigureAwait(true);

		if (!confirmed)
		{
			return;
		}

		Busy = true;

		try
		{
			await _auth.SignOutAsync().ConfigureAwait(true);

			// Reset, not clear. Clearing leaves behind the point it
			// reached so the poll does not fetch it all straight back —
			// which is right for the member who asked for it, and wrong
			// for whoever signs in next: they would inherit a mark set
			// against somebody else's messages and never see their own
			// history, with nothing on screen to say why.
			await _history.ResetAsync().ConfigureAwait(true);

			if (Shell.Current is AppShell shell)
			{
				await shell.RefreshAsync().ConfigureAwait(true);
			}
		}
		finally
		{
			Busy = false;
		}
	}
}
