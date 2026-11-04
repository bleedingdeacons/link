namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The two facts fingerprint sign-in keeps between launches.
///
/// <para>Kept apart from the session on purpose. The session is what
/// Fellowship issued and is the same whether or not a fingerprint is in
/// use; these are about this phone's screens and nothing else, and a
/// session store that also knew about locks would have two reasons to
/// change.</para>
/// </summary>
public interface IFingerprintState
{
	/// <summary>Whether the member has set up fingerprint sign-in on this phone.</summary>
	bool Enabled { get; set; }

	/// <summary>
	/// Whether the member has signed out and the next way in is a finger.
	/// Only ever true while <see cref="Enabled"/> is.
	/// </summary>
	bool Locked { get; set; }
}
