# Construction VR

VR trainer for semi-automatic (MAG/CO2) welding in a railway-car workshop ("LMZ payvandlash sexi"), for Meta Quest 3.

- Unity `6000.6.5f1`, URP `17.6`, OpenXR, single-pass instanced stereo.
- Meta XR SDK `209` (core, interaction, interaction.ovr) and Movement SDK `v209` (git package, full-body player).
- Build target is Android, so the Mobile URP asset is the active one, also in the Editor.

## Working with the team

- The team writes in Uzbek (Latin script): reply, report progress and ask questions in Uzbek. Windows and Unity CLI output on their machines is often Russian. Briefs kept in the repo (for example `Assets/NBM500U/VR_SETUP_BRIEF.md`) are in English.
- Testing is done as a first-time learner would: nobody knows the object names in the hall, and anything that needs reach or precision is where people get stuck. Design for that.

## Scope

The spec is `Resource/VR_Payvandchi_Ssenariy_TZ.docx`: stages 0-12 (login, safety journal signature, PPE, cable check, power on, wire spool, cylinder safety, gas pressure and flow, V/A mode, part fixing, measuring, tack welds, main weld, quality control, clean-up), error scenarios, and four scores (safety, technique, quality, time).

The TZ describes one workshop; the project splits it into two scenes by design:

| Scene | File | Stages |
| --- | --- | --- |
| Engineers' room | `Assets/Scenes/EngineerRoom.unity` | 0-2: documents, putting on PPE |
| Factory hall | `Assets/Scenes/Factory.unity` | 3-12: all welding work |

`NBM500U_VR.unity` is the welding machine's own test scene. Exact numbers (V/A, pressures, flow, score weights) come from a separate "Savolnoma" document that is not in `Resource/`; do not invent them.

Two raw source files are kept out of the repo because of their size (`Resource/полувагон.fbx`, `Resource/Цистерна.rar`).

## Layout

- `Assets/Training/Scripts/Core` - scenario, session, config, localisation (`Scenario`, `TrainingSession`, `TrainingConfig`, `Loc`).
- `Assets/Training/Scripts/Scene` - in-world behaviour: directors (`RoomDirector`, `HallDirector`), guides, grabbing, welding.
- `Assets/Training/Scripts/Ui` - panels and the UI kit (`Ui`, `UiStyle`, `UiCanvas`, `HubPanel`, `SettingsWindow`).
- `Assets/Training/Resources/training_strings.txt` - all UI and guide texts.
- `Assets/Player` - full-body player, outfit, mirror. `Assets/NBM500U` - welding machine and gas cylinder.
- Scenes and prefabs are built by editor scripts, under the `Tools/` menu: `Engineer Room/Build Scene`, `Factory/Build Factory Scene`, `NBM500U/Build VR Prefab and Scene`, `Player/Build Player Body Prefab`, `Training/Install In Open Scene`. Change the builder, not only the saved scene, or the change is lost at the next build.
- A component that a scene builder saves into a scene must sit in a file named after its class. Several MonoBehaviours in one file compile and work until the scene is reloaded, then silently disappear.

## Rendering: no legacy CG shaders

