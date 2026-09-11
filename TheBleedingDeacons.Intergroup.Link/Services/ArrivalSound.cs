using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Plays the platform's own notification tone when a message lands with
/// the app open. See <see cref="IArrivalSound"/> for what this is and is
/// not.
///
/// <para><b>The platform's tone, not one of ours.</b> Shipping an audio
/// file would mean choosing a sound for somebody else's phone and carrying
/// it in the APK; the notification tone is the one the member already
/// recognises as "something arrived", and it follows whatever they have
/// set. It also respects the ringer: a phone on silent stays silent
/// without this code knowing anything about ringer modes.</para>
///
/// <para><b>The preference lives in <see cref="Preferences"/></b> rather
/// than in <c>SecureStorage</c>, which is where Link keeps the session and
/// the private key. This is a yes/no about noise — nothing to protect, and
/// SecureStorage on Android is backed by the keystore, which is a slow
/// place to keep a bool that is read on every arrival.</para>
/// </summary>
public sealed class ArrivalSound : IArrivalSound
{
	private const string PreferenceKey = "arrival_sound_on";

	/// <summary>
	/// On unless turned off. Somebody who has not been to the settings
	/// screen has not asked for silence, and a messaging app that arrives
	/// mute looks broken.
	/// </summary>
	public bool Enabled
	{
		get => Preferences.Default.Get(PreferenceKey, true);
		set => Preferences.Default.Set(PreferenceKey, value);
	}

	public void Play()
	{
		if (!Enabled)
		{
			return;
		}

		try
		{
			PlatformPlay();
		}
		catch (Exception e)
		{
			// Deliberately broad, and deliberately not rethrown. Every
			// caller is on the path that delivers a message, and a phone
			// whose audio stack refuses must still show the message. Logged
			// at Debug because it is a curiosity, not a fault.
			Log.Debug(e, "The arrival sound did not play");
		}
	}

	private static void PlatformPlay()
	{
#if ANDROID
		// RingtoneManager rather than a MediaPlayer we own: no file to
		// load, no player to dispose, and the tone is whatever the member
		// has chosen for notifications. Returns null on a device with no
		// notification sound set, which is a perfectly ordinary state.
		var uri = global::Android.Media.RingtoneManager.GetDefaultUri(
			global::Android.Media.RingtoneType.Notification);

		if (uri is null)
		{
			return;
		}

		var context = global::Android.App.Application.Context;
		var ringtone = global::Android.Media.RingtoneManager.GetRingtone(context, uri);

		ringtone?.Play();
#endif
	}
}
