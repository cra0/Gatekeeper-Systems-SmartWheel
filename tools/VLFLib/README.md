# VLFLib

**VLFLib** is a small helper library for decoding **Very-Low-Frequency (VLF)** bursts that Gatekeeper Systems “SmartWheel” devices embed inside 8-bit mono WAV files.  
It can:

* Parse a WAV file or an in-memory byte sequence into binary data
* Detect start/end markers and bit blips (1 / 0) in the waveform
* Expose decoding progress through events
* Dump raw samples or decoded bytes to disk
* Render a waveform PNG that highlights markers and decoded bits
* Re-emit the data as a clean WAV file

> **Platform note** – The library uses `System.Drawing` and is therefore **Windows-only** (see the `[SupportedOSPlatform("windows")]` attribute).

---

## Installation

```bash
dotnet add package VLFLib
