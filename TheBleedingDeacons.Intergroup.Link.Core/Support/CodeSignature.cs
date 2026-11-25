using System.Buffers.Binary;
using System.Xml;
using System.Xml.Linq;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// The entitlements an iOS executable was actually signed with, read from
/// its own code signature.
/// </summary>
/// <remarks>
/// <para><b>Why read the binary rather than trust a build flag.</b> Sign in
/// with Apple works only when the signature carries
/// <c>com.apple.developer.applesignin</c>, and that is decided by whoever
/// signs the app, not by whoever compiled it. CI builds one unsigned .ipa;
/// signed by the paid team it is entitled, signed by a free personal team
/// it is not. The signature is the one place that knows which happened.</para>
///
/// <para><b>Why not the provisioning profile.</b> A profile says what the
/// team <i>may</i> grant; the signature says what this binary <i>was</i>
/// granted. A build signed against a paid profile without asking for the
/// entitlement would still fail at the sheet.</para>
///
/// <para>iOS has no public call for this — <c>SecTaskCopyValueForEntitlement</c>
/// is macOS only — so the Mach-O is walked by hand: header, then the
/// <c>LC_CODE_SIGNATURE</c> load command, then the signature's superblob,
/// then its entitlements blob, which is an XML property list. Only the
/// header, the load commands and the signature are read, never the code,
/// so a hundred-megabyte executable costs a few kilobytes.</para>
///
/// <para>Anything unexpected answers null rather than throwing: an
/// unsigned build, a simulator build, a truncated file. The caller then
/// falls back to what the build was compiled to assume.</para>
/// </remarks>
public static class CodeSignature
{
	/// <summary>The entitlement Sign in with Apple needs.</summary>
	public const string AppleSignIn = "com.apple.developer.applesignin";

	private const uint MachO64 = 0xFEEDFACF;
	private const uint Fat = 0xCAFEBABE;
	private const uint Arm64 = 0x0100000C;
	private const uint LoadCodeSignature = 0x1D;
	private const uint SuperBlobMagic = 0xFADE0CC0;
	private const uint EntitlementsMagic = 0xFADE7171;
	private const uint EntitlementsSlot = 5;

	// Generous for a signature's index and an entitlements plist; a value
	// past these is a corrupt file, not a bigger app.
	private const int MaxLoadCommands = 1 << 20;
	private const int MaxEntitlementsLength = 1 << 20;

	/// <summary>
	/// The top-level entitlement keys in the executable's signature, or null
	/// when it has no readable signature.
	/// </summary>
	/// <param name="executable">A seekable stream over the executable.</param>
	public static IReadOnlySet<string>? EntitlementKeys(Stream executable)
	{
		ArgumentNullException.ThrowIfNull(executable);

		try
		{
			var slice = ThinSliceOffset(executable);
			if (slice is not { } start)
			{
				return null;
			}

			var signature = SignatureOffset(executable, start);
			if (signature is not { } at)
			{
				return null;
			}

			return Entitlements(executable, start + at);
		}
		catch (Exception ex) when (ex is IOException or EndOfStreamException or XmlException or ArgumentException or OverflowException)
		{
			return null;
		}
	}

	/// <summary>
	/// Where the 64-bit arm64 image starts: zero for a thin file, the
	/// slice's offset inside a universal one.
	/// </summary>
	private static long? ThinSliceOffset(Stream file)
	{
		var head = Read(file, 0, 8);
		if (BinaryPrimitives.ReadUInt32LittleEndian(head) == MachO64)
		{
			return 0;
		}

		if (BinaryPrimitives.ReadUInt32BigEndian(head) != Fat)
		{
			return null;
		}

		// The fat header and its arch table are big-endian, whatever the
		// slices inside are.
		var count = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
		for (var i = 0u; i < Math.Min(count, 32u); i++)
		{
			var arch = Read(file, 8 + (i * 20), 20);
			if (BinaryPrimitives.ReadUInt32BigEndian(arch) == Arm64)
			{
				return BinaryPrimitives.ReadUInt32BigEndian(arch.AsSpan(8));
			}
		}

		return null;
	}

	/// <summary>The signature's offset from the start of the image.</summary>
	private static long? SignatureOffset(Stream file, long image)
	{
		var header = Read(file, image, 32);
		if (BinaryPrimitives.ReadUInt32LittleEndian(header) != MachO64)
		{
			return null;
		}

		var commands = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
		var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
		if (size > MaxLoadCommands)
		{
			return null;
		}

		var table = Read(file, image + 32, (int)size);
		var offset = 0;
		for (var i = 0u; i < commands && offset + 8 <= table.Length; i++)
		{
			var command = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset));
			var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset + 4));

			if (command == LoadCodeSignature && offset + 16 <= table.Length)
			{
				return BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset + 8));
			}

			if (length < 8)
			{
				return null;
			}

			offset += length;
		}

		return null;
	}

	/// <summary>The entitlement keys from the superblob at <paramref name="at"/>.</summary>
	private static HashSet<string>? Entitlements(Stream file, long at)
	{
		// Everything inside a code signature is big-endian.
		var head = Read(file, at, 12);
		if (BinaryPrimitives.ReadUInt32BigEndian(head) != SuperBlobMagic)
		{
			return null;
		}

		var count = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(8));
		for (var i = 0u; i < Math.Min(count, 64u); i++)
		{
			var entry = Read(file, at + 12 + (i * 8), 8);
			if (BinaryPrimitives.ReadUInt32BigEndian(entry) != EntitlementsSlot)
			{
				continue;
			}

			var blob = at + BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(4));
			var blobHead = Read(file, blob, 8);
			var length = BinaryPrimitives.ReadUInt32BigEndian(blobHead.AsSpan(4));
			if (BinaryPrimitives.ReadUInt32BigEndian(blobHead) != EntitlementsMagic || length < 8 || length > MaxEntitlementsLength)
			{
				return null;
			}

			return TopLevelKeys(System.Text.Encoding.UTF8.GetString(Read(file, blob + 8, (int)length - 8)));
		}

		// Signed, but with no entitlements at all: an empty set, not unknown.
		return [];
	}

	private static HashSet<string> TopLevelKeys(string plist)
	{
		// Entitlements plists carry Apple's DOCTYPE; it is ignored rather
		// than fetched or expanded.
		var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
		using var text = new StringReader(plist);
		using var reader = XmlReader.Create(text, settings);
		var document = XDocument.Load(reader);

		var dict = document.Root?.Element("dict");

		return dict is null
			? []
			: dict.Elements("key").Select(key => key.Value).ToHashSet(StringComparer.Ordinal);
	}

	private static byte[] Read(Stream file, long at, int count)
	{
		if (at < 0 || count < 0 || at + count > file.Length)
		{
			throw new EndOfStreamException();
		}

		var buffer = new byte[count];
		file.Seek(at, SeekOrigin.Begin);
		file.ReadExactly(buffer);

		return buffer;
	}
}
