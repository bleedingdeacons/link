using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The sign-in screen.
///
/// <para>One button, and an explanation of the one thing that commonly
/// goes wrong. Sign-in fails for a member whose address the intergroup
/// does not hold, and telling them that plainly — rather than
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

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SignInWithGoogleCommand))]
	private bool _busy;

	public bool HasError => !string.IsNullOrEmpty(Error);

	/// <summary>
	/// Whether this build knows which intergroup it belongs to.
	///
	/// <para>Shown as its own state rather than as a failed sign-in: a
	/// build with no BaseUrl is a packaging mistake, and a member being
	/// told to check their password for it would be a poor use of their
	/// afternoon.</para>
	/// </summary>
	public bool IsConfigured => _configuration.IsConfigured;

	[RelayCommand(CanExecute = nameof(CanSignIn))]
	private async Task SignInWithGoogleAsync()
	{
		Busy = true;
		Error = string.Empty;

		try
		{
			var result = await _auth.SignInWithGoogleAsync().ConfigureAwait(true);

			if (result.Succeeded)
			{
				if (Shell.Current is AppShell shell)
				{
					await shell.RefreshAsync().ConfigureAwait(true);
				}

				return;
			}

			// An empty error is a cancelled sign-in — the member closed
			// the browser tab and knows what they did. Saying anything
			// would be the app talking for the sake of it.
			Error = result.Error;
		}
		finally
		{
			Busy = false;
		}
	}

	private bool CanSignIn() => !Busy && IsConfigured;
}
