#!/usr/bin/env bash
# Runs the CI gate on this machine, so a change is checked here before it goes to GitHub and GitHub's runs are the second look.
#
# The commands are the ones in .github/workflows/ci.yml and ci-extended.yml, in four stages:
#   web        npm audit, lint, production build and unit tests of apps/web         ci.yml  Lint, build & test web (Angular)
#   api        NuGet scan, build and every test of apps/api                         ci.yml  Build & test API (.NET)
#   browsers   Playwright on the three engines, the API stubbed in the browser      Browser tests (chromium, firefox, webkit)
#   fullstack  Playwright on the three engines against the real API and Postgres    ci-extended.yml  Browser tests against the real API
#
# The browser stages run in a Docker image (scripts/verify/Dockerfile): Playwright's image, with the engines CI downloads, plus the .NET
# runtime, so Firefox and WebKit run here as they do on GitHub. The full-stack stage starts a throwaway Postgres container for the run.
# The api stage needs Docker too, because the integration tests start their own Postgres (Testcontainers).
#
# Needs bash (on Windows, Git Bash), Docker, node and npm, the .NET 10 SDK, and jq (the NuGet scan reads its report with it).
# The first browser run builds the image and installs the web dependencies for it, which takes a few minutes; later runs reuse both.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
repo="$PWD"

usage() {
  cat <<'EOF'
usage: scripts/verify.sh [stage ...] [--engines <list>] [--keep-going] [--skip-nuget-scan] [-- <playwright arguments>]

stages (default: all, in this order)
  web         npm audit, lint, build and unit tests of apps/web
  api         NuGet scan, build and every test of apps/api (Docker, for the integration tests)
  browsers    Playwright on the engines below, the API stubbed in the browser (Docker)
  fullstack   Playwright on the engines below against the real API and a throwaway Postgres (Docker)

options
  --engines <list>     comma-separated, from chromium,firefox,webkit (default: all three)
  --keep-going         run the later stages after one fails (default: stop at the first failure)
  --skip-nuget-scan    skip the NuGet vulnerability scan, which needs jq; the summary says it was skipped
  -- <arguments>       passed to `playwright test` in the browser stages, for example: -- e2e/sign-in.spec.ts -g "code"

examples
  scripts/verify.sh                                  everything, as CI does
  scripts/verify.sh browsers --engines firefox       the stubbed browser tests on Firefox only
  scripts/verify.sh fullstack -- e2e/fullstack/students.spec.ts
EOF
}

die() {
  echo "verify: $*" >&2
  exit 2
}

stages=()
engines="chromium,firefox,webkit"
keep_going=0
skip_scan=0
pw_args=()
while [ $# -gt 0 ]; do
  case "$1" in
    web | api | browsers | fullstack) stages+=("$1") ;;
    --engines)
      [ $# -ge 2 ] || die "--engines needs a list"
      engines="$2"
      shift
      ;;
    --engines=*) engines="${1#*=}" ;;
    --keep-going) keep_going=1 ;;
    --skip-nuget-scan) skip_scan=1 ;;
    -h | --help)
      usage
      exit 0
      ;;
    --)
      shift
      pw_args=("$@")
      break
      ;;
    *)
      usage >&2
      die "unknown argument: $1"
      ;;
  esac
  shift
