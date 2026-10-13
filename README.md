# Enttec OpenDMX Controller

A lightweight, self-contained C# controller for the **Enttec Open DMX USB** interface. It provides native FTDI hardware communication, automated device diagnostics, and an embedded real-time web interface with WebSocket synchronization for manual color picking and test patterns.

---

## Overview

The Enttec Open DMX USB interface is based on the FTDI FT232 USB-to-serial UART converter wired to an RS-485 transceiver. Unlike the microprocessor-based Enttec DMX USB Pro, the Open DMX relies on host software to generate precise DMX512 frame timing, breaks, mark-after-break (MAB), and streaming frames.

This application connects directly to the FTDI device using native FTDI drivers, runs a continuous background DMX transmission engine at ~40 Hz, and hosts an embedded web controller reachable from any desktop, tablet, or smartphone browser on your local network.

---

## Features

- **Native FTDI D2XX Driver Integration**: Bypasses Windows virtual COM port (VCP) latency and jitter by directly interfacing with `FTD2XX.dll` to assert true DMX breaks (`FT_SetBreakOn`/`FT_SetBreakOff`) and configured baud rate timing (250 kBaud, 8 data bits, 2 stop bits, no parity).
- **USB & Device Diagnostics via WMI**: Dynamically enumerates all connected USB and serial devices using Windows Management Instrumentation (`System.Management`) and reports device statuses, serial numbers, and driver descriptors on startup.
- **Embedded Web Server & WebSockets**:
  - Zero external web server dependencies (built on native `.NET` `HttpListener` and `System.Net.WebSockets`).
  - Embedded responsive HTML/CSS single-page UI (self-contained within the executable).
  - Bi-directional real-time state synchronization across multiple simultaneous browser sessions.
- **5-Channel Gain + RGBW Fixture Support**:
  - Full support for 5-channel fixtures (`Gain / Dimmer`, `Red`, `Green`, `Blue`, `White`).
  - Interactive HTML color picker (RGB) with synchronized individual sliders for Red, Green, Blue, White, and Master Gain.
  - Quick-preset buttons (Red, Green, Blue, White, Amber) and one-touch Blackout.
- **On-Demand Auto Test Pattern**:
  - Safe launch: defaults all channels to `0` on startup (no unexpected flashing).
  - Interactive toggle button in the web UI to cycle an automated RGBW test pattern, pausing automatically when manual adjustments are made.

---

## Architecture

The project consists of three core components:

```
┌─────────────────────────────────────────────────────────────┐
│                       Web Browser UI                        │
│         (HTML5 / CSS3 / WebSockets - index.html)            │
└──────────────────────────────▲──────────────────────────────┘
                               │
                      WebSocket / HTTP (port 5000)
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                         WebServer                           │
│  - Serves embedded HTML single-page app                     │
│  - Asynchronous WebSocket handler & state broadcast         │
└──────────────────────────────▲──────────────────────────────┘
                               │
                         State updates
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                        Program.cs                           │
│  - App lifecycle management                                 │
│  - State management (Gain, RGBW, Auto Pattern Task)         │
│  - Maps 5-channel fixtures to DMX buffer                    │
└──────────────────────────────▲──────────────────────────────┘
                               │
                   SetDmxValue(channel, val)
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                      EnttecOpenDmx                          │
│  - WMI Device Enumeration (System.Management)               │
│  - FTDI D2XX P/Invoke (`FTD2XX.dll`)                        │
│  - Background transmission thread (~40 Hz continuous loop)  │
│  - Generates DMX Break, MAB, and 513-byte frames            │
└──────────────────────────────▲──────────────────────────────┘
                               │
                         USB / RS-485
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                Enttec Open DMX USB Hardware                 │
│              (FTDI FT232R + RS-485 transceiver)             │
└─────────────────────────────────────────────────────────────┘
```

### Component Breakdown

1. **`EnttecOpenDmx.cs`**:
   - Communicates with native `FTD2XX.dll`.
   - Enumerates connected USB PnP devices using WMI to diagnose hardware connectivity.
   - Configures the FTDI chip (Baud: 250,000, 8N2, RTS cleared, TX/RX purged).
   - Runs `DmxThread`: a background loop sleeping ~25 ms between frames (~40 frames/sec), transmitting break, mark-after-break, start code `0x00`, and the 512-channel packet buffer.

2. **`WebServer.cs`**:
   - Hosts an HTTP and WebSocket server on `http://localhost:5000/`.
   - Embeds `index.html` as a compiled resource (`EmbeddedResource`), ensuring zero runtime file dependencies.
   - Broadcasts real-time state changes to all connected clients whenever color, gain, or pattern toggles are updated.

3. **`Program.cs`**:
   - Orchestrates startup, shutdown, and channel mapping.
   - Maps 5-channel fixtures:
     - **Channel 1**: Master Gain / Brightness
     - **Channel 2**: Red
     - **Channel 3**: Green
     - **Channel 4**: Blue
     - **Channel 5**: White
   - Runs the background cancellation-token-driven automated test pattern when activated from the web interface.

---

## Prerequisites

- **OS**: Windows 10 / 11 (uses Windows Management Instrumentation and native FTDI drivers).
- **.NET SDK**: .NET 6.0 SDK or later.
- **FTDI D2XX Driver**: Installed with the FTDI device (typically provided automatically via Windows Update or the official FTDI CDM driver package).

---

## Getting Started

1. **Clone the repository**:
   ```bash
   git clone https://github.com/funkmeisterb/enttecOpenDMXController.git
   cd enttecOpenDMXController
   ```

2. **Build the application**:
   ```bash
   dotnet build
   ```

3. **Run the controller**:
   ```bash
   dotnet run
   ```

4. **Access the Web Controller**:
   Open your browser and navigate to:
   ```
   http://localhost:5000/
   ```
   You can also connect from another device (e.g., phone or tablet) on the same local network using the host computer's IP address.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

