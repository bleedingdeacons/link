using UIKit;

namespace TheBleedingDeacons.Intergroup.Link;

/// <summary>
/// The iOS entry point.
///
/// <para>Android has no equivalent of this file — there, Android starts
/// <c>MainActivity</c> itself. iOS wants a <c>main</c>, and this is it.
/// </para>
/// </summary>
public static class Program
{
	// This is the main entry point of the application.
	private static void Main(string[] args)
	{
		// If you want to use a different Application Delegate class from
		// "AppDelegate" you can specify it here.
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
