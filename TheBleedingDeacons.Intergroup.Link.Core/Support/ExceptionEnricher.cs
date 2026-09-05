using System.Diagnostics;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Link.Support
{
	/// <summary>
	/// Enriches log events with flattened exception details for easier filtering
	/// in log aggregators (BetterStack, etc.) that key off top-level fields rather
	/// than nested objects. Walks the full inner-exception chain (including
	/// <see cref="AggregateException"/>) so we don't lose diagnostic context behind
	/// the outermost wrapper, and demystifies stack traces so async/iterator frames
	/// read like the source that produced them.
	/// </summary>
	public class ExceptionEnricher : ILogEventEnricher
	{
		// Async state-machine boilerplate typically eats the first 5–10 frames, so
		// the previous cap of 5 was effectively zero useful frames. 40 is generous
		// enough to see the real call chain even in deep async/LINQ/EF stacks while
		// still bounding payload size.
		private const int MaxStackTraceLines = 40;

		// Guard against pathological cycles (Exception.InnerException is normally a
		// tree, but AggregateException flattening plus custom exception types have
		// been known to produce loops).
		private const int MaxInnerDepth = 10;

		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
		{
			var ex = logEvent.Exception;
			if (ex == null)
			{
				return;
			}

			var demystified = ex.Demystify();

			logEvent.AddPropertyIfAbsent(
				propertyFactory.CreateProperty("ExceptionType", ex.GetType().FullName ?? ex.GetType().Name));

			logEvent.AddPropertyIfAbsent(
				propertyFactory.CreateProperty("ExceptionMessage", ex.Message));

			logEvent.AddPropertyIfAbsent(
				propertyFactory.CreateProperty("ExceptionStackTrace", TruncateStack(demystified.StackTrace)));

			var innerChain = BuildInnerChain(ex);
			if (!string.IsNullOrEmpty(innerChain))
			{
				logEvent.AddPropertyIfAbsent(
					propertyFactory.CreateProperty("ExceptionInnerChain", innerChain));
			}
		}

		private static string TruncateStack(string? stack)
		{
			if (string.IsNullOrEmpty(stack))
			{
				return string.Empty;
			}

			var lines = stack.Split('\n');
			if (lines.Length <= MaxStackTraceLines)
			{
				return stack;
			}

			return string.Join('\n', lines.Take(MaxStackTraceLines))
				+ $"\n  ... ({lines.Length - MaxStackTraceLines} more frames truncated)";
		}

		/// <summary>
		/// Formats every inner exception (and, for <see cref="AggregateException"/>,
		/// every entry in <see cref="AggregateException.InnerExceptions"/>) as a single
		/// string. Each frame is prefixed with its depth so BetterStack can still
		/// render it legibly as a flat field.
		/// </summary>
		private static string BuildInnerChain(Exception root)
		{
			var sb = new StringBuilder();
			AppendInners(sb, root, depth: 1, visited: new HashSet<Exception>(ReferenceEqualityComparer.Instance));
			return sb.ToString().TrimEnd();
		}

		private static void AppendInners(StringBuilder sb, Exception current, int depth, HashSet<Exception> visited)
		{
			if (depth > MaxInnerDepth)
			{
				sb.Append('[').Append(depth).AppendLine("] ... (inner exception depth limit reached)");
				return;
			}

			if (current is AggregateException agg)
			{
				var flattened = agg.Flatten();
				foreach (var inner in flattened.InnerExceptions)
				{
					if (!visited.Add(inner))
					{
						continue;
					}

					AppendOne(sb, inner, depth);

					// Pass the branch itself, not its inner exception.
					// AppendInners appends what is *underneath* what it is
					// given, so handing it the child here made it start at
					// the grandchild — dropping a whole level. Flatten()
					// only unwraps nested AggregateExceptions, so a branch
					// that wraps an ordinary exception (an
					// HttpRequestException around a TimeoutException, say)
					// lost the part that said what actually went wrong.
					AppendInners(sb, inner, depth + 1, visited);
				}

				return;
			}

			if (current.InnerException == null)
			{
				return;
			}

			var next = current.InnerException;
			if (!visited.Add(next))
			{
				return;
			}

			AppendOne(sb, next, depth);
			AppendInners(sb, next, depth + 1, visited);
		}

		private static void AppendOne(StringBuilder sb, Exception ex, int depth)
		{
			sb.Append('[').Append(depth).Append("] ")
			  .Append(ex.GetType().FullName ?? ex.GetType().Name)
			  .Append(": ")
			  .AppendLine(ex.Message);

			var stack = ex.Demystify().StackTrace;
			if (!string.IsNullOrEmpty(stack))
			{
				sb.AppendLine(TruncateStack(stack));
			}
		}
	}
}