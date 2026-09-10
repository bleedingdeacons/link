#!/usr/bin/env bash
#
# Write Platforms/iOS/GoogleService-Info.plist, which is what makes iOS push
# work.
#
# <b>Without this the build succeeds and the app polls.</b> The csproj
# includes the file only Condition="Exists(...)", so a CI checkout — where it
# is git-ignored and therefore absent — produces an .ipa in which
# FirebasePush.Configure() finds no plist, PushRegistrar reports no transport,
# and the handset collects its messages on the poll interval instead of at
# once. Everything still arrives; nothing arrives promptly.
#
# <b>Why this job has a step and the Android job does not.</b> Not principle —
# an oversight this file only half corrects. The workflow's own preamble
# records that CI builds without google-services.json and calls the result a
# documented degraded state, and that is still true of the APK. The difference
# is the install path: the .ipa this job archives is the artifact Sideloadly
# puts on the iPhone, so a plist absent here is push absent from the only iOS
# build anybody runs. The Android head is installed from local builds as often
# as from CI. Worth closing the other half too.
#
# A SECRET, not a variable. GoogleService-Info.plist carries an API key and a
# project number — neither a password, and Google expects the file to ship
# inside an app bundle — but a public repository's Actions log is a worse
# place for it than the inside of a binary. So this script NEVER prints the
# file, only what it can safely say about it.
#
# Unset is not a failure. A fork has no Firebase project and must still build.

set -euo pipefail

target=TheBleedingDeacons.Intergroup.Link/Platforms/iOS/GoogleService-Info.plist
expected_bundle=com.thebleedingdeacons.intergroup.link

if [ -z "${GOOGLE_SERVICES_PLIST:-}" ]; then
	echo 'GOOGLE_SERVICES_PLIST is not set; building without Firebase.'
	echo 'The app will enrol POLL-ONLY: alerts arrive on the poll interval while'
	echo 'it is running, and a handset with the app closed will not ring.'
	echo 'Set it as a repository secret to build an artifact with working push.'
	exit 0
fi

mkdir -p "$(dirname "$target")"
printf '%s' "$GOOGLE_SERVICES_PLIST" > "$target"

# Validate without echoing. A plist for the wrong app is worse than an absent
# one: it produces a build that looks completely healthy, registers a token
# against somebody else's project, and silently never receives a push.
python3 - "$target" "$expected_bundle" <<'PY'
import plistlib
import sys

path, expected = sys.argv[1], sys.argv[2]

try:
    with open(path, "rb") as handle:
        data = plistlib.load(handle)
except Exception:
    # Deliberately not printing the exception: plistlib quotes the document.
    sys.exit("::error::GOOGLE_SERVICES_PLIST is not a readable plist.")

if not isinstance(data, dict):
    sys.exit("::error::GOOGLE_SERVICES_PLIST is not a plist dictionary; this is not a GoogleService-Info.plist.")

bundle = data.get("BUNDLE_ID")
if bundle is None:
    sys.exit("::error::GOOGLE_SERVICES_PLIST has no BUNDLE_ID; this is not a GoogleService-Info.plist.")

if bundle != expected:
    # Naming the expected bundle id is fine — it is in the csproj and the
    # Info.plist. What is in the secret stays unprinted.
    sys.exit(
        f"::error::GOOGLE_SERVICES_PLIST is for another app, not {expected}. "
        "Download the file for this app's iOS entry from the Firebase console."
    )

# GCM_SENDER_ID absent means the Firebase iOS app exists but Cloud Messaging
# was never enabled on it, which builds and registers and never delivers.
if not data.get("GCM_SENDER_ID"):
    sys.exit(
        "::error::GOOGLE_SERVICES_PLIST has no GCM_SENDER_ID, so Cloud Messaging is not "
        "enabled for this iOS app. Enable it in the Firebase console and download the file again."
    )

if data.get("IS_GCM_ENABLED") is False:
    sys.exit("::error::GOOGLE_SERVICES_PLIST has IS_GCM_ENABLED=false; this build could never receive a push.")

print(f"GoogleService-Info.plist validated for {expected}, with Cloud Messaging enabled.")
PY

echo "Wrote $target ($(wc -c < "$target") bytes). Contents deliberately not logged."
