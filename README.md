# Stellar Sync Client

A Dalamud plugin that serves as a Mare Synchronos replacement for Final Fantasy XIV, allowing players to synchronize their character appearance with others in real-time.

## Features

- **Character Synchronization**: Sync your character's appearance, gear, and glamours with other players
- **Real-time Updates**: See changes from other players instantly
- **Privacy Controls**: Choose who can see you and who you want to see
- **Customizable Settings**: Configure sync options for different aspects of your character
- **Simple UI**: Clean and intuitive interface built with ImGui

## Project Structure

The project follows a clean, modular architecture:

```
client/
├── StellarSync/                    # Main plugin project
│   ├── Core/                      # Core plugin logic
│   │   └── StellarSync.cs         # Main plugin entry point
│   ├── Configuration/             # Configuration management
│   │   └── Configuration.cs       # Plugin settings and persistence
│   ├── Services/                  # Business logic services
│   │   ├── NetworkService.cs      # WebSocket communication
│   │   └── CharacterSyncService.cs # Character data synchronization
│   ├── UI/                        # User interface components
│   │   └── PluginUI.cs            # Main plugin interface
│   ├── Models/                    # Data models
│   │   └── CharacterData.cs       # Character and equipment data structures
│   ├── Infrastructure/            # External dependencies and stubs
│   │   └── DalamudStubs.cs        # Dalamud interface stubs for development
│   └── StellarSync.csproj         # Project file
├── plugin.json                    # Dalamud plugin manifest
├── StellarSync.sln               # Visual Studio solution
├── build.sh                      # Linux/macOS build script
├── build.bat                     # Windows build script
├── README.md                     # This file
├── LICENSE                       # License information
└── .gitignore                    # Git ignore rules
```

## Installation

### Prerequisites
- Final Fantasy XIV
- Dalamud (latest version)
- .NET 6.0 or later

### Building from Source

1. Clone the repository:
```bash
git clone https://github.com/your-username/Stellar-Sync.git
cd Stellar-Sync/client
```

2. Set up the DALAMUD_PATH environment variable to point to your Dalamud installation:
```bash
export DALAMUD_PATH="/path/to/your/Dalamud"
```

3. Build the project:
```bash
# Using the build script (recommended)
./build.sh

# Or manually
cd StellarSync
dotnet build --configuration Release
```

4. The compiled plugin will be in `StellarSync/bin/Release/net6.0/`

### Installation in Dalamud

1. Copy the compiled files to your Dalamud plugins directory:
   - Windows: `%APPDATA%\XIVLauncher\addon\Hooks\devPlugins\StellarSync\`
   - Linux: `~/.config/XIVLauncher/addon/Hooks/devPlugins/StellarSync/`

2. Restart FFXIV and Dalamud

3. Enable the plugin in Dalamud's plugin installer

## Usage

1. In-game, use the command `/stellarsync` to open the plugin interface
2. Configure your server connection settings
3. Enter your username and password
4. Click "Connect" to join the sync session
5. Adjust sync and privacy settings as needed

## Configuration

### Connection Settings
- **Server URL**: The WebSocket server address (default: http://localhost:8080)
- **Username**: Your account username
- **Password**: Your account password
- **Auto-connect**: Automatically connect when the plugin loads

### Sync Settings
- **Character Sync**: Enable/disable character appearance synchronization
- **Gear Sync**: Enable/disable equipment synchronization
- **Glamour Sync**: Enable/disable glamour plate synchronization
- **Emote Sync**: Enable/disable emote synchronization

### Privacy Settings
- **Allow others to see me**: Control whether other players can see your character
- **Show others to me**: Control whether you can see other players

## Development

### Architecture

The project follows clean architecture principles with clear separation of concerns:

- **Core**: Contains the main plugin logic and entry point
- **Configuration**: Handles all settings and persistence
- **Services**: Contains business logic for networking and synchronization
- **UI**: User interface components and ImGui integration
- **Models**: Data structures and DTOs
- **Infrastructure**: External dependencies and development stubs

### Building for Development
```bash
cd StellarSync
dotnet build --configuration Debug
```

### Debugging
1. Build in Debug configuration
2. Use Dalamud's plugin installer to load the debug version
3. Check Dalamud logs for any errors

### Adding New Features

When adding new features, follow these guidelines:

1. **Services**: Add new business logic in the `Services/` folder
2. **Models**: Create new data structures in the `Models/` folder
3. **UI**: Add new interface components in the `UI/` folder
4. **Configuration**: Add new settings in `Configuration/Configuration.cs`

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes following the established architecture
4. Test thoroughly
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues and questions:
- Create an issue on GitHub
- Join our Discord server (link TBD)
- Check the documentation wiki

## Roadmap

- [ ] Server-side implementation
- [ ] Advanced privacy controls
- [ ] Character customization sync
- [ ] Emote and animation sync
- [ ] Performance optimizations
- [ ] Mobile companion app
