using Grasshopper.Kernel;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Appendage.YOLOcam
{
    public class YOLOControlComponent : GH_Component
    {
        private Process _backendProcess;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(2000)
        };

        private bool _wasActive = false;

        public YOLOControlComponent()
          : base("YOLO Control", "YOLOctrl",
            "Start/Stop YOLO webcam backend",
            "Appendage", "Live YOLO")
        {
            Grasshopper.Instances.DocumentServer.DocumentRemoved += OnDocumentRemoved;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Active", "A", "True = start backend, False = stop", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Running", "R", "Backend is running", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Port", "P", "Backend port", GH_ParamAccess.item);
            pManager.AddTextParameter("Device", "D", "Compute device (cpu/cuda)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool active = false;
            DA.GetData(0, ref active);

            if (active && !_wasActive)
            {
                StartBackend();
                _wasActive = true;
            }
            else if (!active && _wasActive)
            {
                StopBackend();
                _wasActive = false;
            }

            bool running = _backendProcess != null && !_backendProcess.HasExited;
            DA.SetData(0, running);
            DA.SetData(1, 8000);

            string device = "unknown";
            try
            {
                string json = _http.GetStringAsync("http://127.0.0.1:8000/status").GetAwaiter().GetResult();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                device = doc.RootElement.GetProperty("device").GetString() ?? "unknown";
            }
            catch { }
            DA.SetData(2, device);
        }

        private void OnDocumentRemoved(GH_DocumentServer sender, GH_Document doc)
        {
            StopBackend();
            Grasshopper.Instances.DocumentServer.DocumentRemoved -= OnDocumentRemoved;
        }

        private void StartBackend()
        {
            KillExistingBackend();

            string ghaDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string backendDir = Path.Combine(ghaDir, "backend");
            string scriptPath = Path.Combine(backendDir, "backend.py");
            string pythonPath = Path.Combine(backendDir, "venv", "Scripts", "python.exe");

            if (!File.Exists(pythonPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Python not found: {pythonPath}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = backendDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _backendProcess = Process.Start(psi);

            Task.Run(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    try
                    {
                        var response = _http.GetAsync("http://127.0.0.1:8000/status").GetAwaiter().GetResult();
                        if (response.IsSuccessStatusCode)
                        {
                            _http.PostAsync("http://127.0.0.1:8000/start", null).Wait();
                            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => ExpireSolution(true)));
                            return;
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(1000);
                }
                Rhino.RhinoApp.WriteLine("YOLO backend failed to start within 30 seconds.");
            });
        }

        private void KillExistingBackend()
        {
            try
            {
                var processes = Process.GetProcessesByName("python");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(1000); } catch { }
                }
            }
            catch { }

            if (_backendProcess != null)
            {
                try { if (!_backendProcess.HasExited) _backendProcess.Kill(); } catch { }
                _backendProcess = null;
            }
        }

        private void StopBackend()
        {
            try { _http.PostAsync("http://127.0.0.1:8000/stop", null).Wait(1000); }
            catch { }

            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                _backendProcess.Kill();
                _backendProcess = null;
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopBackend();
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("90e76670-deae-4ec1-a1a4-40ca9067efa7");
    }
}
