#!/usr/bin/env bash
# NFR-5 security headers check: fetches one HTTPS URL and fails unless the final response carries the defensive headers the
# platform requires. CI runs it against the deployed static site and the deployed API. The URLs come from repository
# variables, so no host name is stored in the repository.
#
# Checks:
#   Content-Security-Policy   present, with default-src and frame-ancestors, and without unsafe-eval
#   X-Content-Type-Options    nosniff
#   frame protection          X-Frame-Options DENY or SAMEORIGIN, or the CSP's frame-ancestors (not "*")
#   Referrer-Policy           one of the strict values below
#   Strict-Transport-Security max-age of at least one year
#
# Why HTTPS only: browsers ignore Strict-Transport-Security over plain HTTP, so a pass over HTTP would prove nothing.
# Why the last response: a redirect's own headers are not the page's headers, so the check reads the response it ends on.
#
# Usage: check-security-headers.sh <https-url>
# Exits 0 when every check passes, 1 when a check fails, and 2 when the URL is missing, not HTTPS, or cannot be fetched.
set -euo pipefail

readonly MIN_HSTS_MAX_AGE=31536000

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <https-url>" >&2
  exit 2
fi
url="$1"

case "$url" in
  https://*) ;;
  *)
    echo "::error title=Not HTTPS::$url must be an https:// URL. Browsers ignore HSTS over plain HTTP." >&2
    exit 2
    ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Retries cover a free API service that is asleep: its first answer can take a minute or two.
if ! curl --silent --show-error --location --max-time 150 --retry 2 --retry-delay 20 --retry-all-errors \
  --dump-header "$work/all-headers.txt" --output /dev/null "$url"; then
  echo "::error title=Fetch failed::Could not fetch $url" >&2
  exit 2
fi

# Keep only the block of the last response (after the last status line), without carriage returns.
tr -d '\r' < "$work/all-headers.txt" | awk '/^HTTP\//{block=""} {block = block $0 "\n"} END{printf "%s", block}' > "$work/final.txt"
if ! grep -q '^HTTP/' "$work/final.txt"; then
  echo "::error title=No response::$url sent no HTTP response headers." >&2
  exit 2
fi

# The value of a header in the final response, or nothing. The last occurrence wins when a header is repeated.
header_value() {
  grep -i "^$1:" "$work/final.txt" | tail -n 1 | sed -E 's/^[^:]+:[[:space:]]*//' || true
}

failures=0
pass() { echo "PASS  $1: $2"; }
fail() {
  echo "::error title=Security header ($1)::$2"
  echo "FAIL  $1: $2"
  failures=$((failures + 1))
}

check_csp() {
  local csp
  csp="$(header_value Content-Security-Policy)"
  if [ -z "$csp" ]; then
    fail "Content-Security-Policy" "missing"
  elif [[ "$csp" != *"default-src"* ]]; then
    fail "Content-Security-Policy" "no default-src directive: $csp"
  elif [[ "$csp" != *"frame-ancestors"* ]]; then
    fail "Content-Security-Policy" "no frame-ancestors directive: $csp"
  elif [[ "$csp" == *"unsafe-eval"* ]]; then
    fail "Content-Security-Policy" "allows unsafe-eval: $csp"
  else
    pass "Content-Security-Policy" "$csp"
  fi
}

check_nosniff() {
  local value
  value="$(header_value X-Content-Type-Options)"
  if [ "${value,,}" = "nosniff" ]; then
    pass "X-Content-Type-Options" "$value"
  else
    fail "X-Content-Type-Options" "expected nosniff, got '${value:-nothing}'"
  fi
}

check_frame_protection() {
  local xfo csp frame
  xfo="$(header_value X-Frame-Options)"
  csp="$(header_value Content-Security-Policy)"
  frame="${xfo^^}"
  if [ "$frame" = "DENY" ] || [ "$frame" = "SAMEORIGIN" ]; then
    pass "Frame protection" "X-Frame-Options: $xfo"
  elif [[ "$csp" == *"frame-ancestors"* && "$csp" != *"frame-ancestors *"* ]]; then
    pass "Frame protection" "CSP frame-ancestors"
  else
    fail "Frame protection" "neither X-Frame-Options DENY/SAMEORIGIN nor a restricting CSP frame-ancestors"
  fi
}

check_referrer_policy() {
  local value
  value="$(header_value Referrer-Policy)"
  case "${value,,}" in
    no-referrer | same-origin | strict-origin | strict-origin-when-cross-origin)
      pass "Referrer-Policy" "$value"
      ;;
    *)
      fail "Referrer-Policy" "expected a strict policy (no-referrer, same-origin, strict-origin or strict-origin-when-cross-origin), got '${value:-nothing}'"
      ;;
  esac
}

check_hsts() {
  local value max_age
  value="$(header_value Strict-Transport-Security)"
  max_age="$(grep -oE 'max-age=[0-9]+' <<< "$value" | head -n 1 | cut -d= -f2 || true)"
  if [ -z "$value" ]; then
    fail "Strict-Transport-Security" "missing"
  elif [ -z "$max_age" ] || [ "$max_age" -lt "$MIN_HSTS_MAX_AGE" ]; then
    fail "Strict-Transport-Security" "max-age must be at least $MIN_HSTS_MAX_AGE seconds, got '$value'"
  else
    pass "Strict-Transport-Security" "$value"
  fi
}

echo "Checking security headers of $url"
check_csp
check_nosniff
check_frame_protection
check_referrer_policy
check_hsts

if [ "$failures" -gt 0 ]; then
  echo "$failures security header check(s) failed for $url."
  exit 1
fi
echo "All security header checks passed for $url."
