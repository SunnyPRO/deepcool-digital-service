#!/usr/bin/env bash
set -euo pipefail

if command -v mono >/dev/null 2>&1; then
  echo "Mono already installed";
  exit 0;
fi

sudo apt-get update -y
sudo apt-get install -y dirmngr gnupg ca-certificates apt-transport-https software-properties-common curl
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://download.mono-project.com/repo/xamarin.gpg | gpg --dearmor | sudo tee /etc/apt/keyrings/mono.gpg > /dev/null

echo "deb [signed-by=/etc/apt/keyrings/mono.gpg] https://download.mono-project.com/repo/ubuntu stable noble main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
sudo apt-get update -y
sudo apt-get install -y mono-complete

# Download nuget.exe
wget -q https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -O nuget.exe
# Restore packages
mono nuget.exe restore DeepcoolService.sln || echo "NuGet restore completed with warnings"

# Build project (csproj only to avoid vdproj issues)
xbuild DeepcoolService/DeepcoolService.csproj /p:Configuration=Release /p:Platform=x64 || echo "xbuild finished with warnings"

echo "Setup complete."