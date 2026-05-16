using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Celeste.Mod.ConsistencyTracker.Models;

namespace Celeste.Mod.ConsistencyTracker.Stats {

    /*
     {chapter:averageDeathChance#x} - Probability of clearing the chapter with at most X deaths, based on room success rates
     {checkpoint:averageDeathChance#x} - Probability of clearing the current checkpoint with at most X deaths, based on room success rates
         */

    public class AverageDeathChanceStat : Stat {

        public static ValuePlaceholder<int> ChapterAverageDeathChanceOverX = new ValuePlaceholder<int>(
            @"\{chapter:averageDeathChance#(.*?)\}",
            "{chapter:averageDeathChance#(.*?)}"
        );

        public static ValuePlaceholder<int> CheckpointAverageDeathChanceOverX = new ValuePlaceholder<int>(
            @"\{checkpoint:averageDeathChance#(.*?)\}",
            "{checkpoint:averageDeathChance#(.*?)}"
        );

        public static List<string> IDs = new List<string>();

        public AverageDeathChanceStat() : base(IDs) { }

        public override bool ContainsIdentificator(string format) {
            return ChapterAverageDeathChanceOverX.HasMatch(format) || CheckpointAverageDeathChanceOverX.HasMatch(format);
        }

        public override string FormatStat(PathInfo chapterPath, ChapterStats chapterStats, string format) {
            if (chapterPath == null) {
                format = ChapterAverageDeathChanceOverX.ReplaceAll(format, StatManager.MissingPathOutput);
                format = CheckpointAverageDeathChanceOverX.ReplaceAll(format, StatManager.MissingPathOutput);
                return format;
            }

            format = ChapterAverageDeathChanceOverX.ReplaceMatchException(format);
            format = CheckpointAverageDeathChanceOverX.ReplaceMatchException(format);

            List<int> chapterMatchList = ChapterAverageDeathChanceOverX.GetMatchList(format);
            List<int> checkpointMatchList = CheckpointAverageDeathChanceOverX.GetMatchList(format);

            // golden success rate (1 - choke rate) per room, keyed by RoomInfo
            Dictionary<RoomInfo, Tuple<int, float, int, float>> roomData = ChokeRateStat.GetRoomData(chapterPath, chapterStats);

            // Collect success rates for all rooms on the path
            List<double> chapterSuccessRates = new List<double>();
            List<double> checkpointSuccessRates = null;

            foreach (CheckpointInfo cpInfo in chapterPath.Checkpoints) {
                bool isCurrentCheckpoint = false;
                List<double> cpSuccessRates = new List<double>();

                foreach (RoomInfo rInfo in cpInfo.Rooms) {
                    if (rInfo.IsNonGameplayRoom) continue;

                    double sr = 1.0;
                    if (roomData.TryGetValue(rInfo, out Tuple<int, float, int, float> data) && !float.IsNaN(data.Item2))
                        sr = data.Item2; // Item2 is (1 - chokeRate)

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
                format = format.Replace($"{{chapter:averageDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(probability));
            }

            foreach (int maxDeaths in checkpointMatchList) {
                if (checkpointSuccessRates == null) {
                    format = StatManager.NotOnPathFormatPercent(format, $"{{checkpoint:averageDeathChance#{maxDeaths}}}");
                } else {
                    double probability = ClearProbability(checkpointSuccessRates, maxDeaths);
                    format = format.Replace($"{{checkpoint:averageDeathChance#{maxDeaths}}}", StatManager.FormatPercentage(probability));
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
                new KeyValuePair<string, string>("{chapter:averageDeathChance#X}", "Probability of clearing the chapter with at most X deaths on average, based on room success rates"),
                new KeyValuePair<string, string>("{checkpoint:averageDeathChance#X}", "Probability of clearing the current checkpoint with at most X deaths on average, based on room success rates"),
            };
        }

        public override List<StatFormat> GetDefaultFormats() {
            return new List<StatFormat>() {
                new StatFormat("death-chance-0", $"0-death chance on average: {{chapter:averageDeathChance#0}}"),
                new StatFormat("death-chance-1", $"At most 1 death on average: {{chapter:averageDeathChance#1}}"),
            };
        }
    }
}
