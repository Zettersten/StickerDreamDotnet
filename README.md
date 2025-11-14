# Sticker Dream

![](./dream.png)

A voice-activated sticker printer built with .NET 10 and Blazor. Press and hold the button, describe what you want, and it generates a black and white coloring page sticker that prints to a thermal printer.

## How it works

1. Hold the button and speak (max 15 seconds)
2. Whisper transcribes your voice (client-side using HuggingFace Transformers.js)
3. Google Imagen generates a coloring page based on your description
4. Image displays in browser and prints to USB/Bluetooth thermal printer

## Architecture

This is a .NET 10 Blazor Server application using:
- **.NET Aspire 13** for orchestration, service discovery, and dev tunnels
- **Blazor Server** for the web UI with interactive components
- **Google Gemini Imagen API** for image generation
- **CUPS** for printer management (USB/Bluetooth support)
- **HuggingFace Transformers.js** (client-side) for Whisper speech-to-text
- **Dev Tunnels** for public internet access

## Setup

### Prerequisites

- .NET 10 SDK (latest version)
- CUPS (Common Unix Printing System) - usually pre-installed on Linux/macOS
- A USB or Bluetooth thermal printer
- Google Gemini API key
- Azure Dev Tunnels (for public internet access via Aspire)

### Installation

1. Clone the repository

2. Set up your Gemini API key:

```bash
export GEMINI_API_KEY=your_api_key_here
```

Or add it to your `appsettings.json` or user secrets:

```bash
dotnet user-secrets set "GEMINI_API_KEY" "your_api_key_here" --project StickerDream.Server
```

3. Connect your USB or Bluetooth thermal printer. The app will automatically detect and use USB/Bluetooth printers.

## Running

### Using Aspire (Recommended)

Run the Aspire AppHost which orchestrates all services:

```bash
dotnet run --project StickerDream.AppHost
```

This will:
- Start the Blazor Server application
- Open the Aspire dashboard
- Provide service discovery and health checks
- **Automatically create a dev tunnel** for public internet access (via Azure Dev Tunnels)

The app will be available at:
- Local URL: shown in the console (typically `https://localhost:5001`)
- **Public URL**: Available via dev tunnel - check the Aspire dashboard for the public URL

### Running Server Directly

You can also run the server directly:

```bash
cd StickerDream.Server
dotnet run
```

Open `https://localhost:5001` (or the port shown in the console).

## Printers

**TLDR:** [The Phomemo](https://amzn.to/4hOmqki) PM2 will work great over bluetooth or USB.

While any printer will work, we recommend a 4x6 thermal printer with 4x6 shipping labels. These printers are fast, cheap and don't require ink.

The app supports:
- **USB printers** - Automatically detected via CUPS
- **Bluetooth printers** - Automatically detected via CUPS

The app automatically watches for paused/disabled printers and resumes them.

## Tips

The image prints right away, which is magical. Sometimes you can goof up. In this case, simply say "CANCEL", "ABORT" or "START OVER" as part of your recording.

## Development

### Project Structure

- `StickerDream.AppHost` - Aspire orchestration project
- `StickerDream.Server` - Blazor Server application
- `StickerDream.ServiceDefaults` - Shared Aspire configuration

### Key Services

- `ImageGenerationService` - Handles Google Gemini Imagen API calls
- `PrinterService` - Manages CUPS printer operations (USB/Bluetooth)

### Building

```bash
dotnet build
```

### Testing

The app includes health checks available at `/health` and `/alive` endpoints (in development mode).

## Technologies

- .NET 10 (latest)
- Blazor Server
- .NET Aspire 13 (latest)
- Google Gemini Imagen API
- CUPS (for printer management)
- HuggingFace Transformers.js (client-side Whisper)
- Azure Dev Tunnels (for public internet access)

## Features

- **Latest .NET 10 Language Features**: Uses primary constructors, collection expressions, and other modern C# features
- **Dev Tunnels**: Automatically exposes the web frontend via Azure Dev Tunnels for public internet access
- **Aspire 13**: Latest Aspire version with improved orchestration and service discovery

## License

MIT
