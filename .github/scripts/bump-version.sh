#!/usr/bin/env bash
#
# Computes the next version from the commits just merged to main, and writes it
# into the Link csproj.
#
# Ported from Register's script of the same name, deliberately: the two apps
# are MAUI heads with the identical version shape, and a second dialect would
# only mean two sets of habits. Keep them in step.
#
# Rules (see the `version` job in .github/workflows/ci.yml):
#   any `feat:` commit  -> minor bump, patch reset to 0
#   anything else       -> patch bump
#   never major         -> a major release is a deliberate act, taken by hand
#
# ApplicationVersion — the Android versionCode — advances by one alongside it.
# It is what decides whether a build can update an existing install, and Play
# refuses an upload whose code has not advanced, so it must never go backwards
# or stall.
#
# This runs on push to main, not on the pull request. Bumping in the PR put the
# branch's final commit under github-actions[bot], and GitHub will not run
# checks on a bot-authored head unaided — the runs either sat blocked at
# `action_required`, or with [skip ci] never queued at all. Either way the
# commit that actually merged was the one commit nobody built. Versioning after
# the merge keeps pull requests fully checked; the cost is that the version a
# change ships as is decided at merge rather than visible in review.
#
# Usage:  bump-version.sh <commit-range>
#   e.g.  bump-version.sh abc123..def456
# Local dry run against the last merge:
#   .github/scripts/bump-version.sh HEAD~1..HEAD
#
set -euo pipefail

CSPROJ="TheBleedingDeacons.Intergroup.Link/TheBleedingDeacons.Intergroup.Link.csproj"
RANGE="${1:-}"

if [ -z "$RANGE" ]; then
    echo "::error::A commit range is required, e.g. \$before..\$after." >&2
    exit 1
fi

read_prop() { # read_prop <text> <property>
    printf '%s' "$1" | sed -n "s|.*<$2>\([^<]*\)</$2>.*|\1|p" | head -1
}

emit() { # emit <key> <value> — GitHub output when in CI, otherwise just echo
    echo "$1=$2"
    [ -n "${GITHUB_OUTPUT:-}" ] && echo "$1=$2" >> "$GITHUB_OUTPUT"
    return 0
}

# Never bump on top of a bump. Closes two cases: the push this workflow makes
# itself, which would otherwise loop, and a manual re-run of an older push.
head_subject="$(git log -1 --format='%s')"
case "$head_subject" in
    "chore: version "*)
        echo "HEAD is already a version commit; nothing to do."
        emit changed false
        exit 0
        ;;
esac

current="$(cat "$CSPROJ")"
base_version="$(read_prop "$current" ApplicationDisplayVersion)"
base_code="$(read_prop "$current" ApplicationVersion)"

# Three parts, always. AssemblyVersion is $(ApplicationDisplayVersion).0, so a
# fourth component here produces a five-part version and fails the build.
if ! [[ "$base_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "::error::ApplicationDisplayVersion is '$base_version'; expected three numeric parts." >&2
    exit 1
fi
if ! [[ "$base_code" =~ ^[0-9]+$ ]]; then
    echo "::error::ApplicationVersion is '$base_code'; expected an integer." >&2
    exit 1
fi

# The first release ships the version already in the csproj rather than
# bumping past it.
#
# Without this the first merge of a `feat:` PR turns 1.0.0 into 1.1.0, and
# the app's first ever release is a version nobody chose. Fellowship's
# release job has the same rule for the same reason; the difference is only
# how "has anything shipped?" is answered, and here it is the tag this
# workflow writes.
#
# Tags are what make this durable. A marker based on "is there a version
# commit in history" would be wrong on the second run, because the first
# run makes no commit — there is nothing to commit when the version does
# not change.
if [ -z "$(git tag --list 'v[0-9]*')" ]; then
    echo "No release tag yet; adopting $base_version as the first release."
    emit changed false
    emit first true
    emit version "$base_version"
    emit code "$base_code"
    emit bump none
    exit 0
fi

# Subjects only. Scanning bodies too would let prose like "reverts the feat:
# commit" trigger a minor bump.
subjects="$(git log --no-merges --format='%s' "$RANGE" 2>/dev/null || true)"

if [ -z "$subjects" ]; then
    echo "No non-merge commits in $RANGE; nothing to version."
    emit changed false
    exit 0
fi

if printf '%s\n' "$subjects" | grep -qiE '^feat(\([^)]*\))?!?:'; then
    bump=minor
else
    bump=patch
fi

IFS=. read -r major minor patch <<< "$base_version"
case "$bump" in
    minor) minor=$((minor + 1)); patch=0 ;;
    patch) patch=$((patch + 1)) ;;
esac
new_version="$major.$minor.$patch"
new_code=$((base_code + 1))

echo "range:   $RANGE"
echo "commits:"
printf '%s\n' "$subjects" | sed 's/^/  /'
echo "current: $base_version (code $base_code)"
echo "bump:    $bump"
echo "target:  $new_version (code $new_code)"

sed -i "s|<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>|<ApplicationDisplayVersion>$new_version</ApplicationDisplayVersion>|" "$CSPROJ"
sed -i "s|<ApplicationVersion>[^<]*</ApplicationVersion>|<ApplicationVersion>$new_code</ApplicationVersion>|" "$CSPROJ"

emit changed true
emit first false
emit version "$new_version"
emit code "$new_code"
emit bump "$bump"
