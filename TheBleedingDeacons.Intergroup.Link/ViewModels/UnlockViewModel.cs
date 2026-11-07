using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The page a member who signed out behind a fingerprint comes back to.
///
/// <para>Two ways off it. The finger, which is the point — or the phone's
/// own screen-lock code, which the same sheet offers when the finger is
/// not taken; and signing in another way, for a member who can give
/// neither. That second way removes
/// the phone first — the same as Settings' "Remove this phone" — because
/// a fresh sign-in enrols a fresh device row, and leaving the old one live
/// beside it would be a second entry in the intergroup's list for one
/// phone.</para>
/// </summary>
public sealed partial class UnlockViewModel : ObservableObject
{
	private readonly FingerprintSignIn _fingerprint;
	private readonly DeviceAuthService _auth;
	private readonly ISessionStore _sessions;
	private readonly IMessageHistory _history;

	public UnlockViewModel(
		FingerprintSignIn fingerprint,
		DeviceAuthService auth,
		ISessionStore sessions,
		IMessageHistory history)
	{
		_fingerprint = fingerprint;
		_auth = auth;
		_sessions = sessions;
		_history = history;
	}

	[ObservableProperty]
	private string _memberName = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNotice))]
	private string _notice = string.Empty;

	public bool HasNotice => !string.IsNullOrEmpty(Notice);

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
	[NotifyCanExecuteChangedFor(nameof(SignInAnotherWayCommand))]
	private bool _busy;

	public async Task LoadAsync()
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(true);
		MemberName = session?.MemberName ?? string.Empty;
		Notice = string.Empty;
	}

	/// <summary>
	/// Ask for the finger, and go to the messages if it matched.
	///
	/// <para>A closed sheet says nothing, as a closed account picker says
	/// nothing on the sign-in page: the member knows what they did, and the
	/// button is still there.</para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(NotBusy))]
	private async Task UnlockAsync()
	{
		Busy = true;
		Notice = string.Empty;

		try
		{
			var result = await _fingerprint.UnlockAsync().ConfigureAwait(true);

			if (result.Confirmed)
			{
				if (Shell.Current is AppShell shell)
				{
					await shell.RefreshAsync().ConfigureAwait(true);
				}

				return;
			}

			Notice = result.Outcome switch
			{
				FingerprintOutcome.Cancelled => string.Empty,
				FingerprintOutcome.Unavailable => string.IsNullOrEmpty(result.Detail)
					? "This phone cannot take a fingerprint or its screen lock just now. Sign in another way to carry on."
					: result.Detail,
				_ => string.IsNullOrEmpty(result.Detail)
					? "That was not recognised. Try again with your fingerprint or your phone's screen lock, or sign in another way."
					: result.Detail,
			};
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// Remove this phone and go to the sign-in page.
	///
	/// <para>The same as Settings' "Remove this phone", and it says the same
	/// thing first: this is the path that deletes what is held here.</para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(NotBusy))]
	private async Task SignInAnotherWayAsync()
	{
		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null)
		{
			return;
		}

		var confirmed = await page.DisplayAlertAsync(
			"Sign in another way?",
			"This removes this phone from the intergroup's list and deletes the messages held on it. "
				+ "You can then sign in again the usual way.",
			"Continue",
			"Cancel").ConfigureAwait(true);

		if (!confirmed)
		{
			return;
		}

		Busy = true;

		try
		{
			await _auth.SignOutAsync().ConfigureAwait(true);

			// Reset, not clear, for the reason SettingsViewModel.RemovePhoneAsync gives.
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

	private bool NotBusy() => !Busy;
}
