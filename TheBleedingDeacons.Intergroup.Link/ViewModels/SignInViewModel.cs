using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The sign-in screen.
///
/// <para>Three ways in, in a deliberate order. Google and Apple first,
/// because they ask a member to invent or remember nothing. A password
/// underneath, for the member whose intergroup address is neither a Google
/// nor an Apple account and who would otherwise have no way in at all.
/// </para>
///
/// <para>Sign-in fails most often for a member whose address the
/// intergroup does not hold, and telling them that plainly — rather than
/// "authentication failed" — is the difference between a member who
/// emails their secretary and one who deletes the app.</para>
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
	private readonly DeviceAuthService _auth;
	private readonly FellowshipConfiguration _configuration;
	private readonly FingerprintSignIn _fingerprint;
	private readonly FirebaseSessionsOnly _sessions;
	private readonly MessageBackup _backup;
	private readonly IBackupFiles _backupFiles;

	/// <summary>The file chosen, until its passphrase has opened it.</summary>
	private byte[]? _chosenBackup;

	public SignInViewModel(
		DeviceAuthService auth,
		FellowshipConfiguration configuration,
		FingerprintSignIn fingerprint,
		FirebaseSessionsOnly sessions,
		MessageBackup backup,
		IBackupFiles backupFiles)
	{
		_backup = backup;
		_backupFiles = backupFiles;
		_sessions = sessions;
		_auth = auth;
		_configuration = configuration;
		_fingerprint = fingerprint;
	}

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string _error = string.Empty;

	/// <summary>
	/// A neutral message, for the things that are not failures.
	///
	/// <para>Separate from <see cref="Error"/> so "check your email" is
	/// not painted in the warning colour. It is also what the password
	/// request has to say whether or not a link went out, since the
	/// server will not reveal which.</para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNotice))]
	private string _notice = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SignInWithGoogleCommand))]
	[NotifyCanExecuteChangedFor(nameof(SignInWithAppleCommand))]
	[NotifyCanExecuteChangedFor(nameof(SignInWithPasswordCommand))]
	[NotifyCanExecuteChangedFor(nameof(RequestPasswordLinkCommand))]
	[NotifyCanExecuteChangedFor(nameof(SetPasswordCommand))]
	[NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
	[NotifyCanExecuteChangedFor(nameof(OpenBackupCommand))]
	private bool _busy;

	/// <summary>Whether the passphrase for a chosen backup is being asked for.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanOfferRestore))]
	private bool _openingBackup;

	[ObservableProperty]
	private string _backupPassphrase = string.Empty;

	/// <summary>What the sign-in about to happen will do with the backup that is open.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasHeldBackup))]
	[NotifyPropertyChangedFor(nameof(CanOfferRestore))]
	private string _heldBackup = string.Empty;

	public bool HasHeldBackup => !string.IsNullOrEmpty(HeldBackup);

	/// <summary>Whether to offer restoring: the build has an intergroup, and no backup is under way.</summary>
	public bool CanOfferRestore => IsConfigured && !OpeningBackup && !HasHeldBackup;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _password = string.Empty;

	/// <summary>
	/// The code from the emailed link, and the password to go with it.
	/// </summary>
	[ObservableProperty]
	private string _resetCode = string.Empty;

	[ObservableProperty]
	private string _newPassword = string.Empty;

	/// <summary>
	/// Whether the set-a-password section is open.
	///
	/// <para>Collapsed by default. It is the least-used part of the least
	/// used sign-in method, and putting it on screen permanently would
	/// suggest a password is expected — which for nearly every member it
	/// is not.</para>
	/// </summary>
	[ObservableProperty]
	private bool _settingPassword;

	public bool HasError => !string.IsNullOrEmpty(Error);

	public bool HasNotice => !string.IsNullOrEmpty(Notice);

	/// <summary>
	/// Whether this build knows which intergroup it belongs to.
	///
	/// <para>Shown as its own state rather than as a failed sign-in: a
	/// build with no BaseUrl is a packaging mistake, and a member being
	/// told to check their password for it would be a poor use of their
	/// afternoon.</para>
	/// </summary>
	public bool IsConfigured => _configuration.IsConfigured;

	/// <summary>
	/// Whether to offer Sign in with Apple at all.
	///
	/// <para>False on Android, and false on an iOS build without the
	/// entitlement — which a sideloaded build cannot have, because that
	/// entitlement needs a paid Apple Developer Program team. Hidden
	/// rather than shown-and-broken: a button that always fails teaches a
	/// member that the app is unreliable, which is a worse lie than not
	/// offering the choice.</para>
	/// </summary>
	public bool IsAppleAvailable => _auth.IsAppleAvailable;

	/// <summary>
	/// Whether the Apple button is shown: the platform can raise the sheet
	/// <i>and</i> this build knows which intergroup to enrol against.
	/// </summary>
	public bool CanOfferApple => IsConfigured && IsAppleAvailable;

	/// <summary>
	/// Which build is running, exactly as Settings shows it: version, build
	/// number, when it was made, which runtime.
	///
	/// <para>On this page too because Settings cannot be reached from it.
	/// The member who cannot sign in is the one most likely to be asked
	/// which version they have, and until now they had no way to
	/// tell.</para>
	///
	/// <para>Instance, not static, for the reason given on
	/// <c>SettingsViewModel.Build</c>: a XAML binding cannot see a static
	/// member and binds to nothing, silently.</para>
	/// </summary>
#pragma warning disable S2325 // See above: static breaks the binding.
	public string Build => BuildInfo.Summary;
#pragma warning restore S2325

	/// <summary>
	/// The site this build signs in to, as host and path — e.g.
	/// <c>aa-bristol.org/amber</c>. Empty for a build with no intergroup,
	/// which already says so in amber at the top.
	///
	/// <para>The other half of "which release is this": a build pointed at
	/// the test bed looks identical to a live one until a sign-in is
	/// refused for reasons nobody can see.</para>
	/// </summary>
	public string Server =>
		IsConfigured && Uri.TryCreate(_configuration.BaseUrl, UriKind.Absolute, out var site)
			? site.Host + site.AbsolutePath.TrimEnd('/')
			: string.Empty;

	/// <summary>
	/// Say why signing in again is needed, if this launch retired an old session. Once:
	/// it means nothing after they've read it.
	/// </summary>
	public void ShowWhySigningIn()
	{
		if (_sessions.RetiredOne && !_toldWhySigningIn)
		{
			_toldWhySigningIn = true;
			Notice = FirebaseSessionsOnly.Reason;
		}
	}

	private bool _toldWhySigningIn;

	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SignInWithGoogleAsync()
	{
		await RunAsync(() => _auth.SignInWithGoogleAsync()).ConfigureAwait(true);
	}

	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SignInWithAppleAsync()
	{
		await RunAsync(() => _auth.SignInWithAppleAsync()).ConfigureAwait(true);
	}

	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SignInWithPasswordAsync()
	{
		await RunAsync(() => _auth.SignInWithPasswordAsync(Email, Password)).ConfigureAwait(true);

		// Held only as long as it takes to send. Whether the sign-in
		// worked or not, there is no reason for it to stay in a bound
		// property afterwards.
		Password = string.Empty;
	}

	/// <summary>
	/// Ask for a code by email.
	///
	/// <para><b>The message never varies.</b> The server answers the same
	/// thing for a member, for somebody who may not use Link and for an
	/// address that belongs to nobody — deliberately, so that asking is
	/// not a way to find out who is a member. Saying anything more
	/// specific here would give away what the server took care not to.
	/// </para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task RequestPasswordLinkAsync()
	{
		if (string.IsNullOrWhiteSpace(Email))
		{
			Error = "Please enter your email address first.";
			return;
		}

		Busy = true;
		Error = string.Empty;
		Notice = string.Empty;

		try
		{
			var reached = await _auth.RequestPasswordLinkAsync(Email).ConfigureAwait(true);

			Notice = reached
				? "If that address belongs to a member, a code is on its way. It expires in an hour."
				: string.Empty;

			if (!reached)
			{
				Error = "Could not reach the intergroup. Check your connection and try again.";
				return;
			}

			SettingPassword = true;
		}
#pragma warning disable CA1031 // As in RunAsync: whatever it was, the member is told.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Serilog.Log.Error(ex, "Asking for a password code threw");
			Error = EnrolmentResult.Threw(ex).Error;
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// Set the password, with the code from the email.
	///
	/// <para>Does not sign in afterwards. The member returns to the
	/// buttons above and uses the password they just chose, which keeps
	/// setting a password and using one as two separate acts — so a code
	/// that reaches the wrong handset cannot enrol it.</para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SetPasswordAsync()
	{
		Busy = true;
		Error = string.Empty;
		Notice = string.Empty;

		try
		{
			var result = await _auth.SetPasswordAsync(ResetCode, NewPassword).ConfigureAwait(true);

			if (!result.Succeeded)
			{
				Error = result.Error;
				return;
			}

			// The password moves to the sign-in field, so the next thing
			// the member does is the obvious one. The code is spent and
			// the new password is not kept anywhere else.
			Password = NewPassword;
			NewPassword = string.Empty;
			ResetCode = string.Empty;
			SettingPassword = false;

			Notice = "Password set. You can sign in with it now.";
		}
#pragma warning disable CA1031 // As in RunAsync: whatever it was, the member is told.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Serilog.Log.Error(ex, "Setting a password threw");
			Error = EnrolmentResult.Threw(ex).Error;
		}
		finally
		{
			Busy = false;
		}
	}

	[RelayCommand]
	private void ToggleSettingPassword()
	{
		SettingPassword = !SettingPassword;

		Error = string.Empty;
		Notice = string.Empty;
	}

	/// <summary>Choose a backup to move this phone onto. The passphrase is asked for once there is one.</summary>
	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task StartRestoreAsync()
	{
		Error = string.Empty;
		Notice = string.Empty;

		_chosenBackup = await _backupFiles.PickAsync().ConfigureAwait(true);

		OpeningBackup = _chosenBackup is not null;
	}

	/// <summary>
	/// Open the chosen backup and hold it for the sign-in. A wrong passphrase leaves
	/// the question open, so the member needn't choose the file a second time.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task OpenBackupAsync()
	{
		if (_chosenBackup is not { } file)
		{
			return;
		}

		Busy = true;
		Error = string.Empty;

		try
		{
			var held = await _backup.HoldForSignInAsync(file, BackupPassphrase).ConfigureAwait(true);

			BackupPassphrase = string.Empty;

			if (held.Outcome != BackupRestoreOutcome.Held)
			{
				Error = held.Message;
				return;
			}

			_chosenBackup = null;
			OpeningBackup = false;
			HeldBackup = held.Message;
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>Put the backup down, and the key in it. Signing in is then an ordinary sign-in.</summary>
	[RelayCommand]
	private void CancelRestore()
	{
		_backup.ForgetHeld();
		_chosenBackup = null;
		OpeningBackup = false;
		BackupPassphrase = string.Empty;
		HeldBackup = string.Empty;
		Error = string.Empty;
	}

	/// <summary>
	/// Say what became of a backup held for this sign-in, and put its choices back.
	/// On the main thread, which a style needs.
	/// </summary>
	private async Task ShowRestoredAsync(BackupRestore restored, long memberId)
	{
		HeldBackup = string.Empty;

		if (restored.Succeeded)
		{
			_backup.ApplySettings(restored.Settings);

			if (restored.MadeAt is { } madeAt)
			{
				_backupFiles.RememberBackup(memberId, madeAt);
			}
		}

		await Shell.Current.DisplayAlertAsync(
			restored.Succeeded ? "Backup restored" : "Backup not restored",
			restored.Message,
			"OK").ConfigureAwait(true);
	}

	/// <summary>
	/// The shared half of every sign-in: busy on, errors cleared, and the
	/// shell refreshed if a session came back.
	///
	/// <para><b>Every way it ends says something.</b> A refusal shows the
	/// server's words, a sign-in that stopped says so neutrally, and an
	/// exception shows its own message rather than escaping the command.
	/// A handset that has never signed in has no log anyone can read, and
	/// a failure that never reached the intergroup is in no log at all, so
	/// the screen is the only witness.</para>
	/// </summary>
	private async Task RunAsync(Func<Task<EnrolmentResult>> signIn)
	{
		Busy = true;
		Error = string.Empty;
		Notice = string.Empty;

		try
		{
			var result = await signIn().ConfigureAwait(true);

			if (result.Succeeded)
			{
				// The phone is on the list now, and whoever is holding it has
				// just proved who they are — the moment to set up the finger
				// that gets them back in after signing out. A phone with no
				// fingerprint is not asked, and a closed sheet leaves it off;
				// Settings can turn it on later. Either way the member goes
				// on to their messages.
				await _fingerprint.SetUpAsync().ConfigureAwait(true);

				var restored = _auth.TakeRestored();

				if (Shell.Current is AppShell shell)
				{
					await shell.RefreshAsync().ConfigureAwait(true);
				}

				if (restored is not null)
				{
					await ShowRestoredAsync(restored, result.Session!.MemberId).ConfigureAwait(true);
				}

				return;
			}

			Error = result.Error;
			Notice = result.Notice;
		}
#pragma warning disable CA1031 // Deliberately broad: whatever it was, the member is told.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// DeviceAuthService has already logged it inside its attempt.
			Error = EnrolmentResult.Threw(ex).Error;
		}
		finally
		{
			Busy = false;
		}
	}

	private bool CanSignIn() => !Busy && IsConfigured;
}
