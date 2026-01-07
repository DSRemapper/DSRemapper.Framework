
using DSRemapper.Core;
using DSRemapper.Types;
using DSRemapper.Framework;
using System.Reflection;

namespace DSRemapper.Framework
{
    /// <summary>
    /// DSRemapper class that manage emulated controllers for remapper plugins
    /// </summary>
    public class DSROutput : IDSROutput
    {
        private List<IDSROutputController> emulatedControllers = [];
        private static readonly Dictionary<string, SharedController> sharedControllers = [];
        /// <inheritdoc/>
        public IDSROutputController CreateController(string path)
        {
            if (PluginLoader.OutputPlugins.TryGetValue(path, out ConstructorInfo? plugin))
            {
                IDSROutputController controller = (IDSROutputController)plugin.Invoke(null);
                emulatedControllers.Add(controller);
                return controller;
            }

            throw new Exception($"{path}: Emulated controller not found");
        }
        /// <inheritdoc/>
        public IDSROutputController GetController(string id, string path)
        {
            string fullid = $"{path};{id}";
            if (!sharedControllers.TryGetValue(fullid, out SharedController? ctrl))
            {
                ctrl = new SharedController(CreateController(path),this);
                sharedControllers.Add(fullid, ctrl);
            }

            ctrl.Connect(this);
            return ctrl.Controller;

        }
        /// <inheritdoc/>
        public void DisconnectAll()
        {
            foreach (var controller in emulatedControllers)
            {
                controller.Disconnect();
                controller.Dispose();
            }
            DisconnectAllBinded();
            emulatedControllers = [];
        }
        /// <inheritdoc/>
        public void DisconnectAllBinded()
        {
            foreach (string key in sharedControllers.Keys)
            {
                if(sharedControllers.TryGetValue(key,out SharedController? ctrl))
                {
                    ctrl.Disconnect(this);
                    if (ctrl.Count<=0) {
                        sharedControllers.Remove(key);
                        ctrl.Dispose();
                    }
                }
            }
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            DisconnectAll();
            GC.SuppressFinalize(this);
        }
    }
    /// <summary>
    /// Container class for shared emulated controllers
    /// </summary>
    public class SharedController : IDisposable
    {
        /// <summary>
        /// The controller to share between remap profiles
        /// </summary>
        public IDSROutputController Controller { get; private set; }
        /// <summary>
        /// All object references binded to this controller
        /// </summary>
        private readonly List<object> references = [];
        /// <summary>
        /// Count of binded objects
        /// </summary>
        public int Count => references.Count;
        /// <summary>
        /// Shared Controller class constructor
        /// </summary>
        /// <param name="controller">The emulated controller for the shared container</param>
        /// <param name="firstReference">The first object which is binded to the shared controller</param>
        public SharedController(IDSROutputController controller,object firstReference)
        {
            Connect(firstReference);
            Controller = controller;
            controller.Connect();
        }
        /// <summary>
        /// Connects/binds new object reference to the controller
        /// </summary>
        /// <param name="reference">Object to bind to this shared controller</param>
        public void Connect(object reference)
        {
            if (!references.Contains(reference))
                references.Add(reference);
        }
        /// <summary>
        /// Disconnects/unbinds object reference to the controller
        /// </summary>
        /// <param name="reference">Object to bind to this shared controller</param>
        public void Disconnect(object reference)
        {
            references.Remove(reference);
        }
        /// <summary>
        /// Checks if the object is referenced to the shared controller
        /// </summary>
        /// <param name="reference">Object supposedly binded to this shared controller</param>
        /// <returns>True if the object is currently binded to the controller, otherwise false</returns>
        public bool IsReferenced(object reference) => references.Contains(reference);
        /// <inheritdoc/>
        public void Dispose()
        {
            Controller.Disconnect();
            Controller.Dispose();
            GC.SuppressFinalize(this);
        }

    }
}