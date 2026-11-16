using System;
using System.Collections.Generic;

namespace ConstructionVR.Training
{
    /// <summary>The stages of the practice, in the order of the specification (0 .. 12).</summary>
    public enum StageId
    {
        Login = 0, Journal, Ppe, Electrical, PowerOn, Wire, CylinderSafety, Gas, Mode, Parts, Measure, Tack, Weld, Quality, Cleanup,
    }

    public enum ScoreCategory { Safety = 0, Technique = 1, Quality = 2, Time = 3 }

    public enum Severity { Info = 0, Low, Medium, High, Critical }

    public sealed class StepDef
    {
        public readonly string Id;
        public readonly bool Optional;      // only needed in some sessions (e.g. "fit a new spool" when the spool is empty)

        public StepDef(string id, bool optional = false)
        {
            Id = id;
            Optional = optional;
        }

        /// <summary>Text key of the step: "step." + id.</summary>
        public string TextKey => "step." + Id;
    }

    public sealed class StageDef
    {
        public readonly StageId Id;
        public readonly string Number;      // as printed in the specification: "3.1"
        public readonly StepDef[] Steps;
        public readonly bool InHall;        // done in the welding hall (the others in the engineers' room)

        public StageDef(StageId id, string number, bool inHall, params StepDef[] steps)
        {
            Id = id;
            Number = number;
            InHall = inHall;
            Steps = steps;
        }

        public string TitleKey => "stage." + Id + ".title";
        public string GoalKey => "stage." + Id + ".goal";
    }

    /// <summary>
    /// The practice "semi-automatic welding in CO2, T joint" as data: stages in strict order, each a list of steps.
    /// A stage opens when the one before it is complete; the scene objects report steps by their id.
    /// </summary>
    public static class Scenario
    {
        public const string PracticeId = "mag_co2_t_joint";

        private static StepDef S(string id) => new StepDef(id);
        private static StepDef O(string id) => new StepDef(id, true);

        public static readonly StageDef[] Stages =
        {
            new StageDef(StageId.Login, "0", false, S("login.enter"), S("login.practice"), S("login.brief")),
            new StageDef(StageId.Journal, "1", false, S("journal.read"), S("journal.sign")),
            new StageDef(StageId.Ppe, "2", false, S("ppe.wear"), S("ppe.hall"), S("area.fire"), S("area.vent"), S("area.clean"), O("area.remove")),
            new StageDef(StageId.Electrical, "3.1", true, S("el.mains"), S("el.torch"), S("el.ground")),
            new StageDef(StageId.PowerOn, "3.2", true, S("pw.switch"), S("pw.check")),
            new StageDef(StageId.Wire, "4", true, S("wire.cover"), S("wire.spool"), O("wire.replace"), S("wire.tension"), S("wire.feed"), S("wire.close")),
            new StageDef(StageId.CylinderSafety, "5.1", true, S("cyl.chain"), O("cyl.fasten"), S("cyl.reg")),
            new StageDef(StageId.Gas, "5.2", true, S("gas.open"), S("gas.pressure"), O("gas.swap"), S("gas.flow")),
            new StageDef(StageId.Mode, "6", true, S("mode.voltage"), S("mode.current"), S("mode.speed"), S("mode.confirm")),
            new StageDef(StageId.Parts, "7", true, S("parts.pick"), S("parts.place"), S("parts.clamp")),
            new StageDef(StageId.Measure, "8", true, S("meas.measure"), O("meas.fix"), S("meas.confirm")),
            new StageDef(StageId.Tack, "9", true, S("tack.mask"), S("tack.points"), S("tack.measure")),
            new StageDef(StageId.Weld, "10", true, S("weld.mask"), S("weld.arc"), S("weld.seam"), S("weld.rest")),
            new StageDef(StageId.Quality, "11", true, S("qc.visual"), S("qc.measure"), S("qc.record")),
            new StageDef(StageId.Cleanup, "12", true, S("cl.power"), S("cl.gas"), S("cl.tools"), S("cl.waste")),
        };

        private static Dictionary<string, (int stage, int step)> s_index;

        /// <summary>Finds the stage and step of a step id; false when the id is unknown.</summary>
        public static bool Find(string stepId, out int stage, out int step)
        {
            if (s_index == null)
            {
                s_index = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
                for (int i = 0; i < Stages.Length; i++)
                {
                    for (int j = 0; j < Stages[i].Steps.Length; j++)
                    {
                        s_index[Stages[i].Steps[j].Id] = (i, j);
                    }
                }
            }

            if (stepId != null && s_index.TryGetValue(stepId, out var found))
            {
                stage = found.stage;
                step = found.step;
                return true;
            }

            stage = step = -1;
            return false;
        }

        public static StageDef Stage(StageId id) => Stages[(int)id];
    }

    /// <summary>Faults planted in one session. The learner is expected to find them.</summary>
    [Serializable]
    public class ScenarioVariant
    {
        public bool cableDamaged;
        public bool cylinderUnchained;
        public bool spoolEmpty;
        public bool lowPressure;
        public bool misaligned;
        public int seed;

        public static ScenarioVariant Roll(TrainingConfig config)
        {
            int seed = config.fixedSeed != 0 ? config.fixedSeed : Environment.TickCount;
            var random = new Random(seed);
            var v = new ScenarioVariant { seed = seed };
            v.cableDamaged = random.NextDouble() < config.chanceCableDamaged;
            v.cylinderUnchained = random.NextDouble() < config.chanceCylinderUnchained;
            v.spoolEmpty = random.NextDouble() < config.chanceSpoolEmpty;
            v.lowPressure = random.NextDouble() < config.chanceLowPressure;
            v.misaligned = random.NextDouble() < config.chanceMisaligned;
            return v;
        }

        public override string ToString() =>
            $"cable={(cableDamaged ? 1 : 0)} chain={(cylinderUnchained ? 1 : 0)} spool={(spoolEmpty ? 1 : 0)} pressure={(lowPressure ? 1 : 0)} fit={(misaligned ? 1 : 0)}";
    }
}
