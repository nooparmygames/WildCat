using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
    [GenerateSerializer]
    [Alias("wildcat.GameInfo")]
    public class GameInfo
    {
        public enum GameGenre
        {
            Action,
            Arcade,
            Strategy,
            Puzzle,
            Casual,
            Sports
        }

        public enum GameRegion
        {
            Iran,
            Oversea
        }

        public enum GameAgeRating
        {
            EveryOne,
            Kids,
            Teens,
            Mature
        }

        [Id(0)] public Guid Key { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public string Description { get; set; }
        [Id(3)] public GameGenre Genre { get; set; }
        [Id(4)] public string Website { get; set; }
        [Id(5)] public byte[] Image { get; set; }
        [Id(6)] public GameRegion Region { get; set; }
        [Id(7)] public string Platform { get; set; }
        [Id(8)] public GameAgeRating AgeRating { get; set; }
        [Id(9)] public string StudioName { get; set; }
    }
}
