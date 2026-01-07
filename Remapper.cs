using DSRemapper.Core;
using DSRemapper;
using DSRemapper.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Linq.Expressions;

namespace DSRemapper.Framework
{
    /// <summary>
    /// Remapper container class. Contains the thread and the remapper plugin in charge of remapping the controller.
    /// </summary>
    public class Remapper : IEquatable<Remapper>, IEquatable<IDSRInputController>, IDisposable
    {
        private readonly object _remLock = new();
        private readonly DSRLogger logger;
        private readonly IDSRInputController controller;
        /// <summary>
        /// Remapper plugin interface
        /// </summary>
        private IDSRemapper? remapper = null;
        private Thread? thread = null;
        private CancellationTokenSource cancellationTokenSource;
        private CancellationToken cancellationToken;
        /// <summary>
        /// Delegate for the ControllerRead event
        /// </summary>
        /// <param name="report">The device input report</param>
        public delegate void ControllerRead(IDSRInputReport report);
        /// <summary>
        /// Delegate for the ControllerRead event
        /// </summary>
        /// <param name="deviceId">The device id that is sending the report</param>
        /// <param name="report">The device input report</param>
        public delegate void GlobalControllerRead(string deviceId, IDSRInputReport report);
        /// <summary>
        /// Occurs when any DSRemapper standard input report is readed from a controller.
        /// It's throtled at 20Hz (each <see cref="Remapper"/> is independed) since it's use is intended of UI only. 
        /// </summary>
        public static event GlobalControllerRead? OnGlobalRead;
        
        /// <summary>
        /// Delegate for Global Device Console events
        /// </summary>
        public delegate void GlobalDeviceConsoleEventArgs(string deviceId, string message, LogLevel level);
        /// <summary>
        /// Occurs when a remapper plugin sends a Device Console event to DSRemapper.
        /// It's throtled at 10Hz (each <see cref="Remapper"/> is independed) since it's use is intended of UI only. 
        /// </summary>
        public static event GlobalDeviceConsoleEventArgs? OnGlobalDeviceConsole;
        /// <summary>
        /// Delegate for Global Device Console events
        /// </summary>
        public delegate void GlobalDeviceInfoEventArgs(string deviceId, string deviceInfo);
        /// <summary>
        /// Occurs every second that for any <see cref="Remapper"/> is running.
        /// It's throtled at 1Hz (each <see cref="Remapper"/> is independed) since it's use is intended of UI only. 
        /// </summary>
        public static event GlobalDeviceInfoEventArgs? OnDeviceInfo;
        /// <summary>
        /// Dictionary of custom actions created on the controller plugin. This can be used on the interface.
        /// </summary>
        /// <returns>Dictionary of actions</returns>
        public Dictionary<string, Action> CustomActions { get; private set; }
        /// <summary>
        /// Dictionary of custom methods created on the controller plugin. This can be accesed by the remapper plugins.
        /// </summary>
        /// <returns>Dictionary of method delegates</returns>
        public Dictionary<string, Delegate> CustomMethods { get; private set; }
        /// <summary>
        /// Remapper class constructor
        /// </summary>
        /// <param name="controller">The controller asigned to the remapper</param>
        public Remapper(IDSRInputController controller)
        {
            this.controller = controller;
            cancellationTokenSource = new CancellationTokenSource();
            cancellationToken = cancellationTokenSource.Token;
            logger = DSRLogger.GetLogger($"DSRempper.Remapper({controller.Name},{controller.Id})");
            RemapperConfig config = DSRConfigs.GetConfig(controller.Id);
            SetProfile(config.LastProfile);
            if (config.AutoConnect)
                Start();

            var customMethods = controller.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            CustomMethods = new(customMethods
                .Where(m => m.CustomAttributes.Any(a => a.AttributeType == typeof(CustomMethodAttribute)))
                .Select(m =>
                {
                    CustomMethodAttribute? attr = m.GetCustomAttribute<CustomMethodAttribute>();
                    List<Type> parameterTypes = [.. m.GetParameters().Select(p => p.ParameterType)];
                    parameterTypes.Add(m.ReturnType);
                    Type delegateType = Expression.GetDelegateType([.. parameterTypes]);
                    return new KeyValuePair<string, Delegate>(attr?.InternalName ?? m.Name, m.CreateDelegate(delegateType, Controller));
                }));
            CustomActions = new(customMethods
                .Where(m => 
                    m.GetCustomAttribute<CustomMethodAttribute>() is { ScriptOnly: false } &&
                    m.ReturnType == typeof(void) && 
                    m.GetParameters().Length == 0)
                .Select(m =>
                {
                    CustomMethodAttribute? attr = m.GetCustomAttribute<CustomMethodAttribute>();
                    return new KeyValuePair<string, Action>(attr?.InternalName ?? m.Name, m.CreateDelegate<Action>(Controller));
                }));
        }
        /// <inheritdoc cref="IDSRInputController.Id"/>
        public string Id => Controller.Id;
        /// <inheritdoc cref="IDSRInputController.Name"/>
        public string Name => Controller.Name;
        /// <inheritdoc cref="IDSRInputController.Type"/>
        public string Type => Controller.Type;
        /// <inheritdoc cref="IDSRInputController.Info"/>
        public string Info => Controller.Info;
        /// <inheritdoc cref="IDSRInputController.IsConnected"/>
        public bool IsConnected => Controller.IsConnected;
        /// <summary>
        /// Gets if the remapper is runing it's thread.
        /// </summary>
        public bool IsRunning => thread?.IsAlive ?? false;
        /// <summary>
        /// Gets the current assigned remap profile of the remapper.
        /// </summary>
        public string CurrentProfile { get; private set; } = "";
        /// <summary>
        /// Gets the physical controller associated with this remapper
        /// </summary>
        public IDSRInputController Controller { get => controller; }
        
