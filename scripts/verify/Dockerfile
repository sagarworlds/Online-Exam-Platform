# The image the browser stages of scripts/verify.sh run in: Playwright's own image (Ubuntu, with Chromium, Firefox and WebKit for exactly the
# Playwright version in apps/web/package-lock.json, so the engines are the ones CI downloads) with the .NET 10 runtime added, so the
# full-stack run can start the API, built on the host, in the same container as the browsers.
ARG PLAYWRIGHT_VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dotnet

FROM mcr.microsoft.com/playwright:v${PLAYWRIGHT_VERSION}-noble

# The API formats dates and numbers by culture, so it needs ICU rather than the invariant mode.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libicu74 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