done
[ ${#stages[@]} -gt 0 ] || stages=(web api browsers fullstack)

for engine in ${engines//,/ }; do
  case "$engine" in
    chromium | firefox | webkit) ;;
    *) die "unknown engine '$engine' (chromium, firefox or webkit)" ;;
  esac
done
[ -n "${engines//,/}" ] || die "--engines needs at least one engine"

selected() {
  local stage
  for stage in "${stages[@]}"; do
    [ "$stage" = "$1" ] && return 0
  done
  return 1
}

# --- Before anything runs: what the chosen stages need ------------------------------------------------------------------------

missing=()
need() {
  command -v "$1" > /dev/null 2>&1 || missing+=("  $1: $2")
}
if selected web || selected browsers || selected fullstack; then
  need node "Node.js 22 or newer"
  need npm "comes with Node.js"
fi
if selected api || selected fullstack; then
  need dotnet "the .NET 10 SDK"
fi
if selected api && [ "$skip_scan" -eq 0 ]; then
  need jq "reads the NuGet scan report (Windows: winget install jqlang.jq), or run with --skip-nuget-scan"
fi
if selected api || selected browsers || selected fullstack; then
  need docker "Docker Desktop, or Docker Engine"
fi
if [ ${#missing[@]} -gt 0 ]; then
  echo "verify: these are not installed:" >&2
  printf '%s\n' "${missing[@]}" >&2
  exit 2
fi

if selected api || selected browsers || selected fullstack; then
  docker info > /dev/null 2>&1 || die "Docker is installed but not running. Start Docker Desktop, wait until it says it is running, and run this again."
fi

# A Windows checkout (core.autocrlf=true) of a script from before .gitattributes asked for LF has CRLF line endings, which bash cannot run.
if selected api && [ "$skip_scan" -eq 0 ] && grep -q $'\r' scripts/security/check-nuget-vulnerabilities.sh; then
  die "scripts/security/check-nuget-vulnerabilities.sh has Windows line endings. Check it out again so .gitattributes applies: rm scripts/security/*.sh && git checkout -- scripts/security"
fi

# --- Helpers ------------------------------------------------------------------------------------------------------------------

state="$(mktemp -d)"
trap 'rm -rf "$state"' EXIT
trap 'exit 130' INT TERM

step() {
  printf '\n==> %s\n' "$*"
}

# The path as Docker wants it for a bind mount: on Windows the drive form (D:/Study/...), which Git Bash's /d/Study/... is not.
host_path() {
  if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

# `npm ci` for the host's own node_modules, only when there is none or package-lock.json is newer than what was installed.
host_install() {
  if [ ! -d node_modules ] || [ package-lock.json -nt node_modules/.package-lock.json ]; then
    step "Install the web dependencies (npm ci)"
    npm ci
  fi
}

# The production build is made once per run: the browser stages test the build, and a stale one would test last week's code.
build_web() {
  [ -e "$state/web-built" ] && return 0
  (
    set -e
    cd "$repo/apps/web"
    host_install
    step "Build the web app"
    npm run build
  )
  touch "$state/web-built"
}

# The full-stack run starts the API from its Release build (apps/web/e2e/fullstack/run-api.mjs). The api stage has built it already.
build_api() {
  [ -e "$state/api-built" ] && return 0
  (
    set -e
    cd "$repo/apps/api"
    step "Build the API (errors only: the api stage shows the whole build)"
    dotnet build src/Host/ExamPlatform.Api -c Release -clp:ErrorsOnly
  )
  touch "$state/api-built"
}

pw_version=""
image=""
modules_volume=""
resolve_image() {
  [ -n "$image" ] && return 0
  pw_version="$(cd "$repo/apps/web" && node -p "require('./package-lock.json').packages['node_modules/@playwright/test'].version")"
  image="exam-platform-verify:playwright-$pw_version"
  # The Linux node_modules of the container live in a volume, so the host's own (Windows) ones are neither used nor touched.
  modules_volume="exam-platform-verify-modules-$pw_version"
}

ensure_image() {
  resolve_image
  docker image inspect "$image" > /dev/null 2>&1 && return 0
  step "Build the browser image $image (first run only: it pulls about 2 GB)"
  docker build --build-arg "PLAYWRIGHT_VERSION=$pw_version" -t "$image" scripts/verify
}

# Installs the web dependencies into the volume when package-lock.json is not the one they were installed from.
container_install='lock=$(sha256sum package-lock.json | cut -d" " -f1)
if [ "$(cat node_modules/.verify-lock 2>/dev/null)" != "$lock" ]; then
  echo "Installing the web dependencies for the browser image (npm ci)"
  npm ci --no-audit --no-fund
  echo "$lock" > node_modules/.verify-lock
fi'

network=""
e2e_postgres=""
# Runs a script inside the browser image, in apps/web, with the repository mounted at /repo. CI=1 gives the same Playwright settings as CI.
in_container() {
  MSYS_NO_PATHCONV=1 docker run --rm --init --ipc=host \
    --network "${network:-bridge}" \
    -e CI=1 -e "E2E_POSTGRES=$e2e_postgres" \
    -v "$(host_path "$repo"):/repo" \
    -v "$modules_volume:/repo/apps/web/node_modules" \
    -w /repo/apps/web \
    "$image" bash -ec "$1"
}

# --project=<prefix><engine> for each engine, and the extra arguments quoted for the shell inside the container.
project_args() {
  local engine
  for engine in ${engines//,/ }; do printf -- '--project=%s%s ' "$1" "$engine"; done
}
extra_args() {
  [ ${#pw_args[@]} -eq 0 ] || printf '%q ' "${pw_args[@]}"
}

# --- The stages. Each runs in its own subshell with `set -e`, so its first failing command ends it --------------------------------

stage_web() {
  cd "$repo/apps/web"
  host_install
  step "Dependency audit (npm, high and critical)"
  npm audit --audit-level=high
  step "Lint"
  npm run lint
  build_web
  step "Unit tests"
  npm test -- --watch=false
}

stage_api() {
  cd "$repo/apps/api"
  step "Restore"
  dotnet restore ExamPlatform.slnx
  if [ "$skip_scan" -eq 1 ]; then
    step "Dependency vulnerability scan (NuGet): SKIPPED by --skip-nuget-scan"
  else
    step "Dependency vulnerability scan (NuGet)"
    bash "$repo/scripts/security/check-nuget-vulnerabilities.sh" ExamPlatform.slnx
  fi
  step "Build (the security analyzers are errors, so a finding fails here)"
  dotnet build ExamPlatform.slnx --no-restore -c Release
  touch "$state/api-built"
  step "Tests (the integration tests start Postgres in Docker)"
  dotnet test ExamPlatform.slnx --no-build -c Release
}

stage_browsers() {
  build_web
  ensure_image
  # One engine after another, as CI runs them (each its own job), and two workers, which is what Playwright picks on a GitHub runner of
  # four cores. All three at once, or four workers, starve WebKit in a Docker VM: its clicks then time out on tests that pass alone.
  # An engine's report and traces go in a folder of its own, so the next engine does not clear them. A `--workers` after `--` wins.
  local engine script="$container_install
npm run e2e:typecheck
status=0"
  for engine in ${engines//,/ }; do
    script+="
echo; echo '== $engine'
PLAYWRIGHT_HTML_REPORT=playwright-report/$engine PLAYWRIGHT_HTML_OUTPUT_DIR=playwright-report/$engine \\
  npx playwright test --project=$engine --output=test-results/$engine --workers=2 $(extra_args) || status=1"
  done
  step "Browser tests on ${engines//,/, } (the API stubbed in the browser)"
  in_container "$script
exit \$status"
}

stage_fullstack() {
  build_web
  build_api
  ensure_image

  # A Postgres of its own for the run, gone afterwards, so a developer's own database is never written to.
  network="exam-verify-net-$$"
  local postgres="exam-verify-pg-$$"
  # In double quotes so the names are filled in now: the function's own variables are gone when the stage's subshell ends.
  trap "docker rm -f '$postgres' > /dev/null 2>&1; docker network rm '$network' > /dev/null 2>&1" EXIT
  step "Start a Postgres for the run"
  docker network create "$network" > /dev/null
  MSYS_NO_PATHCONV=1 docker run -d --rm --name "$postgres" --network "$network" \
    -e POSTGRES_USER=examplatform -e POSTGRES_PASSWORD=examplatform -e POSTGRES_DB=examplatform \
    --tmpfs /var/lib/postgresql/data postgres:16 > /dev/null
  local tries=0
  until docker exec "$postgres" pg_isready -U examplatform > /dev/null 2>&1; do
    tries=$((tries + 1))
    [ "$tries" -le 40 ] || {
      echo "verify: Postgres did not become ready" >&2
      return 1
    }
    sleep 1
  done
  e2e_postgres="Host=$postgres;Port=5432;Database=examplatform;Username=examplatform;Password=examplatform"

  step "Browser tests against the real API on ${engines//,/, }"
  in_container "$container_install
npm run e2e:fullstack -- $(project_args 'fullstack-') $(extra_args)"
}

# --- Run them, in order, and say what happened ------------------------------------------------------------------------------

results=()
failed_stages=()
failed=0
started=$SECONDS
for stage in web api browsers fullstack; do
  selected "$stage" || continue
  if [ "$failed" -eq 1 ] && [ "$keep_going" -eq 0 ]; then
    results+=("$(printf '%-10s not run (an earlier stage failed)' "$stage")")
    continue
  fi
  printf '\n################ %s ################\n' "$stage"
  stage_started=$SECONDS
  (
    set -e
    "stage_$stage"
  )
  status=$?
  took=$((SECONDS - stage_started))
  if [ "$status" -eq 0 ]; then
    results+=("$(printf '%-10s passed   %dm %02ds' "$stage" $((took / 60)) $((took % 60)))")
  else
    failed=1
    failed_stages+=("$stage")
    results+=("$(printf '%-10s FAILED   %dm %02ds' "$stage" $((took / 60)) $((took % 60)))")
  fi
done

total=$((SECONDS - started))
printf '\n################ summary ################\n'
printf '%s\n' "${results[@]}"
[ "$skip_scan" -eq 0 ] || ! selected api || echo "(the NuGet vulnerability scan was skipped)"
printf 'total      %dm %02ds\n' $((total / 60)) $((total % 60))
if [ "$failed" -eq 1 ]; then
  for stage in ${failed_stages[@]+"${failed_stages[@]}"}; do
    case "$stage" in
      browsers | fullstack)
        echo
        echo "A failed browser run leaves its report and traces in apps/web/playwright-report and apps/web/test-results (the stubbed run in a"
        echo "folder per engine; open a trace with: npx playwright show-trace <trace.zip>). The full-stack run's API log is apps/web/e2e/.run/api.log."
        break
        ;;
    esac
  done
  exit 1
fi
