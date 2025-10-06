using DSRemapper.Core;
using DSRemapper;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace DSRemapper.Framework
{
    /// <summary>
    /// Main class of DSRemapper program. Controls when a supported device is plugged and unplugged, and all remappers in charge of remap the controllers
    /// </summary>
    public static class RemapperCore
    {
        private static readonly DSRLogger logger = DSRLogger.GetLogger("DSRempper.RemapperCore");
        /// <summary>
        /// Remappers list of the devices and their remapper threads. References all the plugged and recogniced devices.
        /// </summary>
        public static List<Remapper> Remappers { get; private set; } = [];
        /// <summary>
        /// Thread that scans for devices.
        /// </summary>
        private static Thread? deviceScannerThread;
        private static CancellationTokenSource tokenSource = new();
        private static CancellationToken cancellationToken;
        /// <summary>
        /// Delegate for RemapperCore device updates
        /// </summary>
        public delegate void RemapperUpdateArgs();
        /// <summary>
        /// Occurs when a new devices are detected by DSRemapper
        /// </summary>
        public static event RemapperUpdateArgs? OnUpdate;
        /// <summary>
        /// Starts the device scanner thread of DSRemapper
        /// </summary>
        public static void StartScanner()
        {
            StopScanner();
            tokenSource = new();
            cancellationToken = tokenSource.Token;
            deviceScannerThread = new(DeviceScanner)
            {
                Name = $"DSRemapper Device Scanner",
                Priority = ThreadPriority.BelowNormal
            };
            deviceScannerThread.Start();
        }
        /// <summary>
        /// Stops the device scanner thread of DSRemapper
        /// </summary>
        public static void StopScanner()
        {
            tokenSource.Cancel();
            deviceScannerThread?.Join();
        }
        /// <summary>
        /// Global devices information retrieval function
        /// </summary>
        /// <returns>An array with all devices plugged to the computer and recogniced by DSRemapper</returns>
        public static IDSRInputDeviceInfo[] GetDevicesInfo() => PluginLoader.Scanners
            .SelectMany((s) => s.Value.ScanDevices()).ToArray();
        private static void DeviceScanner()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var devs = GetDevicesInfo();
                if(devs.Length != Remappers.Count)
                {
                    SetControllers(devs.Select((i) => i.CreateController()).ToList());
                    OnUpdate?.Invoke();
                }
                Thread.Sleep(1000);
            }
        }
        /// <summary>
        /// Stops all remapper threads and device scanner thread
        /// </summary>
        public static void Stop()
        {
            StopScanner();
            DisposeAllRemappers();
            //LoggerFactory.RemoveLogger("DSRMainLogger");
        }
        /// <summary>
        /// Dispose all remappers
        /// </summary>
        public static void DisposeAllRemappers()
        {
            foreach (Remapper r in Remappers)
                r.Dispose();
        }
        /// <summary>
        /// Set RemapperCore devices, adding the new ones and deleting the unplugged ones
        /// </summary>
        /// <param name="controllers">List of current controllers</param>
        public static void SetControllers(List<IDSRInputController> controllers)
        {
            List<Remapper> removeList = [];
            foreach (var rmp in Remappers)
            {
                if (!controllers.Exists((c) => { return c.Id == rmp.Id; }))
                    removeList.Add(rmp);
            }

            foreach (var ctrl in removeList)
                RemoveController(ctrl.Id);

            foreach (var ctrl in controllers)
                AddController(ctrl);
        }
        /// <summary>
        /// Adds a new controller to RemapperCore class
        /// </summary>
        /// <param name="controller">New input controller</param>
        private static void AddController(IDSRInputController controller)
        {
            if (!Remappers.Exists((c) => { return c.Id == controller.Id; }))
            {
                logger.LogInformation($"Physical device plugged: {controller.Name} [{controller.Id}]");
                Remappers.Add(new(controller));
            }
        }
        /// <summary>
        /// Adds a new controller to RemapperCore class
        /// </summary>
        /// <param name="controllerId">The id of the controller to disconnect</param>
        private static void RemoveController(string controllerId)
        {
            Remapper? ctrlRemapper = Remappers.Find((c) => { return c.Id == controllerId; });

            if (ctrlRemapper != null)
            {
                logger.LogInformation($"Physical device unpluged: {ctrlRemapper.Name} [{ctrlRemapper.Id}]");
                Remappers.Remove(ctrlRemapper);
                ctrlRemapper.Dispose();
            }
        }
        /// <summary>
        /// Creates a new remapper object using the remap profile file extension to get the right one.
        /// </summary>
        /// <param name="fileExt"></param>
        /// <returns></returns>
        internal static IDSRemapper? CreateRemapper(string fileExt)
        {
            if (PluginLoader.RemapperPlugins.TryGetValue(fileExt,out ConstructorInfo? remapType))
                return (IDSRemapper?)remapType?.Invoke(null);

            return null;
        }
        /// <summary>
        /// Reload/update all remappers current profiles
        /// </summary>
        public static void ReloadAllProfiles()
        {
            foreach (var rmp in Remappers)
                rmp.ReloadProfile();
        }
    }
}