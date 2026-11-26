using System.Windows.Input;

namespace TheBleedingDeacons.Intergroup.Link.Views;

/// <summary>
/// Apple's own "Continue with Apple" button, drawn by iOS.
///
/// <para><b>Why not a MAUI Button.</b> Apple's guidelines require the
/// system button, <c>ASAuthorizationAppleIDButton</c>, wherever Sign in with
/// Apple is offered, and Beta App Review checks for it before a build goes
/// to external testers. Its wording, logo, font and proportions are
/// Apple's and cannot be restyled, which is the point: members recognise
/// it.</para>
///
/// <para>A view with a <see cref="Command"/>, like Button, so the XAML
/// binds it the same way and it greys out while the command cannot run.
/// The iOS handler draws the system button; the Android one draws an empty
/// view, because the button is never shown there (see
/// <c>AppleSignIn.IsAvailable</c>) and MAUI needs a platform view for
/// every element, visible or not.</para>
/// </summary>
public sealed class AppleSignInButton : View
{
	public static readonly BindableProperty CommandProperty = BindableProperty.Create(
		nameof(Command),
		typeof(ICommand),
		typeof(AppleSignInButton),
		propertyChanged: OnCommandChanged);

	public ICommand? Command
	{
		get => (ICommand?)GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	// Button's own pattern: enabled only while the command can run, so a
	// sign-in already under way cannot be started twice from here.
	protected override bool IsEnabledCore => base.IsEnabledCore && (Command?.CanExecute(null) ?? false);

	/// <summary>Called by the platform handler when the button is tapped.</summary>
	internal void SendTapped()
	{
		if (Command is { } command && command.CanExecute(null))
		{
			command.Execute(null);
		}
	}

	private static void OnCommandChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		var button = (AppleSignInButton)bindable;

		if (oldValue is ICommand old)
		{
			old.CanExecuteChanged -= button.OnCanExecuteChanged;
		}

		if (newValue is ICommand now)
		{
			now.CanExecuteChanged += button.OnCanExecuteChanged;
		}

		button.RefreshIsEnabledProperty();
	}

	private void OnCanExecuteChanged(object? sender, EventArgs e) => RefreshIsEnabledProperty();
}
