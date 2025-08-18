using System;
using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;

namespace MTGATrackerDaemon.Controllers
{
    public class MatchStateController
    {
        private readonly HttpServer _server;

        public MatchStateController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                ManagedClassInstance matchManager = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"];

                string matchId = matchManager.GetValue<string>("<MatchID>k__BackingField");

                ManagedClassInstance localPlayerInfo = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"]["<LocalPlayerInfo>k__BackingField"];

                float LocalMythicPercentile = localPlayerInfo.GetValue<float>("MythicPercentile");
                int LocalMythicPlacement = localPlayerInfo.GetValue<int>("MythicPlacement");
                int LocalRankingClass = localPlayerInfo.GetValue<int>("RankingClass");
                int LocalRankingTier = localPlayerInfo.GetValue<int>("RankingTier");

                ManagedClassInstance opponentInfo = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"]["<OpponentInfo>k__BackingField"];

                float OpponentMythicPercentile = opponentInfo.GetValue<float>("MythicPercentile");
                int OpponentMythicPlacement = opponentInfo.GetValue<int>("MythicPlacement");
                int OpponentRankingClass = opponentInfo.GetValue<int>("RankingClass");
                int OpponentRankingTier = opponentInfo.GetValue<int>("RankingTier");
           
                TimeSpan ts = (DateTime.Now - startTime);
                return $"{{\"matchId\": \"{matchId}\",\"playerRank\":{{\"mythicPercentile\":{LocalMythicPercentile},\"mythicPlacement\":{LocalMythicPlacement},\"class\":{LocalRankingClass},\"tier\":{LocalRankingTier}}},\"opponentRank\":{{\"mythicPercentile\":{OpponentMythicPercentile},\"mythicPlacement\":{OpponentMythicPlacement},\"class\":{OpponentRankingClass},\"tier\":{OpponentRankingTier}}},\"elapsedTime\":{(int)ts.TotalMilliseconds}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.ToString()}\"}}";
            }
        }
    }
}