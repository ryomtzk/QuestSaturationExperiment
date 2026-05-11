QuestSaturationExperiment
Full-Field Color Manipulation Library for Meta Quest 3

Author: Ryo Matsuzaki
License: MIT

A Unity project for manipulating the saturation of the entire passthrough view on Meta Quest 3 in real time. It calls OVRPassthroughLayer.SetBrightnessContrastSaturation every frame, which prevents the Building Blocks passthrough pipeline from silently overwriting the layer, and lets you drive the whole-field saturation anywhere in [-1, +1].

A small 3-condition selection task (no change / strong desaturation / strong over-saturation) is bundled as an example use case.

Requirements
Unity 6000.3.7f1 / URP 17.3 / Meta XR SDK 201.0.0 / OpenXR 1.16 / Meta Quest 3 (Android build).

Scripts
Everything lives in Assets/Scripts/.

SaturationController.cs — the core component. Holds an OVRPassthroughLayer reference and re-applies brightness / contrast / saturation every frame. Call SetSaturation(float) from anywhere to drive it.
SaturationCurve.cs — per-condition target value and time profile (3 s hold at 0, 10 s linear ramp, then held).
ConditionSelector.cs — right-controller raycast UI for picking one of three spheres.
SphereOption.cs — a selectable sphere (scale + emission on hover).
ExperimentManager.cs — two-phase state machine (Selection / Experiment). Hold the right A button for 2 s to return to Selection.
Saturation targets per condition:

A: 0.0
B: -0.8
C: +0.8
Minimal usage
If you only need the library part, assign your OVRCameraRig's OVRPassthroughLayer to a SaturationController and call:

saturationController.SetSaturation(-0.8f); // strong desaturation
Setup
Open the project in Unity 6000.3.7f1 and let packages resolve.
Open Assets/Scenes/PassthroughScene.unity.
Switch the build target to Android, connect a Meta Quest 3 over USB, and Build & Run.
Passthrough cannot be previewed in the Editor, so verify on the device.

Controls
Aim with the right controller, confirm with the index trigger, and hold the right A button for 2 s to reset to the selection screen.

License
MIT License. See LICENSE.

Citation
If you use this software in academic work, please cite: Ryo Matsuzki, [software name], 2026.
