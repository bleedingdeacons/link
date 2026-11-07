namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What the directory last said each sender is called, kept so a push can use it.
///
/// <para>On Firebase the sender's phone seals its own name, so that is only their word.
/// The server sets the sender's id from their sign-in, and the directory says who that
/// id is. A push can't ask the directory, because it has seconds to work and WordPress
/// may be down, so the answer is kept on the phone with the rest of the history.</para>
/// </summary>
/// <param name="Names">Each sender's name, by member id.</param>
/// <param name="FetchedAt">Unix seconds when the directory last answered, or 0.</param>
public sealed record SenderNames(IReadOnlyDictionary<long, string> Names, long FetchedAt)
{
	public static SenderNames None { get; } = new(new Dictionary<long, string>(), 0);
}
