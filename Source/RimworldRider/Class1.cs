using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Sound;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace RimworldRider
{
    public class RimworldRiderMod : Mod
    {
        public RimworldRiderMod(ModContentPack content) : base(content)
        {
            GetSettings<Settings>();
            Version version = typeof(RimworldRiderMod).Assembly.GetName().Version;
            LogMessage($"Kamen Rider Build - Hazard Level 系统已加载 v{version}");
        }

        public override string SettingsCategory()
        {
            return "Kamen Rider Build - Hazard Level";
        }

        public static void LogMessage(string message)
        {
            Log.Message("[RimworldRider] " + message);
        }

        public static void LogError(string message)
        {
            Log.Error("[RimworldRider] " + message);
        }
    }

    public class Settings : ModSettings
    {
        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}

namespace RimworldRider.HazardLevel
{
    [StaticConstructorOnStartup]
    public class StartUp
    {
        static StartUp()
        {
            RimworldRiderMod.LogMessage("Harmony 初始化完成");
            var harmony = new Harmony("rimworldrider.hazardlevel");
            harmony.PatchAll();
        }
    }

    public class CompProperties_HazardLevel : HediffCompProperties
    {
        public List<float> xpRequirements;

        public List<HazardLevelStatModifiers> levelStatModifiers;

        public CompProperties_HazardLevel()
        {
            compClass = typeof(HediffComp_HazardLevel);
        }
    }

    [DefOf]
    public static class HazardLevelDefOf
    {
        public static HediffDef HazardLevel;

        static HazardLevelDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HazardLevelDefOf));
        }
    }

    public class HazardLevelStatModifiers
    {
        public float meleeDamageFactor = 1f;

        public float moveSpeedFactor = 1f;

        public float armorSharpOffset = 0f;

        public float armorBluntOffset = 0f;

        public float meleeCooldownFactor = 1f;
    }

    public static class HazardLevelUtility
    {
        public static HediffComp_HazardLevel GetHazardLevelComp(Pawn pawn)
        {
            if (pawn == null || pawn.health == null)
            {
                return null;
            }
            return pawn.health.hediffSet.GetFirstHediffOfDef(HazardLevelDefOf.HazardLevel)?.TryGetComp<HediffComp_HazardLevel>();
        }

        public static float EstimateCombatPower(Pawn pawn)
        {
            if (pawn.skills == null)
            {
                return 1f;
            }
            float num = 1f;
            SkillRecord skill = pawn.skills.GetSkill(SkillDefOf.Melee);
            if (skill != null)
            {
                num += (float)skill.Level * 1.5f;
            }
            SkillRecord skill2 = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (skill2 != null)
            {
                num += (float)skill2.Level * 0.5f;
            }
            return num;
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs))]
    public class Patch_Pawn_MeleeVerbs
    {
        [HarmonyPatch(nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
        [HarmonyPostfix]
        public static void Postfix_TryMeleeAttack(ref bool __result, Pawn_MeleeVerbs __instance, Thing target, Verb verbToUse = null, bool surpriseAttack = false)
        {
            try
            {
                if (__result)
                {
                    Pawn value = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                    if (value == null || !value.RaceProps.Humanlike)
                    {
                        return;
                    }
                    Pawn pawn = target as Pawn;
                    if (pawn != null)
                    {
                        RimworldRider.HazardLevel.HediffComp_HazardLevel hazardLevelComp = RimworldRider.HazardLevel.HazardLevelUtility.GetHazardLevelComp(value);
                        if (hazardLevelComp != null)
                        {
                            float amount = (pawn.RaceProps.Humanlike ? 1.5f : 1f) + HazardLevelUtility.EstimateCombatPower(pawn) * 0.5f;
                            hazardLevelComp.AddCombatXP(amount);
                        }
                    }
                }
            }catch(Exception ex)
            {
                RimworldRiderMod.LogError("近战Hook: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury))]
    public class Patch_DamageWorker
    {
        [HarmonyPatch("ApplyToPawn", new Type[] { typeof(DamageInfo), typeof(Pawn)})]
        [HarmonyPostfix]
        public static void Postfix_ApplyToPawn(DamageInfo dinfo, Pawn pawn)
        {
            try
            {
                if (pawn.RaceProps.Humanlike)
                {
                    HediffComp_HazardLevel hazardLevelComp = HazardLevelUtility.GetHazardLevelComp(pawn);
                    if(hazardLevelComp != null && dinfo.Amount > 0f)
                    {
                        hazardLevelComp.AddCombatXP(dinfo.Amount * 0.15f);
                    }
                }
            }
            catch (Exception ex)
            {
                RimworldRiderMod.LogError("伤害Hook: " + ex.Message);
            }
        }
    }

    public class HediffComp_HazardLevel : HediffComp
    {
        public float hazardLevel = 1f;

        private float combatXP = 0f;

        public CompProperties_HazardLevel Props => (CompProperties_HazardLevel)props;

        public float CombatXP => combatXP;

        public float XPToNextLevel
        {
            get
            {
                int num = (int)(hazardLevel - 1f);
                if (num < 0) num = 0;
                if (num >= 6) return float.MaxValue;
                if (Props.xpRequirements != null && num < Props.xpRequirements.Count)
                    return Props.xpRequirements[num];
                return DefaultXPRequirements(num);
            }
        }

        public bool IsMaxLevel => hazardLevel >= 7f;

        public float LevelProgress
        {
            get
            {
                if (IsMaxLevel) return 1f;
                float xpNeeded = XPToNextLevel;
                return (xpNeeded > 0f) ? (combatXP / xpNeeded) : 1f;
            }
        }

        public override string CompDescriptionExtra
        {
            get
            {
                string expInfo = (!IsMaxLevel)
                ? $"战斗经验: {combatXP:F0} / {XPToNextLevel:F0} ({LevelProgress * 100f:F1}%)"
                : "已满级 / Max level";
                List<string> StageDes = new List<string> { "初始状态，尚未展现出特殊能力",
                                                           "基础适应阶段。身体开始适应星云气体的能量",
                                                           "稳定阶段。能够熟练运用基础形态的战斗能力",
                                                           "强化阶段。体能得到显著增强，能承受更强的战斗冲击",
                                                           "精锐阶段。已具备较高的战斗素质，足以驾驭更强大的形态",
                                                           "精英阶段。肉体与能量的融合达到新高度，战斗力大幅提升",
                                                           "最高等级！骑士与星云气体的完美同步，爆发出极限战斗力"};
                return string.Format("\n危险等级: {0:F1}\n{1}\n{2}", hazardLevel, expInfo, StageDes[(int)(hazardLevel - 1f)]);
            }
        }

        private float LevelToSeverity(float level)
        {
            return (level - 1f) / 6f;
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            parent.Severity = LevelToSeverity(hazardLevel);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref hazardLevel, "hazardLevel", 1f);
            Scribe_Values.Look(ref combatXP, "combatXP", 0f);
            // 如果是加载模式，进行数据校验
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 确保等级在合理范围内（1~7）
                if (hazardLevel < 1f) hazardLevel = 1f;
                if (hazardLevel > 7f) hazardLevel = 7f;
                // 更新健康状态的严重度
                parent.Severity = LevelToSeverity(hazardLevel);
            }
        }

        public void AddCombatXP(float amount)
        {
            if (IsMaxLevel || amount <= 0f) return;
            combatXP += amount;
            CheckLevelUp();
        }

        private void CheckLevelUp()
        {
            while(!IsMaxLevel && combatXP >= XPToNextLevel)
            {
                combatXP -= XPToNextLevel;
                hazardLevel += 1f;
                if (hazardLevel > 7f) hazardLevel = 7f;
                parent.Severity = LevelToSeverity(hazardLevel);
                OnLevelUp();
            }
        }

        private void OnLevelUp()
        {
            Pawn pawn = parent.pawn;
            if(pawn != null)
            {
                string msg = $"{pawn.NameShortColored} 的危险等级提升至 {hazardLevel:F1}！";
                Messages.Message(msg, pawn, MessageTypeDefOf.PositiveEvent);
                if (pawn.Spawned)
                {
                    string text = $"HL {hazardLevel:F1}";
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, text, 2.5f);
                }
                RimworldRiderMod.LogMessage($"{pawn.LabelShort} HL {hazardLevel:F1}");
            }
        }

        public static float DefaultXPRequirements(int idx)
        {
            float[] array = new float[6] { 100f, 250f, 500f, 1000f, 2000f, 4000f };
            if (idx >= 0 && idx < array.Length)
            {
                return array[idx];
            }
            return float.MaxValue;
        }
    }

    public class Hediff_HazardLevel : HediffWithComps
    {
        public override bool ShouldRemove => false;
    }
}

namespace RimworldRider.Fullbottle
{
    public class CompProperties_Fullbottle : CompProperties
    {
        public string bottleType;
        public HediffDef buffHediffDef;
        public SoundDef bottleSound;

        public CompProperties_Fullbottle()
        {
            compClass = typeof(CompFullbottle);
        }
    }

    public class CompFullbottle : ThingComp
    {
        public CompProperties_Fullbottle Props => (CompProperties_Fullbottle)props;

        public string BottleType
        {
            get
            {
                return Props.bottleType;
            }
        }

        public HediffDef BuffHediffDef
        {
            get
            {
                return Props.buffHediffDef;
            }
        }

        public override string CompInspectStringExtra()
        {
            return "满装瓶罐 - " + BottleType;
        }
    }

    [DefOf]
    public static class FullbottleDefOf
    {
        public static HediffDef FullbottleHandler;
        public static HediffDef FullbottleBuff_Rabbit;
        public static HediffDef FullbottleBuff_Tank;
        public static SoundDef Fullbottle_Shake;
        public static SoundDef Fullbottle_Shake_Driver;
        public static SoundDef BuildRider_BestMatch;

        static FullbottleDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FullbottleDefOf));
        }
    }

    public class Hediff_FullbottleHandler : HediffWithComps
    {
        public int lastShakeTick = -1;

        public int cooldownTicks = 3750;

        public override bool Visible => false;


        public bool CanShakeNow
        {
            get
            {
                if(lastShakeTick < 0)
                {
                    return true;
                }
                return Find.TickManager.TicksGame - lastShakeTick >= cooldownTicks;
            }
        }

        public int GetRemainingCooldownTicks
        {
            get
            {
                if(lastShakeTick < 0)
                {
                    return 0;
                }
                int elapsed = Find.TickManager.TicksGame - lastShakeTick;
                int remaining = cooldownTicks - elapsed;
                return (remaining > 0) ? remaining : 0;
            }
        }

        public bool HasAnyBuff
        {
            get
            {
                if(pawn == null || pawn.health == null)
                {
                    return false;
                }
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for(int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i].def.defName.StartsWith("FullbottleBuff_"))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void RecordShake()
        {
            lastShakeTick = Find.TickManager.TicksGame;
        }

        public void RemoveCurrentBuff()
        {
            if(pawn == null || pawn.health == null)
            {
                return;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for(int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i].def.defName.StartsWith("FullbottleBuff_"))
                {
                    pawn.health.RemoveHediff(hediffs[i]);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastShakeTick, "RWR_lastShackTick", -1);
            Scribe_Values.Look(ref cooldownTicks, "RWR_cooldownTicks", 3750);
        }

        public override bool ShouldRemove
        {
            get
            {
                return false;
            }
        }
    }

    public class Hediff_FullbottleBuff : HediffWithComps
    {
        public override bool ShouldRemove
        {
            get
            {
                if (Severity >= 0.2f)
                {
                    return false;
                }
                else
                {
                    return base.ShouldRemove;
                }
            }
        }

        public override bool Visible
        {
            get
            {
                if(Severity >= 0.2f){
                    return false;
                }
                else
                {
                    return base.Visible;
                }
            }
        }
    }

    public static class FullbottleUtility
    {
        public static Hediff_FullbottleHandler GetHandler(Pawn pawn)
        {
            if (pawn == null || pawn.health == null)
            {
                return null;
            }
            Hediff_FullbottleHandler handler = (Hediff_FullbottleHandler)pawn.health.hediffSet.GetFirstHediffOfDef(FullbottleDefOf.FullbottleHandler, false);
            if(handler == null)
            {
                handler = (Hediff_FullbottleHandler)HediffMaker.MakeHediff(FullbottleDefOf.FullbottleHandler, pawn, null);
                pawn.health.AddHediff(handler, null, null);
            }
            return handler;
        }

        public static List<Thing> GetAvailableBottles(Pawn pawn)
        {
            List<Thing> bottles = new List<Thing>();
            if(pawn == null || pawn.inventory == null)
            {
                return bottles;
            }
            foreach(Thing thing in pawn.inventory.GetDirectlyHeldThings())
            {
                if(thing.TryGetComp<CompFullbottle>() != null)
                {
                    bottles.Add(thing);
                }
            }
            return bottles;
        }

        public static void ShakeBottle(Pawn pawn, Thing bottle)
        {
            CompFullbottle comp = bottle.TryGetComp<CompFullbottle>();
            if(comp == null)
            {
                return;
            }
            Hediff_FullbottleHandler handler = GetHandler(pawn);
            if(handler == null)
            {
                return;
            }

            if (pawn.Spawned && FullbottleDefOf.Fullbottle_Shake != null)
            {
                SoundStarter.PlayOneShot(FullbottleDefOf.Fullbottle_Shake, pawn);
            }

            handler.RemoveCurrentBuff();
            handler.RecordShake();

            Hediff buff = HediffMaker.MakeHediff(comp.BuffHediffDef, pawn, null);
            pawn.health.AddHediff(buff, null, null);

            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "Shake!", 2.5f);
            }
            RimworldRiderMod.LogMessage(string.Format("{0} 使用了 {1}", pawn.LabelShort, bottle.LabelNoCount));
        }

        public static Command_Action CreateShakeGizmo(Pawn pawn)
        {
            Command_Action gizmo = new Command_Action();
            gizmo.defaultLabel = "摇动满装瓶罐";
            gizmo.defaultDesc = "选择一个满装瓶罐摇动，获得临时增益效果。\n同一时间只能激活一种瓶罐增益，增益持续1小时，冷却1.5小时。";

            gizmo.icon = FullbottleTexture.BottleIcon;
            gizmo.action = delegate
            {
                List<Thing> bottles = GetAvailableBottles(pawn);
                if (bottles.Count == 0)
                {
                    Messages.Message("没有可用的满装瓶罐！", pawn, MessageTypeDefOf.RejectInput);
                    return;
                }

                Hediff_FullbottleHandler handler = GetHandler(pawn);
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (Thing bottle in bottles)
                {
                    CompFullbottle comp = bottle.TryGetComp<CompFullbottle>();
                    if (comp == null)
                    {
                        continue;
                    }

                    string label = bottle.LabelNoCount;
                    bool canShake = handler.CanShakeNow;
                    bool hasBuff = handler.HasAnyBuff;

                    if (!canShake)
                    {
                        int remaining = handler.GetRemainingCooldownTicks;
                        float remainingHours = (float)remaining / 2500f;
                        string desc = string.Format("冷却剩余: {0:F1}小时", remainingHours);
                        FloatMenuOption disabledOption = new FloatMenuOption(label + " (" + desc + ")", null);
                        options.Add(disabledOption);
                    }
                    else
                    {
                        FloatMenuOption option = new FloatMenuOption(label, delegate
                        {
                            ShakeBottle(pawn, bottle);
                        });
                        if (hasBuff)
                        {
                            option.tooltip = "将替换当前增益效果";
                        }
                        options.Add(option);
                    }
                }
                Find.WindowStack.Add(new FloatMenu(options));
            };
            List<Thing> availableBottles = GetAvailableBottles(pawn);
            if(availableBottles.Count == 0)
            {
                gizmo.Disable("没有满装瓶罐可用");
            }
            else
            {
                Hediff_FullbottleHandler handler = GetHandler(pawn);
                if (handler != null && !handler.CanShakeNow)
                {
                    int remaining = handler.GetRemainingCooldownTicks;
                    float remainingHours = (float)remaining / 2500f;
                    gizmo.Disable(string.Format("冷却中 ({0:F1}小时)", remainingHours));
                }
            }
            return gizmo;
        }
    }

    [StaticConstructorOnStartup]
    public static class FullbottleTexture
    {
        private static UnityEngine.Texture2D _bottleIcon;

        public static UnityEngine.Texture2D BottleIcon
        {
            get
            {
                if(_bottleIcon == null)
                {
                    _bottleIcon = ContentFinder<UnityEngine.Texture2D>.Get("Things/Fullbottle/GizmoIcon", true);
                }
                return _bottleIcon;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    public static class Patch_Pawn
    {
        [HarmonyPatch(nameof(Pawn.GetGizmos))]
        [HarmonyPostfix]
        public static void Postfix_GetGizmos(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if(__instance == null || !__instance.RaceProps.Humanlike)
                {
                    return;
                }
                if(__instance.inventory == null || __instance.health == null)
                {
                    return;
                }

                bool hasBottle = false;
                foreach(Thing thing in __instance.inventory.GetDirectlyHeldThings())
                {
                    if(thing.TryGetComp<CompFullbottle>() != null)
                    {
                        hasBottle = true;
                        break;
                    }
                }

                if (!hasBottle)
                {
                    return;
                }

                FullbottleUtility.GetHandler(__instance);

                List<Gizmo> gizmos = new List<Gizmo>(__result);
                gizmos.Add(FullbottleUtility.CreateShakeGizmo(__instance));
                __result = gizmos;
            }
            catch(Exception ex)
            {
                RimworldRiderMod.LogError("GizmoHook: " + ex.Message);
            }
        }
    }
}

namespace RimworldRider.BuildDriver
{
    public class CompProperties_BuildDriver : CompProperties
    {
        public CompProperties_BuildDriver()
        {
            compClass = typeof(CompBuildDriver);
        }
    }

    public class CompBuildDriver : ThingComp
    {
        public ThingDef selectedLeftBottle;

        public ThingDef selectedRightBottle;

        public HediffDef Left_hediff;

        public HediffDef Right_hediff;

        private int formChangeCooldownUntilTick = -1;

        public bool InFormChangeCooldown
        {
            get
            {
                return FormChangeCooldownTicksRemaining > 0;
            }
        }

        public int FormChangeCooldownTicksRemaining
        {
            get
            {
                return UnityEngine.Mathf.Max(0, formChangeCooldownUntilTick - Find.TickManager.TicksGame);
            }
        }

        public void NotifyFormChanged()
        {
            formChangeCooldownUntilTick = Find.TickManager.TicksGame + 180;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedLeftBottle, "selectedLeftBottle");
            Scribe_Defs.Look(ref selectedRightBottle, "selectedRightBottle");
            Scribe_Defs.Look(ref Left_hediff, "Left_hediff");
            Scribe_Defs.Look(ref Right_hediff, "Right_hediff");
            Scribe_Values.Look(ref formChangeCooldownUntilTick, "formChangeCooldownUntilTick", -1);
        }
    }

    [DefOf]
    public static class BuildDriverDefOf
    {
        public static ThingDef BuildDriver;

        public static HediffDef BuildRider_RabbitTank;

        static BuildDriverDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BuildDriverDefOf));
        }
    }

    public class BestMatchEntry
    {
        public string leftBottle;

        public string rightBottle;

        public HediffDef formHediff;

        public string label;

        public UnityEngine.Texture2D bestMatchIcon;

        public string animationKey;
    }

    public static class BestMatchDatabase
    {
        public static readonly Dictionary<string, BestMatchEntry> Entries = new Dictionary<string, BestMatchEntry>
        {
            {"Rabbit+Tank", new BestMatchEntry
                {
                    leftBottle = "Fullbottle_Rabbit",
                    rightBottle = "Fullbottle_Tank",
                    formHediff = BuildDriverDefOf.BuildRider_RabbitTank,
                    label = "RabbitTank",
                    bestMatchIcon = ContentFinder<UnityEngine.Texture2D>.Get("Things/BestMatch/RabbitAndTank", true),
                    animationKey = "Rider/Build/RabbitTank"
                }
            }
        };

        public static BestMatchEntry FindMatch(string leftDefName, string rightDefName)
        {
            string key = leftDefName.Substring("Fullbottle_".Length) + "+" + rightDefName.Substring("Fullbottle_".Length);
            BestMatchEntry entry;
            if(Entries.TryGetValue(key, out entry))
            {
                return entry;
            }
            return null;
        }

        public static bool IsValidMatch(ThingDef left, ThingDef right)
        {
            if(left == null || right == null)
            {
                return false;
            }
            return FindMatch(left.defName, right.defName) != null;
        }
    }

    public static class BuildDriverUtility
    {
        public static CompBuildDriver GetDriver(Pawn pawn)
        {
            if(pawn == null || pawn.apparel == null)
            {
                return null;
            }
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for(int i = 0; i < wornApparel.Count; i++)
            {
                CompBuildDriver comp = wornApparel[i].TryGetComp<CompBuildDriver>();
                if (comp != null)
                {
                    return comp;
                }
            }
            return null;
        }

        public static void PlayFullbottleSound(ThingDef FullbottleDef, Pawn pawn)
        {
            SoundDef sound = FullbottleDef.GetCompProperties<RimworldRider.Fullbottle.CompProperties_Fullbottle>().bottleSound;
            sound.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
        }

        public static bool IsBuildRider(Pawn pawn)
        {
            return CurrentRiderHediff(pawn) != null;
        }

        public static Hediff CurrentRiderHediff(Pawn pawn)
        {
            if(pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for(int i = 0; i < hediffs.Count; i++)
            {
                if (IsBuildRiderDef(hediffs[i].def))
                {
                    return hediffs[i];
                }
            }
            return null;
        }

        public static bool IsBuildRiderDef(HediffDef def)
        {
            return def == BuildDriverDefOf.BuildRider_RabbitTank;
        }

        public static bool IsFullbottleBuffDef(HediffDef def)
        {
            return def.defName.StartsWith("FullbottleBuff_");
        }

        public static List<Thing> GetAvailableBottles(Pawn pawn)
        {
            List<Thing> bottles = new List<Thing>();
            if(pawn == null || pawn.inventory == null)
            {
                return bottles;
            }
            foreach(Thing thing in pawn.inventory.GetDirectlyHeldThings())
            {
                if(thing.TryGetComp<RimworldRider.Fullbottle.CompFullbottle>() != null)
                {
                    bottles.Add(thing);
                }
            }
            return bottles;
        }

        public static List<ThingDef> GetAvailableLeftBottleDefs(Pawn pawn)
        {
            List<ThingDef> defs = new List<ThingDef>();
            List<Thing> bottles = GetAvailableBottles(pawn);
            for(int i = 0; i < bottles.Count; i++)
            {
                ThingDef def = bottles[i].def;
                if (!defs.Contains(def) && def.orderedTakeGroup.defName == "Fullbottles_organic")
                {
                    defs.Add(def);
                }
            }
            return defs;
        }

        public static List<ThingDef> GetAvailableRightBottleDefs(Pawn pawn, ThingDef leftBottle)
        {
            List<ThingDef> defs = new List<ThingDef>();
            List<Thing> bottles = GetAvailableBottles(pawn);
            for(int i = 0; i < bottles.Count; i++)
            {
                ThingDef def = bottles[i].def;
                if (!defs.Contains(def) && def.orderedTakeGroup.defName == "Fullbottles_inorganic")
                {
                    defs.Add(def);
                }
            }
            return defs;
        }

        public static bool AddFullbottlHediff(Pawn pawn, CompBuildDriver driver)
        {
            if(pawn == null || driver == null)
            {
                return false;
            }
            Hediff left_hediff = HediffMaker.MakeHediff(driver.Left_hediff, pawn, null);
            left_hediff.Severity = 0.3f;
            Hediff right_hediff = HediffMaker.MakeHediff(driver.Right_hediff, pawn, null);
            right_hediff.Severity = 0.3f;
            pawn.health.AddHediff(left_hediff, null, null);
            pawn.health.AddHediff(right_hediff, null, null);
            return true;
        }

        public static void TryTransform(Pawn pawn, CompBuildDriver driver)
        {
            if(pawn == null || driver == null)
            {
                return;
            }

            if (IsBuildRider(pawn))
            {
                CancelTransformation(pawn);
                return;
            }

            string failReason = TransformFailReason(pawn, driver);
            if(failReason != null)
            {
                Messages.Message(failReason, pawn, MessageTypeDefOf.RejectInput);
                return;
            }
            BestMatchEntry match = BestMatchDatabase.FindMatch(
                driver.selectedLeftBottle.defName,
                driver.selectedRightBottle.defName);
            if(match == null)
            {
                Messages.Message("无效的瓶罐组合！", pawn, MessageTypeDefOf.RejectInput);
                return;
            }

            SoundDef sound = SoundDef.Named("Fullbottle_Shake");
            if (sound != null && pawn.Spawned)
            {
                sound.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
            
            if (pawn.Spawned)
            {
                Thing_BuildTransformAnimation animation = (Thing_BuildTransformAnimation)ThingMaker.MakeThing(
                    DefDatabase<ThingDef>.GetNamedSilentFail("BuildRider_TransformAnimation"));
                if (animation != null)
                {
                    animation.Setup(pawn, match.animationKey);
                    GenSpawn.Spawn(animation, pawn.Position, pawn.Map);
                }
            }

            GameComponent_BuildTransformation.QueueTransformation(pawn, match.formHediff, match.animationKey);
            driver.Left_hediff = driver.selectedLeftBottle.GetCompProperties<RimworldRider.Fullbottle.CompProperties_Fullbottle>().buffHediffDef;
            driver.Right_hediff = driver.selectedRightBottle.GetCompProperties<RimworldRider.Fullbottle.CompProperties_Fullbottle>().buffHediffDef;
            if(!AddFullbottlHediff(pawn, driver))
            {
                Log.Message("Rimworld Rider: Add Hediff Failed");
            }

            string msg = pawn.NameShortColored + " " + match.label + "！";
            Messages.Message(msg, pawn, MessageTypeDefOf.PositiveEvent);

            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "Build Up!", 2.5f);

            RimworldRiderMod.LogMessage(pawn.LabelShort + " transforming " + match.label);
        }

        public static void CancelTransformation(Pawn pawn)
        {
            if(pawn == null)
            {
                return;
            }
            GameComponent_BuildTransformation.CancelPendingTransformation(pawn);
            RemoveRiderHediffs(pawn);
            Messages.Message(string.Format("{0} 解除了变身", pawn.NameShortColored), pawn, MessageTypeDefOf.PositiveEvent);
        }

        private static void RemoveRiderHediffs(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (IsBuildRiderDef(hediffs[i].def) || IsFullbottleBuffDef(hediffs[i].def))
                {
                    pawn.health.RemoveHediff(hediffs[i]);
                }
            }
        }

        private static string TransformFailReason(Pawn pawn, CompBuildDriver driver)
        {
            if (driver.InFormChangeCooldown)
            {
                return "变身冷却中";
            }
            if (driver.selectedLeftBottle == null || driver.selectedRightBottle == null)
            {
                return "请插入瓶罐";
            }
            if (!HasBottleInInventory(pawn, driver.selectedLeftBottle))
            {
                return "背包中没有" + driver.selectedLeftBottle.label;
            }
            if (!HasBottleInInventory(pawn, driver.selectedRightBottle))
            {
                return "背包中没有" + driver.selectedRightBottle.label;
            }

            return null;
        }

        private static bool HasBottleInInventory(Pawn pawn, ThingDef bottleDef)
        {
            if(pawn.inventory == null)
            {
                return false;
            }
            foreach(Thing thing in pawn.inventory.GetDirectlyHeldThings())
            {
                if(thing.def == bottleDef)
                {
                    return true;
                }
            }
            return false;
        }

        public static Command_Action CreateTransformGizmo(Pawn pawn, CompBuildDriver driver)
        {
            Command_Action gizmo = new Command_Action();
            gizmo.defaultLabel = (BestMatchDatabase.IsValidMatch(driver.selectedLeftBottle, driver.selectedRightBottle)) ? "Best Match" : "Just Match";
            gizmo.defaultDesc = "使用选中的满装瓶罐变身为假面骑士Build。";
            gizmo.icon = ContentFinder<UnityEngine.Texture2D>.Get("Things/Item/Build", true);

            gizmo.action = delegate
            {
                if (IsBuildRider(pawn))
                {
                    CancelTransformation(pawn);
                    driver.selectedLeftBottle = null;
                    driver.selectedRightBottle = null;
                }
                else
                {
                    TryTransform(pawn, driver);
                }
            };
            if (IsBuildRider(pawn))
            {
                gizmo.defaultLabel = "解除变身";
                gizmo.defaultDesc = "解除假面骑士Build的变身状态。";
            }
            else
            {
                string failReason = TransformFailReason(pawn, driver);
                if (failReason != null)
                {
                    gizmo.defaultLabel = "变身";
                    gizmo.Disable(failReason);
                }
                else
                {
                    BestMatchEntry match = BestMatchDatabase.FindMatch(
                    driver.selectedLeftBottle.defName,
                    driver.selectedRightBottle.defName);
                    gizmo.icon = match.bestMatchIcon;
                }
            }
            return gizmo;
        }

        public static Command_Action CreateLeftBottleGizmo(Pawn pawn, CompBuildDriver driver)
        {
            Command_Action gizmo = new Command_Action();
            string label = (driver.selectedLeftBottle != null) ? ("左侧: " + driver.selectedLeftBottle.label) : "选择左侧瓶罐";
            gizmo.defaultLabel = label;
            gizmo.defaultDesc = "选择左侧插槽的满装瓶罐。";
            if(driver.selectedLeftBottle != null)
            {
                gizmo.icon = ContentFinder<UnityEngine.Texture2D>.Get(driver.selectedLeftBottle.graphicData.texPath, true);
                if (!HasBottleInInventory(pawn, driver.selectedLeftBottle))
                {
                    driver.selectedLeftBottle = null;
                }
            }
            else
            {
                gizmo.icon = ContentFinder<UnityEngine.Texture2D>.Get("Things/Item/leftEntry", true);
            }

            gizmo.action = delegate
            {
                List<ThingDef> avaliableDefs = GetAvailableLeftBottleDefs(pawn);
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                for (int i = 0; i < avaliableDefs.Count; i++)
                {
                    ThingDef def = avaliableDefs[i];
                    string defLabel = def.label;
                    bool isSelected = (driver.selectedLeftBottle == def);
                    options.Add(new FloatMenuOption(
                        defLabel + (isSelected ? " ✓" : ""),
                        delegate
                        {
                            driver.selectedLeftBottle = def;
                            PlayFullbottleSound(def, pawn);
                            if (driver.selectedRightBottle != null)
                            {
                                if (BestMatchDatabase.IsValidMatch(driver.selectedLeftBottle, driver.selectedRightBottle))
                                {
                                    SoundDelayUtility.Instance.PlaySoundAfterDelay(RimworldRider.Fullbottle.FullbottleDefOf.BuildRider_BestMatch, pawn, 4f);
                                }
                            }
                        }));
                }
                if (options.Count > 0)
                    Find.WindowStack.Add(new FloatMenu(options));
            };
            return gizmo;
        }

        public static Command_Action CreateRightBottleGizmo(Pawn pawn, CompBuildDriver driver)
        {
            Command_Action gizmo = new Command_Action();
            string label = (driver.selectedRightBottle != null) ? ("右侧: " + driver.selectedRightBottle.label) : "选择右侧瓶罐";
            gizmo.defaultLabel = label;
            gizmo.defaultDesc = "选择右侧插槽的满装瓶罐。";
            if(driver.selectedRightBottle != null)
            {
                gizmo.icon = ContentFinder<UnityEngine.Texture2D>.Get(driver.selectedRightBottle.graphicData.texPath, true);
                if(!HasBottleInInventory(pawn, driver.selectedRightBottle))
                {
                    driver.selectedRightBottle = null;
                }
            }
            else
            {
                gizmo.icon = ContentFinder<UnityEngine.Texture2D>.Get("Things/Item/rightEntry", true);
            }

            gizmo.action = delegate
            {
                List<ThingDef> availableDefs = GetAvailableRightBottleDefs(pawn, driver.selectedLeftBottle);
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                if (availableDefs.Count == 0)
                {
                    options.Add(new FloatMenuOption("没有可用的 Best Match 瓶罐", null));
                }
                else
                {
                    for (int i = 0; i < availableDefs.Count; i++)
                    {
                        ThingDef def = availableDefs[i];
                        string defLabel = def.label;
                        bool isSelected = (driver.selectedRightBottle == def);
                        options.Add(new FloatMenuOption(
                            defLabel + (isSelected ? " ✓" : ""),
                            delegate
                            {
                                driver.selectedRightBottle = def;
                                PlayFullbottleSound(def, pawn);
                                if(driver.selectedLeftBottle != null)
                                {
                                    if(BestMatchDatabase.IsValidMatch(driver.selectedLeftBottle, driver.selectedRightBottle))
                                    {
                                        SoundDelayUtility.Instance.PlaySoundAfterDelay(RimworldRider.Fullbottle.FullbottleDefOf.BuildRider_BestMatch, new TargetInfo(pawn.Position, pawn.Map), 4f);
                                    }
                                }
                            }));
                    }
                }
                if(options.Count > 0)
                    Find.WindowStack.Add(new FloatMenu(options));
            };
            return gizmo;
        }

        [HarmonyPatch(typeof(Pawn))]
        public static class Patch_Pawn_GetGizmos_BuildDriver
        {
            private static void AddIfNotNull(List<Gizmo> list, Gizmo gizmo)
            {
                if (gizmo != null)
                    list.Add(gizmo);
            }

            [HarmonyPatch(nameof(Pawn.GetGizmos))]
            [HarmonyPostfix]
            static void Postfix_GetGizmo(Pawn __instance, ref IEnumerable<Gizmo> __result)
            {
                try
                {
                    if (__instance == null || !__instance.RaceProps.Humanlike)
                    {
                        return;
                    }

                    CompBuildDriver driver = BuildDriverUtility.GetDriver(__instance);
                    if (driver == null)
                    {
                        if (IsBuildRider(__instance))
                        {
                            CancelTransformation(__instance);
                        }
                        return;
                    }

                    List<Gizmo> gizmos = new List<Gizmo>(__result);

                    if (__instance.Drafted)
                    {
                        AddIfNotNull(gizmos, BuildDriverUtility.CreateLeftBottleGizmo(__instance, driver));
                        AddIfNotNull(gizmos, BuildDriverUtility.CreateRightBottleGizmo(__instance, driver));
                        AddIfNotNull(gizmos, BuildDriverUtility.CreateTransformGizmo(__instance, driver));
                    }

                    if (IsBuildRider(__instance))
                    {
                        if (!HasBottleInInventory(__instance, driver.selectedLeftBottle) || !HasBottleInInventory(__instance, driver.selectedRightBottle))
                        {
                            CancelTransformation(__instance);
                        }
                    }

                    __result = gizmos;
                }
                catch (Exception ex)
                {
                    Log.Error("[RimworldRider] BuildDriver GizmoHook: " + ex.Message);
                }
            }
        }
    }

    public class Thing_BuildTransformAnimation : Thing
    {
        public const int DefaultFrameCount = 61;
        public const int DefaultTicksPerFrame = 4;
        public const int DefaultDurationTicks = 244;

        private Pawn target;
        private string frameRoot;
        private int ageTicks;
        private int frameCount = DefaultFrameCount;
        private int ticksPerFrame = DefaultTicksPerFrame;
        private float drawSize = 3.45f;

        public void Setup(Pawn pawn, string animationKey)
        {
            this.target = pawn;
            this.frameRoot = FrameRootFor(animationKey);
            this.frameCount = FrameCountFor(animationKey);
            this.ticksPerFrame = DefaultTicksPerFrame;
            this.drawSize = DrawSizeFor(animationKey);
        }

        protected override void Tick()
        {
            base.Tick();
            this.ageTicks++;

            Pawn pawn = this.target;
            if(pawn != null && pawn.Spawned && pawn.Map == base.Map)
            {
                base.Position = pawn.Position;
            }

            if(this.ageTicks >= this.frameCount * this.ticksPerFrame || this.target == null || this.target.Destroyed)
            {
                this.Destroy(DestroyMode.Vanish);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (string.IsNullOrEmpty(this.frameRoot))
            {
                return;
            }

            int frame = Mathf.Clamp(this.ageTicks / this.ticksPerFrame, 0, this.frameCount - 1);
            string texPath = string.Format("{0}{1}", this.frameRoot, frame);
            Texture2D tex = ContentFinder<Texture2D>.Get(texPath, false);
            if(tex == null)
            {
                return;
            }

            Vector2 size = new Vector2(this.drawSize, this.drawSize);
            if(tex.width > 0)
            {
                size.y = this.drawSize * (float)tex.height / (float)tex.width;
            }

            Graphic_Single graphic = (Graphic_Single)GraphicDatabase.Get<Graphic_Single>(texPath, ShaderDatabase.Cutout, size, Color.white);

            Vector3 loc = (this.target != null) ? this.target.DrawPos : drawLoc;
            loc.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            loc.z -= 0.65f;

            graphic.Draw(loc, Rot4.North, this);
        }

        private static string FrameRootFor(string animationKey)
        {
            if(animationKey != null && animationKey.StartsWith("Rider/Build/"))
            {
                string formKey = animationKey.Substring("Rider/Build/".Length);
                return string.Format("Things/Mote/Transform/Build/{0}/Transform/", formKey);
            }
            return "Things/Mote/Transform/Rider/RiderTransform";
        }

        private static int FrameCountFor(string animationKey)
        {
            return DefaultFrameCount;
        }

        private static float DrawSizeFor(string animationKey)
        {
            return 3.45f;
        }

        public static int DurationTicksFor(string animationKey)
        {
            return DefaultFrameCount * DefaultTicksPerFrame;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Pawn>(ref this.target, "target");
            Scribe_Values.Look<string>(ref this.frameRoot, "frameRoot", null);
            Scribe_Values.Look<int>(ref this.ageTicks, "ageTicks", 0);
            Scribe_Values.Look<int>(ref this.frameCount, "frameCount", DefaultFrameCount);
            Scribe_Values.Look<int>(ref this.ticksPerFrame, "ticksPerFrame", DefaultTicksPerFrame);
            Scribe_Values.Look<float>(ref this.drawSize, "drawSize", 3f);
        }
    }

    public class HediffCompProperties_TransformationAppearance : HediffCompProperties
    {
        public string textureRoot;
        public bool replaceBody = true;
        public bool replaceHead = true;
        public float overlayDrawSize = 2.3f;
        public float headOverlayDrawSize = 1.75f;
        public float overlayHorizontalOffset = 0f;
        public float overlayVerticalOffset = 0.32f;
        public float overlayLayerOffset = -0.04f;
        public bool bodyTypeAware = false;
        public string fallbackBodyType = "Male";
        public bool singleTexture = false;

        public HediffCompProperties_TransformationAppearance()
        {
            this.compClass = typeof(HediffComp_TransformationAppearance);
        }
    }

    public class HediffComp_TransformationAppearance : HediffComp
    {
        public HediffCompProperties_TransformationAppearance Props
        {
            get
            {
                return (HediffCompProperties_TransformationAppearance)this.props;
            }
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            DirtyPawnGraphics();
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            DirtyPawnGraphics();
        }

        private void DirtyPawnGraphics()
        {
            Pawn pawn = base.Pawn;
            if(pawn != null)
            {
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                PortraitsCache.SetDirty(pawn);
            }
        }
    }

    public static class TransformationAppearanceUtility
    {
        public static HediffComp_TransformationAppearance ActiveAppearance(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null || pawn.health.hediffSet.hediffs == null || pawn.Dead)
            {
                return null;
            }
            for(int i = pawn.health.hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                HediffComp_TransformationAppearance comp = pawn.health.hediffSet.hediffs[i].TryGetComp<HediffComp_TransformationAppearance>();
                if(comp != null)
                {
                    return comp;
                }
            }
            return null;
        }

        public static string BodyTypeTexturePath(Pawn pawn, HediffComp_TransformationAppearance appearance)
        {
            if (appearance == null || appearance.Props == null || string.IsNullOrEmpty(appearance.Props.textureRoot))
            {
                return null;
            }
            if (!appearance.Props.bodyTypeAware)
            {
                return appearance.Props.textureRoot;
            }
            string bodyType = (pawn != null && pawn.story != null && pawn.story.bodyType != null) ? pawn.story.bodyType.defName : appearance.Props.fallbackBodyType;
            if (string.IsNullOrEmpty(bodyType))
            {
                bodyType = appearance.Props.fallbackBodyType;
            }
            return appearance.Props.textureRoot + "_" + bodyType;
        }

        public static float HeadOnlyScaleFor(Pawn pawn, HediffComp_TransformationAppearance appearance)
        {
            if (pawn == null || appearance == null || appearance.Props == null || !appearance.Props.replaceHead || appearance.Props.replaceBody)
            {
                return 1f;
            }
            Vector2 scale = (pawn.story != null && pawn.story.bodyType != null) ? pawn.story.bodyType.bodyGraphicScale : Vector2.one;
            return Mathf.Clamp(Mathf.Max(scale.x, scale.y) * pawn.BodySize, 0.6f, 1.75f);
        }

        public static float HeadOffsetFor(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return 0.117f;
            }
            if (rot == Rot4.East || rot == Rot4.West)
            {
                return 0.07f;
            }
            return 0f;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker))]
    public static class Patch_PawnRenderNodeWorker_CanDrawNow
    {
        [HarmonyPatch(nameof(PawnRenderNodeWorker.CanDrawNow))]
        [HarmonyPostfix]
        public static void Postfix(PawnRenderNodeWorker __instance, PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            HediffComp_TransformationAppearance appearance = TransformationAppearanceUtility.ActiveAppearance(parms.pawn);
            if(appearance?.Props != null && node?.hediff == null)
            {
                string name = __instance.GetType().Name;
                string text = node?.Props?.debugLabel ?? string.Empty;
                string text2 = node?.Props?.texPath ?? string.Empty;
                bool flag = name.Contains("Body") || text.Contains("body") || text2.Contains("Bodies/");
                bool flag2 = name.Contains("Head") || text.Contains("head") || text2.Contains("Heads/");
                bool flag3 = name.Contains("Hair") || text.Contains("hair");
                bool flag4 = name.Contains("Apparel") || node?.apparel != null;
                if(appearance.Props.replaceBody && (flag || flag2 || flag3 || flag4))
                {
                    __result = false;
                }
                else if (appearance.Props.replaceHead && !appearance.Props.replaceBody && (flag2 || flag3 || name.Contains("Apparel_Head")))
                {
                    __result = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer))]
    public static class Patch_PawnRenderer_RenderPawnAt_Overlay
    {
        [HarmonyPatch(nameof(PawnRenderer.RenderPawnAt))]
        [HarmonyPostfix]
        public static void Postfix(PawnRenderer __instance, Vector3 drawLoc, Rot4? rotOverride = null, bool neverAimWeapon = false)
        {
            Traverse trav = Traverse.Create(__instance);
            Pawn pawn = trav.Field("pawn").GetValue<Pawn>();
            if(pawn == null)
            {
                return;
            }

            Rot4 rot = (rotOverride != null) ? rotOverride.Value : pawn.Rotation;
            TransformationOverlayDrawer.Draw(pawn, drawLoc, rot);
        }
    }

    [HarmonyPatch(typeof(ColonistBarColonistDrawer))]
    public static class Patch_ColonistBarColonistDrawer_Overlay
    {
        [HarmonyPatch(nameof(ColonistBarColonistDrawer.DrawColonist))]
        public static void Postfix(Rect rect, Pawn colonist)
        {
            TransformationOverlayDrawer.DrawColonistBarOverlay(rect, colonist);
        }
    }

    public static class TransformationOverlayDrawer
    {
        public static void Draw(Pawn pawn, Vector3 drawLoc, Rot4 rot)
        {
            HediffComp_TransformationAppearance appearance = TransformationAppearanceUtility.ActiveAppearance(pawn);
            if (appearance == null || appearance.Props == null || string.IsNullOrEmpty(appearance.Props.textureRoot))
            {
                return;
            }

            string texPath = TransformationAppearanceUtility.BodyTypeTexturePath(pawn, appearance);
            if (string.IsNullOrEmpty(texPath))
            {
                return;
            }

            HediffCompProperties_TransformationAppearance props = appearance.Props;
            // Directional texture using Graphic_Multi (supports _south, _east, _north, _west)
            float scale = TransformationAppearanceUtility.HeadOnlyScaleFor(pawn, appearance);
            Vector2 drawSize = (props.replaceHead && !props.replaceBody)
                ? new Vector2(props.headOverlayDrawSize * scale, props.headOverlayDrawSize * scale)
                : new Vector2(props.overlayDrawSize, props.overlayDrawSize);

            Vector3 loc = drawLoc;
            loc.x += props.overlayHorizontalOffset;
            loc.y += props.overlayLayerOffset;
            loc.z += props.overlayVerticalOffset;

            if (props.replaceHead && !props.replaceBody)
            {
                loc.z += TransformationAppearanceUtility.HeadOffsetFor(rot);
            }

            GraphicDatabase.Get<Graphic_Multi>(texPath, ShaderDatabase.Cutout, drawSize, Color.white)
                .Draw(loc, rot, pawn);
        }

        public static void DrawColonistBarOverlay(Rect rect, Pawn colonist)
        {
            HediffComp_TransformationAppearance appearance = TransformationAppearanceUtility.ActiveAppearance(colonist);
            if (appearance == null || appearance.Props == null || string.IsNullOrEmpty(appearance.Props.textureRoot))
            {
                return;
            }

            string texPath = TransformationAppearanceUtility.BodyTypeTexturePath(colonist, appearance);
            if (string.IsNullOrEmpty(texPath))
            {
                return;
            }

            Texture2D tex = ContentFinder<Texture2D>.Get(texPath + "_south", false);
            if (tex == null)
            {
                tex = ContentFinder<Texture2D>.Get(texPath, false);
            }
            if (tex == null)
            {
                return;
            }

            Rect drawRect;
            if (appearance.Props.replaceHead && !appearance.Props.replaceBody)
            {
                drawRect = new Rect(rect.x + rect.width * 0.04f, rect.y - rect.height * 0.22f, rect.width * 0.92f, rect.width * 0.92f);
            }
            else
            {
                drawRect = new Rect(rect.x - rect.width * 0.35f, rect.y - rect.height * 0.56f, rect.width * 1.7f, rect.height * 1.7f);
            }

            GUI.DrawTexture(drawRect, tex);
        }
    }

    public class PendingBuildTransformation
    {
        public Pawn pawn;
        public HediffDef formHediffDef;
        public int ticksLeft;
        public string animationKey;
    }

    public class GameComponent_BuildTransformation : GameComponent
    {
        private static List<PendingBuildTransformation> pendingTransforms = new List<PendingBuildTransformation>();

        public GameComponent_BuildTransformation(Game game)
        {

        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (pendingTransforms.Count == 0)
            {
                return;
            }

            for (int i = pendingTransforms.Count - 1; i >= 0; i--)
            {
                PendingBuildTransformation pending = pendingTransforms[i];
                pending.ticksLeft--;

                if (pending.ticksLeft <= 0)
                {
                    pendingTransforms.RemoveAt(i);
                    ApplyBuildTransformation(pending);
                }
            }
        }

        public static void QueueTransformation(Pawn pawn, HediffDef formHediffDef, string animationKey)
        {
            if (pawn == null || formHediffDef == null)
            {
                return;
            }

            int ticks = Thing_BuildTransformAnimation.DurationTicksFor(animationKey);

            for (int i = pendingTransforms.Count - 1; i >= 0; i--)
            {
                if (pendingTransforms[i].pawn == pawn)
                {
                    pendingTransforms.RemoveAt(i);
                }
            }

            PendingBuildTransformation pending = new PendingBuildTransformation();
            pending.pawn = pawn;
            pending.formHediffDef = formHediffDef;
            pending.ticksLeft = ticks;
            pending.animationKey = animationKey;
            pendingTransforms.Add(pending);
        }

        public static bool HasPendingTransformation(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            for (int i = 0; i < pendingTransforms.Count; i++)
            {
                if (pendingTransforms[i].pawn == pawn)
                {
                    return true;
                }
            }
            return false;
        }

        public static void CancelPendingTransformation(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }
            for (int i = pendingTransforms.Count - 1; i >= 0; i--)
            {
                if (pendingTransforms[i].pawn == pawn)
                {
                    pendingTransforms.RemoveAt(i);
                }
            }
        }

        private static void ApplyBuildTransformation(PendingBuildTransformation pending)
        {
            Pawn pawn = pending.pawn;
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            if (pending.formHediffDef != null)
            {
                Hediff existingHediff = BuildDriverUtility.CurrentRiderHediff(pawn);
                if (existingHediff != null)
                {
                    pawn.health.RemoveHediff(existingHediff);
                }

                Hediff formHediff = HediffMaker.MakeHediff(pending.formHediffDef, pawn, null);
                pawn.health.AddHediff(formHediff, null, null);
            }

            SoundDef activateSound = DefDatabase<SoundDef>.GetNamedSilentFail("BuildRider_Activate");
            if (activateSound != null && pawn.Spawned)
            {
                SoundStarter.PlayOneShot(activateSound, pawn);
            }

            string msg = pawn.NameShortColored + " transformed into Kamen Rider Build!";
            Messages.Message(msg, pawn, MessageTypeDefOf.PositiveEvent);

            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "Best Match!", 3f);
            }

            RimworldRiderMod.LogMessage(pawn.LabelShort + " transformed into Build Rider form");
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }

    public class SoundDelayUtility : MonoBehaviour
    {
        private static SoundDelayUtility _instance;

        // 确保场景里有一个这个组件的实例
        public static SoundDelayUtility Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("SoundDelayUtility");
                    _instance = go.AddComponent<SoundDelayUtility>();
                    GameObject.DontDestroyOnLoad(go); // 切换地图时不销毁
                }
                return _instance;
            }
        }

        // 公开方法：延迟播放音效
        public void PlaySoundAfterDelay(SoundDef sound, TargetInfo target, float delaySeconds)
        {
            StartCoroutine(DelayedPlay(sound, target, delaySeconds));
        }

        // 协程本体
        private IEnumerator DelayedPlay(SoundDef sound, TargetInfo target, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds); // 等待指定秒数
            sound.PlayOneShot(target); // 然后在主线程安全播放
        }
    }
}

