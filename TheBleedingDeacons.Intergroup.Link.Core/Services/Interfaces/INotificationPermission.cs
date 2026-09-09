namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Whether this phone will still show a notification Link posts.
///
/// <para><b>It only reads, and never prompts.</b> Asking for the
/// permission is <c>MainActivity</c>'s job, once, at launch — where a
/// member who says no still has a working app. This is called to
/// draw an indicator on the settings screen, and a settings screen that
/// raised a permission sheet on arrival would be one nobody opens
/// twice.</para>
///
/// <para>Answers false when it cannot tell. An indicator that
/// under-promises gets checked; one that over-promises is how a phone
/// stops telling anybody about their messages.</para>
/// </summary>
public interface INotificationPermission
{
	Task<bool> IsGrantedAsync();
}
