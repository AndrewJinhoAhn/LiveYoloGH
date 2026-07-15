using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveYOLO
{
    public class YOLOFetchComponent : GH_Component
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(80)
        };

        private List<Rectangle3d> _boxes = new List<Rectangle3d>();
        private List<string> _tags = new List<string>();
        private List<int> _ids = new List<int>();
        private List<double> _confs = new List<double>();
        private int _width = 0;
        private int _height = 0;

        public YOLOFetchComponent()
          : base("YOLO Fetch", "YOLOfetch",
            "Fetch latest YOLO detections from backend",
            "Appendage", "Live YOLO")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Port", "P", "Backend port (from YOLO Control)", GH_ParamAccess.item, 8000);
            pManager.AddBooleanParameter("Trigger", "T", "Trigger to fetch (connect to Timer)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddRectangleParameter("Boxes", "B", "Bounding box rectangles (pixel coords, origin bottom-left)", GH_ParamAccess.list);
            pManager.AddTextParameter("Tags", "T", "Class labels", GH_ParamAccess.list);
            pManager.AddIntegerParameter("IDs", "ID", "Track IDs", GH_ParamAccess.list);
            pManager.AddNumberParameter("Confidence", "Conf", "Detection confidence", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Width", "W", "Camera frame width (pixels)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Height", "H", "Camera frame height (pixels)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int port = 8000;
            bool trigger = false;
            DA.GetData(0, ref port);
            DA.GetData(1, ref trigger);

            if (!trigger)
            {
                DA.SetDataList(0, _boxes);
                DA.SetDataList(1, _tags);
                DA.SetDataList(2, _ids);
                DA.SetDataList(3, _confs);
                DA.SetData(4, _width);
                DA.SetData(5, _height);
                return;
            }

            try
            {
                string url = $"http://127.0.0.1:{port}/detections";
                string json = _http.GetStringAsync(url).GetAwaiter().GetResult();

                var response = JsonSerializer.Deserialize<DetectionResponse>(json);

                _width = response.width;
                _height = response.height;
                _boxes.Clear(); _tags.Clear(); _ids.Clear(); _confs.Clear();

                foreach (var d in response.dets)
                {
                    // Convert to Rhino coordinate system (origin bottom-left)
                    var cornerA = new Point3d(d.x1, _height - d.y1, 0);
                    var cornerB = new Point3d(d.x2, _height - d.y2, 0);
                    _boxes.Add(new Rectangle3d(Plane.WorldXY, cornerA, cornerB));
                    _tags.Add(d.tag);
                    _ids.Add(d.id);
                    _confs.Add(d.conf);
                }
            }
            catch (TaskCanceledException)
            {
                // Timeout occurred, likely no detections available
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
            }

            DA.SetDataList(0, _boxes);
            DA.SetDataList(1, _tags);
            DA.SetDataList(2, _ids);
            DA.SetDataList(3, _confs);
            DA.SetData(4, _width);
            DA.SetData(5, _height);
        }

        protected override System.Drawing.Bitmap Icon => IconLoader.Get("YoloFetch.png");

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    internal class DetectionResponse
    {
        public int width { get; set; }
        public int height { get; set; }
        public List<Detection> dets { get; set; }
    }

    internal class Detection
    {
        public string tag { get; set; }
        public double x1 { get; set; }
        public double y1 { get; set; }
        public double x2 { get; set; }
        public double y2 { get; set; }
        public int id { get; set; }
        public double conf { get; set; }
    }
}