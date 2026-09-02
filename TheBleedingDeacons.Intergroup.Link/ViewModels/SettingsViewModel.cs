using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Services;
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

	public SettingsViewModel(DeviceAuthService auth, ISessionStore sessions, IMessageHistory history)
	{
		_auth = auth;
		_sessions = sessions;
		_history = history;
	}

	[ObservableProperty]
	private string _memberName = string.Empty;

	[ObservableProperty]
	private int _held;

	[ObservableProperty]
	private bool _busy;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNotice))]
	private string _notice = string.Empty;

	public bool HasNotice => !string.IsNullOrEmpty(Notice);

	[RelayCommand]
	public async Task LoadAsync()
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(true);
		MemberName = session?.MemberName ?? string.Empty;

		var held = await _history.AllAsync().ConfigureAwait(true);
		Held = held.Count;
	}

	/// <summary>
	/// Clear the messages held on this handset.
	///
	/// <para><b>The confirmation says what it actually does</b>, because
	/// "clear history" reads to most people as "delete the messages" and
	/// it is not that at all. Other people still have their copies,
	/// nothing is unsent, and anything the intergroup has not yet aged out
	/// comes back on the next sync. Saying so is the difference between a
	/// member who uses this to tidy up and one who uses it believing they
	/// have recalled something.</para>
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
			"This deletes the copies held on this handset. It does not unsend anything — everyone else still has theirs — "
				+ "and recent messages will come back the next time this app syncs.",
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
			await _history.ClearAsync().ConfigureAwait(true);

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
