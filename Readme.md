# DSRemapper.Framework - DSR/SDK

The `DSRemapper.Framework` is the main assembly for the DSRemapper application. It acts as the core engine, orchestrating the discovery of controllers, loading of plugins, management of configurations, and the actual remapping process.

**Note: This package is not required to make plugins, unless you need access to DSRemapper main components.**

## Key Components
Breakdown of the main parts of the assembly and their responsibilities:

1.  **`RemapperCore`**: This is the central class of the framework.
    -   It manages the entire lifecycle of device detection and remapping.
    -   It runs a background thread to continuously scan for new physical controllers.
    -   It maintains a list of active `Remapper` instances, one for each connected controller.

2.  **`PluginLoader`**: This static class is responsible for the application's extensibility.
    -   It discovers and loads all plugin assemblies (`.dll` files) from the `Plugins` directory.
    -   It categorizes plugins into Scanners (input), Remappers (logic), and Output (virtual controllers).

3.  **`Remapper`**: This class represents a single, active remapping session for one physical controller.
    -   It wraps a physical device and holds the specific remapper plugin instance.
    -   It manages the remapping thread, which reads, processes, and sends controller inputs.
    -   It handles loading and reloading of remapping profiles.

4.  **`DSRConfigs` & `RemapperConfig`**: These classes handle the persistence of settings for each physical controller.
    -   They allow DSRemapper to remember device-specific settings like the last used profile and auto-connect preferences.

5.  **`ProfileManager`**: A utility class that scans the `Profiles` directory to find all available remap profile files.
