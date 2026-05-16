using System;
using System.Collections.Generic;
using Celeste.Mod.ConsistencyTracker.Models;

namespace Celeste.Mod.ConsistencyTracker.Stats {

    /*
     {chapter:runDeathChance#x}    - Probability of finishing the current run with at most X total deaths,
                                     considering only rooms from the current room to the end of the chapter
                                     and deaths already accumulated this run. Returns 0% if current deaths > X.
     {checkpoint:runDeathChance#x} - Same, but only considers rooms from the current room to the end of the
                                     current checkpoint.
         */

    public class RunDeathChanceStat : Stat {

        public static ValuePlaceholder<int> ChapterRunDeathChanceOverX = new ValuePlaceholder<int>(
            @"\{chapter:runDeathChance#(.*?)\}",
            "{chapter:runDeathChance#(.*?)}"
        );

        public static ValuePlaceholder<int> CheckpointRunDeathChanceOverX = new ValuePlaceholder<int>(
            @"\{checkpoint:runDeathChance#(.*?)\}",
            "{checkpoint:runDeathChance#(.*?)}"
        );

        public static List<string> IDs = new List<string>();

        public RunDeathChanceStat() : base(IDs) { }

        public override bool ContainsIdentificator(string format) {
            return ChapterRunDeathChanceOverX.HasMatch(format) || CheckpointRunDeathChanceOverX.HasMatch(format);
        }

        public override string FormatStat(PathInfo chapterPath, ChapterStats chapterStats, string format) {
            if (chapterPath == null) {
                format = ChapterRunDeathChanceOverX.ReplaceAll(format, StatManager.MissingPathOutput);
                format = CheckpointRunDeathChanceOverX.ReplaceAll(format, StatManager.MissingPathOutput);
                return format;
            }

            format = ChapterRunDeathChanceOverX.ReplaceMatchException(format);
            format = CheckpointRunDeathChanceOverX.ReplaceMatchException(format);

            List<int> chapterMatchList = ChapterRunDeathChanceOverX.GetMatchList(format);
            List<int> checkpointMatchList = CheckpointRunDeathChanceOverX.GetMatchList(format);

            if (chapterPath.CurrentRoom == null) {
                format = ChapterRunDeathChanceOverX.ReplaceAll(format, StatManager.NotOnPathOutput);
                format = CheckpointRunDeathChanceOverX.ReplaceAll(format, StatManager.NotOnPathOutput);
                return format;
            }

            Dictionary<RoomInfo, Tuple<int, float, int, float>> roomData = ChokeRateStat.GetRoomData(chapterPath, chapterStats);

            // Single pass: accumulate deaths before current room, collect remaining success rates
            // for both chapter (all rooms ahead) and checkpoint (only rooms ahead within current CP)
            int currentDeaths = 0;
            bool reachedCurrentRoom = false;
            List<double> chapterRemainingRates = new List<double>();
            List<double> checkpointRemainingRates = null;
            CheckpointInfo currentCp = chapterPath.CurrentRoom.Checkpoint;

            foreach (CheckpointInfo cpInfo in chapterPath.Checkpoints) {
                bool isCurrentCp = cpInfo == currentCp;
                List<double> cpRemainingRates = isCurrentCp ? new List<double>() : null;

                foreach (RoomInfo rInfo in cpInfo.Rooms) {
                    if (rInfo.IsNonGameplayRoom) continue;

                    if (rInfo.DebugRoomName == chapterStats.CurrentRoom.DebugRoomName)
                        reachedCurrentRoom = true;

                    if (!reachedCurrentRoom) {
                        currentDeaths += chapterStats.GetRoom(rInfo.DebugRoomName).DeathsInCurrentRun;
                    } else {
                        double sr = 1.0;
                        if (roomData.TryGetValue(rInfo, out Tuple<int, float, int, float> data) && !float.IsNaN(data.Item2))
                            sr = data.Item2; // Item2 is (1 - chokeRate)

                        chapterRemainingRates.Add(sr);
                        cpRemainingRates?.Add(sr);
                    }
                }

                if (isCurrentCp)
                    checkpointRemainingRates = cpRemainingRates;
            }

            foreach (int maxDeaths in chapterMatchList) {
                string formatted;
                if (currentDeaths > maxDeaths) {
                    formatted = StatManager.FormatPercentage(0.0);
                } else {
                    double probability = AverageDeathChanceStat.ClearProbability(chapterRemainingRates, maxDeaths - currentDeaths);
                    formatted = StatManager.FormatPercentage(probability);
                }
                format = format.Replace($"{{chapter:runDeathChance#{maxDeaths}}}", formatted);
            }

            foreach (int maxDeaths in checkpointMatchList) {
                if (checkpointRemainingRates == null) {
                    format = StatManager.NotOnPathFormatPercent(format, $"{{checkpoint:runDeathChance#{maxDeaths}}}");
                } else if (currentDeaths > maxDeaths) {
                    format = format.Replace($"{{checkpoint:runDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(0.0));
                } else {
                    double probability = AverageDeathChanceStat.ClearProbability(checkpointRemainingRates, maxDeaths - currentDeaths);
                    format = format.Replace($"{{checkpoint:runDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(probability));
                }
            }

            return format;
        }

        public override string FormatSummary(PathInfo chapterPath, ChapterStats chapterStats) {
            return null;
        }

        public override List<KeyValuePair<string, string>> GetPlaceholderExplanations() {
            return new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>("{chapter:runDeathChance#X}", "Probability of finishing the current run with at most X total deaths, considering remaining chapter rooms and current death count"),
                new KeyValuePair<string, string>("{checkpoint:runDeathChance#X}", "Probability of finishing the current checkpoint with at most X total deaths, considering remaining checkpoint rooms and current death count"),
            };
        }

        public override List<StatFormat> GetDefaultFormats() {
            return new List<StatFormat>() {
                new StatFormat("run-death-chance-0", $"Run 0-death chance: {{chapter:runDeathChance#0}}"),
                new StatFormat("run-death-chance-5", $"Run at most 5 deaths: {{chapter:runDeathChance#5}}"),
            };
        }
    }
}
