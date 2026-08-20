# Building YKW Home for iOS on Linux

This guide explains how to build and deploy YKW Home to an iPhone or iPad using [xtool](https://xtool.sh) on Linux.

## Prerequisites

### 1. Install Swift 6.3 Toolchain

```bash
# Download Swift 6.3 for your Linux distro
# https://swift.org/install/linux

# Verify installation
swift --version
# Swift version 6.3 (swift-6.3-RELEASE)
```

### 2. Install xtool

```bash
# Clone xtool
git clone https://github.com/xtool-org/xtool.git
cd xtool

# Build and install
swift build -c release
sudo cp .build/release/xtool /usr/local/bin/

# Verify
xtool --version
```

### 3. Install usbmuxd

xtool uses usbmuxd to communicate with iOS devices over USB.

```bash
# Ubuntu/Debian
sudo apt-get install usbmuxd libimobiledevice-utils

# Start usbmuxd
sudo systemctl start usbmuxd

# Verify device connection (connect iPhone via USB)
ideviceinfo
```

### 4. Set Up the Darwin Swift SDK

xtool needs the Darwin SDK (iOS, macOS, simulator) to cross-compile.

```bash
# xtool can download the SDK automatically
xtool sdk setup

# Or if you have the SDK artifact bundle already:
xtool sdk setup --sdk-path /path/to/darwin.artifactbundle
```

The SDK provides:
- `iPhoneOS.platform` — iOS device builds
- `iPhoneSimulator.platform` — Simulator builds
- `MacOSX.platform` — macOS builds

### 5. Authenticate with Apple Developer Services

```bash
# Sign in with your Apple ID
xtool auth login

# List your development teams
xtool auth teams

# Select your team (if you have multiple)
xtool auth team select <team-id>
```

> **Note:** You need a paid Apple Developer account ($99/year) to sign and install apps on physical devices. Free accounts can only deploy to the simulator.

## Project Structure

YKW Home uses a hybrid approach:
- **Core logic** (crypto, save parsing, cloud) is in C# (.NET)
- **iOS UI** will be a SwiftPM package using the same core library

For now, the .NET Avalonia UI is the primary desktop target. To create an iOS version, you'll need to either:

1. **Use Avalonia's iOS support** (AvaloniaUI supports iOS natively)
2. **Create a native Swift UI** that calls the .NET library via interop

## Option 1: Avalonia iOS (Recommended)

Avalonia UI has first-class iOS support. This allows the same C# codebase to run on iOS.

### Steps

1. **Install the iOS workload** for .NET:
   ```bash
   dotnet workload install ios
   ```

2. **Create an iOS project** in the solution:
   ```bash
   cd ykw-home/ykw-dotnet
   dotnet new ios -n YKWHome.iOS -o YKWHome.iOS
   dotnet sln add YKWHome.iOS/YKWHome.iOS.csproj
   cd YKWHome.iOS
   dotnet add reference ../YKWHome.App/YKWHome.App.csproj
   ```

3. **Configure the iOS project** for Avalonia:
   ```xml
   <!-- YKWHome.iOS.csproj -->
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0-ios</TargetFramework>
       <OutputType>Exe</OutputType>
       <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Avalonia" Version="12.1.0" />
       <PackageReference Include="Avalonia.iOS" Version="12.1.0" />
       <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.0" />
     </ItemGroup>
   </Project>
   ```

4. **Build for iOS device:**
   ```bash
   dotnet build -r ios-arm64 -c Release
   ```

5. **Deploy with xtool:**
   ```bash
   xtool dev --device <device-id>
   ```

## Option 2: Native Swift UI (Advanced)

If you want a fully native iOS experience, create a SwiftPM package that wraps the .NET logic.

### Steps

1. **Create a SwiftPM package:**
   ```bash
   xtool new YKWHomeiOS
   cd YKWHomeiOS
   ```

2. **Add the Darwin SDK dependency** in `Package.swift`:
   ```swift
   // package dependency for the Swift SDK
   .package(url: "https://github.com/apple/swift-sdk", branch: "main")
   ```

3. **Implement the UI** in Swift/SwiftUI

4. **Build and deploy:**
   ```bash
   xtool dev --device <device-id>
   ```

## Building with xtool

### Connect Your Device

```bash
# Connect iPhone via USB
# Trust the computer on your iPhone

# List connected devices
xtool devices
# Example output:
# iPhone 15 Pro (iOS 18.0) - UDID: abc123...
```

### Build and Run

```bash
# From the project directory
xtool dev

# Or specify the device
xtool dev --device <device-udid>

# Build for simulator
xtool dev --destination "platform=iOS Simulator,name=iPhone 16"
```

### Build Release IPA

```bash
# Build a release IPA for distribution
xtool build --configuration release --output ./build

# The IPA will be at ./build/YKWHome.ipa
```

### Install IPA

```bash
# Install to a connected device
xtool install ./build/YKWHome.ipa
```

## Troubleshooting

### "No team found"
Run `xtool auth teams` and `xtool auth team select <team-id>`.

### "Device not detected"
- Check USB connection
- Make sure usbmuxd is running: `sudo systemctl status usbmuxd`
- Try a different USB cable (some are charge-only)
- Trust the computer on your iPhone

### "Provisioning profile expired"
```bash
xtool ds profiles list
xtool ds profiles renew
```

### "SDK not found"
```bash
xtool sdk setup
# Or specify the path to your SDK bundle
xtool sdk setup --sdk-path /path/to/darwin.artifactbundle
```

### Build fails with Swift errors
Make sure you have Swift 6.3+ installed:
```bash
swift --version
```

## Architecture Notes

| Component | Language | Platform |
|-----------|----------|----------|
| Crypto (IeCCode, AES-CCM) | C# | All |
| Save parsing (YW1-4) | C# | All |
| Cloud (Appwrite) | C# | All |
| Desktop UI | Avalonia C# | Linux/macOS/Windows |
| iOS UI | Avalonia C# or Swift | iOS |

The C# core library (`YKWHome.Core`) is platform-agnostic and can be used from both Avalonia and native Swift via interop.

## References

- [xtool Documentation](https://xtool.sh/documentation)
- [Avalonia iOS Guide](https://docs.avaloniaui.net/docs/stay/responsive/ios)
- [Swift on Linux](https://www.swift.org/install/linux/)
- [usbmuxd](https://github.com/libimobiledevice/usbmuxd)
