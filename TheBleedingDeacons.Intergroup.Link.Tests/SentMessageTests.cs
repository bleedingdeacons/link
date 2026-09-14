using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What the ticks on a sent message say, from the counts behind them.
/// </summary>
public sealed class SentMessageTests
{
	[Theory]
	[InlineData(1, 0, 0, ReceiptState.Sent)]
	[InlineData(1, 1, 0, ReceiptState.Received)]
	[InlineData(1, 1, 1, ReceiptState.Read)]
	[InlineData(3, 2, 0, ReceiptState.Sent)]
	[InlineData(3, 3, 2, ReceiptState.Received)]
	[InlineData(3, 3, 3, ReceiptState.Read)]
	[InlineData(0, 0, 0, ReceiptState.Sent)]
	public void TheTicksSayWhatIsTrueOfEveryRecipient(int recipients, int received, int read, ReceiptState expected)
	{
		var sent = new SentMessage { Id = 20, Recipients = recipients, Received = received, Read = read };

		Assert.Equal(expected, sent.State);
	}

	[Theory]
	[InlineData(1, 0, 0, "Sent")]
	[InlineData(1, 1, 0, "Received")]
	[InlineData(1, 1, 1, "Read")]
	[InlineData(5, 0, 0, "Sent to 5")]
	[InlineData(5, 3, 0, "Received by 3 of 5")]
	[InlineData(5, 5, 2, "Read by 2 of 5")]
	public void TheSummarySaysHowMany(int recipients, int received, int read, string expected)
	{
		var sent = new SentMessage { Id = 20, Recipients = recipients, Received = received, Read = read };

		Assert.Equal(expected, sent.Summary);
	}

	[Fact]
	public void ACountNeverWalksBackwards()
	{
		// A recipient row removed by erasure or the sweep is not the
		// message becoming less delivered.
		var sent = new SentMessage { Id = 20, Recipients = 3, Received = 3, Read = 2 };

		var after = sent.With(new MessageReceipt(20, 2, 2, 1));

		Assert.Equal(sent, after);
	}
}
