# NBM-500U VR setup brief

Project: `D:\Construction\Construction VR` (Unity 6000.6.5f1, URP, `com.unity.pipeline` installed).
Model: `Assets/NBM500U/NBM500U.fbx`, panel texture `Assets/NBM500U/Textures/T_PS_Panel.png`.
Blender source: `D:\Blender\NBM500U\NBM500U.blend` (1 unit = 1 m, Z up, front of the machine = -Y).

## Goal
1. Configure the project for Meta Quest (Android, OpenXR).
2. Use Meta's own SDK (Meta XR Core SDK + Meta XR Interaction SDK), NOT XR Interaction Toolkit.
3. Make these parts usable with hands/controllers:
   - Buttons (poke): the 12 objects `PS_Btn_*` under `PowerSource/PS_ControlPanel`, and `WF_Button_01..03` on the wire feeder.
   - Knobs (grab and turn around their own axis): `PS_EncoderKnob`, `WF_Knob_Current`, `WF_Knob_Voltage`,
     `WF_Drive_TensionKnob_Front`, `WF_Drive_TensionKnob_Rear`.
   - Welding torch (grab and carry): everything under `Torch_MIG` except `Torch_Cable`, `Torch_EuroNut`, `Torch_EuroPlug`
     (`Torch_Handle`, `Torch_Trigger`, `Torch_CableSupport`, `Torch_Neck`, `Torch_NozzleInsulator`, `Torch_GasNozzle`, `Torch_ContactTip`).
     Group them under one rigid body; the grab point is `Torch_Handle`.
4. Test in the Editor (compile, Play mode) and report what was and was not verified.

## Import notes
- Turn on "Bake Axis Conversion" on the FBX importer.
- Materials: extract or remap; `M_PS_PanelFace` uses `T_PS_Panel.png` as base colour
  (panel plate and all `PS_Btn_*` share it through UVs, the legends are in the texture).
- Curves and cables were exported as meshes. Hierarchy root is `NBM500U` with children
  `Trolley`, `WaterCooler_WRC35`, `PowerSource`, `WireFeeder`, `Torch_MIG`, `GroundClamp`, `Cables`.

## Hinged parts (pivots are already on the hinge line)
- `WF_Door`: opens -100 deg about its Blender-local Y axis (custom props `hinge_axis`, `open_angle_deg`).
- `WF_SpoolCover`: opens +110 deg about its Blender-local Y axis.
  After axis conversion the Unity-local axis must be checked in the Editor.

## Other named parts worth knowing
- 53 indicator LEDs `PS_LED_*` (e.g. `PS_LED_Mode_Pulse`, `PS_LED_Gas_CO2`, `PS_LED_Dia_1_2`), displays `PS_Display_Current`, `PS_Display_Voltage`.
- Button names: `PS_Btn_WeldMode`, `PS_Btn_WireMaterial`, `PS_Btn_ShieldGas`, `PS_Btn_WireDiameter`, `PS_Btn_OperationMode`,
  `PS_Btn_WireInch`, `PS_Btn_GasCheck`, `PS_Btn_Store_F1`, `PS_Btn_Recall_F2`, `PS_Btn_ParamLeft_UpDown`,
  `PS_Btn_ParamRight_UpDown`, `PS_Btn_LeftRight`.
- Objects with the custom property `assumed` on the rear panel were guessed, not taken from photos.
