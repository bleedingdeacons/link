using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Link's back end on Firebase, behind the same seam as
/// <see cref="FellowshipClient"/>.
///
/// <para>REST over <see cref="HttpClient"/> rather than the native SDKs, so that
/// Link.Core stays plain .NET and all of this can be tested without a phone. Like
/// the other client, every method answers a value and never throws over a lost
/// network.</para>
///
/// <para><b>The <c>token</c> every method takes is a Firebase refresh token</b>,
/// kept where the device token used to be.</para>
///
/// <para><b>The member list stays in WordPress.</b> So the directory, the password
/// check and setting a password all still go to Fellowship, and sending fails if
/// WordPress is down because a Function has to ask it who a message reaches.
/// Receiving doesn't touch WordPress at all.</para>
///
/// <para><b>What the server no longer knows.</b> Messages are sealed on the phone,
/// so unread counts and read state can't come from it: <see cref="InboxPage.Unread"/>
/// is always 0, nothing reads that value in the app today, and a message read on
/// one of a member's phones stays unread on another.</para>
/// </summary>
public sealed class FirebaseFellowshipClient : IFellowshipClient
{
	/// <summary>Fellowship's own largest poll page.</summary>
	public const int InboxPageSize = 200;

	/// <summary>
	/// How far behind its mark a phone looks for messages.
	///
	/// <para>A message's id is issued when its audience is resolved, and a send
	/// may present one up to ten minutes old. So a message can land with an id
	/// below one already fetched, and looking only above the mark would never
	/// find it. This covers those ten minutes and clock differences between
	/// Google's servers. <see cref="MessageService"/> skips what it already
	/// holds without opening it.</para>
	/// </summary>
	public const long LateArrivalOverlap = 15 * 60 * 1000 * 1024L;

	/// <summary>The most a send may take: FCM's 4KB less the rest of the push.</summary>
	internal const int MaxEnvelopeLength = 3900;

	/// <summary>A send tries twice, because a phone enrolling in between makes the first one stale.</summary>
	private const int SendAttempts = 2;

	/// <summary>How many writes go at once when acknowledging a long list.</summary>
	private const int ReceiptWritesAtOnce = 8;

	private const string NotReached = "Could not reach the intergroup. The message has not been sent.";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly FirebaseConfiguration _firebase;
	private readonly FellowshipConfiguration _fellowship;
	private readonly IFellowshipClient _site;
	private readonly FirebaseIdentity _identity;

	/// <summary>This member's name from the directory, which is the only place it is known.</summary>
	private string? _ownName;

	/// <param name="http">Shared with the site's client, for the same reasons.</param>
	/// <param name="firebase">The project.</param>
	/// <param name="fellowship">The site, for the password check.</param>
	/// <param name="site">What still lives in WordPress: the directory and setting a password.</param>
	public FirebaseFellowshipClient(
		HttpClient http,
		FirebaseConfiguration firebase,
		FellowshipConfiguration fellowship,
		IFellowshipClient site)
	{
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_firebase = firebase ?? throw new ArgumentNullException(nameof(firebase));
		_fellowship = fellowship ?? throw new ArgumentNullException(nameof(fellowship));
		_site = site ?? throw new ArgumentNullException(nameof(site));
		_identity = new FirebaseIdentity(http, firebase);
	}

