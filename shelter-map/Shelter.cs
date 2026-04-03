using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace shelter_map
{
    public class Shelter
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Cats { get; set; }
        public int Kittens { get; set; }
        public int Dogs { get; set; }
        public int CatsSaved { get; set; }
        public int KittensSaved { get; set; }
        public int DogsSaved { get; set; }
        public int Total { get; set; }
        public double SaveRate { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
