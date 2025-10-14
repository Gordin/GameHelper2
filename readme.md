# GameHelper2 - PoE2 External Overlay

[![GameHelper Discord](https://img.shields.io/discord/1414482159024210033?style=flat&logo=Discord&logoColor=%23fff&label=GameHelper2)]() [![GPLv3 License](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

GameHelper2 is an advanced external overlay and automation tool for Path of Exile 2, providing real-time game information, automated triggers, and quality-of-life features to enhance your gameplay experience. It was originally created by [GameHelper](https://www.ownedcore.com/forums/members/1040190-gamehelper.html) and is now an actively maintained community project released under the GPLv3 licence.

![Screenshot](https://i.ibb.co/c6yhhV3/asdasdsd.png)

## 🚀 Installation

#### ⚠️ Limited User Account
It is **important** to setup a **limited user account** using **[this](https://www.ownedcore.com/forums/path-of-exile-2/path-of-exile-2-bots-program/1061318-run-poe-limited-user.html)** guide, otherwise your PoE account **may get banned**. In order to validate that the method is working successfully, task manager should show your newly created user account against the PoE game process. For steam users, run the whole steam application on your limited user account.

### Prerequisites
- **.NET 8.0 Runtime**: [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Windows 10/11**: 64-bit operating system

### Quick Start
1. Download the latest release from the [Releases](https://github.com/derekShaheen/GameHelper2/releases) page
2. Extract the files to your preferred location
3. Run `Launcher.exe`
4. Launch Path of Exile 2
5. The overlay will automatically attach to the game process

## 🛠️ Plugin Development

GameHelper supports custom plugins for extended functionality:

### Creating a Plugin
1. Use the `SamplePluginTemplate` or any other plugin as a starting point
2. Inherit from the `PCore` abstract class
3. Build your plugin and place the DLL in the `Plugins` folder
4. Restart GameHelper to load the plugin

### Plugin Structure
```csharp
public class MyPlugin : PCore<MyPluginSettings>
{
    public override void DrawUI() { /* Your UI code */ }
    public override void DrawSettings() { /* Settings UI */ }
    public override void OnEnable(bool isGameOpened) { /* Initialization */ }
    public override void OnDisable() { /* Cleanup Resources */ }
    public override void SaveSettings() { /* Save MyPluginSettings */ }
}
```

## 🤝 Contributing

We welcome contributions! Please follow these guidelines:

### How to Contribute
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Standards
- Follow existing code style and patterns
- Test changes thoroughly
- Consider performance implications

## 📜 License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## 🔗 Links

- Join the [Discord]()
- Original GameHelper thread on [OwnedCore](https://www.ownedcore.com/forums/path-of-exile-2/path-of-exile-2-bots-program/1062208-gamehelper-poe-2-a.html)

---

**Disclaimer**: This tool is not affiliated with or endorsed by © Grinding Gear Games. It is intended as a reasearch and learning project.
