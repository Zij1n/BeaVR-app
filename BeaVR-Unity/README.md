# BeaVR Unity Project

**Unity 6.2** | **OpenXR** | **XR Hands**

---

## 📁 Project Structure

```
Assets/
├── Scripts/
│   ├── Gesture Detection/
│   │   └── GestureDetectorXR.cs       # Hand tracking & pinch detection
│   ├── Network/
│   │   └── NetMQController.cs         # NetMQ messaging
│   ├── NetworkManager.cs              # Network config
│   ├── UI/                            # IP input, canvas switching
│   ├── Camera Stream Scripts/
│   │   └── CameraOneStreamer.cs       # Receive camera feed
│   └── GraphStream.cs                 # Receive graph data
│
└── Resources/Configurations/
    └── Network.json                   # IP addresses & ports
```

---

## 🔄 Data Flow

```
XR Hands Subsystem
       ↓
GestureDetectorXR (26 joints/hand)
       ↓
NetMQController.SendMessage()
       ↓
   [Network]  ←─────────────┐
       ↓                    │
   Server              CameraOneStreamer
                       GraphStream
```

**Message Format**: `"mode:px,py,pz,qx,qy,qz,qw|..."` (26 joints x 7 floats = 182 floats per hand)

---

## Core Components

| Component | Purpose |
|-----------|---------|
| `GestureDetectorXR.cs` | XR Hands tracking (position + rotation) → NetMQ |
| `NetMQController.cs` | ZeroMQ pub/sub messaging |
| `NetworkManager.cs` | Load `Network.json` config |
| `CameraOneStreamer.cs` | Receive & display camera |
| `GraphStream.cs` | Receive & display graphs |

### Hand Joint Order (XR Hands)
```
0:Wrist  1:Palm
2-6:   Thumb (Metacarpal→Tip)
7-11:  Index
12-16: Middle
17-21: Ring
22-26: Little
```

---

## ⚙️ Unity Setup

### Required Packages
- XR Plugin Management
- XR Hands (v1.6.1)
- XR Interaction Toolkit
- NetMQ 4.0.2.1 (NuGet)

### Project Settings
```
XR Plug-in Management → OpenXR
  ✓ OpenXR provider
  ✓ Meta XR Hand Tracking Aim
```

### Scene Requirements
```
XR Origin (XR Rig)
  ├── Camera Offset
  │   └── Main Camera
  └── [Controllers/Hands]

EventSystem
  └── XR UI Input Module

Canvas (World Space)
  └── Tracked Device Graphic Raycaster
```

---

## Building for Quest

| Setting | Value |
|---------|-------|
| **Platform** | Android |
| **Scripting Backend** | IL2CPP |
| **Target API Level** | 32+ |
| **Texture Compression** | ASTC |
| **XR Provider** | OpenXR (Android tab) |

**Build**: File → Build Settings → Build and Run

---

## Troubleshooting

**Common Issues:**
- **NuGet packages not loading**: Reinstall NuGet packages (NetMQ, AsyncIO, NaCl.Net)
- **Hand tracking not working**: Enable OpenXR Hand Tracking Subsystem in Project Settings
- **Build settings**: Platform (Android) and Target API Level (32+) are already configured

---

## 📦 Dependencies

**NuGet** (in `Assets/Packages/`):
- NetMQ 4.0.2.1
- AsyncIO 0.1.69
- NaCl.Net 0.1.13

**Unity**:
- XR Hands (1.6.1)
- XR Interaction Toolkit
- TextMesh Pro

---

## OVR vs OpenXR

This project uses **OpenXR with Meta XR Interaction building blocks** instead of the legacy Oculus Integration SDK.

**Key Differences:**
- **Hand Tracking**: XR Hands provides 26 joints per hand vs OVR's bone structure
- **Joint Order**: Different ordering - ensure receiver code matches XR Hands format (see above)
- **Scene Setup**: Uses Meta XR Interaction building blocks for camera rig and UI interaction

---

## License

MIT License
