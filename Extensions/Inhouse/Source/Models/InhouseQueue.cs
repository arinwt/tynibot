﻿using Discord;
using System;
using LiteDB;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Discord.Inhouse
{
    public enum Rank
    {
        GrandChamp = 1500,
        Champ3 = 1400,
        Champ2 = 1300,
        Champ1 = 1200,
        Diamond3 = 1100,
        Diamond2 = 1000,
        Diamond1 = 930,
        Plat3 = 855,
        Plat2 = 775,
        Plat1 = 700,
        Gold3 = 615,
        Gold2 = 555,
        Gold1 = 495,
        Silver3 = 435,
        Silver2 = 375,
        Silver1 = 315,
        Bronze3 = 255,
        Bronze2 = 195,
        Bronze1 = 60
    }

    public enum TeamSize
    {
        Duel = 1, 
        Doubles = 2,
        Standard = 3
    }

    public class InhouseQueue
    {
        [BsonId]
        public ulong Id { get; set; }

        public Player Owner { get; set; }

        public Dictionary<ulong, Player> Players { get; private set; } = new Dictionary<ulong, Player>();

        public InhouseQueue() { }

        public InhouseQueue(ulong id, Player owner)
        {
            Id = id;
            Owner = owner;
            Players = new Dictionary<ulong, Player>() { { owner.Id, owner } };
        }
        public static async Task<InhouseQueue> GetQueueAsync(ulong id, IDiscordClient channel, LiteCollection<InhouseQueue> collection)
        {
            var queue = collection.FindOne(g => g.Id == id);
            if (queue == null)
                throw new KeyNotFoundException();

            foreach (var u in queue.Players.Values)
                u.DiscordUser = await channel.GetUserAsync(u.Id);

            return queue;
        }
    }
}
