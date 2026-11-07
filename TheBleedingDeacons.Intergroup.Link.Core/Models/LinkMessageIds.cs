namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>What the two kinds of message id a phone can hold have to agree on.</summary>
public static class LinkMessageIds
{
	/// <summary>
	/// The lowest id Firebase could issue.
	///
	/// <para>Its ids are milliseconds times 1024, so any since 2001 is above this.
	/// A phone that was signed in before the move still holds messages with
	/// Fellowship's ids, which counted up from 1 and never got near it. That is what
	/// lets one history hold both and tell them apart.</para>
	/// </summary>
	public const long FirstFirebaseId = 1_000_000_000_000L * 1024;
}
