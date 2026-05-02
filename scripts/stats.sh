#!/usr/bin/env bash
set -euo pipefail

# Engagement dashboard for the current GitHub repo.
# Shows stars, downloads, traffic, and open issues at a glance.
# Run from anywhere inside the repo.

REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"

bar() { printf '%.0s═' {1..60}; echo; }

bar
echo "  $REPO — engagement dashboard"
echo "  $(date '+%Y-%m-%d %H:%M %Z')"
bar

# --- Repo stats ---
gh api "repos/$REPO" --jq '
    "\n[Repo]"
  + "\n  Stars:        " + (.stargazers_count|tostring)
  + "\n  Forks:        " + (.forks_count|tostring)
  + "\n  Watchers:     " + (.subscribers_count|tostring)
  + "\n  Open issues:  " + (.open_issues_count|tostring)
'

# --- Latest release ---
echo ""
echo "[Latest release]"
gh api "repos/$REPO/releases/latest" --jq '
    "  Tag:       \(.tag_name)"
  + "\n  Published: \(.published_at[:10])"
  + "\n  Assets:"
  + (if (.assets | length) == 0 then
       "\n    (none)"
     else
       "\n" + ([.assets[] | "    \(.name): \(.download_count)"] | join("\n"))
     end)
  + "\n  Total:     " + ([.assets[].download_count] | add // 0 | tostring) + " downloads"
' 2>/dev/null || echo "  (no published release)"

# --- All-time downloads across releases ---
echo ""
echo "[All releases — lifetime downloads]"
gh api "repos/$REPO/releases" --paginate --jq '
  .[] | "  \(.tag_name): " + ([.assets[].download_count] | add // 0 | tostring) + " downloads"
' || echo "  (none)"

# --- Traffic (last 14 days; requires push access) ---
echo ""
echo "[Traffic — past 14 days]"
gh api "repos/$REPO/traffic/views" --jq '"  Views:  total=\(.count), unique=\(.uniques)"' 2>/dev/null \
  || echo "  Views:  (unavailable — push access required)"
gh api "repos/$REPO/traffic/clones" --jq '"  Clones: total=\(.count), unique=\(.uniques)"' 2>/dev/null \
  || echo "  Clones: (unavailable — push access required)"

# --- Top referrers ---
echo ""
echo "[Top referrers — past 14 days]"
gh api "repos/$REPO/traffic/popular/referrers" --jq '
  if length == 0 then "  (no referrers yet)"
  else (.[] | "  \(.referrer): \(.count) views (\(.uniques) unique)")
  end
' 2>/dev/null || echo "  (unavailable)"

# --- Recent issues ---
echo ""
echo "[Recent open issues]"
ISSUES=$(gh issue list --state open --limit 5 --json number,title,createdAt -q \
  '.[] | "  #\(.number) \(.title) (opened \(.createdAt[:10]))"' 2>/dev/null || true)
if [ -z "$ISSUES" ]; then
  echo "  (none)"
else
  echo "$ISSUES"
fi

echo ""
