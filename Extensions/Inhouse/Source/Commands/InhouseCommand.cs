﻿using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using LiteDB;
using TyniBot;
using Discord.WebSocket;
using Discord.Matches;

namespace Discord.Inhouse
{
    public class TeamComparer : Comparer<Tuple<List<Player>, List<Player>>>
    {
        public override int Compare(Tuple<List<Player>, List<Player>> x, Tuple<List<Player>, List<Player>> y)
        {
            return MMRDifference(x).CompareTo(MMRDifference(y));
        }

        private int MMRDifference(Tuple<List<Player>, List<Player>> match)
        {
            // Foreach might be faster.

            int mmrFirstTeam = match.Item1.Sum(item => item.MMR);

            int mmrSecondTeam = match.Item2.Sum(item => item.MMR);

            return Math.Abs(mmrFirstTeam - mmrSecondTeam);
        }
    }

    public class PlayerMMRComparer : Comparer<Player>
    {
        public override int Compare(Player x, Player y)
        {
            return x.MMR.CompareTo(y.MMR);
        }
    }

    [Group("inhouse")]
    public class InhouseCommand : ModuleBase<TyniBot.CommandContext>
    {
        Dictionary<string, Rank> RankMap = new Dictionary<string, Rank>()
        {
            { "gc", Rank.GrandChamp },
            { "c3", Rank.Champ3 },
            { "c2", Rank.Champ2 },
            { "c1", Rank.Champ1 },
            { "d3", Rank.Diamond3 },
            { "d2", Rank.Diamond2 },
            { "d1", Rank.Diamond1 },
            { "p3", Rank.Plat3 },
            { "p2", Rank.Plat2 },
            { "p1", Rank.Plat1 },
            { "g3", Rank.Gold3 },
            { "g2", Rank.Gold2 },
            { "g1", Rank.Gold1 },
            { "s3", Rank.Silver3 },
            { "s2", Rank.Silver2 },
            { "s1", Rank.Silver1 },
            { "b3", Rank.Bronze3 },
            { "b2", Rank.Bronze2 },
            { "b1", Rank.Bronze1 }
        };

        Dictionary<string, TeamSize> TeamSizeMap = new Dictionary<string, TeamSize>()
        {
            { "3", TeamSize.Standard },
            { "2", TeamSize.Doubles },
            { "1", TeamSize.Duel },
        };

        Dictionary<string, SplitMode> SplitModeMap = new Dictionary<string, SplitMode>()
        {
            { "random", SplitMode.Random},
            { "skillgroup", SplitMode.SkillGroup},
        };