namespace RimworldRider.Ability
{
    [DefOf]
    public static class AbilityDefOf
    {
        public static AbilityDef BuildRider_RabbitJump;

        static AbilityDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RimworldRider.Ability.AbilityDefOf));
        }
    } 

    public class CompProperties_AbilityBuildHide : CompProperties_AbilityEffect
    {
        public HediffDef FullbottleBuff;

        public CompProperties_AbilityBuildHide()
        {
            compClass = typeof(CompAbilityEffect_BuildHide);
        }
    }

    public class CompAbilityEffect_BuildHide : CompAbilityEffect
    {
        public new CompProperties_AbilityBuildHide Props => (CompProperties_AbilityBuildHide)props;

        private float buffSeverity
        {
            get
            {
                parent.pawn.health.hediffSet.TryGetHediff(Props.FullbottleBuff, out var h);
                if(h == null)
                {
                    return 0f;
                }
                return h.Severity;
            }
        }

        public override bool ShouldHideGizmo
        {
            get
            {
                if(buffSeverity >= 0.3f)
                {
                    return false;
                }
                return true;
            }
        }
    }

    public static class BuildSkillUtility
    {
        public static void ApplyTreadsDamage(Pawn caster, Pawn target, float HazardLevel)
        {
            if(caster == null || target == null || target.Dead)
            {
                return;
            }

            int hits = 6;
            float damagePerHit = 7f * HazardLevel;
            for(int i = 0; i < hits; i++)
            {
                if(target.Destroyed || target.Dead)
                {
                    break;
                }
                DamageInfo dinfo = new DamageInfo(DamageDefOf.Blunt, damagePerHit, 0.5f * 4 * (HazardLevel / 7f), -1f, caster, null, null);
                target.TakeDamage(dinfo);
            }
        }
    }

    public class CompProperties_TankTreads : CompProperties_AbilityEffect
    {
        public CompProperties_TankTreads()
        {
            compClass = typeof(CompAbilityEffect_TankTreads);
        }
    }

    public class CompAbilityEffect_TankTreads : CompAbilityEffect
    {
        public new CompProperties_TankTreads Props => (CompProperties_TankTreads)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = parent.pawn;
            if(pawn == null || !RimworldRider.BuildDriver.BuildDriverUtility.IsBuildRider(pawn))
            {
                return;
            }
            Pawn pawn1 = target.Pawn;
            Building building = target.Thing as Building;
            RimworldRider.HazardLevel.HediffComp_HazardLevel comp = RimworldRider.HazardLevel.HazardLevelUtility.GetHazardLevelComp(pawn);
            float HazardLevel = 1f;
            if(comp != null)
            {
                HazardLevel = comp.hazardLevel;
            }
            if(pawn1 != null && pawn1.Faction != Faction.OfPlayer)
            {
                BuildSkillUtility.ApplyTreadsDamage(pawn, pawn1, HazardLevel);
            }
        }
    }
}