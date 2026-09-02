using System.Globalization;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// One message, as this handset holds it.
///
/// <para>Built from an opened envelope, never from anything readable on
/// the wire — see <see cref="MessagePayloadCipher"/>. A push carries the
/// same fields as a poll, so <see cref="FromPayload"/> is the only place
/// that knows the shape and both routes go through it.</para>
/// </summary>
public sealed record LinkMessage
{
	public required long Id { get; init; }

	public string Uuid { get; init; } = string.Empty;

	public string Subject { get; init; } = string.Empty;

	public string Body { get; init; } = string.Empty;

	/// <summary>
	/// Who sent it, as they are willing to be known in the fellowship.
	///
	/// <para>Fellowship sends Unity's anonymous name, not a legal one, and
	/// never an email address. A message list is read over shoulders.</para>
	/// </summary>
	public string Sender { get; init; } = string.Empty;

	/// <summary>Unix seconds, as the server recorded it.</summary>
	public long CreatedAt { get; init; }

	/// <summary>The message this one answers, or 0.</summary>
	public long ReplyToId { get; init; }

	/// <summary>
	/// When this member read it, or null.
	///
	/// <para>Read state is the member's rather than the handset's — the
	/// server keeps one row per member — so a message read on a phone
	/// arrives already read on the same member's tablet.</para>
	/// </summary>
	public long? ReadAt { get; init; }

	public bool IsRead => ReadAt is > 0;

	public DateTimeOffset Sent => DateTimeOffset.FromUnixTimeSeconds(CreatedAt);

	/// <summary>
	/// Read a message out of an opened envelope, or null if the envelope
	/// did not contain one.
	///
	/// <para>An id is the one field with no sensible default: everything
	/// else can be missing and leave a message that is merely thin, but a
	/// message with no id cannot be marked read, replied to, or
	/// de-duplicated against the copy that arrives on the next poll.</para>
	/// </summary>
	public static LinkMessage? FromPayload(IReadOnlyDictionary<string, JsonElement> payload)
	{
		ArgumentNullException.ThrowIfNull(payload);

		var id = Number(payload, "id");
		if (id <= 0)
		{
			return null;
		}

		var readAt = Number(payload, "read_at");

		return new LinkMessage
		{
			Id = id,
			Uuid = Text(payload, "uuid"),
			Subject = Text(payload, "subject"),
			Body = Text(payload, "body"),
			Sender = Text(payload, "sender"),
			CreatedAt = Number(payload, "created_at"),
			ReplyToId = Number(payload, "reply_to"),
			ReadAt = readAt > 0 ? readAt : null,
		};
	}

	private static string Text(IReadOnlyDictionary<string, JsonElement> payload, string key)
	{
		if (!payload.TryGetValue(key, out var value))
		{
			return string.Empty;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.Number => value.ToString(),
			_ => string.Empty,
		};
	}

	/// <summary>
	/// Read a number that may have been encoded either way.
	///
	/// <para>PHP's <c>wp_json_encode</c> writes an int as a number, but
	/// the values that reach the payload have passed through arrays where
	/// a stringly-typed one is easy to introduce and impossible to notice
	/// — every FCM data value is a string, for instance. Accepting both
	/// costs four lines and removes a class of bug that would show up as
	/// a message with id 0.</para>
	/// </summary>
	private static long Number(IReadOnlyDictionary<string, JsonElement> payload, string key)
	{
		if (!payload.TryGetValue(key, out var value))
		{
			return 0;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
		{
			return number;
		}

		if (value.ValueKind == JsonValueKind.String
			&& long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		{
			return parsed;
		}

		return 0;
	}
}