        #region Commands
        [Command("new"), Summary("**!inhouse new <queueName>** Creates a new game of inhouse soccar! Each individual player needs to join.")]
        public async Task NewInhouseCommand(string name)
        {
            try
            {
                var queue = await CreateQueue(name);
                await Output.QueueStarted(Context.Channel, queue);
            }
            catch (Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("queue"), Summary("**!inhouse queue <queueName> <rank=(c1,d2,p3 etc....)>** Joins a new game of inhouse soccar!")]
        public async Task JoinCommand(string queueName, string rank)
        {
            try
            {
                int mmr = (int)ParseRank(rank);
                var player = Player.ToPlayer(Context.User, mmr);
                var queue = await QueuePlayer(queueName, player);
                await Output.PlayersAdded(Context.Channel, queue, new List<Player>() { player });
            }
            catch (Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("leave"), Summary("**!inhouse leave <queueName>** Leaves a new game of inhouse soccar!")]
        public async Task LeaveCommand(string queueName)
        {
            try
            {
                var player = Player.ToPlayer(Context.User, 0);
                var players = new List<Player>() { player };
                var queue = await DequeuePlayers(queueName, players);
                await Output.PlayersRemoved(Context.Channel, queue, players);
            }
            catch (Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("boot"), Summary("**!inhouse boot <queueName> <@player>** Kicks a player from the queue for inhouse soccar!")]
        public async Task BootCommand(string queueName, [Remainder]string message = "")
        {
            try
            {
                var players = Context.Message.MentionedUsers.Select(s => Player.ToPlayer(s, 0)).ToList();
                var queue = await DequeuePlayers(queueName, players);
                await Output.PlayersRemoved(Context.Channel, queue, players);
            }
            catch(Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("teams"), Summary("**!inhouse teams <queueName> <mode=(3,2,1)> <splitMode=(random, skillgroup)>** Divides teams \"equally\"!")]
        public async Task TeamsCommand(string queueName, string teamSizeStr, string splitModeStr)
        {
            try
            {
                TeamSize size = ParseTeamSize(teamSizeStr);
                SplitMode splitMode = ParseSplitMode(splitModeStr);

                var queue = await GetQueue(queueName);

                if (queue == null)
                {
                    throw new ArgumentException("Did not find any current inhouse queue for this channel.");
                }

                List<List<Player>> playerGroups = SplitQueue(size, queue, splitMode);
                int groupNumber = 1;

                foreach (List<Player> players in playerGroups)
                {
                    var matches = await DivideTeams(size, players);

                    if (matches != null)
                    {
                        await Context.Channel.SendMessageAsync($"Group {groupNumber}");
                        await OutputUniqueMatches(matches, Context.Channel);
                    }

                    ++groupNumber;
                }
            }
            catch (Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("fakeTeams"), Summary("**!inhouse fakeTeams <queueName> <players>** Fills up the queue with enough fake players at random ranks, up to the number of players requested.")]
        public async Task FakeTeamsCommand(string queueName, string playersCount)
        {
            try
            {
                int numPlayers;
                Int32.TryParse(playersCount, out numPlayers);
                var queues = Context.Database.GetCollection<InhouseQueue>();
                var queue = await GetQueue(queueName);

                if (numPlayers <= queue.Players.Count)
                {
                    await Context.Channel.SendMessageAsync($"Already enough players!");
                    return;
                }

                Random rnd = new Random();

                for (int i=0; i < numPlayers - queue.Players.Count; i++)
                {
                    Player botPlayer = new Player();
                    botPlayer.Id = (ulong)i;
                    botPlayer.Username = i.ToString();
                    botPlayer.MMR = (int)RankMap.Values.ElementAt<Rank>(rnd.Next(1, RankMap.Values.Count));

                    await QueuePlayer(queueName, botPlayer);
                   
                }

            }
            catch (Exception e)
            {
                await Context.Channel.SendMessageAsync($"Error: {e.Message}");
            }
        }

        [Command("inhouse"), Summary("**!inhouse help** | Displays this help text.")]
        public async Task HelpCommand()
        {
            await Output.HelpText(Context.Channel);
        }
        #endregion

        #region Helpers

        private TeamSize ParseTeamSize(string teamSize)
        {
            if (!TeamSizeMap.ContainsKey(teamSize.ToLower()))
                throw new ArgumentException($"Unsupported Mode({teamSize})");

            return TeamSizeMap[teamSize];
        }

        private SplitMode ParseSplitMode(string splitMode)
        {
            if (!SplitModeMap.ContainsKey(splitMode.ToLower()))
            {
                throw new ArgumentException($"Unsupported SplitMode({splitMode})");
            }

            return SplitModeMap[splitMode];
        }

        private Rank ParseRank(string rank)
        {
            if (!RankMap.ContainsKey(rank.ToLower()))
                throw new ArgumentException($"Unsupported Rank({rank})");

            return RankMap[rank];
        }

        private async Task<InhouseQueue> CreateQueue(string queueName)
        {
            var newQueue = new InhouseQueue(Context.Channel.Id, queueName);

            var queues = Context.Database.GetCollection<InhouseQueue>();

            // Delete current queue if exists
            try
            {
                var existing = await InhouseQueue.GetQueueAsync(Context.Channel.Id, queueName, Context.Client, queues);
                if (existing != null)
                    queues.Delete(g => g.Name == existing.Name);
            }
            catch (Exception) { }

            // Insert into DB
            queues.Insert(newQueue);
            queues.EnsureIndex(x => x.Name);

            return newQueue;
        }

        private async Task<InhouseQueue> QueuePlayer(string queueName, Player player)
        {
            var queue = await GetQueue(queueName);

            if (queue.Players.ContainsKey(player.Id))
            {   // update player if already exists to allow MMR updates
                queue.Players[player.Id] = player;
            }
            else
            {   // else just queue the new player
                queue.Players.Add(player.Id, player);
            }

            var queues = Context.Database.GetCollection<InhouseQueue>();
            queues.Update(queue);
            return queue;
        }

        private async Task<InhouseQueue> DequeuePlayers(string queueName, List<Player> players)
        {
            var queue = await GetQueue(queueName);

            foreach (var player in players)
            {
                if (queue.Players.ContainsKey(player.Id))
                {
                    queue.Players.Remove(player.Id);
                }
            }

            var queues = Context.Database.GetCollection<InhouseQueue>();
            queues.Update(queue);
            return queue;
        }

        private async Task<List<Tuple<List<Player>, List<Player>>>> DivideTeams(TeamSize size, List<Player> players)
        {
            int teamSize = Convert.ToInt32(size);

            var uniqueTeams = Combinations.Combine<Player>(players, minimumItems: teamSize, maximumItems: teamSize);
            var matches = new List<Tuple<List<Player>, List<Player>>>();

            while (uniqueTeams.Count > 0)
            {
                var team1 = uniqueTeams.First();
                var team2 = uniqueTeams.Where(l => l.ContainsNone(team1)).First();

                matches.Add(new Tuple<List<Player>, List<Player>>(team1, team2));

                uniqueTeams.Remove(team1);
                uniqueTeams.Remove(team2);
            }
            TeamComparer teamComparer = new TeamComparer();
            matches.Sort(teamComparer);

            // TODO: Calculate possible teams with queue.Players and TeamSize and return the possibilities 
            return matches;
        }

        private List<List<Player>> SplitQueue(TeamSize size, InhouseQueue queue, SplitMode splitMode)
        {
            if (queue == null)
            {
                return null;
            }

            List<Player> players = queue.Players.Values.ToList<Player>();

            if (splitMode == SplitMode.Random)
            {
                return SplitQueueRandom(players, size);
            }
            else if (splitMode == SplitMode.SkillGroup)
            {
                return SplitQueueSkillGroup(players, size);
            }
            else
            {
                return null;
            }
        }

        private List<List<Player>> SplitQueueSkillGroup(List<Player> players, TeamSize size)
        {
            PlayerMMRComparer mmrComparer = new PlayerMMRComparer();
            players.Sort(mmrComparer);
            return SplitSortedGroup(players, size);
        }

        private List<List<Player>> SplitQueueRandom(List<Player> players, TeamSize size)
        {
            Random rnd = new Random();
            
            return SplitSortedGroup(players.OrderBy(x => rnd.Next()).ToList(), size);
        }

        private static List<List<Player>> SplitSortedGroup(List<Player> players, TeamSize size)
        {
            int matchSize = (int)size * 2;

            List<List<Player>> playerGroups = new List<List<Player>>();

            while (players.Count > matchSize)
            {
                playerGroups.Add(players.Take(matchSize).ToList());
                players.RemoveRange(0, matchSize);
            }

            playerGroups.Add(players.ToList());

            return playerGroups;
        }

        private static async Task<IMessage> OutputUniqueMatches(List<Tuple<List<Player>, List<Player>>> matches, IMessageChannel channel)
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var team1 = string.Join(' ', match.Item1.Select(m => m.Username));
                var team2 = string.Join(' ', match.Item2.Select(m => m.Username));

                int team1MMR = match.Item1.Sum(item => item.MMR) / match.Item1.Count;
                int team2MMR = match.Item2.Sum(item => item.MMR) / match.Item2.Count;

                string team1Str = $"Orange: {team1}";
                string team2Str = $"Blue: {team2}";
                string team1MMRStr = $" OrangeMMR: {team1MMR}";
                string team2MMRStr = $" BlueMMR: {team2MMR}";


                embedBuilder.AddField($"Match {i + 1}:", team1Str + team1MMRStr + "\r\n" + team2Str + team2MMRStr);
            }

            return await channel.SendMessageAsync($"**Unique Matches: {matches.Count}**", false, embedBuilder.Build());
        }

        private async Task<InhouseQueue> GetQueue(string queueName)
        {
            var queues = Context.Database.GetCollection<InhouseQueue>();
            var queue = await InhouseQueue.GetQueueAsync(Context.Channel.Id, queueName, Context.Client, queues);
            if (queue == null) throw new ArgumentException("Did not find any current inhouse queue for this channel.");
            return queue;
        }

        #endregion
    }
}
