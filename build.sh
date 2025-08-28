#!/bin/bash

# Stellar Sync Client Build Script

echo "Building Stellar Sync Client..."

# Check if DALAMUD_PATH is set
if [ -z "$DALAMUD_PATH" ]; then
    echo "Warning: DALAMUD_PATH environment variable is not set."
    echo "Please set it to your Dalamud installation path:"
    echo "export DALAMUD_PATH=\"/path/to/your/Dalamud\""
    echo ""
fi

# Change to the StellarSync project directory
cd StellarSync

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean

# Restore packages
echo "Restoring packages..."
dotnet restore

# Build in Release configuration
echo "Building in Release configuration..."
dotnet build --configuration Release --no-restore

if [ $? -eq 0 ]; then
    echo ""
    echo "Build successful!"
    echo "Output location: bin/Release/net6.0/"
    echo ""
    echo "To install in Dalamud:"
    echo "1. Copy the contents of bin/Release/net6.0/ to your Dalamud devPlugins folder"
    echo "2. Restart FFXIV and Dalamud"
    echo "3. Enable the plugin in Dalamud's plugin installer"
else
    echo ""
    echo "Build failed!"
    exit 1
fi
