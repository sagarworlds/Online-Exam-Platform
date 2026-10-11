#!/usr/bin/env bash
# NFR-5 dependency gate: fails the build when any NuGet package the solution uses has a known vulnerability, at any severity.
#
# Why it does not trust the exit code: `dotnet list package --vulnerable` exits 0 even when it reports a vulnerable
# package (checked with the .NET 10 SDK, 10.0.112). So the gate reads the findings itself, from two reports that must agree:
# the JSON report (the findings) and the text report (a marker line per vulnerable project). If the JSON is not the
# expected shape, or the text report has no project lines, the gate fails. An unreadable scan must never pass.
#
# Usage: check-nuget-vulnerabilities.sh <solution-or-project>
#   Run after `dotnet restore`, because the scan reads the restored package graph and needs network access to NuGet's
#   vulnerability data.
# Exits 0 when no package is vulnerable, and 1 when one is or when the scan cannot be read.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <solution-or-project>" >&2
  exit 1
fi
target="$1"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

dotnet list "$target" package --vulnerable --include-transitive --format json > "$work/report.json"
dotnet list "$target" package --vulnerable --include-transitive > "$work/report.txt"

# Fail closed on a report we do not recognise: a changed format would otherwise count as zero findings.
if ! jq -e '.projects | type == "array"' "$work/report.json" > /dev/null; then
  echo "::error title=NuGet scan unreadable::The JSON report has no projects array; the gate cannot tell whether packages are vulnerable." >&2
  exit 1
fi
if ! grep -q "vulnerable packages" "$work/report.txt"; then
  echo "::error title=NuGet scan unreadable::The text report has no project lines; the gate cannot tell whether packages are vulnerable." >&2
  exit 1
fi

# One line per vulnerability: the project, the package, its resolved version, the severity and the advisory.
findings="$(jq -r '
  .projects[]? as $project
  | $project.frameworks[]? as $framework
  | ($framework.topLevelPackages[]?, $framework.transitivePackages[]?)
  | select((.vulnerabilities // []) | length > 0)
  | . as $package
  | .vulnerabilities[]
  | "\($project.path) [\($framework.framework)] \($package.id) \($package.resolvedVersion) \(.severity) \(.advisoryurl)"
' "$work/report.json")"

flagged_projects="$(grep -c "has the following vulnerable packages" "$work/report.txt" || true)"
finding_count="$(printf '%s' "$findings" | grep -c . || true)"

if [ "$finding_count" -gt 0 ] || [ "$flagged_projects" -gt 0 ]; then
  echo "::error title=Vulnerable NuGet packages::$finding_count finding(s) in $flagged_projects project(s). Upgrade the package or record a justified exception."
  printf '%s\n' "$findings"
  echo "--- text report ---"
  cat "$work/report.txt"
  exit 1
fi

echo "No vulnerable NuGet packages in $target."
