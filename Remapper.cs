using DSRemapper.Core;
using DSRemapper;
using DSRemapper.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSRemapper.Framework
{
    /// <summary>
    /// Remapper container class. Contains the thread and the remapper plugin in charge of remapping the controller.
    /// </summary>
    public class Remapper : IDisposable
    {
        private readonly DSRLogger logger;

        private readonly IDSRInputController controller;
        private IDSRemapper? remapper = null;
        private Thread? thread = null;
        private CancellationTokenSource cancellationTokenSource;
        private CancellationToken cancellationToken;
        /// <summary>
        /// Delegate for the ControllerRead event
        /// </summary>
        /// <param name="report"></param>
        public delegate void ControllerRead(IDSRInputReport report);
        /// <summary>
        /// Occurs when a DSRemapper standard input report from the controller is readed
        /// </summary>
        public event ControllerRead? OnRead;
        /// <summary>
        /// Gets all the subscriptions to the 'OnRead' event. For debugging purposes.
        /// </summary>
        public int OnReadSubscriptors => OnRead?.GetInvocationList().Length ?? 0;
        /// <summary>
        /// Occurs when the remapper plugin invokes an OnLog event.
        /// </summary>
        public event RemapperEventArgs? OnLog;
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
            GC.SuppressFinalize(this);
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
                    Priority = ThreadPriority.Normal
                };
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
            }
        }
        /// <summary>
        /// Sets the profile to the remapper plugin object used for the remap.
        /// </summary>
        /// <param name="profile">File path to the remap profile</param>
        public void SetProfile(string profile)
        {
            if (remapper != null)
                remapper.OnLog -= OnLog;
            remapper?.Dispose();
            remapper = null;

            if(profile != "")
            {
                string fullPath = Path.Combine(DSRPaths.ProfilesPath, profile);
                if (File.Exists(fullPath))
                {
                    string ext = Path.GetExtension(fullPath)[1..];
                    remapper = RemapperCore.CreateRemapper(ext);
                    if (remapper != null)
                    {
                        remapper.OnLog += OnLog;
                        remapper.SetScript(fullPath);
                    }
                }
            }
            CurrentProfile = profile;
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
                try
                {
                    if (IsConnected)
                    {
                        IDSRInputReport report = controller.GetInputReport();
                        OnRead?.Invoke(report);
                        if (remapper != null)
                        {
                            controller.SendOutputReport(remapper.Remap(report));
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError($"{e.Source}:\n{e.Message}\n{e.StackTrace}");
                }

                Thread.Sleep(1);
            }
        }
    }
}
