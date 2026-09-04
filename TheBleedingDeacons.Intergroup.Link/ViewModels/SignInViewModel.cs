using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The sign-in screen.
///
/// <para>Five ways in, in a deliberate order. Four providers first,
/// because they are how nearly everybody signs in and none of them asks
/// a member to invent or remember anything. A password underneath, for
/// the member whose intergroup address is not a Google, Microsoft, Apple
/// or Facebook account and who would otherwise have no way in at all.
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

	public SignInViewModel(DeviceAuthService auth, FellowshipConfiguration configuration)
	{
		_auth = auth;
		_configuration = configuration;
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
	[NotifyCanExecuteChangedFor(nameof(SignInWithProviderCommand))]
	[NotifyCanExecuteChangedFor(nameof(SignInWithAppleCommand))]
	[NotifyCanExecuteChangedFor(nameof(SignInWithPasswordCommand))]
	[NotifyCanExecuteChangedFor(nameof(RequestPasswordLinkCommand))]
	[NotifyCanExecuteChangedFor(nameof(SetPasswordCommand))]
	private bool _busy;

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
	/// Google, Microsoft or Facebook — every provider whose flow is a
	/// browser leg, chosen by the button that was tapped.
	///
	/// <para>One command with a parameter rather than three commands,
	/// because there is genuinely one behaviour here; three copies is
	/// how two of them end up subtly different. Apple is not one of
	/// these: it has no browser leg, so it gets its own command below
	/// rather than a special case inside this one.</para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SignInWithProviderAsync(string provider)
	{
		await RunAsync(() => _auth.SignInWithProviderAsync(provider)).ConfigureAwait(true);
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

	/// <summary>
	/// The shared half of every sign-in: busy on, errors cleared, and the
	/// shell refreshed if a session came back.
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
				if (Shell.Current is AppShell shell)
				{
					await shell.RefreshAsync().ConfigureAwait(true);
				}

				return;
			}

			// An empty error is a cancelled sign-in — the member closed
			// the browser tab or dismissed the sheet, and knows what they
			// did. Saying anything would be the app talking for the sake
			// of it.
			Error = result.Error;
		}
		finally
		{
			Busy = false;
		}
	}

	private bool CanSignIn() => !Busy && IsConfigured;
}
