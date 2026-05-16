using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Celeste.Mod.ConsistencyTracker.Models;

namespace Celeste.Mod.ConsistencyTracker.Stats {

    /*
     {chapter:runDeathChance#x} - Probability of clearing the chapter with at most X deaths, based on room success rates
     {checkpoint:runDeathChance#x} - Probability of clearing the current checkpoint with at most X deaths, based on room success rates
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

            // Collect success rates for all rooms on the path
            List<double> chapterSuccessRates = new List<double>();
            List<double> checkpointSuccessRates = null;

            foreach (CheckpointInfo cpInfo in chapterPath.Checkpoints) {
                bool isCurrentCheckpoint = false;
                List<double> cpSuccessRates = new List<double>();

                foreach (RoomInfo rInfo in cpInfo.Rooms) {
                    if (rInfo.IsNonGameplayRoom) continue;
                    RoomStats rStats = chapterStats.GetRoom(rInfo.DebugRoomName);
                    double sr = rStats.AverageSuccessOverN(StatManager.AttemptCount);
                    if (StatManager.IgnoreUnplayedRooms && rStats.IsUnplayed)
                        sr = 1.0;

                    chapterSuccessRates.Add(sr);
                    cpSuccessRates.Add(sr);

                    if (rInfo.DebugRoomName == chapterStats.CurrentRoom.DebugRoomName)
                        isCurrentCheckpoint = true;
                }

                if (isCurrentCheckpoint)
                    checkpointSuccessRates = cpSuccessRates;
            }

            foreach (int maxDeaths in chapterMatchList) {
                double probability = ClearProbability(chapterSuccessRates, maxDeaths);
                format = format.Replace($"{{chapter:runDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(probability));
            }

            foreach (int maxDeaths in checkpointMatchList) {
                if (checkpointSuccessRates == null) {
                    format = StatManager.NotOnPathFormatPercent(format, $"{{checkpoint:runDeathChance#{maxDeaths}}}");
                } else {
                    double probability = ClearProbability(checkpointSuccessRates, maxDeaths);
                    format = format.Replace($"{{checkpoint:runDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(probability));
                }
            }

            return format;
        }

        public override string FormatSummary(PathInfo chapterPath, ChapterStats chapterStats) {
            return null;
        }

        /// <summary>
        /// Probability of clearing all rooms with at most <paramref name="maxDeaths"/> deaths total.
        /// Each room is a geometric trial: P(k deaths before success) = (1-p)^k * p
        /// </summary>
        public static double ClearProbability(List<double> successRates, int maxDeaths) {
            // dp[d] = probability of having exactly d deaths so far
            Dictionary<int, double> dp = new Dictionary<int, double>();
            dp[0] = 1.0;

            foreach (double p in successRates) {
                double q = 1.0 - p;
                Dictionary<int, double> newDp = new Dictionary<int, double>();

                foreach (KeyValuePair<int, double> kv in dp) {
                    int currentDeaths = kv.Key;
                    double currentProb = kv.Value;

                    int maxExtra = maxDeaths - currentDeaths;
                    for (int extra = 0; extra <= maxExtra; extra++) {
                        double prob = Math.Pow(q, extra) * p;
                        int totalDeaths = currentDeaths + extra;

                        if (!newDp.ContainsKey(totalDeaths))
                            newDp[totalDeaths] = 0.0;
                        newDp[totalDeaths] += currentProb * prob;
                    }
                }

                dp = newDp;
            }

            double total = 0.0;
            foreach (KeyValuePair<int, double> kv in dp) {
                if (kv.Key <= maxDeaths)
                    total += kv.Value;
            }
            return total;
        }

        public override List<KeyValuePair<string, string>> GetPlaceholderExplanations() {
            return new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>("{chapter:runDeathChance#X}", "Probability of clearing the chapter with at most X deaths, based on room success rates"),
                new KeyValuePair<string, string>("{checkpoint:runDeathChance#X}", "Probability of clearing the current checkpoint with at most X deaths, based on room success rates"),
            };
        }

        public override List<StatFormat> GetDefaultFormats() {
            return new List<StatFormat>() {
                new StatFormat("death-chance-0", $"0-death chance: {{chapter:runDeathChance#0}}"),
                new StatFormat("death-chance-1", $"At most 1 death: {{chapter:runDeathChance#1}}"),
            };
        }
    }
}
