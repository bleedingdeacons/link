using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Replaces email addresses in log text with a short token that stands for
/// the address without revealing it: <c>email:3f9a2c1b</c>.
///
/// <para><b>Why Link needs it now.</b> Until sign-in was logged in detail,
/// nothing Link wrote carried an address: members are ids, and the
/// directory has no addresses in it. A password sign-in, a password-reset
/// request and Fellowship's own refusals all do, and they are exactly the
/// lines somebody diagnosing "I can't get in" needs. Passed to Inventory as
/// <c>Redact</c>, so it reaches every sink — the local file and logcat as
/// well as Better Stack — and exception text too.</para>
///
/// <para><b>Why a token and not a blank.</b> A log that says "sign-in
/// refused for [redacted]" cannot show that the same address failed three
/// times running, or tell two members' failures apart. So each address
/// becomes the first 32 bits of an HMAC-SHA256 of it, lower-cased and
/// trimmed, under this handset's own key: the same address gives the same
/// token every time, and different addresses almost never share one.</para>
///
/// <para><b>Why the correlation is limited.</b> The key is generated on the
/// handset and never leaves it, so tokens line up within one handset's logs
/// and nowhere else. Somebody holding the logs and a list of members'
/// addresses cannot test which token is whose without the handset.</para>
///
/// <para>Register's, the same rule. Names are deliberately left alone.</para>
/// </summary>
public sealed class EmailRedactor
{
	/// <summary>What a value becomes when it could not be checked in time.</summary>
	public const string Unchecked = "[redacted: not checked]";

	private const int KeyBytes = 32;
	private const int TokenBytes = 4;

	// Anything shaped like an address is hidden, whether or not it would be
	// accepted as one.
	private static readonly Regex Address = new(
		@"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+",
		RegexOptions.CultureInvariant,
		TimeSpan.FromMilliseconds(250));

	private readonly byte[] _key;

	/// <param name="key">This handset's key; see <see cref="NewKey"/>.</param>
	public EmailRedactor(byte[] key)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (key.Length < 16)
		{
			throw new ArgumentException("The key must be at least 16 bytes.", nameof(key));
		}

		_key = [.. key];
	}

	/// <summary>A fresh random key, for a handset that has none yet.</summary>
	public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyBytes);

	/// <summary>
	/// <paramref name="text"/> with every address in it replaced by its
	/// token; the same instance when there was nothing to replace. Text that
	/// cannot be checked in time is withheld whole rather than passed on.
	/// </summary>
	public string Redact(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		if (!text.Contains('@', StringComparison.Ordinal))
		{
			return text;
		}

		try
		{
			return Address.Replace(text, match => Token(match.Value));
		}
		catch (RegexMatchTimeoutException)
		{
			return Unchecked;
		}
	}

	/// <summary>The token for one address.</summary>
	public string Token(string address)
	{
		ArgumentNullException.ThrowIfNull(address);

		var normalised = Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant());
		var mac = HMACSHA256.HashData(_key, normalised);

		return "email:" + Convert.ToHexStringLower(mac, 0, TokenBytes);
	}
}
