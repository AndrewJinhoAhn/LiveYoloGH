import threading
import cv2
from ultralytics import YOLO
from fastapi import FastAPI
import uvicorn
import torch

app = FastAPI()

#shared status
_state = {"dets": [], "running": False, "width": 0, "height": 0, "device": "unknown"}
_lock = threading.Lock()
_thread = None
_stop_event = threading.Event()

#capture and update
def capture_loop():
    device = "cuda" if torch.cuda.is_available() else "cpu" #force CUDA if available
    with _lock: #lock thread when accessing _state
        _state["device"] = device
    model = YOLO("yolo26n.pt")
    cap = cv2.VideoCapture(0) #use primary cam #consider user camera selection in the next release

    while not _stop_event.is_set():
        ok, frame = cap.read()
        if not ok:
            continue

        h, w = frame.shape[:2] #actual camera frame size
        results = model.track(frame, persist=True, imgsz=640, verbose=False, device=device) #consider customizable imgsz in the next release
        r = results[0]

        dets = [] #detection results
        if r.boxes is not None:
            for b in r.boxes:
                x1, y1, x2, y2 = b.xyxy[0].tolist()
                dets.append({
                    "tag":  model.names[int(b.cls)],
                    "x1":   x1,
                    "y1":   y1,
                    "x2":   x2,
                    "y2":   y2,
                    "id":   int(b.id) if b.id is not None else -1,
                    "conf": float(b.conf),
                })

        with _lock:
            _state["dets"] = dets
            _state["width"] = w
            _state["height"] = h

    cap.release()

#routing
@app.post("/start")
def start():
    global _thread, _stop_event
    with _lock:
        if _state["running"]:
            return {"status": "already running"}
        _stop_event.clear()
        _thread = threading.Thread(target=capture_loop, daemon=True) #scrap capture_loop if program ends
        _thread.start()
        _state["running"] = True
    return {"status": "started"}

@app.post("/stop")
def stop():
    global _thread
    with _lock:
        if not _state["running"]:
            return {"status": "not running"}
        _stop_event.set()
        _state["running"] = False
        _state["dets"] = []
    return {"status": "stopped"}

@app.get("/detections")
def detections():
    with _lock:
        return {"width": _state["width"], "height": _state["height"], "dets": _state["dets"]}

@app.get("/status")
def status():
    with _lock:
        return {"running": _state["running"], "device": _state["device"]}

#entry point action
if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)