	/// <summary>
	/// Begin a sign-in. Only Apple needs one here.
	///
	/// <para>Nothing is asked of the server: the nonce only has to tie Apple's
	/// answer to this attempt, and Firebase checks it. Apple is given the hash
	/// and Firebase the raw value, which is the way round Firebase requires.
	/// Google signs in natively and has no browser flow here, so it answers
	/// null.</para>
	/// </summary>
	public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default)
	{
		if (provider != "apple")
		{
			return Task.FromResult<SignInStart?>(null);
		}

		var raw = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

		return Task.FromResult<SignInStart?>(new SignInStart
		{
			State = raw,
			Nonce = Convert.ToHexStringLower(SHA256.HashData(FirebaseHttp.Utf8(raw))),
		});
	}

	public async Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var (identity, refusal) = await ProveAsync(
			request.FirebaseToken, request.IdToken, request.State, request.Email, request.Password, cancellationToken).ConfigureAwait(false);
		if (identity is null)
		{
			return EnrolmentResult.Failed(refusal);
		}

		var data = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["publicKey"] = request.PublicKey,
			["platform"] = request.Platform,
		};

		AddIfPresent(data, "label", request.Label);
		AddIfPresent(data, "pushToken", request.PushToken);

		var enrolled = await CallAsync("enrol", identity, data, cancellationToken).ConfigureAwait(false);
		if (!enrolled.Succeeded)
		{
			return EnrolmentResult.Failed(enrolled.Words("This device could not be enrolled. Please try again."));
		}

		var customToken = FirebaseHttp.Text(enrolled.Result, "token");
		var deviceId = FirebaseHttp.Text(enrolled.Result, "deviceId");
		var signedIn = customToken.Length == 0
			? null
			: await _identity.SignInWithCustomTokenAsync(customToken, cancellationToken).ConfigureAwait(false);

		if (signedIn?.User is null)
		{
			return EnrolmentResult.Failed("This device could not be enrolled. Please try again.");
		}

		await _identity.HoldAsync(signedIn, cancellationToken).ConfigureAwait(false);
		await FetchDirectoryAsync(signedIn.RefreshToken, cancellationToken).ConfigureAwait(false);

		return EnrolmentResult.Ok(new DeviceSession
		{
			Token = signedIn.RefreshToken,
			FirebaseDeviceId = deviceId,
			MemberId = FirebaseHttp.Number(enrolled.Result, "memberId"),
			MemberName = _ownName ?? string.Empty,
		});
	}

	public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) =>
		_site.RequestPasswordLinkAsync(email, cancellationToken);

	public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) =>
		_site.SetPasswordAsync(code, password, cancellationToken);

	public async Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default)
	{
		var (user, failure) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return InboxPage.Refused(failure);
		}

		var messages = new List<SealedMessage>();
		var from = Math.Max(0, sinceId - LateArrivalOverlap);

		// Keeps going until it is past the mark. Otherwise a full page inside the
		// overlap would look like no progress, and the sync would stop there.
		while (true)
		{
			var (rows, refused) = await RunQueryAsync(
				$"{_firebase.Documents}/devices/{Uri.EscapeDataString(user.DeviceId)}:runQuery",
				user,
				InboxQuery(from),
				cancellationToken).ConfigureAwait(false);

			if (rows is null)
			{
				return InboxPage.Refused(refused);
			}

			var last = from;

			foreach (var fields in rows)
			{
				var seq = FirestoreValue.Integer(fields, "seq");
				var wrappedKey = FirestoreValue.Text(fields, "k");
				var payload = FirestoreValue.Text(fields, "p");

				last = Math.Max(last, seq);

				if (seq > 0 && wrappedKey.Length > 0 && payload.Length > 0)
				{
					messages.Add(new SealedMessage
					{
						Id = seq,
						WrappedKey = wrappedKey,
						Payload = payload,
						SenderMemberId = FirestoreValue.Integer(fields, "senderMemberId"),
					});
				}
			}

			if (rows.Count < InboxPageSize || last > sinceId || last <= from)
			{
				return new InboxPage { Messages = messages, More = rows.Count == InboxPageSize && last > from };
			}

			from = last;
		}
	}

	public async Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default)
	{
		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return false;
		}

		var status = await WriteReceiptAsync(user, messageId, "readAt", cancellationToken).ConfigureAwait(false);

		return status is { } arrived && IsSuccess(arrived);
	}

	public async Task<bool> MarkReceivedAsync(
		string token,
		IReadOnlyCollection<long> messageIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messageIds);

		if (messageIds.Count == 0)
		{
			return true;
		}

		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return false;
		}

		// One write each rather than a batch. A rule on a receipt reads the
		// message, a batched write has a low limit on those reads, and one
		// refused receipt would fail all the others with it.
		//
		// A refusal counts as accepted, as Fellowship ignores an id never addressed
		// to this member. Otherwise a message since swept from the server would be
		// asked about on every sync, for ever.
		var arrived = true;

		try
		{
			await Parallel.ForEachAsync(
				messageIds,
				new ParallelOptions { MaxDegreeOfParallelism = ReceiptWritesAtOnce, CancellationToken = cancellationToken },
				async (id, cancelled) =>
				{
					var status = await WriteReceiptAsync(user, id, "receivedAt", cancelled).ConfigureAwait(false);
					if (status is null || status >= HttpStatusCode.InternalServerError)
					{
						Volatile.Write(ref arrived, false);
					}
				}).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return false;
		}

		return Volatile.Read(ref arrived);
	}

	public async Task<IReadOnlyList<MessageReceipt>?> FetchReceiptsAsync(
		string token,
		IReadOnlyCollection<long> messageIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messageIds);

		// A phone that moved here still holds messages it sent through Fellowship.
		// Firestore has never heard of those, and they would break the query below,
		// because it sorts ids as text and a short id sorts after a long one.
		var ours = messageIds.Where(id => id >= LinkMessageIds.FirstFirebaseId).ToList();

		if (ours.Count == 0)
		{
			return [];
		}

		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return null;
		}

		// A query rather than asking for each id. The rules read a document to
		// decide, so asking for one already swept is refused, and in a batch that
		// would lose the answers for all the others too.
		var (rows, _) = await RunQueryAsync(
			$"{_firebase.Documents}:runQuery",
			user,
			SentSinceQuery(user.MemberId, ours.Min()),
			cancellationToken).ConfigureAwait(false);

		if (rows is null)
		{
			return null;
		}

		var asked = ours.ToHashSet();
		var receipts = new List<MessageReceipt>();

		foreach (var fields in rows)
		{
			var id = FirestoreValue.IdOf(fields);
			if (asked.Contains(id))
			{
				receipts.Add(new MessageReceipt(
					id,
					(int)FirestoreValue.Integer(fields, "recipientCount"),
					(int)FirestoreValue.Integer(fields, "receivedCount"),
					(int)FirestoreValue.Integer(fields, "readCount")));
			}
		}

		return receipts;
	}

	/// <summary>
	/// Ask who a message reaches, seal it to each of their devices, and hand the
	/// envelopes back.
	///
	/// <para>The server's id goes inside the seal, so a push can be opened by the
	/// same code as a poll. <c>created_at</c> is read from that id rather than this
	/// phone's clock, which could be wrong. It is still only the sender's word,
	/// as is the name; see <see cref="MessageService"/> for what a recipient
	/// checks.</para>
	/// </summary>
	public async Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return SendResult.Failed(NotReached);
		}

		var senderName = await OwnNameAsync(token, cancellationToken).ConfigureAwait(false);
		if (senderName is null)
		{
			return SendResult.Failed(NotReached);
		}

		var result = SendResult.Failed(NotReached);

		for (var attempt = 0; attempt < SendAttempts; attempt++)
		{
			var resolved = await CallAsync("resolveAudience", user.IdToken, Audience(request), cancellationToken).ConfigureAwait(false);
			if (!resolved.Succeeded)
			{
				return SendResult.Failed(resolved.Words(resolved.Arrived ? "The message could not be sent." : NotReached));
			}

			var id = FirebaseHttp.Number(resolved.Result, "id");
			var devices = Devices(resolved.Result);

			if (id <= 0 || devices.Count == 0)
			{
				return SendResult.Failed("None of those members have Link yet.");
			}

			var payload = Payload(request, id, user.MemberId, senderName);
			var envelopes = SealAll(payload, devices);

			if (envelopes.Refusal.Length > 0)
			{
				return SendResult.Failed(envelopes.Refusal);
			}

			var data = Audience(request);
			data["id"] = id;
			data["envelopes"] = envelopes.Sealed;

			var sent = await CallAsync("sendMessage", user.IdToken, data, cancellationToken).ConfigureAwait(false);

			if (sent.Succeeded)
			{
				return new SendResult
				{
					MessageId = FirebaseHttp.Number(sent.Result, "id"),
					Recipients = (int)FirebaseHttp.Number(sent.Result, "reachable"),
					Addressed = (int)FirebaseHttp.Number(sent.Result, "members"),
					CreatedAt = FirebaseHttp.Number(sent.Result, "createdAt"),
				};
			}

			result = SendResult.Failed(sent.Words(sent.Arrived ? "The message could not be sent." : NotReached));

			if (sent.Reason != "fellowship_stale_audience")
			{
				return result;
			}
		}

		return result;
	}

	public async Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default)
	{
		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return FellowshipDirectory.Empty;
		}

		var directory = await _site.FetchDirectoryAsync(user.IdToken, cancellationToken).ConfigureAwait(false);
		var own = directory.Members.FirstOrDefault(member => member.Id == user.MemberId);

		if (own is not null)
		{
			_ownName = own.Name;
		}

		return directory;
	}

	public async Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default)
	{
		var fields = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["pushToken"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["stringValue"] = pushToken ?? string.Empty },
		};

		return await UpdateDeviceAsync(token, "pushToken", fields, cancellationToken).ConfigureAwait(false);
	}

	public async Task<RotateKeyResult> RotateKeyAsync(string token, RotateKeyRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return RotateKeyResult.Failed("This device is not signed in.");
		}

		var (identity, refusal) = await ProveAsync(
			request.FirebaseToken, request.IdToken, request.State, request.Email, request.Password, cancellationToken).ConfigureAwait(false);
		if (identity is null)
		{
			return RotateKeyResult.Failed(refusal);
		}

		var data = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["deviceId"] = user.DeviceId,
			["publicKey"] = request.PublicKey,
		};

		var replaced = await CallAsync("replaceKey", identity, data, cancellationToken).ConfigureAwait(false);

		return replaced.Succeeded
			? RotateKeyResult.Ok()
			: RotateKeyResult.Failed(replaced.Words("That did not work. Please try again."));
	}

	public async Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default)
	{
		var fields = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["keyFault"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["booleanValue"] = true },
		};

		return await UpdateDeviceAsync(token, "keyFault", fields, cancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default)
	{
		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return false;
		}

		var signedOut = await CallAsync(
			"signOut",
			user.IdToken,
			new Dictionary<string, object>(StringComparer.Ordinal),
			cancellationToken).ConfigureAwait(false);

		await _identity.ForgetAsync().ConfigureAwait(false);
		_ownName = null;

		return signedOut.Succeeded;
	}

	/// <summary>
	/// An identity token from whichever credential was gathered, or the words to
	/// show instead.
	///
	/// <para>The password goes to Fellowship, because it stays in Unity, and only
	/// ever there.</para>
	/// </summary>
	private async Task<(string? IdToken, string Refusal)> ProveAsync(
		string firebaseToken,
		string appleToken,
		string rawNonce,
		string email,
		string password,
		CancellationToken cancellationToken)
	{
		if (firebaseToken.Length > 0)
		{
			return (firebaseToken, string.Empty);
		}

		if (password.Length > 0)
		{
			var (customToken, refusal) = await PasswordTokenAsync(email, password, cancellationToken).ConfigureAwait(false);
			if (customToken is null)
			{
				return (null, refusal);
			}

			var signedIn = await _identity.SignInWithCustomTokenAsync(customToken, cancellationToken).ConfigureAwait(false);

			return signedIn is null ? (null, CouldNotSignIn) : (signedIn.IdToken, string.Empty);
		}

		if (appleToken.Length > 0 && rawNonce.Length > 0)
		{
			var signedIn = await _identity.SignInWithAppleAsync(appleToken, rawNonce, cancellationToken).ConfigureAwait(false);

			return signedIn is null ? (null, CouldNotSignIn) : (signedIn.IdToken, string.Empty);
		}

		return (null, "Please sign in with Google, Apple or your password.");
	}

	private const string CouldNotSignIn = "The sign-in did not complete. Please try again.";

	private async Task<(string? Token, string Refusal)> PasswordTokenAsync(
		string email,
		string password,
		CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["email"] = email,
			["password"] = password,
		};

		var (json, status) = await SendAsync(
			HttpMethod.Post,
			_fellowship.Route("auth/identity/password"),
			bearer: null,
			body,
			cancellationToken).ConfigureAwait(false);

		if (status is null)
		{
			return (null, "Could not reach the intergroup. Check your connection and try again.");
		}

		var token = json is null ? string.Empty : FirebaseHttp.Text(json.Value, "token");
		if (IsSuccess(status.Value) && token.Length > 0)
		{
			return (token, string.Empty);
		}

		var message = json is null ? string.Empty : FirebaseHttp.Text(json.Value, "message");

		return (null, message.Length > 0 ? message : CouldNotSignIn);
	}

	/// <summary>The name to seal, or null when it can't be found out at all.</summary>
	private async Task<string?> OwnNameAsync(string token, CancellationToken cancellationToken)
	{
		if (_ownName is not null)
		{
			return _ownName;
		}

		var directory = await FetchDirectoryAsync(token, cancellationToken).ConfigureAwait(false);

		// A member with no anonymous name isn't listed, and has no name to seal.
		// An empty directory means it couldn't be asked, and sending needs
		// WordPress anyway.
		return directory.Members.Count == 0 ? null : _ownName ?? string.Empty;
	}

	private static Dictionary<string, object> Audience(SendRequest request)
	{
		var audience = new Dictionary<string, object>(StringComparer.Ordinal);

		if (request.MemberIds.Count > 0)
		{
			audience["memberIds"] = request.MemberIds;
		}

		if (request.Committees.Count > 0)
		{
			audience["committees"] = request.Committees;
		}

		if (request.AllGsrs)
		{
			audience["gsrs"] = true;
		}

		return audience;
	}

	private static Dictionary<string, object> Payload(SendRequest request, long id, long senderId, string senderName) =>
		new(StringComparer.Ordinal)
		{
			["id"] = id,
			["uuid"] = Guid.NewGuid().ToString(),
			["subject"] = request.Subject,
			["body"] = request.Body,
			["sender"] = senderName,
			["sender_id"] = senderId,
			["created_at"] = id / 1024 / 1000,
			["reply_to"] = request.ReplyToId,
			["read_at"] = 0,
		};

	private static List<(string DeviceId, string PublicKey)> Devices(JsonElement resolved)
	{
		var devices = new List<(string, string)>();

		if (resolved.ValueKind == JsonValueKind.Object
			&& resolved.TryGetProperty("devices", out var array)
			&& array.ValueKind == JsonValueKind.Array)
		{
			foreach (var device in array.EnumerateArray())
			{
				devices.Add((FirebaseHttp.Text(device, "deviceId"), FirebaseHttp.Text(device, "publicKey")));
			}
		}

		return devices;
	}

	private static (List<Dictionary<string, string>> Sealed, string Refusal) SealAll(
		Dictionary<string, object> payload,
		List<(string DeviceId, string PublicKey)> devices)
	{
		var envelopes = new List<Dictionary<string, string>>(devices.Count);

		foreach (var (deviceId, publicKey) in devices)
		{
			SealedEnvelope envelope;

			try
			{
				envelope = MessageSealer.Seal(payload, publicKey);
			}
			catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
			{
				// The server requires an envelope for every device, so one it holds
				// a bad key for stops the send. It checks keys at enrolment, so
				// this shouldn't happen, and it says nothing about who that was.
				Log.Error(e, "A device's public key could not be used; the message was not sent");

				return (envelopes, "The message could not be sent.");
			}

			if (envelope.WrappedKey.Length + envelope.Payload.Length > MaxEnvelopeLength)
			{
				return (envelopes, "That message is too long to send. Please shorten it.");
			}

			envelopes.Add(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["deviceId"] = deviceId,
				["k"] = envelope.WrappedKey,
				["p"] = envelope.Payload,
			});
		}

		return (envelopes, string.Empty);
	}

	private async Task<bool> UpdateDeviceAsync(
		string token,
		string field,
		Dictionary<string, object> fields,
		CancellationToken cancellationToken)
	{
		var (user, _) = await _identity.CurrentAsync(token, cancellationToken).ConfigureAwait(false);
		if (user is null)
		{
			return false;
		}

		var uri = new Uri($"{_firebase.Documents}/devices/{Uri.EscapeDataString(user.DeviceId)}?updateMask.fieldPaths={field}");
		var (_, status) = await SendAsync(
			HttpMethod.Patch,
			uri,
			user.IdToken,
			new Dictionary<string, object>(StringComparer.Ordinal) { ["fields"] = fields },
			cancellationToken).ConfigureAwait(false);

		return status is { } arrived && IsSuccess(arrived);
	}

	private async Task<HttpStatusCode?> WriteReceiptAsync(
		FirebaseUser user,
		long messageId,
		string field,
		CancellationToken cancellationToken)
	{
		var id = messageId.ToString(CultureInfo.InvariantCulture);
		var member = user.MemberId.ToString(CultureInfo.InvariantCulture);
		var uri = new Uri($"{_firebase.Documents}/messages/{id}/receipts/{member}?updateMask.fieldPaths={field}");

		var body = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["fields"] = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				[field] = new Dictionary<string, object>(StringComparer.Ordinal)
				{
					["timestampValue"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
				},
			},
		};

		var (_, status) = await SendAsync(HttpMethod.Patch, uri, user.IdToken, body, cancellationToken).ConfigureAwait(false);

		return status;
	}

	private static Dictionary<string, object> InboxQuery(long from) => new(StringComparer.Ordinal)
	{
		["from"] = new[] { new Dictionary<string, object>(StringComparer.Ordinal) { ["collectionId"] = "inbox" } },
		["where"] = FirestoreValue.Filter("seq", "GREATER_THAN", FirestoreValue.Integer(from)),
		["orderBy"] = new[] { FirestoreValue.Ascending("seq") },
		["limit"] = InboxPageSize,
	};

	/// <summary>
	/// This member's sent messages from the oldest asked about.
	///
	/// <para>Ids have the same number of digits until long after anyone here will
	/// care, so sorting them by name is sorting them by age. An equality with a
	/// range on the name needs no index beyond the automatic ones.</para>
	/// </summary>
	private Dictionary<string, object> SentSinceQuery(long memberId, long oldest) => new(StringComparer.Ordinal)
	{
		["from"] = new[] { new Dictionary<string, object>(StringComparer.Ordinal) { ["collectionId"] = "messages" } },
		["where"] = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["compositeFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["op"] = "AND",
				["filters"] = new[]
				{
					FirestoreValue.Filter("senderMemberId", "EQUAL", FirestoreValue.Integer(memberId)),
					FirestoreValue.Filter(
						"__name__",
						"GREATER_THAN_OR_EQUAL",
						new Dictionary<string, object>(StringComparer.Ordinal)
						{
							["referenceValue"] = _firebase.DocumentName("messages/" + oldest.ToString(CultureInfo.InvariantCulture)),
						}),
				},
			},
		},
		["orderBy"] = new[] { FirestoreValue.Ascending("__name__") },
		["limit"] = MessageService.IdsPerRequest * 2,
	};

	/// <summary>Run a query, and answer the documents' fields, or why it couldn't.</summary>
	private async Task<(List<JsonElement>? Rows, FellowshipFailure Failure)> RunQueryAsync(
		string url,
		FirebaseUser user,
		Dictionary<string, object> query,
		CancellationToken cancellationToken)
	{
		var (json, status) = await SendAsync(
			HttpMethod.Post,
			new Uri(url),
			user.IdToken,
			new Dictionary<string, object>(StringComparer.Ordinal) { ["structuredQuery"] = query },
			cancellationToken).ConfigureAwait(false);

		if (status is null)
		{
			return (null, FellowshipFailure.Network);
		}

		if (!IsSuccess(status.Value))
		{
			return (null, FailureFor(status.Value));
		}

		var rows = new List<JsonElement>();

		if (json is { ValueKind: JsonValueKind.Array } array)
		{
			foreach (var row in array.EnumerateArray())
			{
				// An empty answer is one row with only a readTime.
				if (row.TryGetProperty("document", out var document))
				{
					rows.Add(document);
				}
			}
		}

		return (rows, FellowshipFailure.None);
	}

	/// <summary>
	/// What a refusal means to a handset.
	///
	/// <para>The rules answer the same way for a revoked device and a taken-over
	/// one, and both mean sign in again. Firebase can't tell this phone that the
	/// member has gone, as Fellowship's 403 does, because it doesn't know
	/// who they are.</para>
	/// </summary>
	private static FellowshipFailure FailureFor(HttpStatusCode status) => status switch
	{
		HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FellowshipFailure.Unauthenticated,
		_ => FellowshipFailure.Server,
	};

	private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

	private async Task<Called> CallAsync(string name, string idToken, object data, CancellationToken cancellationToken)
	{
		var (json, status) = await SendAsync(
			HttpMethod.Post,
			_firebase.Function(name),
			idToken,
			new Dictionary<string, object>(StringComparer.Ordinal) { ["data"] = data },
			cancellationToken).ConfigureAwait(false);

		if (status is null)
		{
			return new Called { Arrived = false };
		}

		if (IsSuccess(status.Value) && json is { ValueKind: JsonValueKind.Object } answer && answer.TryGetProperty("result", out var result))
		{
			return new Called { Arrived = true, Succeeded = true, Result = result };
		}

		if (json is { ValueKind: JsonValueKind.Object } refused && refused.TryGetProperty("error", out var error))
		{
			var reason = error.TryGetProperty("details", out var details) ? FirebaseHttp.Text(details, "reason") : string.Empty;

			return new Called { Arrived = true, Reason = reason, Message = FirebaseHttp.Text(error, "message") };
		}

		return new Called { Arrived = true };
	}

	/// <summary>
	/// One request, and its answer. A null status means it didn't arrive.
	///
	/// <para>Only the path reaches the log: the query holds the API key, and a
	/// request body here can hold a password or a token.</para>
	/// </summary>
	private async Task<(JsonElement? Json, HttpStatusCode? Status)> SendAsync(
		HttpMethod method,
		Uri uri,
		string? bearer,
		object body,
		CancellationToken cancellationToken)
	{
		try
		{
			using var request = new HttpRequestMessage(method, uri)
			{
				Content = JsonContent.Create(body, options: JsonOptions),
			};

			if (!string.IsNullOrEmpty(bearer))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
			}

			using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			var json = await FirebaseHttp.ReadAsync(response, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				Log.Warning(
					"{Method} {Path} refused with {Status}: {Code}",
					method.Method,
					uri.AbsolutePath,
					(int)response.StatusCode,
					json is null ? string.Empty : FirebaseHttp.ErrorMessage(json.Value));
			}

			return (json, response.StatusCode);
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			Log.Debug(e, "{Method} {Path} did not arrive", method.Method, uri.AbsolutePath);

			return (null, null);
		}
	}

	private static void AddIfPresent(Dictionary<string, object> into, string key, string value)
	{
		if (value.Length > 0)
		{
			into[key] = value;
		}
	}

	/// <summary>What a callable function answered.</summary>
	private readonly record struct Called
	{
		/// <summary>False when the request never got there.</summary>
		public bool Arrived { get; init; }

		public bool Succeeded { get; init; }

		public JsonElement Result { get; init; }

		/// <summary>The function's <c>details.reason</c>, which is what code may act on.</summary>
		public string Reason { get; init; }

		/// <summary>The function's own sentence, written to be read by a member.</summary>
		public string Message { get; init; }

		public string Words(string otherwise) => string.IsNullOrEmpty(Message) ? otherwise : Message;
	}

	/// <summary>Firestore's REST shape for values.</summary>
	private static class FirestoreValue
	{
		public static string Text(JsonElement document, string field) =>
			TryField(document, field, out var value) ? FirebaseHttp.Text(value, "stringValue") : string.Empty;

		public static long Integer(JsonElement document, string field) =>
			TryField(document, field, out var value) ? FirebaseHttp.Number(value, "integerValue") : 0;

		public static Dictionary<string, object> Integer(long value) => new(StringComparer.Ordinal)
		{
			["integerValue"] = value.ToString(CultureInfo.InvariantCulture),
		};

		/// <summary>A message's id, from the end of its document name.</summary>
		public static long IdOf(JsonElement document)
		{
			var name = FirebaseHttp.Text(document, "name");
			var tail = name[(name.LastIndexOf('/') + 1)..];

			return long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
		}

		public static Dictionary<string, object> Filter(string field, string op, Dictionary<string, object> value) =>
			new(StringComparer.Ordinal)
			{
				["fieldFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
				{
					["field"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["fieldPath"] = field },
					["op"] = op,
					["value"] = value,
				},
			};

		public static Dictionary<string, object> Ascending(string field) => new(StringComparer.Ordinal)
		{
			["field"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["fieldPath"] = field },
			["direction"] = "ASCENDING",
		};

		private static bool TryField(JsonElement document, string field, out JsonElement value)
		{
			value = default;

			return document.TryGetProperty("fields", out var fields) && fields.TryGetProperty(field, out value);
		}
	}
}
