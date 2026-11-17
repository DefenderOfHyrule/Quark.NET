# Quark for Android

A native Android USB and network host for Leaflet. This lets your android device serve your game dumps locally,
allowing you to browse/install content from Leaflet the same way you would from the Quark.NET desktop app. 

~~This project totally was not based off of OmniRCM's android app~~

## Requirements

- Any device running Android 5.0 (API 21) or newer (x86, x64, armv7, armv8)
- For USB: a device with USB host ("OTG") support, + a USB-C/USB-A OTG
  adapter and cable if your phone doesn't have a USB-C port that supports
  host mode directly
- For network: your phone and Switch on the same WiFi network

## How it differs from the desktop app

Quark.NET on desktop reads and writes files directly on your filesystem.
Android doesn't allow that for arbitrary folders on modern versions, so this
app uses the Storage Access Framework (SAF) instead; you pick folders
through the system folder picker, the app is granted persistent access to
just those folders, and everything Leaflet asks for is served out of them.

There's no "drives" list, just the content paths you add.

## How to use

Usage is simple:

1. Install the `Quark-android.apk` from the [releases](https://github.com/DefenderOfHyrule/Quark.NET/releases/latest) page,
1. Open the app from your home menu,
1. Open Quark and tap **+ Add folder** under **Content Paths**, then pick a
   folder and give it a name (this is what Leaflet will show as a browsable
   location).
1. toggle the **Server** switch.
1. Either:
   - Plug your Switch (running Leaflet) into your device via USB; Quark
     will prompt for USB permission and pick it up automatically, or
   - Launch Leaflet and connect over **Wireless (LAN)**, using your devuce's
     IP address as the host.
1. Use **Install from Quark** on the Switch as usual.

The **Connections** tab shows currently attached USB devices and connected
network clients.

Enable **Start server automatically** to have Quark start the background
server when the app is opened and after the device reboots.

## Permissions

- USB host: required for USB connections; not required for network-only use
- Notifications: used for the persistent "server running" notification
  required by Android while the foreground service is active
- Install unknown apps: only needed if you use in-app updates

No storage permission is requested; folder access is entirely obtained through
SAF folder pickers.

## Building from source

### Requirements

- A computer running Linux (Windows instructions may be added in the future)

### Instructions

1. Download the latest release of the "Command line tools only" package for Linux from https://developer.android.com/studio#command-tools (version number changes, so I can't provide a static link)
1. Clone the repository in a terminal window with `git clone --recurse-submodules https://github.com/DefenderOfHyrule/Quark.NET.git`,
1. `cd` into the cloned repository,
1. Run the following commands in a separate terminal window from the directory you've downloaded the command line tools package with (replace `*` with the version number of the package you've downloaded):
    ```
    mkdir -p ~/android-sdk/cmdline-tools
    unzip commandlinetools-linux-*.zip -d ~/android-sdk/cmdline-tools
    mv ~/android-sdk/cmdline-tools/cmdline-tools ~/android-sdk/cmdline-tools/latest
    ```
1. Point gradle at the SDK installation (same terminal window as the one you cloned the repository with):
    ```
    echo "sdk.dir=$HOME/android-sdk" > android/local.properties
    ```
1. Install the project dependencies:
    ```
    export PATH="$HOME/android-sdk/cmdline-tools/latest/bin:$PATH"
    yes | sdkmanager --licenses
    sdkmanager "platform-tools" "platforms;android-36" "build-tools;35.0.0" "ndk;28.2.13676358" "cmake;3.22.1"
    ```
1. `cd` into the `android` directory,
1. Run the following command from the `android` directory to start building the app:
    ```
    ./gradlew assembleDebug
    ```

The `app-debug.apk` release will be located in `app/build/outputs/apk/debug`

## Protocol notes

This app implements the same GLCI/GLCO 4 KB block command protocol as
Quark.NET:

- USB: VID `0x057E` / PID `0x3000`, bulk endpoints `0x01` (out) / `0x81` (in)
- TCP: port `2313`, identical command framing

All 17 original command IDs are supported, + the console ID announcement
command used by Leaflet (18 total), covering drive/path
listing, stat, file read/write, create/delete/rename, special paths, and the
remote file picker.
