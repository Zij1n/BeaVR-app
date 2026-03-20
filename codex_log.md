# Codex Log

## Request Summary

The work in this session had four concrete goals:

1. Make the Unity app send joint rotation information together with the existing joint position information.
2. Determine why the Unity project did not build in the current environment and fix the missing pieces needed to produce an Android APK.
3. Document the network payload clearly enough for a server-side reader with no repository context.
4. Preserve the package state in Git so the project remains buildable on another machine instead of only in one local Unity cache.

## Initial Project State

- Repository root: `/Users/zijin/workspace/beavr_workspace/BeaVR-app`
- Unity project root: `/Users/zijin/workspace/beavr_workspace/BeaVR-app/BeaVR-Unity`
- Unity version required by the project: `6000.2.2f1`
- Unity Android module was not initially available, then was installed later through Unity Hub during the session.

## Early Build Investigation

The first Unity batch run against the real project failed because Unity detected the project was already open elsewhere and refused to run a second instance against the same project directory.

To avoid interfering with the user’s active editor state, the project was cloned to:

- `/tmp/BeaVR-Unity-build`

Unity batch runs were then executed against that temp clone.

## First Real Build Blockers Found

Two separate blockers were identified before any Android packaging could happen:

1. The Android build support module was missing from the Unity editor.
2. The C# project did not compile because `NetMQ` namespaces and socket classes were unresolved.

The Android tooling issue was solved after the user installed the Android build module in Unity Hub.

The C# issue turned out to be the more important one.

## Root Cause of the C# Compile Failure

The project expected several NuGet packages under:

- `BeaVR-Unity/Assets/Packages`

Those package folders existed, but the actual DLL binaries were missing from the repository.

This caused Unity compile errors like:

- missing namespace `NetMQ`
- missing types `PushSocket`
- missing types `SubscriberSocket`

The main affected runtime scripts were:

- `Assets/Scripts/Network/NetMQController.cs`
- `Assets/Scripts/Camera Stream Scripts/CameraOneStreamer.cs`
- `Assets/Scripts/GraphStream.cs`
- `Assets/Scripts/NetMQCleanup.cs`

## Why the DLLs Were Missing

The root `.gitignore` had global rules ignoring `*.dll`.

That meant the NuGet package binaries required by Unity under `Assets/Packages` were not being preserved in Git, even though the Unity project depended on them.

This was fixed by adding an exception for package DLLs and by narrowing package tracking behavior so Git keeps:

- package directories
- package `.meta` files
- package `.dll` files

and ignores the rest of the extracted NuGet payload unless already tracked.

## NuGet Recovery Work

The required packages from `BeaVR-Unity/Assets/packages.config` were restored from NuGet.

Relevant packages:

- `AsyncIO 0.1.69`
- `KaitaiStruct.Runtime.CSharp 0.10.0`
- `NaCl.Net 0.1.13`
- `NetMQ 4.0.2.1`
- `System.Security.Principal.Windows 5.0.0`
- `System.ServiceModel.Primitives 8.1.2`

The first direct restore extracted too much package content into `Assets/Packages`, which caused Unity/IL2CPP linker issues because multiple runtime/framework variants of the same assemblies were imported at once.

That over-extracted state was then trimmed down to the Unity-relevant assemblies only, specifically:

- `AsyncIO.dll` from `lib/netstandard2.0`
- `Kaitai.Struct.Runtime.dll` from `lib/netstandard1.3`
- `NaCl.dll` from `lib/netstandard2.1`
- `NetMQ.dll` from `lib/netstandard2.1`
- `System.Security.Principal.Windows.dll` from `lib/netstandard2.0`
- `System.ServiceModel.Primitives.dll` from `lib/netstandard2.0`
- `System.ServiceModel.Security.dll` from `lib/netstandard2.0`
- `System.ServiceModel.Duplex.dll` from `lib/netstandard2.0`

Corresponding Unity `.meta` files for those DLLs were also restored so importer state and GUIDs are present in the repo.

## Rotation Payload Implementation

The runtime payload change was made in:

- `BeaVR-Unity/Assets/Scripts/Gesture Detection/GestureDetectorXR.cs`

Before the change:

- each joint sent only position
- format per joint: `px,py,pz`
- total floats per hand: `26 x 3 = 78`

After the change:

- each joint sends position and rotation quaternion
- format per joint: `px,py,pz,qx,qy,qz,qw`
- total floats per hand: `26 x 7 = 182`

Implementation details:

- Added a `HandJointPoseData` struct carrying:
  - `Vector3 Position`
  - `Quaternion Rotation`
  - `bool IsTracked`
- Replaced position-only serialization with a joint pose serializer using invariant-culture floats.
- Collected both `pose.position` and `pose.rotation` from Unity XR Hands.
- Preserved a fixed 26-joint order even when a joint is not tracked.
- For untracked joints, inserted the fallback tuple:
  - position: `0,0,0`
  - rotation: `0,0,0,1`

