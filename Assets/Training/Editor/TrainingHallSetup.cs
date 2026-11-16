using System;
using System.Linq;
using System.Text;
using ConstructionVR.Training;
using NBM500U;
using NBM500U.EditorTools;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ConstructionVR.EditorTools
{
    // The welding hall: what the scenario adds to the scene the hall builder made. Everything is placed in
    // world coordinates around the welding booth (machine at x = -4.3, stand from x = -2.9, player in between).
    public static partial class TrainingSceneSetup
    {
        private const string MaterialFolder = "Assets/Training/Materials";
        private const float PlateTop = 0.875f;                              // top of the work plate on the stand

        private static void InstallHall(Scene scene, StringBuilder log)
        {
            NBM500UVRSetup.EnsureFolder(MaterialFolder);
            Material steel = Lit("M_T_Steel", new Color(0.55f, 0.58f, 0.62f), 0.85f, 0.45f);
            Material darkSteel = Lit("M_T_WorkPlate", new Color(0.25f, 0.26f, 0.28f), 0.7f, 0.3f);
            Material red = Lit("M_T_Red", new Color(0.75f, 0.06f, 0.05f), 0.1f, 0.5f);
            Material green = Lit("M_T_Green", new Color(0.1f, 0.45f, 0.18f), 0.1f, 0.4f);
            Material black = Lit("M_T_Rubber", new Color(0.03f, 0.03f, 0.035f), 0f, 0.25f);
            Material rag = Lit("M_T_Rag", new Color(0.55f, 0.42f, 0.2f), 0f, 0.05f);
            Material copper = Lit("M_T_Copper", new Color(0.85f, 0.45f, 0.22f), 0.95f, 0.6f);
            Material yellow = Lit("M_T_Yellow", new Color(0.95f, 0.75f, 0.08f), 0.1f, 0.4f);
            Material bead = Lit("M_T_Bead", new Color(0.62f, 0.66f, 0.72f), 0.9f, 0.35f, true);
            Material arc = Unlit("M_T_Arc", new Color(6f, 8f, 12f, 1f), false);
            Material marker = Unlit("M_T_Marker", new Color(1f, 0.78f, 0.2f, 1f), false);
            Material overlay = Unlit("M_T_MaskFilter", new Color(0f, 0f, 0f, 0.5f), true);
            Material sparks = Particle("M_T_Sparks", new Color(3f, 2f, 0.8f, 1f), true);
            Material flames = Particle("M_T_Flames", new Color(1.6f, 0.8f, 0.25f, 0.9f), true);
            Material spray = Particle("M_T_Spray", new Color(1f, 1f, 1f, 0.5f), false);
            AssetDatabase.SaveAssets();

            // parts of the machine and the cylinder the scenario works with
            Transform T(string name) => Find(scene, name) ?? throw new InvalidOperationException("Hall scene: '" + name + "' not found.");
            Transform machineRoot = T("NBM500U");
            foreach (string hidden in new[] { "Clamp_Cable", "Clamp_CableLug", "Clamp_JawPad_Lower", "Clamp_JawPad_Upper", "Clamp_LeverLower", "Clamp_LeverUpper", "Clamp_PivotPin", "Clamp_Spring" })
            {
                T(hidden).gameObject.SetActive(true);                      // an earlier install hid them
            }

            foreach (WeldTorch old in Object.FindObjectsByType<WeldTorch>(FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(old);
            }

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var director = root.AddComponent<HallDirector>();
            var mask = root.AddComponent<WeldingMask>();
            Set(mask, ("_overlayMaterial", overlay));

            var rig = Object.FindAnyObjectByType<OVRCameraRig>() ?? throw new InvalidOperationException("The scene has no OVRCameraRig.");
            AddLocomotion(rig, new Vector2(-11.5f, -26f), new Vector2(4.3f, 26f));

            // ---- work plate on the stand, with everything laid out on it
            Box(root, "Work Plate", new Vector3(-2.5f, PlateTop - 0.01f, -0.15f), new Vector3(0.8f, 0.02f, 1.5f), darkSteel);

            Transform baseTarget = Anchor(root, "Base Plate Target", new Vector3(-2.62f, PlateTop + 0.004f, 0.25f), Vector3.forward);
            Transform webTarget = Anchor(root, "Web Plate Target", new Vector3(-2.62f, PlateTop + 0.008f + 0.05f, 0.25f), Vector3.forward);
            SnapItem basePlate = Plate(root, "Plate Base 300x150x8", new Vector3(0.15f, 0.008f, 0.30f), new Vector3(-2.52f, PlateTop + 0.004f, -0.30f), Quaternion.identity,
                steel, "300 × 150 × 8", baseTarget, "base");
            SnapItem webPlate = Plate(root, "Plate Web 300x100x8", new Vector3(0.008f, 0.10f, 0.30f), new Vector3(-2.52f, PlateTop + 0.004f, -0.64f), Quaternion.Euler(0f, 0f, 90f),
                steel, "300 × 100 × 8", webTarget, "web");
            SnapItem thin = Plate(root, "Plate Thin 300x150x4", new Vector3(0.15f, 0.004f, 0.30f), new Vector3(-2.27f, PlateTop + 0.002f, -0.30f), Quaternion.identity,
                steel, "300 × 150 × 4", baseTarget, "wrong");
            SnapItem shortPlate = Plate(root, "Plate Short 200x100x8", new Vector3(0.008f, 0.10f, 0.20f), new Vector3(-2.27f, PlateTop + 0.004f, -0.64f), Quaternion.Euler(0f, 0f, 90f),
                steel, "200 × 100 × 8", webTarget, "wrong");

            var clamps = new SnapItem[2];
            for (int i = 0; i < 2; i++)
            {
                Transform target = Anchor(root, "Clamp Target " + (i + 1), new Vector3(-2.62f + 0.004f + 0.011f, PlateTop + 0.008f + 0.025f, 0.15f + i * 0.20f), Vector3.forward);
                GameObject clamp = Group(root, "Clamp " + (i + 1), new Vector3(-2.27f, PlateTop + 0.025f, 0.02f + i * 0.12f), Quaternion.identity);
                Box(clamp, "Body", clamp.transform.position, new Vector3(0.022f, 0.05f, 0.06f), red);
                Box(clamp, "Magnet", clamp.transform.position + new Vector3(-0.012f, 0f, 0f), new Vector3(0.004f, 0.046f, 0.05f), darkSteel);
                Grabbable g = MakeGrabbable(clamp, Vector3.zero, new Vector3(0.05f, 0.06f, 0.07f));
                clamps[i] = AddSnap(clamp, g, target, 0.1f, "clamp", false, null);
            }

            // the joint: seam in the corner on the welder's side (west) of the standing plate, run from north to south
            GameObject jointObject = Group(root, "Weld Joint", new Vector3(-2.62f - 0.004f, PlateTop + 0.008f, 0.40f), Quaternion.LookRotation(Vector3.back, Vector3.up));
            var joint = jointObject.AddComponent<WeldJoint>();
            Set(joint, ("_beadMaterial", bead), ("_markerMaterial", marker));

            // measuring tools
            GameObject squareObject = Group(root, "Square", new Vector3(-2.27f, PlateTop + 0.003f, 0.36f), Quaternion.Euler(90f, 0f, 0f));
            Box(squareObject, "Blade", squareObject.transform.TransformPoint(new Vector3(-0.06f, 0.012f, 0f)), new Vector3(0.12f, 0.024f, 0.003f), yellow, squareObject.transform.rotation);
            Box(squareObject, "Stock", squareObject.transform.TransformPoint(new Vector3(-0.008f, 0.05f, 0f)), new Vector3(0.016f, 0.10f, 0.008f), darkSteel, squareObject.transform.rotation);
            Transform squareProbe = Anchor(squareObject, "Probe", squareObject.transform.position, squareObject.transform.forward);
            TMP_Text squareText = Text3D(squareObject, "Readout", squareObject.transform.TransformPoint(new Vector3(-0.06f, 0.05f, -0.004f)), squareObject.transform.rotation,
                new Vector2(0.11f, 0.03f), string.Empty, new Color(1f, 0.85f, 0.3f));
            Grabbable squareGrab = MakeGrabbable(squareObject, new Vector3(-0.05f, 0.05f, 0f), new Vector3(0.14f, 0.12f, 0.04f));
            var square = squareObject.AddComponent<MeasureTool>();
            Set(square, ("_grabbable", squareGrab), ("_returnToRest", squareObject.GetComponent<NBMReturnToRest>()), ("_probe", squareProbe), ("_readout", squareText));
            SetFloat(square, "_reach", 0.06f);

            GameObject gaugeObject = Group(root, "Fillet Gauge", new Vector3(-2.27f, PlateTop + 0.003f, 0.54f), Quaternion.identity);
            Box(gaugeObject, "Plate", gaugeObject.transform.position, new Vector3(0.06f, 0.003f, 0.04f), steel);
            Transform gaugeProbe = Anchor(gaugeObject, "Probe", gaugeObject.transform.position + new Vector3(-0.03f, 0f, 0f), Vector3.forward);
            TMP_Text gaugeText = Text3D(gaugeObject, "Readout", gaugeObject.transform.position + new Vector3(0f, 0.0025f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.right),
                new Vector2(0.038f, 0.05f), string.Empty, new Color(0.05f, 0.05f, 0.08f));
            Grabbable gaugeGrab = MakeGrabbable(gaugeObject, Vector3.zero, new Vector3(0.07f, 0.03f, 0.05f));
            var gauge = gaugeObject.AddComponent<MeasureTool>();
            Set(gauge, ("_grabbable", gaugeGrab), ("_returnToRest", gaugeObject.GetComponent<NBMReturnToRest>()), ("_probe", gaugeProbe), ("_readout", gaugeText));
            SetFloat(gauge, "_reach", 0.05f);

            // ---- workplace: flammable rag, bin, extinguisher, extractor switch
            GameObject bin = Group(root, "Waste Bin", new Vector3(-5.25f, 0f, -0.55f), Quaternion.identity);
            Cylinder(bin, "Body", new Vector3(-5.25f, 0.24f, -0.55f), 0.38f, 0.48f, green);
            Transform binTarget = Anchor(root, "Bin Target", new Vector3(-5.25f, 0.5f, -0.55f), Vector3.forward);

            GameObject ragObject = Group(root, "Oily Rag", new Vector3(-2.84f, PlateTop + 0.012f, -0.02f), Quaternion.Euler(0f, 20f, 0f));
            Box(ragObject, "Cloth", ragObject.transform.position, new Vector3(0.2f, 0.024f, 0.15f), rag, ragObject.transform.rotation);
            Grabbable ragGrab = MakeGrabbable(ragObject, Vector3.zero, new Vector3(0.2f, 0.05f, 0.15f));
            SnapItem ragSnap = AddSnap(ragObject, ragGrab, binTarget, 0.4f, "rag", true, null);
            var fire = ragObject.AddComponent<FireHazard>();
            Set(fire, ("_flameMaterial", flames));

            GameObject wasteObject = Group(root, "Wire Scraps", new Vector3(-2.80f, PlateTop + 0.008f, 0.56f), Quaternion.Euler(0f, -15f, 0f));
            Box(wasteObject, "Scraps", wasteObject.transform.position, new Vector3(0.07f, 0.016f, 0.045f), steel, wasteObject.transform.rotation);
            Grabbable wasteGrab = MakeGrabbable(wasteObject, Vector3.zero, new Vector3(0.08f, 0.04f, 0.06f));
            SnapItem waste = AddSnap(wasteObject, wasteGrab, binTarget, 0.4f, "waste", true, null);

            GameObject extObject = Group(root, "Fire Extinguisher", new Vector3(-5.15f, 0f, 0.75f), Quaternion.identity);
            Cylinder(extObject, "Body", new Vector3(-5.15f, 0.22f, 0.75f), 0.13f, 0.40f, red);
            Box(extObject, "Valve", new Vector3(-5.15f, 0.45f, 0.75f), new Vector3(0.05f, 0.07f, 0.05f), darkSteel);
            Box(extObject, "Handle", new Vector3(-5.15f, 0.495f, 0.78f), new Vector3(0.025f, 0.012f, 0.12f), black);
            Box(extObject, "Nozzle", new Vector3(-5.15f, 0.44f, 0.83f), new Vector3(0.03f, 0.03f, 0.12f), black);
            Transform nozzle = Anchor(extObject, "Nozzle Tip", new Vector3(-5.15f, 0.44f, 0.9f), Vector3.forward);
            Grabbable extGrab = MakeGrabbable(extObject, new Vector3(0f, 0.25f, 0.02f), new Vector3(0.16f, 0.52f, 0.2f));
            var extinguisher = extObject.AddComponent<Extinguisher>();
            Set(extinguisher, ("_grabbable", extGrab), ("_nozzle", nozzle), ("_sprayMaterial", spray));

            GameObject switchBox = Group(root, "Extractor Switch", new Vector3(-2.975f, 0.80f, -0.62f), Quaternion.identity);
            Box(switchBox, "Box", switchBox.transform.position, new Vector3(0.05f, 0.13f, 0.09f), darkSteel);
            GameObject extractorLever = Box(switchBox, "Lever", switchBox.transform.position + new Vector3(-0.032f, 0f, 0f), new Vector3(0.016f, 0.05f, 0.03f), green);
            var extractor = switchBox.AddComponent<TouchSwitch>();
            Set(extractor, ("_lever", extractorLever.transform));
            SetVector(extractor, "_leverAxis", Vector3.forward);

            // ---- machine: mains switch, mode knobs, displays, wire
            Transform lever = T("PS_MainSwitch_Lever");
            GameObject mainSwitchObject = Group(root, "Main Switch", lever.GetComponent<Renderer>().bounds.center, Quaternion.identity);
            var mainSwitch = mainSwitchObject.AddComponent<TouchSwitch>();
            Set(mainSwitch, ("_lever", lever));
            SetVector(mainSwitch, "_leverAxis", Vector3.right);

            NBMKnobTwistTransformer voltageKnob = T("WF_Knob_Voltage").GetComponent<NBMKnobTwistTransformer>();
            NBMKnobTwistTransformer currentKnob = T("WF_Knob_Current").GetComponent<NBMKnobTwistTransformer>();
            NBMKnobTwistTransformer speedKnob = T("PS_EncoderKnob").GetComponent<NBMKnobTwistTransformer>();
            NBMKnobTwistTransformer tensionFront = T("WF_Drive_TensionKnob_Front").GetComponent<NBMKnobTwistTransformer>();
            NBMKnobTwistTransformer tensionRear = T("WF_Drive_TensionKnob_Rear").GetComponent<NBMKnobTwistTransformer>();

            var displays = new TMP_Text[4];
            string[] displayNames = { "WF_Display_Voltage", "PS_Display_Voltage", "WF_Display_Current", "PS_Display_Current" };
            for (int i = 0; i < displayNames.Length; i++)
            {
                Transform d = T(displayNames[i]);
                Bounds b = d.GetComponent<Renderer>().bounds;
                Vector3 inwards = d.up;                                     // the display windows face along -up of their mesh
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, inwards).normalized;
                displays[i] = Text3D(root, "Display " + displayNames[i], b.center - inwards * 0.003f, Quaternion.LookRotation(inwards, up), new Vector2(0.042f, 0.018f),
                    string.Empty, new Color(1f, 0.25f, 0.12f));
            }

            Transform coil = T("WF_Spool_WireCoil");
            Transform tip = T("Torch_ContactTip");
            Transform torch = T("Torch_Grabbable");
            Vector3 wireAxis = (tip.GetComponent<Renderer>().bounds.center - T("Torch_GasNozzle").GetComponent<Renderer>().bounds.center).normalized;
            GameObject wireEnd = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wireEnd.name = "Training_WireEnd";
            Object.DestroyImmediate(wireEnd.GetComponent<Collider>());
            wireEnd.transform.SetParent(torch, true);
            wireEnd.transform.position = tip.position + wireAxis * 0.014f;
            wireEnd.transform.rotation = Quaternion.FromToRotation(Vector3.up, wireAxis);
            wireEnd.transform.localScale = new Vector3(0.0012f, 0.006f, 0.0012f);
            wireEnd.GetComponent<Renderer>().sharedMaterial = copper;

            GameObject machineObject = Group(root, "Machine", machineRoot.position, Quaternion.identity);
            var machine = machineObject.AddComponent<WeldMachine>();
            Set(machine, ("_mainSwitch", mainSwitch), ("_fan", T("PS_Fan_Blades")), ("_voltageKnob", voltageKnob), ("_currentKnob", currentKnob), ("_speedKnob", speedKnob),
                ("_tensionFront", tensionFront), ("_tensionRear", tensionRear), ("_wireInchButton", T("PS_Btn_WireInch_Surface").GetComponent<NBMButton>()),
                ("_spoolWire", coil.gameObject), ("_wireAtTip", wireEnd));
            SetFloat(machine, "_voltageSign", KnobSign(voltageKnob, Vector3.back));
            SetFloat(machine, "_currentSign", KnobSign(currentKnob, Vector3.back));
            SetFloat(machine, "_speedSign", KnobSign(speedKnob, Vector3.back));
            SetFloat(machine, "_tensionSign", KnobSign(tensionFront, Vector3.up));
            SetVector(machine, "_fanAxis", T("PS_Fan_Blades").InverseTransformDirection(Vector3.forward));
            SetArray(machine, "_voltageDisplays", displays[0], displays[1]);
            SetArray(machine, "_currentDisplays", displays[2], displays[3]);

            // a full spool waiting on the tool cabinet
            Transform spool = T("WF_WireSpool");
            Transform hub = Anchor(root, "Spool Hub Target", spool.position, spool.forward);
            hub.rotation = spool.rotation;
            GameObject newSpool = Group(root, "New Wire Spool", new Vector3(-5.6f, 1.16f, 1.75f), spool.rotation);
            foreach (Transform part in spool)
            {
                GameObject copy = Object.Instantiate(part.gameObject, newSpool.transform);
                copy.name = part.name;
                copy.transform.localPosition = part.localPosition;
                copy.transform.localRotation = part.localRotation;
                copy.transform.localScale = part.localScale;
                copy.SetActive(true);
            }

            Bounds spoolBounds = WorldBounds(newSpool);
            Grabbable spoolGrab = MakeGrabbable(newSpool, newSpool.transform.InverseTransformPoint(spoolBounds.center), new Vector3(0.14f, 0.3f, 0.3f), true);
            SnapItem spoolSnap = AddSnap(newSpool, spoolGrab, hub, 0.22f, "spool", true, null);

            // earth clamp: a copy that can be carried, on a cable that follows it
            Transform plug = T("Clamp_DinsePlug");
            Transform pivot = T("Clamp_PivotPin");
            GameObject clampObject = Group(root, "Earth Clamp", pivot.GetComponent<Renderer>().bounds.center, Quaternion.identity);
            Vector3 lugCentre = T("Clamp_CableLug").GetComponent<Renderer>().bounds.center;
            foreach (string part in new[] { "Clamp_CableLug", "Clamp_JawPad_Lower", "Clamp_JawPad_Upper", "Clamp_LeverLower", "Clamp_LeverUpper", "Clamp_PivotPin", "Clamp_Spring" })
            {
                Transform original = T(part);
                GameObject copy = Object.Instantiate(original.gameObject, clampObject.transform, true);
                copy.name = part;
                original.gameObject.SetActive(false);
            }

            T("Clamp_Cable").gameObject.SetActive(false);
            Bounds clampBounds = WorldBounds(clampObject);
            Grabbable clampGrab = MakeGrabbable(clampObject, clampObject.transform.InverseTransformPoint(clampBounds.center), Vector3.Max(clampBounds.size, new Vector3(0.08f, 0.06f, 0.06f)));
            Transform earthTarget = Anchor(root, "Earth Clamp Target", new Vector3(-2.86f, PlateTop + 0.03f, 0.56f), Vector3.forward);
            earthTarget.rotation = Quaternion.identity;
            SnapItem earthSnap = AddSnap(clampObject, clampGrab, earthTarget, 0.18f, "earth", false, null);
            Transform cableEnd = Anchor(clampObject, "Cable End", lugCentre, Vector3.forward);
            GameObject cable = Group(root, "Earth Cable", Vector3.zero, Quaternion.identity);
            var sag = cable.AddComponent<SagLine>();
            Set(sag, ("_from", plug), ("_to", cableEnd), ("_material", black));
            SetFloat(sag, "_slack", 0.55f);
            SetFloat(sag, "_width", 0.016f);

            // damaged mains cable: bare copper where the sheath is cut
            Transform mains = T("Cable_Mains");
            Vector3 cut = NearestVertex(mains, mains.GetComponent<Renderer>().bounds.center + new Vector3(0f, 0.1f, 0f));
            GameObject damage = Group(root, "Cable Damage", cut, Quaternion.identity);
            Sphere(damage, "Scorch", cut, 0.05f, black);
            Sphere(damage, "Copper", cut + new Vector3(0f, 0.006f, 0f), 0.034f, copper);

            // cylinder chain: from one post across the front of the cylinder to the other
            Transform cylinderBody = T("Gas_Cylinder_Body");
            Bounds cyl = cylinderBody.GetComponent<Renderer>().bounds;
            float chainZ = cyl.min.z - 0.03f, chainY = 1.0f;
            Transform chainAnchor = Anchor(root, "Chain Anchor", new Vector3(cyl.min.x - 0.05f, chainY, chainZ), Vector3.forward);
            Transform chainTarget = Anchor(root, "Chain Hook Target", new Vector3(cyl.max.x + 0.05f, chainY, chainZ), Vector3.forward);
            chainTarget.rotation = Quaternion.identity;
            Box(root, "Chain Post A", chainAnchor.position + new Vector3(0f, -0.5f, 0.02f), new Vector3(0.03f, 1.06f, 0.03f), darkSteel);
            Box(root, "Chain Post B", chainTarget.position + new Vector3(0f, -0.5f, 0.02f), new Vector3(0.03f, 1.06f, 0.03f), darkSteel);
            GameObject hookObject = Group(root, "Chain Hook", chainAnchor.position + new Vector3(0.02f, -0.36f, -0.01f), Quaternion.identity);
            Box(hookObject, "Hook", hookObject.transform.position, new Vector3(0.035f, 0.045f, 0.012f), steel);
            Grabbable hookGrab = MakeGrabbable(hookObject, Vector3.zero, new Vector3(0.07f, 0.07f, 0.05f));
            SnapItem hookSnap = AddSnap(hookObject, hookGrab, chainTarget, 0.15f, "chain", false, null);
            GameObject chain = Group(root, "Chain", Vector3.zero, Quaternion.identity);
            var chainLine = chain.AddComponent<SagLine>();
            Set(chainLine, ("_from", chainAnchor), ("_to", hookObject.transform), ("_material", steel));
            SetFloat(chainLine, "_slack", 0.03f);
            SetFloat(chainLine, "_width", 0.012f);

            // ---- things to judge
            InspectPoint inspectFire = Inspect(root, "area.fire", StageId.Ppe, new Vector3(-5.15f, 0.95f, 0.75f), "inspect.q.fire", null, null, null, "missed.generic", Severity.Medium);
            InspectPoint inspectArea = Inspect(root, "area.clean", StageId.Ppe, new Vector3(-2.80f, PlateTop + 0.45f, -0.05f), "inspect.q.area", "inspect.area.ok", "inspect.area.fault",
                "found.area", "missed.area", Severity.High);
            InspectPoint inspectMains = Inspect(root, "el.mains", StageId.Electrical, cut + new Vector3(0f, 0.45f, 0f), "inspect.q.mains", null, null, "found.mains", "missed.mains", Severity.Medium);
            InspectPoint inspectTorch = Inspect(root, "el.torch", StageId.Electrical, T("Torch_Cable").GetComponent<Renderer>().bounds.center + new Vector3(0.25f, 0.15f, -0.25f),
                "inspect.q.torch", null, null, null, "missed.generic", Severity.Medium);
            InspectPoint inspectPanel = Inspect(root, "pw.check", StageId.PowerOn, T("PS_Display_Current").GetComponent<Renderer>().bounds.center + new Vector3(0.38f, 0.12f, -0.1f),
                "inspect.q.panel", null, null, null, "missed.generic", Severity.Medium);
            InspectPoint inspectSpool = Inspect(root, "wire.spool", StageId.Wire, spool.position + new Vector3(-0.3f, 0.36f, 0f), "inspect.q.spool", "inspect.spool.ok", "inspect.spool.fault",
                "found.spool", "missed.spool", Severity.Medium);
            InspectPoint inspectChain = Inspect(root, "cyl.chain", StageId.CylinderSafety, new Vector3(cyl.center.x, 1.3f, cyl.min.z - 0.25f), "inspect.q.chain", "inspect.chain.ok",
                "inspect.chain.fault", "found.chain", "missed.chain", Severity.Critical);
            InspectPoint inspectRegulator = Inspect(root, "cyl.reg", StageId.CylinderSafety, T("Gas_Reg_Body").GetComponent<Renderer>().bounds.center + new Vector3(-0.2f, 0.3f, -0.2f),
                "inspect.q.reg", null, null, null, "missed.generic", Severity.Medium);
            InspectPoint inspectPressure = Inspect(root, "gas.pressure", StageId.Gas, T("Gas_Gauge_Dial").GetComponent<Renderer>().bounds.center + new Vector3(0.05f, 0.28f, -0.2f),
                "inspect.q.pressure", "inspect.pressure.ok", "inspect.pressure.fault", "found.pressure", "missed.pressure", Severity.Medium);

            // ---- torch
            var weldTorch = torch.gameObject.AddComponent<WeldTorch>();
            Set(weldTorch, ("_grabbable", torch.GetComponent<Grabbable>()), ("_returnToRest", torch.GetComponent<NBMReturnToRest>()), ("_tip", tip), ("_machine", machine),
                ("_gas", Object.FindAnyObjectByType<NBMGasCylinder>()), ("_joint", joint), ("_arcMaterial", arc), ("_sparkMaterial", sparks));
            SetVector(weldTorch, "_tipAxis", tip.InverseTransformDirection(wireAxis));
            PrefabUtility.RecordPrefabInstancePropertyModifications(weldTorch);

            Transform qcAnchor = Anchor(root, "Check Sheet Anchor", new Vector3(-2.2f, 1.45f, 0.25f), Vector3.right);
            Transform reportAnchor = Anchor(root, "Report Anchor", new Vector3(-3.4f, 1.5f, 0.2f), Vector3.right);

            Set(director, ("_machine", machine), ("_gas", Object.FindAnyObjectByType<NBMGasCylinder>()), ("_gasValve", T("Gas_Valve_Handwheel").GetComponent<NBMKnobTwistTransformer>()),
                ("_spoolCover", T("WF_SpoolCover")), ("_feederDoor", T("WF_Door")), ("_inspectFire", inspectFire), ("_inspectArea", inspectArea), ("_extractor", extractor),
                ("_rag", ragSnap), ("_fire", fire), ("_extinguisher", extinguisher), ("_inspectMains", inspectMains), ("_cableDamage", damage), ("_inspectTorch", inspectTorch),
                ("_earthClamp", earthSnap), ("_inspectPanel", inspectPanel), ("_inspectSpool", inspectSpool), ("_newSpool", spoolSnap), ("_inspectChain", inspectChain),
                ("_chainHook", hookSnap), ("_inspectRegulator", inspectRegulator), ("_inspectPressure", inspectPressure), ("_basePlate", basePlate), ("_webPlate", webPlate),
                ("_square", square), ("_gauge", gauge), ("_torch", weldTorch), ("_joint", joint), ("_mask", mask), ("_waste", waste), ("_qcPanelAnchor", qcAnchor),
                ("_reportAnchor", reportAnchor));
            SetArray(director, "_wrongPlates", thin, shortPlate);
            SetArray(director, "_clamps", clamps[0], clamps[1]);

            log.AppendLine($"  hall: joint at {jointObject.transform.position}, mains switch at {mainSwitchObject.transform.position}, cable cut at {cut}, " +
                           $"knob signs V {KnobSign(voltageKnob, Vector3.back)} A {KnobSign(currentKnob, Vector3.back)} speed {KnobSign(speedKnob, Vector3.back)} tension {KnobSign(tensionFront, Vector3.up)}");
        }

        // ------------------------------------------------------------------ builders

        private static GameObject Group(GameObject parent, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        private static Transform Anchor(GameObject parent, string name, Vector3 position, Vector3 forward)
        {
            return Group(parent, name, position, Quaternion.LookRotation(forward, Vector3.up)).transform;
        }

        private static GameObject Primitive(PrimitiveType type, GameObject parent, string name, Vector3 position, Vector3 size, Material material, Quaternion rotation)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, true);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private static GameObject Box(GameObject parent, string name, Vector3 centre, Vector3 size, Material material, Quaternion? rotation = null)
        {
            return Primitive(PrimitiveType.Cube, parent, name, centre, size, material, rotation ?? Quaternion.identity);
        }

        private static GameObject Cylinder(GameObject parent, string name, Vector3 centre, float diameter, float height, Material material)
        {
            return Primitive(PrimitiveType.Cylinder, parent, name, centre, new Vector3(diameter, height / 2f, diameter), material, Quaternion.identity);
        }

        private static GameObject Sphere(GameObject parent, string name, Vector3 centre, float diameter, Material material)
        {
            return Primitive(PrimitiveType.Sphere, parent, name, centre, Vector3.one * diameter, material, Quaternion.identity);
        }

        private static SnapItem Plate(GameObject root, string name, Vector3 size, Vector3 position, Quaternion rotation, Material material, string label, Transform target, string kind)
        {
            GameObject plate = Group(root, name, position, rotation);
            Box(plate, "Steel", position, size, material, rotation);
            // the size is written on the broad face that looks up while the plate lies on the table
            bool standing = size.x < size.y;
            Vector3 normal = plate.transform.TransformDirection(standing ? Vector3.right : Vector3.up);
            float offset = (standing ? size.x : size.y) / 2f + 0.0008f;
            // on a lying plate the size is written near one long edge, clear of where another plate will stand on it
            Vector3 aside = standing ? Vector3.zero : plate.transform.right * (size.x * 0.3f);
            Text3D(plate, "Label", position + normal * offset + aside, Quaternion.LookRotation(-normal, Vector3.right), new Vector2(0.2f, standing ? 0.05f : 0.032f), label,
                new Color(0.95f, 0.95f, 0.9f));
            Grabbable grabbable = MakeGrabbable(plate, Vector3.zero, Vector3.Max(size, Vector3.one * 0.03f));
            return AddSnap(plate, grabbable, target, 0.14f, kind, false, null);
        }

        /// <summary>Collider, kinematic body, grab interactables and "go back where you lay" for an object.</summary>
        private static Grabbable MakeGrabbable(GameObject go, Vector3 colliderCentre, Vector3 colliderSize, bool twoHands = false)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = colliderCentre;
            box.size = colliderSize;
            Rigidbody body = NBM500UVRSetup.AddKinematicBody(go);
            var free = go.AddComponent<GrabFreeTransformer>();
            Grabbable grabbable = NBM500UVRSetup.AddGrabbable(go, body, free, twoHands ? free : null);
            NBM500UVRSetup.AddGrabInteractables(go, body, grabbable, null);
            var rest = go.AddComponent<NBMReturnToRest>();
            Set(rest, ("_grabbable", grabbable));
            return grabbable;
        }

        private static SnapItem AddSnap(GameObject go, Grabbable grabbable, Transform target, float radius, string kind, bool hide, Transform snapPoint)
        {
            var snap = go.AddComponent<SnapItem>();
            Set(snap, ("_grabbable", grabbable), ("_returnToRest", go.GetComponent<NBMReturnToRest>()), ("_target", target), ("_snapPoint", snapPoint));
            SetFloat(snap, "_radius", radius);
            using (var so = new SerializedObject(snap))
            {
                so.FindProperty("_hideWhenSnapped").boolValue = hide;
                so.FindProperty("_kind").stringValue = kind;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return snap;
        }

        private static InspectPoint Inspect(GameObject root, string step, StageId stage, Vector3 position, string question, string ok, string fault, string found, string missed,
            Severity severity)
        {
            GameObject go = Group(root, "Inspect " + step, position, Quaternion.identity);
            var point = go.AddComponent<InspectPoint>();
            using (var so = new SerializedObject(point))
            {
                so.FindProperty("_stepId").stringValue = step;
                so.FindProperty("_stage").intValue = (int)stage;
                so.FindProperty("_questionKey").stringValue = question;
                if (ok != null) so.FindProperty("_okKey").stringValue = ok;
                if (fault != null) so.FindProperty("_faultKey").stringValue = fault;
                so.FindProperty("_foundKey").stringValue = found ?? "found.generic";
                so.FindProperty("_missedKey").stringValue = missed;
                so.FindProperty("_missedSeverity").intValue = (int)severity;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return point;
        }

        private static TMP_Text Text3D(GameObject parent, string name, Vector3 position, Quaternion rotation, Vector2 size, string text, Color color)
        {
            GameObject go = Group(parent, name, position, rotation);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.02f;
            tmp.fontSizeMax = 2f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }

        /// <summary>+1 when turning the knob clockwise (seen from where its face points) raises its angle, else -1.</summary>
        private static float KnobSign(NBMKnobTwistTransformer knob, Vector3 faceNormal)
        {
            Vector3 axis = knob.transform.TransformDirection(NBM500UVRSetup.AxisVector((int)knob.Axis));
            return Vector3.Dot(axis, faceNormal) > 0f ? -1f : 1f;
        }

        private static Vector3 NearestVertex(Transform meshObject, Vector3 world)
        {
            Mesh mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            Vector3 best = meshObject.position;
            float bestDistance = float.MaxValue;
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 p = meshObject.TransformPoint(v);
                float d = (p - world).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

            return best;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                b.Encapsulate(r.bounds);
            }

            return b;
        }

        // ------------------------------------------------------------------ serialized fields

        private static void Set(Object target, params (string field, Object value)[] values)
        {
            using var so = new SerializedObject(target);
            foreach ((string field, Object value) in values)
            {
                SerializedProperty property = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no field {field}");
                property.objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            using var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(Object target, string field, Vector3 value)
        {
            using var so = new SerializedObject(target);
            so.FindProperty(field).vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, params Object[] values)
        {
            using var so = new SerializedObject(target);
            SerializedProperty array = so.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ materials

        private static Material Asset(string name, string shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader) ?? throw new InvalidOperationException("Shader not found: " + shader));
                AssetDatabase.CreateAsset(m, path);
            }

            return m;
        }

        private static Material Lit(string name, Color color, float metallic, float smoothness, bool emissive = false)
        {
            Material m = Asset(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Unlit(string name, Color color, bool transparent)
        {
            Material m = Asset(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            if (transparent)
            {
                Transparent(m, false);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Particle(string name, Color color, bool additive)
        {
            Material m = Asset(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetColor("_BaseColor", color);
            Transparent(m, additive);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Transparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
