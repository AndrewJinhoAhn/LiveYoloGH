using Grasshopper.Kernel;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace LiveYOLO
{
    public class YOLOControlComponent : GH_Component
    {
        private Process _backendProcess;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(2000)
        };

        private bool _wasActive = false;
        private bool _setupRunning = false;

        // Self-contained backend lives here (downloaded on first run, never bundled).
        private static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveYolo");

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
            //stop backend if active is false and backend is running, start backend if active is true and backend is not running
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

            string pythonPath = Path.Combine(BaseDir, "python", "python.exe");
            string ready = Path.Combine(BaseDir, ".ready"); //setup completion marker file

            // First run (or if setup is incomplete): bootstrap the backend, then launch.
            if (!File.Exists(pythonPath) || !File.Exists(ready)) 
            {
                RunSetupThenStart();
                return; //LaunchBackend() will be called inside RunSetupThenStart() after background setup is complete.
            }

            LaunchBackend();
        }

        // Runs setup script(setup.ps1) in a background thread so that the Grasshopper UI remains responsive.
        private void RunSetupThenStart()
        {
            if (_setupRunning) return;
            _setupRunning = true;

            string ghaDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location); //get the directory of the liveyolo.gha file
            string setupScript = Path.Combine(ghaDir, "setup.ps1");

            if (!File.Exists(setupScript))
            {
                _setupRunning = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Dependency setup wizard is missing.");
                return;
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "First time dependency setup is in progress. See the Rhino command line for progress.");
            Rhino.RhinoApp.WriteLine("First-time LiveYolo dependency setup is in progress. This may take several minutes. Do not intervene!");

            Task.Run(() =>
            {
                int exitCode = -1;
                try
                {
                    // bypass execution policy and run the setup script
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{setupScript}\" -GhaDir \"{ghaDir}\" -Base \"{BaseDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    // Start the process and display output
                    var p = Process.Start(psi);
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) Rhino.RhinoApp.WriteLine("[LiveYolo] " + e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) Rhino.RhinoApp.WriteLine("[LiveYolo] " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
                catch (Exception ex)
                {
                    Rhino.RhinoApp.WriteLine("[LiveYolo] setup error: " + ex.Message);
                }
                finally
                {
                    _setupRunning = false;
                }

                string pythonPath = Path.Combine(BaseDir, "python", "python.exe");
                if (exitCode == 0 && File.Exists(pythonPath))
                {
                    Rhino.RhinoApp.WriteLine("[LiveYolo] Setup complete.");
                    Rhino.RhinoApp.InvokeOnUiThread(new Action(() => { if (_wasActive) LaunchBackend(); }));
                }
                else
                {
                    Rhino.RhinoApp.WriteLine("[LiveYolo] Setup failed (exit " + exitCode + "). Toggle Active off and on to retry.");
                }
            });
        }

        private void LaunchBackend()
        {
            string backendDir = Path.Combine(BaseDir, "backend");
            string scriptPath = Path.Combine(backendDir, "backend.py");
            string pythonPath = Path.Combine(BaseDir, "python", "python.exe");

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

            _backendProcess = Process.Start(psi); //start backend server

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
                Rhino.RhinoApp.WriteLine("YOLO server is not responding.");
            });
        }

        private void KillExistingBackend()
        {
            try
            {
                var processes = Process.GetProcessesByName("python");
                foreach (var p in processes)
                {
                    try
                    {
                        // Check if the process is running from our BaseDir
                        string path = p.MainModule?.FileName;
                        if (path != null && path.StartsWith(BaseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                    }
                    catch { }
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

        protected override System.Drawing.Bitmap Icon =>IconLoader.Get("YoloDevice.png");

        public override Guid ComponentGuid => new Guid("90e76670-deae-4ec1-a1a4-40ca9067efa7");
    }
}
