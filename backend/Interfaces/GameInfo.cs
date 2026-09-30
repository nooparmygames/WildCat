using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
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

        public Guid Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public GameGenre Genre { get; set; }
        public string Website { get; set; }
        public byte[] Image { get; set; }
        public GameRegion Region { get; set; }
        public string Platform { get; set; }
        public GameAgeRating AgeRating { get; set; }
        public string StudioName { get; set; }
    }
}
