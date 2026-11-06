namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>Where a phone's messages and sign-in live.</summary>
public enum LinkBackend
{
	/// <summary>Fellowship's own device list and messages, in WordPress.</summary>
	Fellowship,

	/// <summary>Firestore and Cloud Functions. The member list stays in WordPress.</summary>
	Firebase,
}

/// <summary>What the two back ends agree on about message ids.</summary>
public static class LinkMessageIds
{
	/// <summary>
	/// The lowest id Firebase could issue.
	///
	/// <para>Its ids are milliseconds times 1024, so any since 2001 is above this,
	/// and Fellowship's count up from 1 and will never get near it. That is what
	/// lets one history hold both kinds and still tell them apart.</para>
	/// </summary>
	public const long FirstFirebaseId = 1_000_000_000_000L * 1024;
}
