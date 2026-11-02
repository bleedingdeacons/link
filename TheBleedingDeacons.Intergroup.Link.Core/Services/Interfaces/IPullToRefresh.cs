namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Whether pulling the message list down checks for new messages.
///
/// <para><b>Off unless turned on.</b> The list already checks every time
/// it is shown, and push brings messages in while it is open, so the pull
/// is a convenience rather than the way messages arrive. Left on, it
/// fired whenever a member scrolled back to the top a little too hard,
/// and the spinner it drew read as the app being stuck.</para>
///
/// <para>The member's own choice, on this handset, like
/// <see cref="IRecipientListing"/>.</para>
/// </summary>
public interface IPullToRefresh
{
	/// <summary>Whether the pull is on. Persisted, and read each time the list is shown.</summary>
	bool Enabled { get; set; }
}