## Exact Hand Payload Shape

Each message is sent as a plain string on either the `RightHand` or `LeftHand` socket.

Message structure:

`<mode>:<joint0>|<joint1>|...|<joint25>:`

Each joint entry:

`px,py,pz,qx,qy,qz,qw`

Example shape:

`relative:0.10,1.20,0.30,0.00,0.00,0.00,1.00|0.11,1.22,0.31,0.01,0.02,0.03,0.99|...:`

Important parsing notes:

- the first `:` separates mode from the joint list
- joints are separated with `|`
- values inside one joint are separated with `,`
- a trailing `:` is appended at the end of the entire message

## Joint Order

The transmitted joint order is fixed and comes directly from the sender code:

1. Wrist
2. Palm
3. ThumbMetacarpal
4. ThumbProximal
5. ThumbDistal
6. ThumbTip
7. IndexMetacarpal
8. IndexProximal
9. IndexIntermediate
10. IndexDistal
11. IndexTip
12. MiddleMetacarpal
13. MiddleProximal
14. MiddleIntermediate
15. MiddleDistal
16. MiddleTip
17. RingMetacarpal
18. RingProximal
19. RingIntermediate
20. RingDistal
21. RingTip
22. LittleMetacarpal
23. LittleProximal
24. LittleIntermediate
25. LittleDistal
26. LittleTip

## Relative vs Absolute Semantics

The sender exposes two modes:

- `relative`
- `absolute`

These are switched by left-hand gestures:

- index pinch -> `relative`
- middle pinch -> `absolute`
- ring pinch -> stop streaming

Important implementation detail:

The current code does **not** mathematically transform the joint data differently between the two modes. Both modes currently send the same world-space style joint data and only differ by the text mode prefix.

That means:

- `absolute` is world-frame pose data
- `relative` is also currently world-frame pose data, but labeled as relative for downstream interpretation

True wrist-relative or palm-relative math was **not** added in this session.

## Diagnostic Test Messages

The app can send diagnostic messages on the same sockets used for regular streaming.

These messages are generated by:

- `NetMQController.PerformDiagnosticTests()`

Format:

`DIAGNOSTIC_TEST_<socketName>_<timestamp>`

Examples:

- `DIAGNOSTIC_TEST_RightHand_03:15:42.128`
- `DIAGNOSTIC_TEST_LeftHand_03:15:42.129`

When they are sent:

- at startup after the delayed socket initialization path
- after socket connect/reconnect paths that call diagnostics

They are separate messages, not embedded inside a normal hand payload.

Server-side implication:

- if a message starts with `DIAGNOSTIC_TEST_`, it should be ignored by the hand-payload parser

## Build Automation Added

A batch build entry point was added at:

- `BeaVR-Unity/Assets/Editor/BatchBuild.cs`

This enables a non-interactive Android build through Unity batchmode and writes the output APK to:

- `BeaVR-Unity/Builds/Android/BeaVR.apk`

## Documentation Updated

The Unity-specific README was updated to reflect:

- new message format
- that the hand stream now contains position plus rotation

File:

- `BeaVR-Unity/README.md`

## Android Build Result

After restoring the package DLLs, trimming package variants, and adding the batch build entry, Unity was able to complete:

- script compilation
- IL2CPP conversion
- native ARM64 compile
- Gradle packaging

The produced APK was copied into the repo workspace at:

- `/Users/zijin/workspace/beavr_workspace/BeaVR-app/BeaVR-Unity/Builds/Android/BeaVR.apk`

## Build Warnings Observed

The APK build succeeded, but a few warnings remain:

1. Several Meta XR native `.so` files were reported as not 16 KB aligned.
   - This may matter on some Android 15+ ARM64 devices.

2. Unity logged:
   - `OVRPlugin not updated. Restart the editor to update.`
   - This did not block APK generation.

3. `relative` mode is still only a label.
   - If downstream behavior depends on true relative coordinates/rotations, sender logic still needs to be changed.

## Files Intentionally Changed

- `.gitignore`
- `BeaVR-Unity/Assets/Scripts/Gesture Detection/GestureDetectorXR.cs`
- `BeaVR-Unity/Assets/Editor/BatchBuild.cs`
- `BeaVR-Unity/Assets/Editor/BatchBuild.cs.meta`
- `BeaVR-Unity/Assets/Editor.meta`
- `BeaVR-Unity/README.md`
- selected DLLs and DLL `.meta` files under `BeaVR-Unity/Assets/Packages`
- `codex_log.md`

## Final Output

Primary deliverables from this session:

1. Unity app now sends joint rotation together with joint position.
2. Android APK built successfully.
3. Payload format and runtime behavior were analyzed and documented for server-side integration.
