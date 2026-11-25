using System.Buffers.Binary;
using System.Text;
using TheBleedingDeacons.Intergroup.Link.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Whether an iOS executable was signed with Sign in with Apple, read from
/// its own code signature — so the same .ipa shows the button when the paid
/// team signs it and hides it when a free team does.
/// </summary>
public sealed class CodeSignatureTests
{
	private const string Entitled = """
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		<plist version="1.0">
		<dict>
			<key>application-identifier</key>
			<string>ABCDE12345.org.thebleedingdeacons.intergroup.link</string>
			<key>com.apple.developer.applesignin</key>
			<array>
				<string>Default</string>
			</array>
			<key>aps-environment</key>
			<string>development</string>
		</dict>
		</plist>
		""";

	private const string FreeTeam = """
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		<plist version="1.0">
		<dict>
			<key>application-identifier</key>
			<string>ZZZZZ99999.org.thebleedingdeacons.intergroup.link</string>
			<key>get-task-allow</key>
			<true/>
		</dict>
		</plist>
		""";

	[Fact]
	public void APaidTeamSignatureCarriesAppleSignIn()
	{
		var keys = CodeSignature.EntitlementKeys(Thin(Entitled));

		Assert.NotNull(keys);
		Assert.Contains(CodeSignature.AppleSignIn, keys);
		Assert.Contains("aps-environment", keys);
	}

	[Fact]
	public void AFreeTeamSignatureDoesNot()
	{
		var keys = CodeSignature.EntitlementKeys(Thin(FreeTeam));

		Assert.NotNull(keys);
		Assert.DoesNotContain(CodeSignature.AppleSignIn, keys);
	}

	[Fact]
	public void OnlyTopLevelKeysCount()
	{
		// The entitlement named as a value, or nested, is not the entitlement.
		const string nested = """
			<plist version="1.0"><dict>
				<key>keychain-access-groups</key>
				<array><string>com.apple.developer.applesignin</string></array>
				<key>outer</key>
				<dict><key>com.apple.developer.applesignin</key><true/></dict>
			</dict></plist>
			""";

		var keys = CodeSignature.EntitlementKeys(Thin(nested));

		Assert.NotNull(keys);
		Assert.DoesNotContain(CodeSignature.AppleSignIn, keys);
	}

	[Fact]
	public void ASignatureWithNoEntitlementsIsEmptyNotUnknown()
	{
		var keys = CodeSignature.EntitlementKeys(Thin(entitlements: null));

		Assert.NotNull(keys);
		Assert.Empty(keys);
	}

	[Fact]
	public void TheArm64SliceOfAUniversalBinaryIsRead()
	{
		var keys = CodeSignature.EntitlementKeys(Universal(Entitled));

		Assert.NotNull(keys);
		Assert.Contains(CodeSignature.AppleSignIn, keys);
	}

	[Fact]
	public void AnUnsignedExecutableIsUnknown()
	{
		// What CI produces before anybody signs it, and what the simulator runs.
		Assert.Null(CodeSignature.EntitlementKeys(new MemoryStream(Image(signature: null))));
	}

	[Fact]
	public void SomethingThatIsNotMachOIsUnknown()
	{
		Assert.Null(CodeSignature.EntitlementKeys(new MemoryStream(Encoding.ASCII.GetBytes("#!/bin/sh\necho hello\n"))));
	}

	[Fact]
	public void ATruncatedFileIsUnknownRatherThanAnError()
	{
		var whole = Thin(Entitled).ToArray();

		for (var length = 0; length < whole.Length; length += 7)
		{
			var keys = CodeSignature.EntitlementKeys(new MemoryStream(whole[..length]));

			Assert.True(keys is null || !keys.Contains(CodeSignature.AppleSignIn), $"length {length}");
		}
	}

