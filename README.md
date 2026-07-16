# LiveYOLO

Real-time object detection inside Grasshopper. LiveYOLO streams live webcam detections into Rhino as geometry, powered by a YOLO model running on a self-contained Python backend that installs itself on first use.

## Components

| Component | Nickname | Description |
|-----------|----------|-------------|
| **YOLO Control** | YOLOctrl | Starts and stops the webcam detection backend from a single toggle. |
| **YOLO Fetch** | YOLOfetch | Fetches the latest detections and outputs them as bounding-box rectangles, with class tags, track IDs, and confidence values. |

## How it works

`YOLO Control` launches a local Python backend (FastAPI + Ultralytics YOLO) that captures the webcam and runs detection frame by frame. `YOLO Fetch` polls that backend and returns the current detections as Rhino geometry, so a Grasshopper definition can react to what the camera sees in real time.

## First-run setup (automatic)

The first time you enable `YOLO Control`, LiveYOLO installs a self-contained Python environment (Python, PyTorch, Ultralytics) into `%LOCALAPPDATA%\LiveYolo`. This is a one-time download of roughly 5 GB and can take several minutes.

- Progress is shown in the Rhino command line.
- **Do not close Rhino or press Esc while setup is running.** It runs only once.
- After setup the backend starts automatically. A CUDA GPU is used when available, otherwise CPU.

The detection model is downloaded on first detection and cached alongside the backend.

## Installation

**Rhino Package Manager (recommended)**

1. In Rhino 8, run `_PackageManager`.
2. Search for **liveyolo** and click Install.

**Manual**

Download the `.yak` from the [latest release](https://github.com/AndrewJinhoAhn/LiveYoloGH/releases) and install it through the Package Manager.

## Requirements

- Rhino 8 (Windows only)
- A webcam
- Internet connection for the one-time first-run setup
- A CUDA-capable GPU is recommended but not required