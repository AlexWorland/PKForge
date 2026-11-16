# Build-and-smoke environment for the Linux head (the one the port was developed in).
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      xvfb xauth x11-apps imagemagick \
      libgtk-3-0t64 libsdl2-2.0-0 libfontconfig1 \
      libegl1 libgles2 libgl1 libgl1-mesa-dri \
      libxkbcommon0 libxkbcommon-x11-0 \
      sway xdotool jq procps \
 && rm -rf /var/lib/apt/lists/*