	[Fact]
	public void ASignatureOffsetPastTheEndIsUnknown()
	{
		var bytes = Image(SuperBlob(Entitled));

		// LC_CODE_SIGNATURE's dataoff, after the header and the segment
		// command, pointed far past the file.
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32 + 72 + 8), 0x7FFF_FFF0);

		Assert.Null(CodeSignature.EntitlementKeys(new MemoryStream(bytes)));
	}

	private static MemoryStream Thin(string? entitlements) =>
		new(Image(SuperBlob(entitlements)));

	/// <summary>
	/// A universal binary with an x86_64 slice first and the arm64 slice
	/// second, so finding the right one is actually exercised.
	/// </summary>
	private static MemoryStream Universal(string entitlements)
	{
		var decoy = Image(SuperBlob(FreeTeam));
		var arm64 = Image(SuperBlob(entitlements));

		const int decoyAt = 0x1000;
		var arm64At = decoyAt + decoy.Length + 0x100;
		var file = new byte[arm64At + arm64.Length];

		BinaryPrimitives.WriteUInt32BigEndian(file, 0xCAFEBABE);
		BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), 2);
		Arch(file.AsSpan(8), 0x01000007, decoyAt, decoy.Length);
		Arch(file.AsSpan(28), 0x0100000C, arm64At, arm64.Length);
		decoy.CopyTo(file, decoyAt);
		arm64.CopyTo(file, arm64At);

		return new MemoryStream(file);

		static void Arch(Span<byte> at, uint cpu, int offset, int size)
		{
			BinaryPrimitives.WriteUInt32BigEndian(at, cpu);
			BinaryPrimitives.WriteUInt32BigEndian(at[8..], (uint)offset);
			BinaryPrimitives.WriteUInt32BigEndian(at[12..], (uint)size);
		}
	}

	/// <summary>
	/// A 64-bit Mach-O: a header, a segment command that is not the
	/// signature (so the walk has to step over it), then LC_CODE_SIGNATURE
	/// when there is a signature, and the signature itself at the end.
	/// </summary>
	private static byte[] Image(byte[]? signature)
	{
		const int header = 32;
		const int segment = 72;
		const int codeSignature = 16;
		var commands = signature is null ? 1 : 2;
		var commandBytes = segment + (signature is null ? 0 : codeSignature);
		const int signatureAt = 0x400;

		var image = new byte[signatureAt + (signature?.Length ?? 0)];

		BinaryPrimitives.WriteUInt32LittleEndian(image, 0xFEEDFACF);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), 0x0100000C);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(12), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16), (uint)commands);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(20), (uint)commandBytes);

		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(header), 0x19);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(header + 4), segment);

		if (signature is not null)
		{
			var at = header + segment;
			BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at), 0x1D);
			BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 4), codeSignature);
			BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 8), signatureAt);
			BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 12), (uint)signature.Length);
			signature.CopyTo(image, signatureAt);
		}

		return image;
	}

	/// <summary>
	/// A code signature superblob, big-endian as Apple writes it: a code
	/// directory slot first, as a real one has, then the entitlements.
	/// </summary>
	private static byte[] SuperBlob(string? entitlements)
	{
		var codeDirectory = Blob(0xFADE0C02, new byte[24]);
		var blobs = new List<(uint Slot, byte[] Bytes)> { (0, codeDirectory) };
		if (entitlements is not null)
		{
			blobs.Add((5, Blob(0xFADE7171, Encoding.UTF8.GetBytes(entitlements))));
		}

		var indexEnd = 12 + (blobs.Count * 8);
		var total = indexEnd + blobs.Sum(blob => blob.Bytes.Length);
		var superBlob = new byte[total];

		BinaryPrimitives.WriteUInt32BigEndian(superBlob, 0xFADE0CC0);
		BinaryPrimitives.WriteUInt32BigEndian(superBlob.AsSpan(4), (uint)total);
		BinaryPrimitives.WriteUInt32BigEndian(superBlob.AsSpan(8), (uint)blobs.Count);

		var offset = indexEnd;
		for (var i = 0; i < blobs.Count; i++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(superBlob.AsSpan(12 + (i * 8)), blobs[i].Slot);
			BinaryPrimitives.WriteUInt32BigEndian(superBlob.AsSpan(16 + (i * 8)), (uint)offset);
			blobs[i].Bytes.CopyTo(superBlob, offset);
			offset += blobs[i].Bytes.Length;
		}

		return superBlob;
	}

	private static byte[] Blob(uint magic, byte[] payload)
	{
		var blob = new byte[8 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(blob, magic);
		BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4), (uint)blob.Length);
		payload.CopyTo(blob, 8);

		return blob;
	}
}
