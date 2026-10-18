# Download

> **Code signing for this project is provided by the [SignPath Foundation](https://signpath.org/foundation).**

## Releases

Download the latest build from GitHub Releases:

<https://github.com/KaBanei/peripheral-battery/releases>

The first signed release is pending SignPath Foundation approval. Until then:

- unsigned CI builds are available under **Actions → build → Artifacts**;
- you can build from source with the steps below.

## Build from source

Requirements: Windows 10/11 x64 with .NET Framework 4.8 (built into Windows).

    git clone https://github.com/KaBanei/peripheral-battery.git
    cd peripheral-battery
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Probe
    .\dist\app\PeripheralBattery.exe

The build uses the system csc.exe; no .NET SDK, NuGet, or third-party libraries are required.
