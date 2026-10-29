using System.Diagnostics;
using System.Security.Cryptography;
using Serilog;
using Serilog.Context;
using Serilog.Core.Enrichers;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// One sign-in, key replacement or sign-out, as the log sees it: where it
/// started, every step it took, and how it ended.
///
/// <para><b>Why one object rather than a line per step.</b> A sign-in is
/// not one request. Google may try the native sheet, fall back to the
/// browser, spend a one-time code and then enrol, and each of those can
/// fail on its own terms; Fellowship's client logs its refusals from a
/// layer that does not know a sign-in is happening at all. While an attempt
/// is open, every event logged anywhere on its async path carries
/// <c>AuthAttempt</c>, <c>AuthOperation</c> and <c>AuthMethod</c> through
/// Serilog's log context — which Inventory enriches from — so one query on
/// the attempt id gives the whole story, from whichever layer told
/// it.</para>
///
/// <para><b>Exactly one outcome line.</b> Succeeded, cancelled, refused or
/// threw, with how long it took. Cancelling is Information, not a Warning:
/// it is a member changing their mind, and a dashboard of warnings full of
/// closed browser tabs hides the refusals that matter.</para>
///
/// <para><b>What never goes in.</b> Passwords, one-time codes, ID tokens,
/// Firebase tokens and the device token. The address a member typed does,
/// because "which address failed" is the first question about a refused
/// sign-in — and <see cref="EmailRedactor"/> turns it into a token before
/// any sink sees it.</para>
/// </summary>
public sealed class AuthAttempt : IDisposable
{
	private readonly ILogger? _log;
	private readonly IDisposable _context;
	private readonly long _started;
	private bool _finished;

	private AuthAttempt(string operation, string method, ILogger? log)
	{
		_log = log;
		Operation = operation;
		Method = method;
		Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
		_started = Stopwatch.GetTimestamp();
		_context = LogContext.Push(
			new PropertyEnricher("AuthAttempt", Id),
			new PropertyEnricher("AuthOperation", operation),
			new PropertyEnricher("AuthMethod", method));
	}

	/// <summary>Eight hex digits, unique enough to find one attempt in a day's log.</summary>
	public string Id { get; }

	public string Operation { get; }

	public string Method { get; }

	public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

	private long ElapsedMs => (long)Elapsed.TotalMilliseconds;

	// Resolved on every call rather than captured. Inventory rebuilds the
	// pipeline when it is told where to ship, which a sign-in is exactly the
	// moment for, and a captured logger would be a disposed one by the time
	// the outcome is written.
	private ILogger Log => _log ?? Serilog.Log.Logger;

	/// <summary>
	/// Open an attempt and log that it started.
	/// </summary>
	/// <param name="operation">What is being done: "Sign-in", "Key replacement", "Sign-out".</param>
	/// <param name="method">How: a provider name, "apple", "password".</param>
	/// <param name="log">For tests; the app logs to the global logger.</param>
	public static AuthAttempt Begin(string operation, string method, ILogger? log = null)
	{
		var attempt = new AuthAttempt(operation, method, log);

		attempt.Log.Information(
			"{AuthOperation} with {AuthMethod} started (attempt {AuthAttempt})",
			operation,
			method,
			attempt.Id);

		return attempt;
	}

	/// <summary>Log how a sign-in ended, and hand the result on.</summary>
	public EnrolmentResult Finished(EnrolmentResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		if (result.Succeeded)
		{
			Succeeded(result.Session!.DeviceId, result.Session.MemberId);
		}
		else
		{
			NotSucceeded(result.Error, result.Unavailable);
		}

		return result;
	}

	/// <summary>Log how a key replacement ended, and hand the result on.</summary>
	public RotateKeyResult Finished(RotateKeyResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		if (result.Succeeded)
		{
			_finished = true;
			Log.Information(
				"{AuthOperation} with {AuthMethod} succeeded in {ElapsedMs} ms",
				Operation,
				Method,
				ElapsedMs);
		}
		else
		{
			NotSucceeded(result.Error, result.Unavailable);
		}

		return result;
	}

	/// <summary>
	/// Log an attempt that ended in a plain yes or no — a sign-out, which
	/// happens on the handset whatever the server said.
	/// </summary>
	public void Finished(bool succeeded, string detail)
	{
		_finished = true;

		if (succeeded)
		{
			Log.Information(
				"{AuthOperation} with {AuthMethod} finished in {ElapsedMs} ms: {Detail}",
				Operation,
				Method,
				ElapsedMs,
				detail);
		}
		else
		{
			Log.Warning(
				"{AuthOperation} with {AuthMethod} finished in {ElapsedMs} ms, but {Detail}",
				Operation,
				Method,
				ElapsedMs,
				detail);
		}
	}

	/// <summary>
	/// Log an exception escaping the attempt, from an exception filter.
	///
	/// <para>Always false, so the filter never catches: the exception goes
	/// on exactly as it would have, and is logged while the attempt's
	/// properties are still on the log context — which a catch block in a
	/// caller, after this has been disposed, would no longer have.</para>
	/// </summary>
	public bool Threw(Exception exception)
	{
		_finished = true;
		Log.Error(
			exception,
			"{AuthOperation} with {AuthMethod} threw after {ElapsedMs} ms",
			Operation,
			Method,
			ElapsedMs);

		return false;
	}

	public void Dispose()
	{
		if (!_finished)
		{
			// A path that returned without saying how it ended. Not a fault
			// in the sign-in, but a hole in the log worth seeing.
			Log.Debug(
				"{AuthOperation} with {AuthMethod} ended after {ElapsedMs} ms without an outcome",
				Operation,
				Method,
				ElapsedMs);
		}

		_context.Dispose();
	}

	private void Succeeded(long deviceId, long memberId)
	{
		_finished = true;
		Log.Information(
			"{AuthOperation} with {AuthMethod} succeeded in {ElapsedMs} ms: device {DeviceId}, member {MemberId}",
			Operation,
			Method,
			ElapsedMs,
			deviceId,
			memberId);
	}

	private void NotSucceeded(string error, bool unavailable)
	{
		_finished = true;

		if (unavailable)
		{
			Log.Information(
				"{AuthOperation} with {AuthMethod} is not offered by the intergroup ({ElapsedMs} ms)",
				Operation,
				Method,
				ElapsedMs);
		}
		else if (string.IsNullOrEmpty(error))
		{
			// An empty error is the member backing out — the screen says
			// nothing, and neither should a warning.
			Log.Information(
				"{AuthOperation} with {AuthMethod} cancelled by the member after {ElapsedMs} ms",
				Operation,
				Method,
				ElapsedMs);
		}
		else
		{
			Log.Warning(
				"{AuthOperation} with {AuthMethod} failed after {ElapsedMs} ms: {Reason}",
				Operation,
				Method,
				ElapsedMs,
				error);
		}
	}
}
