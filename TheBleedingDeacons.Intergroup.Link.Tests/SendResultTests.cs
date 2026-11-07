using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>A sender is told who a message did not reach, rather than assuming it reached everyone.</summary>
public sealed class SendResultTests
{
	[Theory]
	[InlineData(3, 3, "")]
	[InlineData(5, 0, "")] // Nothing reported how many were addressed.
	[InlineData(2, 3, "Sent to 2 of 3 members. The other member doesn't have Link yet")]
	[InlineData(1, 3, "Sent to 1 of 3 members. The other 2 members don't have Link yet")]
	public void ASenderIsToldWhoItDidNotReach(int recipients, int addressed, string expected)
	{
		var shortfall = new SendResult { MessageId = 1, Recipients = recipients, Addressed = addressed }.Shortfall;

		if (expected.Length == 0)
		{
			Assert.Empty(shortfall);
		}
		else
		{
			Assert.StartsWith(expected, shortfall, StringComparison.Ordinal);
		}
	}
}