        private readonly Stopwatch sw = new();
        private double onReadTimer = onReadLoopTime;
        private const double onReadLoopTime = 1.0/20.0; // 20Hz
        private double infoTimer = infoLoopTime;
        private const double infoLoopTime = 1.0; // 1Hz
        private double consoleTimer = consoleLoopTime;
        private const double consoleLoopTime = 1.0/10.0; // 10Hz
        private (string message, LogLevel level) lastConsole = ("", LogLevel.None);

        /// <inheritdoc cref="IDSRInputController.Connect"/>
        public bool Connect()
        {
            if (!IsConnected)
                controller.Connect();

            return IsConnected;
        }
        /// <inheritdoc cref="IDSRInputController.Disconnect"/>
        public bool Disconnect()
        {
            if (IsConnected)
                controller.Disconnect();

            return IsConnected;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            Stop();
            remapper?.Dispose();
            controller.Dispose();
        }
        /// <summary>
        /// Starts the remapper thread for remapping the controller
        /// </summary>
        public void Start()
        {
            Stop();
            if (Connect())
            {
                cancellationTokenSource = new CancellationTokenSource();
                cancellationToken = cancellationTokenSource.Token;
                thread = new(RemapThread)
                {
                    Name = $"{controller.Name} Remapper",
                    Priority = ThreadPriority.AboveNormal
                };
                ReloadProfile();
                thread.Start();
            }
        }
        /// <summary>
        /// Stops the remapper thread for remapping the controller
        /// </summary>
        public void Stop()
        {
            cancellationTokenSource.Cancel();
            if (thread != null && thread.IsAlive)
            {
                thread.Join();
                Disconnect();
                DisposeRemapper();
                sw.Reset();
                sw.Stop();
            }
        }
        private void DisposeRemapper()
        {
            if (remapper != null)
                remapper.OnDeviceConsole -= OnDeviceConsole;
            remapper?.Dispose();
            remapper = null;
        }
        /// <summary>
        /// Sets the profile to the remapper plugin object used for the remap.
        /// </summary>
        /// <param name="profile">File path to the remap profile</param>
        public void SetProfile(string profile)
        {
            lock (_remLock){
                DisposeRemapper();

                if (ProfileManager.TryGetProfile(profile, out FileInfo? profileFile) && profileFile != null)
                {
                    string ext = profileFile.Extension[1..];
                    remapper = RemapperCore.CreateRemapper(ext, logger);
                    if (remapper != null)
                    {
                        remapper.OnDeviceConsole += OnDeviceConsole;
                        remapper.SetScript(profileFile, CustomMethods);
                    }
                }
                CurrentProfile = profile;
                DSRConfigs.SetLastProfile(Id, profile);
            }
        }
        private void OnDeviceConsole(object sender, string message, LogLevel level)
        {
            lastConsole = (message, level);
        }
        /// <summary>
        /// Reloads the current profile on the remapper plugin object.
        /// If the remap profile file is changed, this function has to be called for the changes to take effect.
        /// </summary>
        public void ReloadProfile() => SetProfile(CurrentProfile);
        private void RemapThread()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_remLock){
                    double delta = sw.Elapsed.TotalSeconds;
                    sw.Restart();
                    try
                    {
                        if (IsConnected)
                        {
                            IDSRInputReport report = controller.GetInputReport();
                            if (remapper != null)
                                controller.SendOutputReport(remapper.Remap(report, delta));

                            onReadTimer += delta;
                            infoTimer += delta;
                            consoleTimer += delta;
                            if (onReadTimer >= onReadLoopTime)
                            {
                                onReadTimer -= onReadLoopTime;
                                OnGlobalRead?.Invoke(Id, report);
                            }
                            if (infoTimer >= infoLoopTime)
                            {
                                infoTimer -= infoLoopTime;
                                OnDeviceInfo?.Invoke(Id, controller.Info);
                            }
                            if (consoleTimer >= consoleLoopTime)
                            {
                                consoleTimer -= consoleLoopTime;
                                OnGlobalDeviceConsole?.Invoke(Id, lastConsole.message, lastConsole.level);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"{e.Source}[{e.TargetSite}]({e.GetType().FullName}):\n{e.Message}\n{e.StackTrace}");
                    }
                }

                //Thread.Sleep(1); // to prevent CPU overload and leave space for other threads if it's necesary
            }
        }
        /// <summary>
        /// Returns the byte array of the image for the controller
        /// </summary>
        /// <returns>The image as a byte array</returns>
        public byte[] GetImage() => PluginLoader.GetControllerImage(controller);
        
        /// <inheritdoc/>
        public bool Equals(Remapper? other) => Id.Equals(other?.Id);
        /// <inheritdoc/>
        public bool Equals(IDSRInputController? other) => Id.Equals(other?.Id);
        /// <inheritdoc/>
        public override bool Equals(object? other)
        {
            if (other is Remapper remapper)
                return Equals(remapper);
            if (other is IDSRInputController controller)
                return Equals(controller);
            return false;
        }
        /// <inheritdoc/>
        public static bool operator ==(Remapper? left, Remapper? right) => left?.Equals(right) ?? false;
        /// <inheritdoc/>
        public static bool operator !=(Remapper? left, Remapper? right) => (!left?.Equals(right)) ?? true;
        /// <inheritdoc/>
        public override int GetHashCode() => Id.GetHashCode();
    }
}