In this Unity + URP version, any shader built on `UnityCG.cginc` (UI/Default, Sprites/Default, Unlit/Color, stock TextMesh Pro shaders, Meta's `Interaction/OculusRayVisual`, `OculusRayCursor`, `OculusHand`) is drawn in the left eye only, and shifted. Shaders that include the URP library (`Core.hlsl`) are fine. This was measured in the simulator and seen over Quest Link; it is not verified on an Android/Vulkan build.

- Never give a new material a built-in or CG shader (`Sprites/Default`, `Unlit/*`, `UI/Default`, `Legacy Shaders/*`). Use URP shaders or a URP-library port.
- UI images use `ConstructionVR/UI/Image` through `UiStyle.ImageMaterial`; create images only with `Ui.NewImage`. TMP font materials use `ConstructionVR/UI/Text`. Both shaders are in `Assets/Training/Shaders`.
- Meta's rig visuals live in an immutable package, so `StereoShaderFix` swaps their shaders at run time for the ports in `Assets/Training/Resources/StereoShaders`. To cover another one, add a port and one `Replace(...)` line. Still legacy: `Interaction/OculusHand`, Meta's teleport arc/hotspot and tunneling shaders.
- When checking anything visual, capture the left AND the right eye. Left-only captures hid this bug for days.
- A camera rendered by hand into a multisampled RenderTexture comes out black here; use `antiAliasing = 1` (the planar mirror does).
- `AssetDatabase.FindAssets` without a folder filter also returns package assets. Pass `new[] { "Assets" }` before modifying and saving anything.

## UI follows the Figma design

The interface is built to the customer's finished Figma design ("Temir yo'l VR"; the trainer is one module of the "O'zbekiston temir yo'llari - VR Simulator" suite), not to taste. The link is in `Resource/Figma link.txt`; Page 1 holds the UI (login, hub with profile and practice cards, MENU, Qo'llanma step list, Sozlamalar groups).

- Look: deep navy glass panels, thin light-blue outline, blue glow along the bottom, white text, blue pill buttons, segmented controls, glowing sliders.
- The Sozlamalar window's sizes and behaviour are specified in `Resource/UI Sozlamalar TO'LIQ.html` (hub 1680x920, hit areas at least 56 px).
- The look lives in `UiStyle.cs` and `Ui.cs`. Build new screens from those pieces and compare them with the Figma frames before calling them done.
- Not in the project yet: the design's own font, the emblem, the photo backgrounds.

## Interaction conventions

- **Picking up.** A long white ray from the hand appears only when it lands on a grabbable model; the model glows; grip takes it and it comes to the hand. `RemoteGrab` is added to the rig at run time and covers every `Grabbable` with a collider, so nothing is added per object. Free things are pulled to the hand; knobs and hinged covers are worked in place. Keep new grabbables kinematic: a rigidbody must not fly to the hand under force.
- **Placing.** While a `SnapItem` with a target is held, `PlaceGhost` shows a half-transparent glowing copy at its place. Glows are drawn by `ModelGlow` with `StereoShaders/Highlight`.
- **Wearing PPE.** Point the ray, the item glows, trigger puts it on. Grabbing and bringing it to the body still works.
- **Knobs.** A knob step says the value now, the wanted range and what comes next (`HallDirectorGuide.Knob`), and the knob glows. Its reader sets `NBMKnobTwistTransformer.IncreaseSign` so that thumbstick-right on the holding hand means "more". The roller pressure has two knobs and the lower one counts: guide each in turn.
- **Collider margins** are sized in world metres (`GasCylinderVRSetup.GrabMargin`). A margin given in mesh units on a model imported at scale 100 came out a metre wide and made knobs hard to turn.
- **Controller models** are hidden only while a hand is shown in their place.
- **Panels.** `UiCanvas.OnTop` (default true) draws a panel over the world. Panels fixed in the room and pressed with a finger (hub, journal, door) set `OnTop = false` so the hand in front stays visible. Move panels with `UiCanvas.Place(position)`, never `transform.position`: `Place` brings the panel forward of colliders between the eyes and it and keeps its angular size, which needs colliders on the occluding models.
- **Readings on hand-held tools** do not sit on the tool face, where the holding hand covers them. `MeasureTool` shows a tag 10 cm above the probe, turned to the eyes.
- The room login comes pre-typed.

## Every step is pointed at in the world

Step text on the task card is not enough. `StepGuide` (created by `TrainingUi.Ensure`) shows an arrow and a caption over the target of the first undone step, and a small "turn this way" arrow when it is out of view.

- Targets come from `Guide(step)` in `HallDirectorGuide.cs` and `RoomDirectorGuide.cs`; captions are the `guide.*` rows of `training_strings.txt`.
- When a step, a tool or a carried item is added or changed, add or update its `Guide` case and caption in the same change, and check it in both eyes.
- The arrow shows where a step is done, not the answer: checks point at the place to look at, part picking points at the whole rack.
- `GuideTarget.Focus` makes the thing itself glow green.

## Testing

**In the Editor over Quest Link** (D3D11, OpenXR runtime "Oculus"): this is how the trainer is normally played.

- When something "does not work on the Quest", read `Logs/Editor.log` first: `Runtime Name:` tells Link from the simulator, `Unavailable OpenXR extension:` tells what the runtime lacks.
- Body tracking over Link needs **Settings > Developer > Developer Runtime Features** in the Meta Horizon Link app. Without it `PlayerBodyAnchor.IsTracked` is false: the body is hidden, `PlayerOutfit.WearPoint` falls back to head and hand anchors, `PlayerHandVisuals` keeps the rig hands. That fallback must keep working.

**In Meta XR Simulator with Meta XR Operator** (skills in `.claude/skills/hz-meta-xr-operator*`):

- **Never refresh or recompile while the Editor is in Play mode.** Check `editor status` first. A domain reload mid-play loses `TrainingSession.Current` and everything throws each frame. If someone is playing, finish the edit, ask them to stop Play, then compile and test.
- After editing a script, force `AssetDatabase.Refresh()` and a recompile and confirm the new code is loaded before Play: an unfocused Editor does not auto-refresh, and Meta writes `Assets/StreamingAssets/RuntimeActionBindings.json` at Play start, which triggers the pending compile inside Play mode. Right after a recompile the Editor is briefly unreachable; wait for `editor status` = ready.
- The rig follows the controller **grip** pose: set both grip and aim poses. Grab input is `Grip`.
- The rig's position and yaw differ per scene (it is rotated in the engineers' room): read them from the scene when converting Unity positions to OpenXR `local_floor`.
- A controller orientation set as "look at the target" leaves the UI ray far off. Read `RayInteractor.Forward`, turn the orientation by `FromToRotation(ray.Forward, toTarget)` and set it again; twice for small targets.
- Without controllers: `RemoteGrab.Instance.TestAim / TestGrip / TestTrigger / TestState` drive the hand ray; `Grabbable.ProcessPointerEvent` with Hover, Select, Move, Unselect, Unhover (one eval call per phase) fakes a grab; `TrainingSession.Current.Complete(step)` jumps to a step, then read `StepGuide.Instance.Target`.
- Setting and clearing something in the same eval frame is never seen by `Update`: split it over two calls.
- Read scene state with `unity command script eval --file`; the Operator's `unity_get_world_pose` once crashed the Editor.
- Synthetic hands: poke and pinch are stable. A palm grab pushed about 25 degrees past a hinge limit blows up the ISDK hand skeleton (restart Play mode). With Meta's Locomotor active this dropped the player through the floor, so `NBM500UVRSetup.EnableLocomotion` is false.
- **Body tracking in the simulator is a playback**, not derived from the head or controllers: the body stands at a fixed spot whatever the headset does. `PlayerBodyAnchor` shifts it under the headset so the mirror, first-person view and wear zones can be tested; body-to-head alignment can only be judged on a real headset. `PlayerBodyAnchor.SimulateUntracked = true` hides the body and tests the untracked path.
- The Movement SDK hands the retargeter poses that are already in world space. The character must sit at the scene root with an identity transform; under a rotated `OVRCameraRig` it came out turned twice.
- Body fingers move in the simulator only through hand tracking (hand gestures), not with controllers.
- Some Meta Project Setup Tool fixes open modal dialogs ("Update AndroidManifest.xml") that block the Editor and the CLI until someone clicks.

## Repository

- Binary assets (models, textures, fonts, DLLs, archives, documents) are in Git LFS; run `git lfs install` before cloning. The patterns are in `.gitattributes`.
- `Assets/Resources/DevAgentSettings.asset` is ignored on purpose: it holds a per-machine address and access token, and the Meta SDK recreates it.
- `Library/`, `Logs/`, `UserSettings/` and `Assets/_Recovery/` are ignored.
